using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves the <see cref="ImageSource"/> to <c>HICON</c> conversion: the header layout GDI reads,
/// the straight-alpha conversion that keeps semi-transparent icons from getting dark halos, the
/// monochrome AND mask (including the row stride trap for widths that are not a multiple of
/// eight), and - the point of the task - that both <c>HBITMAP</c>s are released on the success
/// path, on both partial-failure paths and on the icon-failure path, so no icon replacement can
/// leak a GDI object (R007).
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on an STA thread (<see cref="StaFactAttribute"/>): WPF image objects require it.
/// </para>
/// <para>
/// <b>Two kinds of evidence, deliberately.</b> The seam counters
/// (<see cref="FakeShellApi.DeletedObjects"/>) prove the release calls were made - a count cannot
/// be satisfied by accident the way a resource delta can - and the real
/// <see cref="GdiHandles.Count"/> measurement proves the real GDI objects really do come back to
/// their starting state when the real <see cref="ShellApi"/> is used. Neither alone is enough:
/// the fake never touches GDI, and the counter cannot attribute a delta.
/// </para>
/// <para>
/// <b>The seam is the only owner of the bitmaps.</b> Because <c>CreateDIBSection</c> is on the
/// seam, a fake-driven conversion allocates no real GDI object at all, which is why the
/// handle-count assertion in <see cref="No_GDI_handle_leak_across_50_conversions"/> stays
/// meaningful: it can only stay flat if nothing bypassed the seam, while the release calls are
/// still counted exactly.
/// </para>
/// </remarks>
public sealed class HiconFactoryTests
{
    /// <summary>The icon edge length used by most tests.</summary>
    private const int IconSize = 32;

