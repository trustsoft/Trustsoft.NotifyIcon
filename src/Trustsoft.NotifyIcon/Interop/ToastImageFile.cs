using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// One image the toast path needs on disk: a WPF <see cref="ImageSource"/> persisted as a PNG in
/// the library's own temp folder, together with the absolute <c>file:///</c> reference handed to
/// the shell and the ownership of the file behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a file at all (D005, D059).</b> The shell reads a toast image from a file or a URL, not
/// from an in-memory image, so the <c>HICON</c> conversion the tray uses cannot be reused here - a
/// notification-area icon is a GDI handle, and this is a path. This class is therefore the second
/// half of the <see cref="ToastImage.Source"/> contract: it owns the only part of the toast that
/// lives outside the process, the bytes on disk.
/// </para>
/// <para>
/// <b>The reference is built, never concatenated.</b> <c>new Uri(path, UriKind.Absolute)</c> is the
/// only correct spelling: <c>"file:///" + path</c> leaves a raw space (and a raw non-ASCII
/// character) in the URI for any temp path that has one, and the payload builder deliberately never
/// URL-encodes an attribute value, so the malformed URI would reach the shell unchanged.
/// </para>
/// <para>
/// <b>Ownership is explicit, short-lived, and there is exactly one delete path.</b> The file belongs
/// to the object <see cref="Create(ImageSource)"/> returns, from the moment it returns until
/// <see cref="Delete"/> is called. That object travels with the resolved reference into the payload
/// (<c>ToastPayload.ImageFile</c>), and <c>ToastShow</c> calls <see cref="Delete"/> from its single
/// unwind path - the path a disposal and a failed show both take - so the object that wrote the file
/// is the object that removes it, exactly once. A payload built from a path alone (the
/// resolved-reference constructor the exact-string contract tests use) adopts its file through
/// <see cref="Adopt"/>, so that shape ends in the same single delete rather than in a second delete of
/// its own. The directory is fixed and documented (<see cref="DefaultFolder"/>) rather than a cache:
/// a process killed before its teardown can leave one orphan file in it, which is a documented
/// limitation instead of a background sweeper.
/// </para>
/// <para>
/// <b>Everything is translated.</b> A source that cannot be read, encoded or written - including a
/// source the rasterizer refuses because it belongs to another thread - surfaces as
/// <see cref="ToastException"/> with the <see cref="OperationImageResolution"/> operation and the
/// source type plus the underlying message in the detail. A <see cref="TrayIconException"/> or a
/// raw WPF exception must never escape the toast path, and no half-written file survives a failure.
/// </para>
/// <para>
/// <c>internal</c> by design: the resolver is implementation behind <see cref="ToastImage.Source"/>,
/// not part of the shipped surface (D002/D010), in the same file-per-concern style as
/// <c>HiconFactory</c>, <c>ToastIdentity</c> and <c>ToastPayload</c>.
/// </para>
/// </remarks>
internal sealed class ToastImageFile
{
    /// <summary>
    /// The <see cref="ToastException.Operation"/> reported for every failure of this class; the
    /// public constant on <see cref="ToastException"/> is the same string, so the resolver and the
    /// exception type cannot drift apart.
    /// </summary>
    internal const string OperationImageResolution = ToastException.OperationImageResolution;

    /// <summary>
    /// The square edge length in pixels a non-bitmap (drawing) source is rasterized at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A library choice, because the shared rasterizer is square-only.</b>
    /// <see cref="HiconFactory.GetBgraPixels"/> takes a single edge length (the tray icon is
    /// square), so a single square edge is the only size this class can honestly ask it for. One
    /// size for both placements is also deliberate: the shell scales the image to the placement it
    /// is used with, and re-rasterizing per placement would invent a second rendering decision the
    /// consumer never asked for.
    /// </para>
    /// <para>
    /// <b>256 downscales cleanly.</b> It is a power of two and an exact 16x reduction of the
    /// documented <c>appLogoOverride</c> 48x48 and hero 364x180 targets' common factors, so the
    /// shell's downscale stays crisp for both placements while the payload stays small (256x256
    /// straight-alpha BGRA is 256 KB before PNG compression).
    /// </para>
    /// </remarks>
    internal const int VectorPixelSize = 256;

    /// <summary>The fixed subfolder of the user's temp directory the library writes its images to.</summary>
    private const string TempFolderName = "Trustsoft.NotifyIcon";

