using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// Converts a WPF <see cref="ImageSource"/> into an <c>HICON</c> that the shell can put in the
/// notification area, using nothing but WPF pixel access and P/Invoke through
/// <see cref="IShellApi"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>System.Drawing.Common</c>, deliberately</b> (R011, D001). It has been a separate
/// package since .NET 7, and taking it would both add the runtime dependency this project exists
/// without and mean acquiring a second icon-ownership model next to the one this library already
/// implements. The conversion is therefore: WPF pixel access into a straight-alpha <c>BGRA</c>
/// buffer, two device-independent bitmaps through <c>CreateDIBSection</c>, and
/// <c>CreateIconIndirect</c>.
/// </para>
/// <para>
/// <b>Ownership is the whole point of this class</b> (R007). <c>CreateIconIndirect</c>
/// <em>copies</em> the two bitmaps, so the caller keeps them and must delete them with
/// <c>DeleteObject</c>. Both are therefore created inside a single <c>try</c> whose
/// <c>finally</c> is the only place in this class that releases them: the success path, the
/// "colour bitmap failed" path, the "mask bitmap failed" path and the "icon creation failed"
/// path all go through it. A single early <c>return</c> that skipped the delete would leak two
/// GDI objects per icon replacement - a leak that is invisible in a short test and is how a
/// long-running tray process slowly exhausts its GDI quota.
/// </para>
/// <para>
/// <b>The returned <c>HICON</c> is the caller's.</b> This class never calls <c>DestroyIcon</c>:
/// an icon destroyed here would be a double free later, because <c>TrayIcon</c> owns the handle
/// under the retain-and-destroy rule (D012) and destroys it when it is replaced or when the
/// instance is disposed.
/// </para>
/// <para>
/// <b>Premultiplied input is converted, not copied.</b> WPF hands out <c>Pbgra32</c> (colour
/// channels premultiplied by alpha) while a <c>BI_RGB</c> 32bpp DIB wants straight
/// <c>Bgra32</c>. Copying the bytes through would produce dark halos wherever the icon is
/// semi-transparent - a symptom that reads as a rendering or design problem rather than the
/// byte-order mistake it is. <see cref="GetBgraPixels"/> is the single place that conversion
/// happens, and it is the ground truth the tests assert against.
/// </para>
/// <para>
/// <b>Thread affinity is handled explicitly.</b> A non-frozen <see cref="ImageSource"/> belongs
/// to the dispatcher that created it, and reading it from another thread throws an
/// <see cref="InvalidOperationException"/> from inside WPF. That failure must not escape
/// unnamed, so the affinity is resolved up front: an image that can be frozen is frozen (which
/// makes it thread-safe), and one that cannot be frozen but belongs to another thread is
/// reported as a <see cref="TrayIconException"/> naming the problem.
/// </para>
/// <para>
/// <b>Why the bitmap path does not use <see cref="RenderTargetBitmap"/>, measured.</b> The
/// compositor-based rasterizer is the obvious way to produce pixels, and it was implemented
/// first, but two measured behaviours rule it out for an ordinary bitmap source: its constructor
/// rejects <see cref="PixelFormats.Bgra32"/> outright ("'Bgra32' PixelFormat is not supported for
/// this operation"), and on an STA thread whose dispatcher is not pumping -
/// which is what a plain test thread is - <c>Render</c> hands back a blank bitmap and a later
/// <c>CopyPixels</c> does not complete at all. The compositor is therefore used only where there
/// is no alternative: a non-bitmap source (a <see cref="DrawingImage"/>, a
/// <see cref="DrawingVisual"/>) has no pixel buffer to read, so it is rasterized through
/// <see cref="RenderTargetBitmap"/> - and that path is used from a real WPF application, whose
/// dispatcher is pumping. Everything with pixels goes through WPF's imaging layer
/// (<c>FormatConvertedBitmap</c> and <c>CopyPixels</c>), which is synchronous, dispatcher-free and
/// produces exactly the bytes the DIB section receives.
/// </para>
/// <para>
/// <b>A frozen drawing source is rasterized once, not once per replacement (R007, measured).</b>
/// <see cref="RenderTargetBitmap"/> is the only rasterizer WPF offers for a drawing and it is
/// <em>single-use</em>: after its first <see cref="RenderTargetBitmap.Render(Visual)"/> a second
/// call on the same instance throws <c>ArgumentException: The Image passed to the
/// ImageVisualManager cannot be frozen</c>, and that holds even after
/// <see cref="RenderTargetBitmap.Clear"/> (both measured on .NET 8; <c>WriteableBitmap</c>, the
/// other mutable surface, has no <c>Render(Visual)</c> at all). There is therefore no reusable
/// rasterizer to keep - but each new instance also holds about two GDI objects until the GC
/// finalizes it, which is invisible in a test that renders a handful of frames and very visible in
/// an application that rotates a vector icon once a second: the live sample climbed from 123 to
/// 235 GDI objects in 57 seconds with no collection in the window. The answer is to <em>not
/// allocate a rasterizer per replacement</em>: the pixels of a <b>frozen</b> source are memoized
/// per pixel size, because a frozen <see cref="Freezable"/> is immutable by construction, so its
/// rasterization cannot go stale. A source that is still mutable is rasterized afresh every time -
/// it may legitimately change between two replacements, and a cache keyed on it would return the
/// previous picture with no way for the caller to notice.
/// </para>
/// <para>
/// <c>internal</c> by design: the converter is not part of the shipped API surface
/// (D002/D010), only of the implementation behind <c>TrayIcon.IconSource</c>.
/// </para>
/// </remarks>
internal static class HiconFactory
{
    /// <summary>
    /// How many distinct pixel sizes are memoized for one source before the conversion stops
    /// caching and starts rasterizing on every call.
    /// </summary>
    /// <remarks>
    /// A single source is normally asked for one or two sizes (the notification-area icon and,
    /// later, a DPI-scaled one). The cap exists so an application that sweeps a source through
    /// hundreds of sizes cannot trade a GDI problem for an unbounded managed one; past the cap the
    /// behaviour is exactly what it was before the cache.
    /// </remarks>
    private const int MaxCachedRasterizationsPerSource = 8;