    /// <summary>
    /// Pins the sizes and offsets of the bitmap headers, because GDI reads them as raw memory and
    /// a wrong value is not a compile error.
    /// </summary>
    /// <remarks>
    /// The 124 bytes are the whole point of the version-5 header: the 36-byte
    /// <c>CIEXYZTRIPLE</c> block in the middle is what a hand-written struct usually gets wrong,
    /// and forgetting it shifts every following field while leaving the first five fields - the
    /// ones a reader normally checks - perfectly correct.
    /// </remarks>
    [Fact]
    public void Bitmap_headers_have_the_sizes_and_offsets_GDI_reads()
    {
        Assert.Equal(124, Marshal.SizeOf<BITMAPV5HEADER>());
        Assert.Equal(40, Marshal.SizeOf<BITMAPINFOHEADER>());
        Assert.Equal(48, Marshal.SizeOf<BITMAPINFO>());

        Assert.Equal(0, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Size)));
        Assert.Equal(4, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Width)));
        Assert.Equal(8, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Height)));
        Assert.Equal(12, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Planes)));
        Assert.Equal(14, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5BitCount)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Compression)));
        Assert.Equal(20, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5SizeImage)));
        Assert.Equal(40, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5RedMask)));
        Assert.Equal(52, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5AlphaMask)));
        Assert.Equal(56, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5CSType)));
        Assert.Equal(60, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Endpoints)));
        Assert.Equal(96, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5GammaRed)));
        Assert.Equal(112, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5ProfileData)));
        Assert.Equal(120, (int)Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Reserved)));

        // The version-3 header must be the version-5 header's first forty bytes field for field,
        // because both are handed to the same Win32 function.
        Assert.Equal(
            Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Size)),
            Marshal.OffsetOf<BITMAPINFOHEADER>(nameof(BITMAPINFOHEADER.biSize)));
        Assert.Equal(
            Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5Width)),
            Marshal.OffsetOf<BITMAPINFOHEADER>(nameof(BITMAPINFOHEADER.biWidth)));
        Assert.Equal(
            Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5SizeImage)),
            Marshal.OffsetOf<BITMAPINFOHEADER>(nameof(BITMAPINFOHEADER.biSizeImage)));
        Assert.Equal(
            Marshal.OffsetOf<BITMAPV5HEADER>(nameof(BITMAPV5HEADER.bV5ClrImportant)),
            Marshal.OffsetOf<BITMAPINFOHEADER>(nameof(BITMAPINFOHEADER.biClrImportant)));

        // The two colour-table entries have to sit immediately after the version-3 header: they
        // are the bytes GDI reads for a 1bpp bitmap, and they must be inside the structure.
        Assert.Equal(40, (int)Marshal.OffsetOf<BITMAPINFO>(nameof(BITMAPINFO.bmiColors0)));
        Assert.Equal(44, (int)Marshal.OffsetOf<BITMAPINFO>(nameof(BITMAPINFO.bmiColors1)));
    }

    /// <summary>
    /// The plain success path: a valid <c>Bgra32</c> source produces an icon, the factory hands
    /// that icon's ownership to the caller, and both temporary bitmaps are released.
    /// </summary>
    [StaFact]
    public void Valid_Bgra32_source_produces_a_nonzero_icon_and_releases_both_bitmaps()
    {
        var fake = new FakeShellApi();
        IntPtr icon = IntPtr.Zero;

        try
        {
            icon = HiconFactory.CreateIcon(fake, CreateSolid(IconSize, 0x40, 0x80, 0xC0, 0xFF), IconSize);

            Assert.NotEqual(IntPtr.Zero, icon);

            // Ownership: the factory never destroys the icon it returns - that belongs to TrayIcon
            // under the retain-and-destroy rule, and destroying it here would be a double free.
            Assert.Equal(0, fake.DestroyedIcons);

            // The colour bitmap and the mask were both created and both released.
            Assert.Equal(2, fake.DibSectionRequests.Count);
            Assert.Equal(1, fake.CreatedIcons);
            Assert.Equal(2, fake.DeletedObjects);

            ShellCall indirect = fake.Calls.Last(call => call.Operation == nameof(IShellApi.CreateIconIndirect));

            // fIcon = true; a cursor (0) would produce an HICON-shaped handle the shell rejects.
            Assert.Equal(1u, indirect.Flags);
        }
        finally
        {
            if (icon != IntPtr.Zero)
            {
                fake.DestroyIcon(icon);
            }
        }
    }

    /// <summary>
    /// Premultiplied (<c>Pbgra32</c>) input must come out as straight alpha, not as a byte
    /// passthrough.
    /// </summary>
    /// <remarks>
    /// The two buffers differ in exactly the way the bug would: a mid-blue at half alpha is stored
    /// as <c>B = 128</c> when premultiplied and <c>B = 255</c> when straight. Copying the bytes
    /// through would leave the icon's blue channel at half intensity - the dark halo. The
    /// assertion is on <see cref="HiconFactory.GetBgraPixels"/>'s real buffer, so it measures the
    /// conversion the way the DIB section receives it rather than at a second remove.
    /// </remarks>
    [StaFact]
    public void Premultiplied_source_is_converted_to_straight_alpha()
    {
        const byte halfAlpha = 128;

        // Premultiplied: the stored blue is already scaled by alpha.
        BitmapSource premultiplied = CreateSolid(IconSize, b: 128, g: 0, r: 0, a: halfAlpha, format: PixelFormats.Pbgra32);

        // Straight: the same colour and alpha, stored unscaled.
        BitmapSource straight = CreateSolid(IconSize, b: 255, g: 0, r: 0, a: halfAlpha, format: PixelFormats.Bgra32);

        byte[] fromPremultiplied = HiconFactory.GetBgraPixels(premultiplied, IconSize);
        byte[] fromStraight = HiconFactory.GetBgraPixels(straight, IconSize);

        // The input really was premultiplied - otherwise this test would prove nothing.
        Assert.Equal(128, (int)GetStoredBlue(premultiplied));

        // Alpha survives both conversions.
        Assert.Equal(halfAlpha, fromPremultiplied[3]);
        Assert.Equal(halfAlpha, fromStraight[3]);

        // Blue is unpremultiplied back to the straight value: a passthrough would have left 128.
        Assert.True(
            fromPremultiplied[0] >= 250,
            $"the premultiplied blue channel should be unpremultiplied to ~255 but was {fromPremultiplied[0]}");

        Assert.True(
            Math.Abs(fromPremultiplied[0] - fromStraight[0]) <= 4,
            $"premultiplied {fromPremultiplied[0]} and straight {fromStraight[0]} blue channels must agree after conversion");

        // And both shapes convert into a real icon.
        var fake = new FakeShellApi();
        IntPtr fromPremultipliedIcon = IntPtr.Zero;
        IntPtr fromStraightIcon = IntPtr.Zero;

        try
        {
            fromPremultipliedIcon = HiconFactory.CreateIcon(fake, premultiplied, IconSize);
            fromStraightIcon = HiconFactory.CreateIcon(fake, straight, IconSize);

            Assert.NotEqual(IntPtr.Zero, fromPremultipliedIcon);
            Assert.NotEqual(IntPtr.Zero, fromStraightIcon);
            Assert.Equal(2, fake.CreatedIcons);
            Assert.Equal(4, fake.DeletedObjects);
        }
        finally
        {
            if (fromPremultipliedIcon != IntPtr.Zero)
            {
                fake.DestroyIcon(fromPremultipliedIcon);
            }

            if (fromStraightIcon != IntPtr.Zero)
            {
                fake.DestroyIcon(fromStraightIcon);
            }
        }
    }

    /// <summary>
    /// An <see cref="ImageSource"/> WPF refuses to read on this thread reaches the caller as a
    /// named <see cref="TrayIconException"/>, not as an unnamed failure from inside WPF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The concrete, reproducible input is a <see cref="WriteableBitmap"/> that was created on
    /// another dispatcher and then locked, so it cannot be frozen either: WPF will not read it
    /// from a different thread, and the caller gets the named failure that says to freeze the
    /// image where it is created (R015).
    /// </para>
    /// <para>
    /// The plan suggested a <c>DrawingImage</c> with no raster bounds or an <c>Indexed8</c> bitmap
    /// built with an all-zero palette as alternative "unsupported" inputs. Both were tried and
    /// rejected as inputs: a <c>DrawingImage</c> is <em>supported</em> (it is the shape the sample
    /// application uses), and an indexed source with a valid palette converts to Bgra32 without
    /// complaint - the palette assertion fails before a bitmap can even be constructed with a
    /// zero-entry one. The affinity case is the real refusal to demonstrate, and WPF read errors
    /// of any other kind are covered by the wrapping in the implementation rather than invented
    /// here.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Unsupported_source_throws_TrayIconException_with_ConvertIcon_operation()
    {
        WriteableBitmap? foreign = null;
        Dispatcher? foreignDispatcher = null;
        bool created = false;

        using var imageCreated = new ManualResetEventSlim(false);

        // The helper thread pumps messages (Dispatcher.Run) because a WriteableBitmap's writes are
        // deferred to the render thread, which needs a running dispatcher.
        var thread = new Thread(() =>
        {
            foreignDispatcher = Dispatcher.CurrentDispatcher;

            var bitmap = new WriteableBitmap(CreateSolid(IconSize, 0x10, 0x20, 0x30, 0xFF));
            bitmap.Lock();
            foreign = bitmap;

            imageCreated.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            created = imageCreated.Wait(TimeSpan.FromSeconds(30));

            Assert.True(created, "the helper thread did not finish creating the image");
            Assert.NotNull(foreign);
            Assert.NotSame(Dispatcher.CurrentDispatcher, foreign!.Dispatcher);
            Assert.False(
                foreign.CanFreeze,
                "a locked WriteableBitmap must not be freezable, otherwise this test does not exercise the branch it names");

            var fake = new FakeShellApi();

            TrayIconException ex = Assert.Throws<TrayIconException>(
                () => { HiconFactory.CreateIcon(fake, foreign, IconSize); });

            Assert.Equal(TrayIconException.OperationConvertIcon, ex.Operation);

            // No Win32 call was made, so there is no error code to report - and 0 must not be
            // read as success.
            Assert.Equal(0, ex.Win32ErrorCode);

            // The message names the source and what to do about it. This is the reason the
            // dispatcher is read before the frozen state in the implementation: asking a
            // foreign-thread image whether it is frozen throws, so the obvious order never
            // reaches this message.
            Assert.Contains(nameof(WriteableBitmap), ex.Message, StringComparison.Ordinal);
            Assert.Contains("dispatcher", ex.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Empty(fake.DibSectionRequests);
            Assert.Equal(0, fake.DeletedObjects);
            Assert.Equal(0, fake.CreatedIcons);
        }
        finally
        {
            if (created && foreignDispatcher is not null && foreign is not null)
            {
                WriteableBitmap owned = foreign;
                foreignDispatcher.Invoke(owned.Unlock);
            }

            foreignDispatcher?.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// Fifty conversions with fifty icon destructions leave the real process GDI count flat, and
    /// the seam saw exactly two bitmap releases per conversion.
    /// </summary>
    /// <remarks>
    /// A leaked <c>HBITMAP</c> pair per conversion would show a delta of about a hundred, so the
    /// assertion is decisive rather than a tolerance. The release count is the stronger of the two
    /// assertions: it cannot be satisfied by a delta that happened to cancel out.
    /// </remarks>
    [StaFact]
    public void No_GDI_handle_leak_across_50_conversions()
    {
        const int conversions = 50;

        var fake = new FakeShellApi();
        BitmapSource source = CreateSolid(IconSize, 0x20, 0x60, 0xA0, 0xFF);

        int before = GdiHandles.Count();

        for (int i = 0; i < conversions; i++)
        {
            IntPtr icon = HiconFactory.CreateIcon(fake, source, IconSize);

            Assert.NotEqual(IntPtr.Zero, icon);
            fake.DestroyIcon(icon);
        }

        int after = GdiHandles.Count();

        Assert.InRange(after - before, 0, 2);
        Assert.Equal(conversions, fake.CreatedIcons);
        Assert.Equal(conversions, fake.DestroyedIcons);

        // Exactly two HBITMAPs released per conversion - the direct evidence that the finally
        // ran, and that it ran once per bitmap rather than twice or not at all.
        Assert.Equal(conversions * 2, fake.DeletedObjects);
        Assert.Equal(0, fake.OutstandingIcons);
    }

    /// <summary>
    /// When <c>CreateIconIndirect</c> fails, the failure is named and carries the Win32 code, and
    /// both bitmaps are still released.
    /// </summary>
    /// <remarks>
    /// This is the failure path that a "return early on error" implementation would leak two GDI
    /// objects from - every time an icon replacement failed, not once.
    /// </remarks>
    [StaFact]
    public void Failed_CreateIconIndirect_reports_the_operation_and_leaks_nothing()
    {
        const int expectedError = 5;

        var fake = new FakeShellApi { LastErrorToReport = expectedError };
        fake.FailNext(ShellOperation.CreateIconIndirect);

        TrayIconException ex = Assert.Throws<TrayIconException>(
            () => { HiconFactory.CreateIcon(fake, CreateSolid(IconSize, 1, 2, 3, 0xFF), IconSize); });

        Assert.Equal(TrayIconException.OperationConvertIcon, ex.Operation);
        Assert.Equal(expectedError, ex.Win32ErrorCode);

        Assert.Equal(0, fake.CreatedIcons);

        // Both bitmaps were created and both were released, on the failure path.
        Assert.Equal(2, fake.DibSectionRequests.Count);
        Assert.Equal(2, fake.DeletedObjects);
        Assert.Contains("CreateIconIndirect", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// When the mask bitmap cannot be created, the colour bitmap that already exists is still
    /// released.
    /// </summary>
    /// <remarks>
    /// The asymmetric case: only the second of the two allocations fails. An implementation that
    /// puts its cleanup after the last allocation - or that returns on the first error without a
    /// <c>finally</c> - leaks the colour bitmap here and nowhere else.
    /// </remarks>
    [StaFact]
    public void Failed_mask_bitmap_creation_still_releases_the_colour_bitmap()
    {
        const int expectedError = 87;

        var fake = new FakeShellApi { LastErrorToReport = expectedError };
        fake.FailCallNumber(ShellOperation.CreateDIBSection, 2);

        TrayIconException ex = Assert.Throws<TrayIconException>(
            () => { HiconFactory.CreateIcon(fake, CreateSolid(IconSize, 4, 5, 6, 0xFF), IconSize); });

        Assert.Equal(TrayIconException.OperationConvertIcon, ex.Operation);
        Assert.Equal(expectedError, ex.Win32ErrorCode);

        // The colour bitmap was created, the mask was not, and exactly one release happened.
        Assert.Single(fake.DibSectionRequests);
        Assert.Equal(32, fake.DibSectionRequests[0].BitCount);
        Assert.Equal(1, fake.DeletedObjects);
        Assert.Equal(0, fake.CreatedIcons);
    }

    /// <summary>
    /// When the very first bitmap cannot be created, nothing is released - because nothing was
    /// allocated - and the Win32 code still reaches the caller.
    /// </summary>
    /// <remarks>
    /// The counterpart of the asymmetric case: a cleanup that does not check for
    /// <see cref="IntPtr.Zero"/> would call <c>DeleteObject(0)</c> here, which is a no-op in Win32
    /// but would show up as a bogus release in the seam's count and hide a real double-release
    /// elsewhere.
    /// </remarks>
    [StaFact]
    public void Failed_colour_bitmap_creation_releases_nothing()
    {
        const int expectedError = 87;

        var fake = new FakeShellApi { LastErrorToReport = expectedError };
        fake.FailNext(ShellOperation.CreateDIBSection);

        TrayIconException ex = Assert.Throws<TrayIconException>(
            () => { HiconFactory.CreateIcon(fake, CreateSolid(IconSize, 7, 8, 9, 0xFF), IconSize); });

        Assert.Equal(TrayIconException.OperationConvertIcon, ex.Operation);
        Assert.Equal(expectedError, ex.Win32ErrorCode);

        Assert.Empty(fake.DibSectionRequests);
        Assert.Equal(0, fake.DeletedObjects);
        Assert.Contains("colour bitmap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pins the memory layout of both bitmaps: top-down (negative height), 32bpp colour, 1bpp
    /// mask with the two-byte-rounded row stride, and the mask bits derived from the alpha
    /// channel.
    /// </summary>
    /// <remarks>
    /// The width is 17 on purpose. <c>17 / 8</c> is 2, while a monochrome DIB row needs
    /// <c>((17 + 15) / 16) * 2 = 4</c> bytes: a buffer sized with the naive stride skews every row
    /// after the first, and the icon comes out as diagonal stripes with no error anywhere. The
    /// assertion is on the released buffer, which is the only place the written mask is visible.
    /// </remarks>
    [StaFact]
    public void Mask_uses_the_dib_row_stride_and_marks_only_fully_transparent_pixels()
    {
        const int size = 17;

        var fake = new FakeShellApi();

        var pixelData = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixelData[(i * 4) + 0] = 10;
            pixelData[(i * 4) + 1] = 20;
            pixelData[(i * 4) + 2] = 30;
            pixelData[(i * 4) + 3] = 255;
        }

        // Exactly one fully transparent pixel, at the top-left corner.
        pixelData[3] = 0;

        BitmapSource source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixelData, size * 4);

        IntPtr icon = IntPtr.Zero;

        try
        {
            icon = HiconFactory.CreateIcon(fake, source, size);

            Assert.Equal(2, fake.DibSectionRequests.Count);

            DibSectionRequest color = fake.DibSectionRequests[0];
            Assert.Equal(32, color.BitCount);
            Assert.Equal(size, color.Width);
            Assert.Equal(-size, color.Height);
            Assert.Equal(size * 4, color.RowStride);
            Assert.Equal(0u, color.Usage);

            DibSectionRequest mask = fake.DibSectionRequests[1];
            Assert.Equal(1, mask.BitCount);
            Assert.Equal(size, mask.Width);
            Assert.Equal(-size, mask.Height);
            Assert.Equal(4, mask.RowStride);
            Assert.NotEqual(size / 8, mask.RowStride);
            Assert.Equal(0u, mask.Usage);

            Assert.NotNull(color.ReleasedContent);
            byte[] colorBytes = color.ReleasedContent!;
            Assert.Equal(size * 4 * size, colorBytes.Length);

            // The pixel bytes really were copied into the DIB, in BGRA order: the top-left pixel
            // is the transparent one, its right-hand neighbour is the opaque source colour.
            Assert.InRange((int)colorBytes[0], 8, 12);
            Assert.InRange((int)colorBytes[1], 18, 22);
            Assert.InRange((int)colorBytes[2], 28, 32);
            Assert.Equal(0, (int)colorBytes[3]);
            Assert.InRange((int)colorBytes[4], 8, 12);
            Assert.InRange((int)colorBytes[5], 18, 22);
            Assert.InRange((int)colorBytes[6], 28, 32);
            Assert.Equal(255, (int)colorBytes[7]);

            Assert.NotNull(mask.ReleasedContent);
            byte[] maskBytes = mask.ReleasedContent!;
            Assert.Equal(4 * size, maskBytes.Length);

            // Only the transparent pixel is masked out, and it is the high bit of the first byte.
            Assert.Equal(0x80, (int)maskBytes[0]);
            Assert.Equal(0, (int)maskBytes[1]);

            for (int i = 4; i < maskBytes.Length; i++)
            {
                Assert.Equal(0, (int)maskBytes[i]);
            }
        }
        finally
        {
            if (icon != IntPtr.Zero)
            {
                fake.DestroyIcon(icon);
            }
        }
    }

    /// <summary>
    /// The same conversion against the real <see cref="ShellApi"/>: a real DIB section pair, a
    /// real icon, and a GDI count that returns to where it started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the measurement R007 actually rests on. The fake-driven tests above assert the
    /// release calls were made; this one asserts that when the release calls are real GDI calls,
    /// the real process GDI count comes back. It also exercises the real
    /// <c>CreateDIBSection</c> declaration - entry point, calling convention, the
    /// <c>out</c> pixel pointer - which no fake-driven test can.
    /// </para>
    /// <para>
    /// Nothing here touches the notification area: no <c>Shell_NotifyIcon</c> call is made, so no
    /// icon appears in the developer's tray (that proof is the UAT checklist's, per D009).
    /// </para>
    /// </remarks>
    [StaFact]
    public void Real_shell_conversion_leaves_the_GDI_count_flat()
    {
        const int conversions = 20;

        var shell = new ShellApi();
        BitmapSource source = CreateSolid(IconSize, 0x30, 0x70, 0xB0, 0xFF);

        // One warm-up conversion so lazily created process-wide GDI objects are not counted as a
        // leak by the baseline.
        IntPtr warmup = HiconFactory.CreateIcon(shell, source, IconSize);
        Assert.NotEqual(IntPtr.Zero, warmup);
        Assert.True(shell.DestroyIcon(warmup));

        int before = GdiHandles.Count();

        for (int i = 0; i < conversions; i++)
        {
            IntPtr icon = HiconFactory.CreateIcon(shell, source, IconSize);

            Assert.NotEqual(IntPtr.Zero, icon);
            Assert.True(shell.DestroyIcon(icon));
        }

        int after = GdiHandles.Count();

        // A leaked HBITMAP pair per conversion would show a delta near forty here.
        Assert.InRange(after - before, 0, 2);
    }

    /// <summary>
    /// The caller contract is enforced: a null image and a non-positive size are rejected as the
    /// programmer errors they are, not silently converted into an icon.
    /// </summary>
    [StaFact]
    public void Null_source_and_non_positive_size_are_rejected()
    {
        var fake = new FakeShellApi();
        BitmapSource source = CreateSolid(IconSize, 1, 1, 1, 0xFF);

        Assert.Throws<ArgumentNullException>(() => { HiconFactory.CreateIcon(fake, null!, IconSize); });
        Assert.Throws<ArgumentNullException>(() => { HiconFactory.CreateIcon(null!, source, IconSize); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { HiconFactory.CreateIcon(fake, source, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { HiconFactory.CreateIcon(fake, source, -16); });

        // Nothing was created, nothing was released, and no partial icon was left behind.
        Assert.Empty(fake.DibSectionRequests);
        Assert.Equal(0, fake.DeletedObjects);
    }

    /// <summary>
    /// Builds a square, single-colour image in the requested pixel format.
    /// </summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <param name="b">The blue channel value, as stored (premultiplied when the format says so).</param>
    /// <param name="g">The green channel value, as stored.</param>
    /// <param name="r">The red channel value, as stored.</param>
    /// <param name="a">The alpha channel value.</param>
    /// <param name="format">The pixel format; <c>Bgra32</c> when omitted.</param>
    /// <returns>The image.</returns>
    /// <remarks>
    /// <c>BitmapSource.Create</c> rather than a <see cref="WriteableBitmap"/>, and that is
    /// not a style preference: a <c>WriteableBitmap</c>'s writes are deferred to the render
    /// thread, so on a thread that is not pumping messages (a test thread) <c>CopyPixels</c>
    /// reads an empty bitmap and then blocks waiting for an update that never arrives. A
    /// <c>BitmapSource</c> created from a byte array holds the pixels immediately and can be read
    /// synchronously.
    /// </remarks>
    private static BitmapSource CreateSolid(int size, byte b, byte g, byte r, byte a, PixelFormat? format = null)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = a;
        }

        return BitmapSource.Create(size, size, 96, 96, format ?? PixelFormats.Bgra32, null, pixels, size * 4);
    }

    /// <summary>
    /// Reads the stored blue channel of a bitmap's first pixel, without going through the
    /// conversion under test.
    /// </summary>
    /// <param name="bitmap">The bitmap to read.</param>
    /// <returns>The stored blue value.</returns>
    private static byte GetStoredBlue(BitmapSource bitmap)
    {
        var pixel = new byte[bitmap.Format.BitsPerPixel / 8];
        bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, pixel.Length, 0);
        return pixel[0];
    }
}