    /// <summary>The file-name prefix, so the library's own files are recognisable in the folder.</summary>
    private const string FileNamePrefix = "toast-";

    /// <summary>The file-name extension; PNG is what the toast image schema expects for a file.</summary>
    private const string FileNameExtension = ".png";

    /// <summary>The device-independent resolution recorded in the PNG.</summary>
    private const int EncodeDpi = 96;

    /// <summary>
    /// Initializes an owner for a file that has already been written.
    /// </summary>
    /// <param name="path">The absolute path of the file that was written.</param>
    /// <param name="pixelWidth">The image width in pixels.</param>
    /// <param name="pixelHeight">The image height in pixels.</param>
    private ToastImageFile(string path, int pixelWidth, int pixelHeight)
    {
        Path = path;
        Reference = new Uri(path, UriKind.Absolute).AbsoluteUri;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>
    /// Gets the library's fixed temp folder for toast images:
    /// <c>Path.Combine(Path.GetTempPath(), "Trustsoft.NotifyIcon")</c>.
    /// </summary>
    /// <value>
    /// An absolute path. The folder is created on demand by <see cref="Create(ImageSource)"/> and
    /// is never itself deleted, so its existence is not a claim that a show is live.
    /// </value>
    internal static string DefaultFolder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), TempFolderName);

    /// <summary>
    /// Gets the absolute path of the file this object owns.
    /// </summary>
    /// <value>The path the PNG was written to; it is not checked for existence here.</value>
    internal string Path { get; }

    /// <summary>
    /// Gets the absolute <c>file:///</c> URI of <see cref="Path"/>, ready to put into the payload.
    /// </summary>
    /// <value>
    /// A URI built with <c>new Uri(Path, UriKind.Absolute).AbsoluteUri</c>, so a space or a
    /// non-ASCII character in the path is percent-encoded rather than left raw.
    /// </value>
    internal string Reference { get; }

    /// <summary>
    /// Gets the image width in pixels the encoder wrote, or <c>0</c> for a file
    /// <see cref="Adopt"/> took over without measuring it.
    /// </summary>
    internal int PixelWidth { get; }

    /// <summary>
    /// Gets the image height in pixels the encoder wrote, or <c>0</c> for a file
    /// <see cref="Adopt"/> took over without measuring it.
    /// </summary>
    internal int PixelHeight { get; }

    /// <summary>
    /// Persists <paramref name="source"/> as a PNG under the library's fixed temp folder and returns
    /// the object that owns the file.
    /// </summary>
    /// <param name="source">The image to persist; must not be <see langword="null"/>.</param>
    /// <returns>An owner exposing the path, the <c>file:///</c> reference and the pixel extent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ToastException">
    /// The source could not be read, encoded or written. <see cref="ToastException.Operation"/> is
    /// <see cref="OperationImageResolution"/>, and no file is left behind.
    /// </exception>
    internal static ToastImageFile Create(ImageSource source) => Create(source, DefaultFolder);

    /// <summary>
    /// Persists <paramref name="source"/> as a PNG under <paramref name="folder"/> and returns the
    /// object that owns the file.
    /// </summary>
    /// <param name="source">The image to persist; must not be <see langword="null"/>.</param>
    /// <param name="folder">
    /// The absolute folder to write into. Production code uses <see cref="DefaultFolder"/>; the
    /// overload exists so a test can drive a folder whose name contains a space (and a non-ASCII
    /// character), which is the case the reference builder exists to get right.
    /// </param>
    /// <returns>An owner exposing the path, the <c>file:///</c> reference and the pixel extent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is null or empty.</exception>
    /// <exception cref="ToastException">
    /// The source could not be read, encoded or written. <see cref="ToastException.Operation"/> is
    /// <see cref="OperationImageResolution"/>, and no file is left behind.
    /// </exception>
    internal static ToastImageFile Create(ImageSource source, string folder) => Create(source, folder, WritePng);

    /// <summary>
    /// Persists <paramref name="source"/> through <paramref name="encode" /> - the seam the cleanup
    /// guard's <see cref="ToastException"/> branch is proven with - and returns the object that owns
    /// the file.
    /// </summary>
    /// <param name="source">The image to persist; must not be <see langword="null"/>.</param>
    /// <param name="folder">The absolute folder to write into.</param>
    /// <param name="encode">
    /// The encode step: it receives the source and the absolute path to write and returns the pixel
    /// extent to report, exactly as <see cref="WritePng"/> does. Production code always passes
    /// <see cref="WritePng"/>, through the overload above.
    /// </param>
    /// <returns>An owner exposing the path, the <c>file:///</c> reference and the pixel extent.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="encode"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is null or empty.</exception>
    /// <exception cref="ToastException">
    /// The source could not be read, encoded or written, or a helper raised the library's own failure
    /// type. The file is removed in either case; the exception is the translated
    /// <see cref="OperationImageResolution"/> failure for an internal failure of any other type, and
    /// the helper's own unchanged <see cref="ToastException"/> when it already spoke that language.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why the seam exists.</b> The guard below must remove a half-written file for every exception
    /// type, including a <see cref="ToastException"/> an internal helper raised - which is cleaned up
    /// and rethrown unchanged rather than translated a second time. No source, and no double of one,
    /// can make the real encode step throw this library's own type: <see cref="ToastException"/> is
    /// sealed, and <see cref="HiconFactory.GetBgraPixels"/> turns everything it catches into a
    /// <see cref="TrayIconException"/> that this class then translates. The seam is therefore the only
    /// way to reach that branch, and it exists for
    /// <c>ToastImageFileTests.A_toast_exception_from_the_encode_step_is_cleaned_up_and_rethrown_unchanged</c>.
    /// </para>
    /// <para>
    /// <b>The guard runs once, for whichever step failed.</b> Creating the folder and running the encode
    /// step share one <c>try</c>, so a failure after the file was opened but before the encoder committed
    /// (a disk-full <see cref="IOException"/>, an encoder refusal) cannot leave the partial file in the
    /// library's folder.
    /// </para>
    /// </remarks>
    internal static ToastImageFile Create(
        ImageSource source,
        string folder,
        Func<ImageSource, string, (int PixelWidth, int PixelHeight)> encode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(encode);

        // The random name is the whole collision story: two shows resolving the same content at the
        // same time (or the same content shown twice) must not share a file, because one show's
        // teardown would then delete the other's image. Guid.NewGuid() with the "N" format is
        // enough and introduces no counter to keep in sync.
        string path = System.IO.Path.Combine(folder, FileNamePrefix + Guid.NewGuid().ToString("N") + FileNameExtension);

        try
        {
            Directory.CreateDirectory(folder);

            (int pixelWidth, int pixelHeight) = encode(source, path);

            return new ToastImageFile(path, pixelWidth, pixelHeight);
        }
        catch (ToastException)
        {
            // A helper already spoke the library's own failure language: its operation, code and detail
            // are the report, so the file is removed and the exception rethrown unchanged rather than
            // translated into a second ToastException that would lose the original.
            DeleteFileIfPresent(path);

            throw;
        }
        catch (Exception ex)
        {
            // The one unwind path for a failed resolution. A run that fails after the file was
            // opened but before the encoder committed (a disk-full IOException, an encoder refusal)
            // would otherwise leave the partial file in the library's folder forever.
            DeleteFileIfPresent(path);

            throw new ToastException(
                OperationImageResolution,
                DescribeCode(ex),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The image source of type '{0}' could not be persisted as a PNG at '{1}': {2}",
                    source.GetType().FullName,
                    path,
                    ex.Message));
        }
    }

    /// <summary>
    /// Takes ownership of a toast image file that already exists at <paramref name="path"/>, without
    /// measuring it.
    /// </summary>
    /// <param name="path">The absolute path of the file to own; it is not checked for existence.</param>
    /// <returns>An owner whose <see cref="Delete"/> removes the file at <paramref name="path"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="UriFormatException"><paramref name="path"/> is not an absolute path.</exception>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> <see cref="Create(ImageSource)"/> is the only method here that writes a
    /// file, but a payload can also be built from a path alone (the resolved-reference constructor).
    /// Wrapping that path in an owner keeps one delete path: every payload that names a file carries
    /// the owner the show deletes through, so the show never deletes a bare string.
    /// </para>
    /// <para>
    /// <b>The extent reads <c>0</c>, because nothing measured this file.</b>
    /// <see cref="PixelWidth"/> and <see cref="PixelHeight"/> are the encoder's own readings from
    /// <see cref="Create(ImageSource)"/> - the notifier's resolution trace line is their only reader -
    /// and an adopted file was written by someone else, so there is no reading to report: <c>0</c>
    /// means "not measured", never a zero-pixel image.
    /// </para>
    /// <para>
    /// <b>The reference is derived here, not trusted.</b> <see cref="Reference"/> is built from
    /// <paramref name="path"/> exactly as <see cref="Create(ImageSource)"/> builds it. It is not a claim
    /// about what such a payload renders: the reference that reaches the shell is the resolved
    /// reference passed into the payload separately, which is the caller's own string.
    /// </para>
    /// </remarks>
    internal static ToastImageFile Adopt(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new ToastImageFile(path, 0, 0);
    }

    /// <summary>
    /// Deletes the file this object owns: the library's one delete path for a toast image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the delete the production show performs.</b> The notifier hands this instance to
    /// <c>ToastShow</c> through the payload, and <c>ToastShow.DeleteImageFile</c> - the show's single
    /// unwind point, reached from both its failure path and its disposal - calls this method after the
    /// unsubscribes and the handle releases. Nothing else in the library deletes a toast image file: a
    /// payload that knew the file by path alone adopts it into this type first.
    /// </para>
    /// <para>
    /// <b>Idempotent and safe after the file is gone.</b> <c>File.Delete</c> succeeds for a path
    /// that does not exist, so calling this twice - or calling it after something else removed the
    /// file - is a no-op rather than an error. That is what lets the show call it from that single
    /// unwind point without tracking whether a delete already ran.
    /// </para>
    /// <para>
    /// <b>A failed delete does not throw.</b> The file is the library's own temp artefact and the
    /// caller is usually unwinding a show; an <see cref="IOException"/> (the file is opened by the
    /// shell) or an <see cref="UnauthorizedAccessException"/> here must not replace the failure
    /// that caused the unwind. A file that could not be deleted is left for the user's own temp
    /// cleanup, which is the same end state as a process killed before teardown.
    /// </para>
    /// </remarks>
    internal void Delete()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Best effort: see the remarks on this method.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: see the remarks on this method.
        }
    }

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="path"/> as a PNG and returns the pixel
    /// extent that was encoded.
    /// </summary>
    /// <param name="source">The image to encode.</param>
    /// <param name="path">The absolute path of the file to write.</param>
    /// <returns>The width and height in pixels of the encoded frame.</returns>
    /// <remarks>
    /// <para>
    /// <b>A bitmap is encoded as it is.</b> No resampling and no recolouring: the pixels that reach
    /// the PNG are the source's own, and the shell does the scaling. WPF's PNG encoder converts the
    /// premultiplied formats itself (measured: a <c>Pbgra32</c> source with <c>B=64 G=32 R=16</c> at
    /// alpha 128 decodes back as straight <c>B=127 G=63 R=31 A=128</c>), so the straight-alpha
    /// conversion the icon path performs by hand is not needed here.
    /// </para>
    /// <para>
    /// <b>The extent is read before the file is opened</b>, so a source whose raster extent cannot
    /// be described is refused while there is still nothing on disk to clean up.
    /// </para>
    /// </remarks>
    private static (int PixelWidth, int PixelHeight) WritePng(ImageSource source, string path)
    {
        BitmapSource bitmap = source as BitmapSource ?? Rasterize(source);

        int pixelWidth = bitmap.PixelWidth;
        int pixelHeight = bitmap.PixelHeight;

        try
        {
            using FileStream stream = OpenOutput(path);
            SavePng(bitmap, stream);
        }
        catch (NotSupportedException)
        {
            // The encoder refused the source's own pixel format. WPF's converter produces the
            // straight-alpha Bgra32 picture the PNG container does support, and the retry opens the
            // file with FileMode.Create, which truncates whatever the refused attempt had written.
            // Measured: every PixelFormat WPF actually exposes survives the first attempt (WIC
            // inserts its own converter), so this is a future-proofing branch rather than a path
            // with a known trigger - the refusal the suite does exercise is an
            // InvalidOperationException for a source WPF cannot wrap into a frame, which is
            // translated rather than retried.
            using FileStream stream = OpenOutput(path);
            SavePng(ToBgra32(bitmap), stream);
        }

        return (pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Rasterizes a non-bitmap source through the existing square rasterizer.
    /// </summary>
    /// <param name="source">A <see cref="DrawingImage"/> or other non-bitmap source.</param>
    /// <returns>A <see cref="BitmapSource"/> holding the straight-alpha Bgra32 pixels.</returns>
    /// <exception cref="TrayIconException">The source cannot be read on this thread.</exception>
    /// <remarks>
    /// <para>
    /// <b>No second rasterizer.</b> <see cref="HiconFactory.GetBgraPixels"/> already owns the
    /// measured rules for turning a drawing into pixels (the compositor is the only rasterizer WPF
    /// offers, it needs a pumping dispatcher, and frozen drawings are memoized per size), so this
    /// class reuses it rather than repeating them. The one thing it cannot reuse is the size: the
    /// rasterizer is square-only, hence <see cref="VectorPixelSize"/>.
    /// </para>
    /// <para>
    /// The returned buffer is straight alpha, which is exactly what the <c>Bgra32</c> bitmap the
    /// PNG encoder receives must contain.
    /// </para>
    /// </remarks>
    private static BitmapSource Rasterize(ImageSource source)
    {
        byte[] pixels = HiconFactory.GetBgraPixels(source, VectorPixelSize);

        var bitmap = BitmapSource.Create(
            VectorPixelSize,
            VectorPixelSize,
            EncodeDpi,
            EncodeDpi,
            PixelFormats.Bgra32,
            null,
            pixels,
            VectorPixelSize * 4);

        if (bitmap.CanFreeze)
        {
            // Frozen so the bitmap carries no thread affinity of its own; it is created and used on
            // one thread here, but a frozen image cannot become a cross-thread surprise later.
            bitmap.Freeze();
        }

        return bitmap;
    }

    /// <summary>
    /// Encodes one frame of <paramref name="bitmap"/> into <paramref name="stream"/> as a PNG.
    /// </summary>
    /// <param name="bitmap">The bitmap to encode.</param>
    /// <param name="stream">The open, writable stream to save into.</param>
    private static void SavePng(BitmapSource bitmap, Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    /// <summary>
    /// Converts a bitmap to straight-alpha <c>Bgra32</c> through WPF's own converter.
    /// </summary>
    /// <param name="bitmap">The bitmap to convert.</param>
    /// <returns>The converted bitmap.</returns>
    private static BitmapSource ToBgra32(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

        if (converted.CanFreeze)
        {
            converted.Freeze();
        }

        return converted;
    }

    /// <summary>
    /// Opens <paramref name="path"/> for writing, truncating any file already there.
    /// </summary>
    /// <param name="path">The file to create or replace.</param>
    /// <returns>The open stream, which the caller owns and disposes.</returns>
    /// <remarks>
    /// <c>FileMode.Create</c> rather than <c>CreateNew</c>: the name is a fresh GUID, so a collision
    /// is not a case worth failing on, and truncation is what makes the encoder-retry path safe.
    /// </remarks>
    private static FileStream OpenOutput(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None);

    /// <summary>
    /// Deletes <paramref name="path"/> if it exists, ignoring anything the delete can fail with.
    /// </summary>
    /// <param name="path">The file to remove.</param>
    /// <remarks>
    /// Used only on the failure paths of <see cref="Create(ImageSource, string)"/>, where the original
    /// exception is the report and a cleanup failure must not replace it.
    /// </remarks>
    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The original failure is the report; see the remarks.
        }
        catch (UnauthorizedAccessException)
        {
            // The original failure is the report; see the remarks.
        }
    }

    /// <summary>
    /// Picks the most specific code available for a translated failure.
    /// </summary>
    /// <param name="cause">The exception that failed the resolution.</param>
    /// <returns>
    /// The rasterizer's own Win32 code for a <see cref="TrayIconException"/>, the failing
    /// exception's <c>HRESULT</c> for a WPF or IO failure, or <c>0</c> when neither describes it.
    /// </returns>
    /// <remarks>
    /// No single call of this library returns an <c>HRESULT</c> for image resolution: the failure
    /// can come from the rasterizer, from WPF's encoder or from the file system, so the code is the
    /// failing step's own report. <c>0</c> means "no code describes this failure" and never success,
    /// which is the same reading <see cref="ToastException"/> documents.
    /// </remarks>
    private static int DescribeCode(Exception cause)
    {
        if (cause is TrayIconException tray)
        {
            return tray.Win32ErrorCode;
        }

        return cause.HResult;
    }
}