    /// <summary>
    /// Rasterized straight-alpha pixels of a <b>frozen</b> non-bitmap source, keyed by source and
    /// then by pixel size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ConditionalWeakTable{TKey,TValue}"/> rather than a dictionary so the cache
    /// cannot keep an image alive: the entry dies with the source that owns it, exactly like the
    /// rasterizer it replaces. Frozen sources are immutable, which is what makes memoizing their
    /// pixels sound; the lookup is therefore guarded only against concurrent access to the inner
    /// dictionary, not against the image changing underneath it.
    /// </para>
    /// <para>
    /// This is the R007 answer for drawings: WPF's rasterizer cannot be reused (the class remarks
    /// carry the measurement), so the cheapest possible replacement for a <em>repeated</em>
    /// replacement of the same drawing is to not rasterize at all.
    /// </para>
    /// </remarks>
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, byte[]>> RasterizedPixels = new();

    /// <summary>
    /// Rasterizes <paramref name="source"/> and builds an icon of
    /// <paramref name="pixelSize"/> x <paramref name="pixelSize"/> pixels from it.
    /// </summary>
    /// <param name="shell">The seam every Win32 call goes through.</param>
    /// <param name="source">The image to convert.</param>
    /// <param name="pixelSize">The icon edge length in pixels; the image is scaled to it.</param>
    /// <returns>
    /// An <c>HICON</c> that the <b>caller</b> owns and must release with
    /// <see cref="IShellApi.DestroyIcon"/>. This method never destroys it.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shell"/> or <paramref name="source"/> is <see langword="null"/>. A null
    /// source is a caller contract violation, not a conversion failure: clearing an icon is the
    /// caller's job and does not go through this method.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelSize"/> is not positive.</exception>
    /// <exception cref="TrayIconException">
    /// The image could not be read (including a source that is dispatcher-affine and cannot be
    /// frozen), a bitmap could not be created, or the icon could not be built. The
    /// <see cref="TrayIconException.Operation"/> is always
    /// <see cref="TrayIconException.OperationConvertIcon"/>, and
    /// <see cref="TrayIconException.Win32ErrorCode"/> is the code from the failing Win32 call -
    /// or 0 when the failure was not a Win32 one.
    /// </exception>
    internal static IntPtr CreateIcon(IShellApi shell, ImageSource source, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);

        byte[] pixels = GetBgraPixels(source, pixelSize);
        int colorStride = pixelSize * 4;

        IntPtr hbmColor = IntPtr.Zero;
        IntPtr hbmMask = IntPtr.Zero;

        // The try opens BEFORE the first CreateDIBSection, so there is no path between a
        // successful bitmap creation and this block - and therefore no path that skips the
        // finally below. Handles start at IntPtr.Zero and the finally deletes only what was
        // actually created.
        try
        {
            BITMAPV5HEADER colorHeader = BitmapHeaders.CreateTopDownColorHeader(pixelSize, (uint)(colorStride * pixelSize));
            hbmColor = shell.CreateDIBSection(IntPtr.Zero, ref colorHeader, BitmapHeaders.DIB_RGB_COLORS, out IntPtr colorBits, IntPtr.Zero, 0);

            if (hbmColor == IntPtr.Zero)
            {
                throw new TrayIconException(
                    TrayIconException.OperationConvertIcon,
                    shell.GetLastError(),
                    "The 32bpp colour bitmap for the icon could not be created with CreateDIBSection.");
            }

            Marshal.Copy(pixels, 0, colorBits, colorStride * pixelSize);

            int maskStride = BitmapHeaders.GetMaskStride(pixelSize);
            byte[] maskBitsSource = CreateMaskBits(pixels, pixelSize);
            BITMAPINFO maskInfo = BitmapHeaders.CreateTopDownMaskInfo(pixelSize, (uint)(maskStride * pixelSize));
            hbmMask = shell.CreateDIBSection(IntPtr.Zero, ref maskInfo, BitmapHeaders.DIB_RGB_COLORS, out IntPtr maskBits, IntPtr.Zero, 0);

            if (hbmMask == IntPtr.Zero)
            {
                throw new TrayIconException(
                    TrayIconException.OperationConvertIcon,
                    shell.GetLastError(),
                    "The monochrome AND mask bitmap for the icon could not be created with CreateDIBSection.");
            }

            Marshal.Copy(maskBitsSource, 0, maskBits, maskBitsSource.Length);

            ICONINFO iconInfo = new()
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor,
            };

            IntPtr hIcon = shell.CreateIconIndirect(ref iconInfo);

            if (hIcon == IntPtr.Zero)
            {
                throw new TrayIconException(
                    TrayIconException.OperationConvertIcon,
                    shell.GetLastError(),
                    "CreateIconIndirect did not produce an icon from the converted bitmaps.");
            }

            return hIcon;
        }
        finally
        {
            // CreateIconIndirect COPIES hbmMask and hbmColor, so both are still ours after the
            // call and releasing them here is correct and mandatory. This finally is the ONLY
            // place in this class that frees them: it runs on the success, the mask-failure and
            // the icon-failure paths alike, which is what keeps a failed replacement from
            // leaking a GDI object pair (R007).
            if (hbmColor != IntPtr.Zero)
            {
                shell.DeleteObject(hbmColor);
            }

            if (hbmMask != IntPtr.Zero)
            {
                shell.DeleteObject(hbmMask);
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="source"/> as exactly
    /// <paramref name="pixelSize"/> x <paramref name="pixelSize"/> straight-alpha <c>BGRA</c>
    /// pixels.
    /// </summary>
    /// <param name="source">The image to read.</param>
    /// <param name="pixelSize">The output edge length in pixels.</param>
    /// <returns>The pixel buffer, top row first, four bytes per pixel in blue-green-red-alpha
    /// order - the same layout the DIB section receives.</returns>
    /// <remarks>
    /// <para>
    /// This is the ground truth for what the icon actually contains, which is why it is internal
    /// rather than private: a test can assert the straight-alpha property on the real buffer
    /// instead of inferring it from a rendered icon.
    /// </para>
    /// <para>
    /// <b>A frozen drawing source is read from the memoized rasterization</b> described in the class
    /// remarks: the same <see cref="ImageSource"/> at the same size is rasterized once, so repeated
    /// replacement costs no rasterizer - and therefore no GDI object - after the first pass. A
    /// mutable source is rasterized on every call, so a change to it is always reflected.
    /// </para>
    /// <para>
    /// <b>Scaling is nearest-neighbour for M001.</b> It avoids inventing semi-transparent
    /// interpolation halos, which would blur exactly the straight-alpha property this method
    /// exists to guarantee, and it is done with integer arithmetic over the already-converted
    /// buffer rather than by a transform, so the result depends on nothing but the input pixels.
    /// DPI-correct resampling is S03's concern.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelSize"/> is not positive.</exception>
    /// <exception cref="TrayIconException">
    /// The source cannot be read on this thread; the message names the source type and the
    /// underlying WPF failure.
    /// </exception>
    internal static byte[] GetBgraPixels(ImageSource source, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);

        int stride = pixelSize * 4;
        var pixels = new byte[stride * pixelSize];

        try
        {
            ImageSource ready = PrepareForCurrentThread(source);

            if (ready is BitmapSource bitmap)
            {
                BitmapSource straight = bitmap.Format == PixelFormats.Bgra32 ? bitmap : ToStraightBgra32(bitmap);
                CopyScaledOrDirect(straight, pixels, pixelSize, stride);
            }
            else
            {
                // A drawing source has no pixel buffer: the only rasterizer WPF offers is the
                // compositor, which produces premultiplied pixels and therefore goes through the
                // same straight-alpha conversion. (See the class remarks for the measured
                // constraints on this path.) Rasterization of a frozen source is memoized, so the
                // bytes are copied out rather than written into the caller's buffer directly: the
                // cached array must stay read-only from every caller's point of view.
                byte[] rasterized = GetRasterizedPixels(ready, pixelSize);
                Buffer.BlockCopy(rasterized, 0, pixels, 0, rasterized.Length);
            }
        }
        catch (Exception ex) when (ex is not TrayIconException)
        {
            // A WPF read failure (a locked or otherwise unusable bitmap, an unsupported source)
            // must reach the caller as the named conversion failure, not as an
            // InvalidOperationException from inside WPF. The message keeps the WPF text so the
            // cause is still diagnosable.
            throw new TrayIconException(
                TrayIconException.OperationConvertIcon,
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The ImageSource of type '{0}' could not be read as {1}x{1} straight-alpha BGRA pixels: {2}",
                    source.GetType().FullName,
                    pixelSize,
                    ex.Message));
        }

        return pixels;
    }

    /// <summary>
    /// Returns the straight-alpha <c>BGRA</c> pixels of a non-bitmap source, rasterizing them once
    /// for a frozen source and on every call for a mutable one.
    /// </summary>
    /// <param name="source">The drawing source to read; already resolved for this thread.</param>
    /// <param name="pixelSize">The icon edge length in pixels.</param>
    /// <returns>The pixel buffer, top row first, straight-alpha <c>BGRA</c>.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only a frozen source is cached, and that is the whole correctness argument.</b> A frozen
    /// <see cref="Freezable"/> cannot change, so the pixels memoized for it cannot go stale and
    /// every caller's copy is the same picture. A source that is still mutable may legitimately be
    /// edited between two replacements - that is what a mutable drawing is <em>for</em> - so it is
    /// rasterized afresh, and the caller always sees the current drawing rather than a cached
    /// earlier one.
    /// </para>
    /// <para>
    /// The returned array is the cache's own buffer. Callers copy out of it (see
    /// <see cref="GetBgraPixels"/>) and never write to it, so one cached picture serves every
    /// caller without an extra copy per hit beyond the one the caller already makes.
    /// </para>
    /// </remarks>
    private static byte[] GetRasterizedPixels(ImageSource source, int pixelSize)
    {
        if (!source.IsFrozen)
        {
            return RasterizeToStraightBgra32(source, pixelSize);
        }

        Dictionary<int, byte[]> byPixelSize = RasterizedPixels.GetValue(source, static _ => new Dictionary<int, byte[]>());

        lock (byPixelSize)
        {
            if (byPixelSize.TryGetValue(pixelSize, out byte[]? cached))
            {
                return cached;
            }
        }

        byte[] computed = RasterizeToStraightBgra32(source, pixelSize);

        lock (byPixelSize)
        {
            // Two threads can race here on a frozen, thread-safe source. Keeping the first value
            // and dropping the second is correct because both are rasterizations of the same
            // immutable picture; the loser is simply collected.
            if (byPixelSize.Count < MaxCachedRasterizationsPerSource)
            {
                byPixelSize[pixelSize] = computed;
            }
        }

        return computed;
    }

    /// <summary>
    /// Rasterizes a drawing source and reads it back as straight-alpha <c>BGRA</c> pixels.
    /// </summary>
    /// <param name="source">The drawing source to rasterize.</param>
    /// <param name="pixelSize">The icon edge length in pixels.</param>
    /// <returns>A freshly allocated pixel buffer.</returns>
    /// <remarks>
    /// The compositor produces premultiplied pixels, so the rasterization always goes through
    /// <see cref="ToStraightBgra32"/>: the same conversion the bitmap path applies, which is what
    /// keeps a semi-transparent drawing from reaching the icon with its colour channels already
    /// scaled down by alpha.
    /// </remarks>
    private static byte[] RasterizeToStraightBgra32(ImageSource source, int pixelSize)
    {
        int stride = pixelSize * 4;
        var pixels = new byte[stride * pixelSize];
        ToStraightBgra32(Rasterize(source, pixelSize)).CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>
    /// Derives the monochrome AND mask bits from the alpha channel of a straight-alpha
    /// <c>BGRA</c> pixel buffer.
    /// </summary>
    /// <param name="bgraPixels">The pixel buffer, top row first.</param>
    /// <param name="pixelSize">The edge length in pixels the buffer describes.</param>
    /// <returns>The mask buffer, top row first, <see cref="BitmapHeaders.GetMaskStride"/> bytes
    /// per row.</returns>
    /// <remarks>
    /// <para>
    /// In an icon mask a <b>set bit means transparent</b> (the bit is ANDed with the screen), so
    /// exactly the fully transparent pixels - alpha 0 - get a set bit. Semi-transparent pixels do
    /// not: their transparency lives in the colour bitmap's alpha channel, and marking them in
    /// the mask as well would punch them out entirely.
    /// </para>
    /// <para>
    /// The buffer is allocated for the full mask stride, so the padding bits at the end of each
    /// row are zero by construction rather than by a separate zeroing pass.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="bgraPixels"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelSize"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The buffer is smaller than the described image.</exception>
    internal static byte[] CreateMaskBits(byte[] bgraPixels, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);

        int required = pixelSize * 4 * pixelSize;

        if (bgraPixels.Length < required)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The pixel buffer holds {0} bytes but a {1}x{1} BGRA image needs {2}.",
                    bgraPixels.Length,
                    pixelSize,
                    required),
                nameof(bgraPixels));
        }

        int stride = BitmapHeaders.GetMaskStride(pixelSize);
        var mask = new byte[stride * pixelSize];

        for (int y = 0; y < pixelSize; y++)
        {
            int rowStart = y * pixelSize * 4;
            int maskRowStart = y * stride;

            for (int x = 0; x < pixelSize; x++)
            {
                if (bgraPixels[rowStart + (x * 4) + 3] == 0)
                {
                    mask[maskRowStart + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Resolves the one thing that makes a WPF image usable on this thread: an image this thread
    /// is allowed to read.
    /// </summary>
    /// <param name="source">The caller's image.</param>
    /// <returns>An image that is safe to read on this thread.</returns>
    /// <exception cref="TrayIconException">
    /// The image belongs to another dispatcher and cannot be frozen, so WPF would refuse to read
    /// it here.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The dispatcher is read first, and that order is not cosmetic.</b>
    /// <c>Freezable.Dispatcher</c> is a plain field read, while
    /// <c>Freezable.IsFrozen</c> calls <c>VerifyAccess</c> and throws
    /// <see cref="InvalidOperationException"/> when the object belongs to another thread. A check
    /// written the obvious way round - ask whether it is frozen, then look at the dispatcher -
    /// therefore never reaches its own error message for the case it exists to report, and the
    /// caller gets a WPF thread-affinity exception instead. Reading the dispatcher first makes the
    /// refusal reachable; the identity probe below is still wrapped, because every other
    /// freezable member may be thread-affine too.
    /// </para>
    /// <para>
    /// An image this thread already owns is returned untouched: there is nothing to make safe.
    /// </para>
    /// </remarks>
    private static ImageSource PrepareForCurrentThread(ImageSource source)
    {
        if (ReferenceEquals(source.Dispatcher, Dispatcher.CurrentDispatcher))
        {
            return source;
        }

        if (TryFreeze(source))
        {
            return source;
        }

        throw new TrayIconException(
            TrayIconException.OperationConvertIcon,
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "The ImageSource of type '{0}' belongs to another thread's dispatcher and cannot be frozen, so it cannot be converted into an icon on this thread. Freeze the image where it is created, or set the icon source from its owning dispatcher thread.",
                source.GetType().FullName));
    }

    /// <summary>
    /// Tries to make <paramref name="source"/> safe to read from another thread by freezing it.
    /// </summary>
    /// <param name="source">The image to freeze.</param>
    /// <returns><see langword="true"/> when the image is frozen - or already was - and can be read
    /// from any thread; <see langword="false"/> when it cannot be made thread-safe.</returns>
    /// <remarks>
    /// Every probe is inside the <c>try</c> because the members involved may be thread-affine:
    /// asking a foreign-thread image whether it is frozen can throw rather than answer. A refusal
    /// here means "cannot be used on this thread", which the caller reports; it is not an error to
    /// swallow, and the returned boolean is what decides the message the caller sees.
    /// </remarks>
    private static bool TryFreeze(ImageSource source)
    {
        try
        {
            if (source.IsFrozen)
            {
                return true;
            }

            if (!source.CanFreeze)
            {
                return false;
            }

            // Legitimate cross-thread use of a WPF image: freeze it so it stops being bound to
            // the thread that created it (R015).
            source.Freeze();
            return source.IsFrozen;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts any bitmap to straight-alpha <c>Bgra32</c> using WPF's own conversion.
    /// </summary>
    /// <param name="bitmap">The bitmap to convert.</param>
    /// <returns>The converted bitmap, frozen so it can safely be read from any thread.</returns>
    /// <remarks>
    /// This is where premultiplied input becomes straight alpha: <c>Pbgra32</c> stored
    /// <c>B = 128</c> at alpha 128 is read as <c>B = 255</c> here. Doing it with WPF's converter
    /// instead of hand-rolled arithmetic is what the DIB wants and what the icon needs - a
    /// passthrough would leave the icon's colour channels scaled down by the alpha value.
    /// </remarks>
    private static BitmapSource ToStraightBgra32(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

        if (converted.CanFreeze)
        {
            converted.Freeze();
        }

        return converted;
    }

    /// <summary>
    /// Copies a straight-alpha bitmap into the icon's pixel buffer at exactly
    /// <paramref name="pixelSize"/> x <paramref name="pixelSize"/>, resampling when the source is
    /// a different size.
    /// </summary>
    /// <param name="bitmap">The straight-alpha <c>Bgra32</c> bitmap to read.</param>
    /// <param name="target">The destination buffer, already sized for the icon.</param>
    /// <param name="pixelSize">The icon edge length in pixels.</param>
    /// <param name="targetStride">The destination row stride, <c>pixelSize * 4</c>.</param>
    /// <exception cref="TrayIconException">The bitmap has no raster extent to read.</exception>
    private static void CopyScaledOrDirect(BitmapSource bitmap, byte[] target, int pixelSize, int targetStride)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        if (width <= 0 || height <= 0)
        {
            throw new TrayIconException(
                TrayIconException.OperationConvertIcon,
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The ImageSource has no raster extent ({0}x{1} pixels), so there are no pixels to convert into an icon.",
                    width,
                    height));
        }

        int sourceStride = width * 4;
        var sourcePixels = new byte[sourceStride * height];
        bitmap.CopyPixels(sourcePixels, sourceStride, 0);

        if (width == pixelSize && height == pixelSize)
        {
            // The frequent case - the icon source is already the icon's size - is a plain copy, so
            // the values reaching the DIB are the source's own, byte for byte.
            Buffer.BlockCopy(sourcePixels, 0, target, 0, targetStride * pixelSize);
            return;
        }

        for (int y = 0; y < pixelSize; y++)
        {
            int sourceRow = (int)((long)y * height / pixelSize) * sourceStride;
            int targetRow = y * targetStride;

            for (int x = 0; x < pixelSize; x++)
            {
                int sourcePixel = sourceRow + ((int)((long)x * width / pixelSize) * 4);
                int targetPixel = targetRow + (x * 4);

                target[targetPixel] = sourcePixels[sourcePixel];
                target[targetPixel + 1] = sourcePixels[sourcePixel + 1];
                target[targetPixel + 2] = sourcePixels[sourcePixel + 2];
                target[targetPixel + 3] = sourcePixels[sourcePixel + 3];
            }
        }
    }

    /// <summary>
    /// Rasterizes a non-bitmap source (a drawing) at the icon's size through the compositor.
    /// </summary>
    /// <param name="source">The drawing source to rasterize.</param>
    /// <param name="pixelSize">The icon edge length in pixels.</param>
    /// <returns>The rasterized bitmap, still premultiplied (the caller converts it).</returns>
    /// <remarks>
    /// <para>
    /// This path requires a thread whose dispatcher is pumping messages - the normal situation in
    /// a WPF application, and not the situation on a bare test thread. It exists because a
    /// <see cref="DrawingImage"/> has no pixel buffer at all, so there is nothing to read; see the
    /// class remarks for why an ordinary bitmap never goes through here.
    /// </para>
    /// <para>
    /// <b>One instance per call, because WPF leaves no choice.</b> A
    /// <see cref="RenderTargetBitmap"/> is single-use: rendering into it twice throws, and
    /// <c>Clear()</c> does not make it reusable; <c>WriteableBitmap</c> offers no
    /// <c>Render(Visual)</c> at all (measured, .NET 8). The cost of that is a fresh rasterizer - and
    /// the two GDI objects it holds until it is finalized - per rasterization, which is why the
    /// callers only reach this method once per frozen source and size (see
    /// <see cref="GetRasterizedPixels"/>).
    /// </para>
    /// </remarks>
    private static BitmapSource Rasterize(ImageSource source, int pixelSize)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, pixelSize, pixelSize));
        }

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }
}
