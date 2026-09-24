using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S04 image half of R017 at the unit level: <c>ToastImageFile</c> persists a WPF
/// <see cref="ImageSource"/> as a PNG in the library's own temp folder, hands over an absolute
/// <c>file:///</c> reference and owns the file until it is deleted.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this suite can and cannot prove.</b> These tests prove the encoder output (a PNG whose
/// pixels and extent match the source), the reference spelling (an escaped absolute <c>file:///</c>
/// URI that decodes back to the exact path), the ownership rule (the file exists after
/// <c>Create</c> and <c>Delete</c> removes it, idempotently), and the translated failure (an
/// unencodable source becomes a named <see cref="ToastException"/>, not a
/// <see cref="TrayIconException"/> or a raw WPF exception, and leaves no file behind). What it
/// cannot prove is that a rendering shell paints the picture - that is a live reading
/// (<c>docs/UAT-S04.md</c>), not a unit fact.
/// </para>
/// <para>
/// <b>Only <c>BitmapSource</c> inputs, deliberately (MEM040).</b> A non-bitmap source goes through
/// WPF's compositor, which needs a pumping dispatcher and hands back a blank bitmap on a bare test
/// thread; that path is exercised live by the windowless sample instead of being faked here. The
/// lone non-bitmap input below is a double that fails, which needs no dispatcher to be interesting.
/// </para>
/// <para>
/// <b>Every temp file is created inside a per-test folder and removed in a <c>finally</c></b>, so a
/// failing assertion cannot leave the library's artefact behind, and a passing test leaves no
/// directory in the user's temp path.
/// </para>
/// </remarks>
public sealed class ToastImageFileTests
{
    /// <summary>
    /// A created PNG decodes back with the source's extent and its first pixel's exact channels, so
    /// the bytes on disk are the picture the consumer asked for.
    /// </summary>
    /// <remarks>
    /// The comparison is byte-for-byte on a straight-alpha <c>Bgra32</c> source, which is the format
    /// the PNG container stores natively: an encoder that reordered channels, dropped alpha or
    /// resampled would move at least one of these four values.
    /// </remarks>
    [StaFact]
    public void Create_writes_a_png_that_decodes_to_the_source_pixels()
    {
        const int size = 4;
        BitmapSource source = CreateSolid(size, b: 12, g: 34, r: 56, a: 200);
        string folder = CreateTempFolder();

        try
        {
            ToastImageFile file = ToastImageFile.Create(source, folder);

            Assert.Equal(size, file.PixelWidth);
            Assert.Equal(size, file.PixelHeight);
            Assert.True(File.Exists(file.Path), "the PNG must exist as soon as Create returns");

            using FileStream read = File.OpenRead(file.Path);
            BitmapFrame decoded = BitmapFrame.Create(read, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            Assert.Equal(size, decoded.PixelWidth);
            Assert.Equal(size, decoded.PixelHeight);

            byte[] firstPixel = new byte[4];
            decoded.CopyPixels(new Int32Rect(0, 0, 1, 1), firstPixel, 4, 0);

            Assert.Equal(12, firstPixel[0]);
            Assert.Equal(34, firstPixel[1]);
            Assert.Equal(56, firstPixel[2]);
            Assert.Equal(200, firstPixel[3]);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// The reference is an absolute <c>file:///</c> URI that decodes back to the exact path, with no
    /// raw space even though the folder's name contains one (and a non-ASCII character).
    /// </summary>
    /// <remarks>
    /// The contrast assertion is the point of the test: <c>"file:///" + path</c> - the spelling this
    /// class must not use - does contain the raw space, because the payload builder never
    /// URL-encodes an attribute value. The fixture therefore cannot pass vacuously.
    /// </remarks>
    [StaFact]
    public void Reference_is_an_escaped_absolute_file_uri_for_a_folder_with_a_space()
    {
        string folder = CreateTempFolder();

        try
        {
            ToastImageFile file = ToastImageFile.Create(CreateSolid(2, 1, 2, 3, 255), folder);

            Assert.Contains(' ', file.Path);
            Assert.Equal(new Uri(file.Path, UriKind.Absolute).AbsoluteUri, file.Reference);
            Assert.StartsWith("file:///", file.Reference, StringComparison.Ordinal);
            Assert.DoesNotContain(' ', file.Reference);
            Assert.Equal(file.Path, new Uri(file.Reference).LocalPath);

            // The spelling this class must not use, pinned as the reason the assertion above is not
            // vacuous: the naive concatenation keeps the raw space in the URI.
            Assert.Contains(' ', "file:///" + file.Path);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// The file exists after <c>Create</c>, <c>Delete</c> removes it, and calling <c>Delete</c>
    /// again - or after something else removed the file - is a no-op rather than an error.
    /// </summary>
    /// <remarks>
    /// The idempotence matters beyond tidiness: <c>ToastShow.DeleteImageFile</c> - the show's single
    /// unwind point, reached from both its failure path and its disposal - calls <c>Delete</c> without
    /// tracking whether a delete already ran, and the shell may still hold the file open when the
    /// unwind happens.
    /// </remarks>
    [StaFact]
    public void File_exists_after_create_and_delete_removes_it_idempotently()
    {
        string folder = CreateTempFolder();

        try
        {
            ToastImageFile file = ToastImageFile.Create(CreateSolid(2, 9, 9, 9, 255), folder);

            Assert.True(File.Exists(file.Path));

            file.Delete();

            Assert.False(File.Exists(file.Path));

            // Second delete: nothing to do, and still nothing thrown.
            file.Delete();

            Assert.False(File.Exists(file.Path));

            // Recreated and removed by something else first: Delete must tolerate the missing file.
            ToastImageFile recreated = ToastImageFile.Create(CreateSolid(2, 9, 9, 9, 255), folder);

            File.Delete(recreated.Path);
            recreated.Delete();

            Assert.False(File.Exists(recreated.Path));
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// A source that cannot be encoded fails with <see cref="ToastException"/> carrying
    /// <see cref="ToastException.OperationImageResolution"/>, is not a
    /// <see cref="TrayIconException"/>, names the source type in its message, and leaves no file
    /// behind.
    /// </summary>
    /// <remarks>
    /// This is the translation contract the toast path depends on: a raw WPF exception escaping
    /// here would reach a consumer as an unnamed failure of the wrong type, and a half-written PNG
    /// left in the library's folder would be an artefact the library owns but never cleans.
    /// </remarks>
    [StaFact]
    public void A_source_that_cannot_be_encoded_fails_with_the_named_resolution_operation()
    {
        string folder = CreateTempFolder();

        try
        {
            var source = new ThrowingBitmapSource();

            ToastException exception = Assert.Throws<ToastException>(() => ToastImageFile.Create(source, folder));

            Assert.Equal(ToastException.OperationImageResolution, exception.Operation);
            Assert.IsNotType<TrayIconException>(exception);
            Assert.Contains(nameof(ThrowingBitmapSource), exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(folder, "toast-*.png"));
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// A <see cref="ToastException"/> raised by the encode step is cleaned up and rethrown unchanged:
    /// the half-written file is gone and the caller receives the same instance, not a translation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the branch the seam exists for (the three-argument
    /// <c>ToastImageFile.Create(source, folder, encode)</c> overload):
    /// the guard must remove the file for <em>every</em> exception type, including the library's own,
    /// and a failure a helper already reported in the library's vocabulary must keep its operation,
    /// code and detail. <c>Assert.Same</c> is the proof that no second <see cref="ToastException"/> was
    /// constructed - a translation would be a different instance.
    /// </para>
    /// <para>
    /// The throwaway PNG the seam writes is what makes the cleanup assertion meaningful: the file
    /// really exists when the exception is raised, so "no <c>toast-*.png</c> left behind" is a reading
    /// rather than a vacuous one.
    /// </para>
    /// </remarks>
    [StaFact]
    public void A_toast_exception_from_the_encode_step_is_cleaned_up_and_rethrown_unchanged()
    {
        string folder = CreateTempFolder();

        try
        {
            ImageSource source = CreateSolid(4, 12, 34, 56, 200);
            var raised = new ToastException(
                ToastException.OperationImageResolution,
                unchecked((int)0x8000FFFF),
                "Simulated: an internal helper reported the library's own failure type.");

            ToastException caught = Assert.Throws<ToastException>(() => ToastImageFile.Create(
                source,
                folder,
                (_, path) =>
                {
                    // The half-written file the guard has to remove: the encode step wrote bytes and
                    // then failed.
                    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

                    throw raised;
                }));

            Assert.Same(raised, caught);
            Assert.Equal(ToastException.OperationImageResolution, caught.Operation);
            Assert.Equal(unchecked((int)0x8000FFFF), caught.ErrorCode);
            Assert.IsNotType<TrayIconException>(caught);
            Assert.Empty(Directory.GetFiles(folder, "toast-*.png"));
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// <see cref="ToastImageFile.Adopt"/> takes over a file that already exists, derives the same
    /// absolute <c>file:///</c> reference <c>Create</c> would, and removes the file through the same
    /// <c>Delete</c> - which is how a payload built from a path alone reaches the single delete path.
    /// </summary>
    /// <remarks>
    /// The extent assertion is the honest half of adoption: nothing measured the file, so
    /// <c>PixelWidth</c> and <c>PixelHeight</c> read <c>0</c> ("not measured") rather than a
    /// fabricated size. The reference is still built from the path, so an adopted file in a folder
    /// with a space in its name is escaped exactly like an encoded one.
    /// </remarks>
    [StaFact]
    public void Adopt_takes_ownership_of_an_existing_file_and_deletes_it_through_the_same_path()
    {
        string folder = CreateTempFolder();

        try
        {
            string path = System.IO.Path.Combine(folder, "toast-" + Guid.NewGuid().ToString("N") + ".png");

            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

            ToastImageFile adopted = ToastImageFile.Adopt(path);

            Assert.Equal(path, adopted.Path);
            Assert.Contains(' ', adopted.Path);
            Assert.Equal(new Uri(path, UriKind.Absolute).AbsoluteUri, adopted.Reference);
            Assert.Equal(0, adopted.PixelWidth);
            Assert.Equal(0, adopted.PixelHeight);
            Assert.True(File.Exists(path));

            adopted.Delete();

            Assert.False(File.Exists(path), "the adopted owner must delete the file it was handed");

            // The same idempotence the created owner has: a second delete is a no-op.
            adopted.Delete();

            Assert.False(File.Exists(path));
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    /// <summary>
    /// Adoption refuses the two paths that cannot name a file, so a payload cannot carry an owner
    /// whose path is not one.
    /// </summary>
    [Fact]
    public void Adopt_refuses_a_null_or_empty_path()
    {
        Assert.Throws<ArgumentNullException>(() => ToastImageFile.Adopt(null!));
        Assert.Throws<ArgumentException>(() => ToastImageFile.Adopt(string.Empty));
    }

    /// <summary>
    /// A <see langword="null"/> source is a caller contract violation, not an encoding failure, so
    /// it is refused with the framework's own argument exception before any file is named.
    /// </summary>
    [Fact]
    public void Create_refuses_a_null_source()
    {
        Assert.Throws<ArgumentNullException>(() => ToastImageFile.Create(null!));
    }

    /// <summary>
    /// A freshly constructed <see cref="ToastImage"/> carries no source, so "no typed image" is the
    /// documented default and the string <see cref="ToastImage.Reference"/> path stays the
    /// untouched default of the type.
    /// </summary>
    [Fact]
    public void New_image_has_no_source()
    {
        Assert.Null(new ToastImage().Source);
    }

    /// <summary>
    /// Creates a folder under the user's temp directory whose name contains a space and a non-ASCII
    /// character, unique per call so two tests never share a directory.
    /// </summary>
    /// <returns>The absolute path of the created folder.</returns>
    private static string CreateTempFolder()
    {
        string folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Trustsoft NotifyIcon test \u00fc " + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    /// Removes a per-test folder and everything in it, ignoring a failure to do so.
    /// </summary>
    /// <param name="folder">The folder to remove.</param>
    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a cleanup failure must not mask the assertion result.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a cleanup failure must not mask the assertion result.
        }
    }

    /// <summary>
    /// Builds a solid straight-alpha <c>Bgra32</c> bitmap of <paramref name="size"/> x
    /// <paramref name="size"/>.
    /// </summary>
    /// <param name="size">The square edge length.</param>
    /// <param name="b">The blue channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="r">The red channel.</param>
    /// <param name="a">The alpha channel.</param>
    /// <returns>A frozen, readable bitmap.</returns>
    private static BitmapSource CreateSolid(int size, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = a;
        }

        BitmapSource bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);

        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        return bitmap;
    }

    /// <summary>
    /// A <see cref="BitmapSource"/> with a valid-looking extent whose pixels cannot be read, so the
    /// PNG encoder refuses to produce a frame from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both routes fail on purpose: the extent is describable (so the failure happens while the
    /// encoder is producing the frame, after the output file has been opened), and every pixel-copy
    /// overload throws, so a WPF internals change that reaches the pixels instead of the metadata
    /// cannot silently turn this double into a success.
    /// </para>
    /// <para>
    /// The measured failure on .NET 8 is an <see cref="InvalidOperationException"/> from
    /// <c>BitmapEncoder.SaveFrame</c> - the encoder cannot wrap a source that has no Windows Imaging
    /// Component backing - which is exactly the "any WPF encode failure" the resolver must
    /// translate.
    /// </para>
    /// </remarks>
    private sealed class ThrowingBitmapSource : BitmapSource
    {
        /// <inheritdoc/>
        public override int PixelWidth => 4;

        /// <inheritdoc/>
        public override int PixelHeight => 4;

        /// <inheritdoc/>
        public override double DpiX => 96;

        /// <inheritdoc/>
        public override double DpiY => 96;

        /// <inheritdoc/>
        public override PixelFormat Format => PixelFormats.Bgra32;

        /// <inheritdoc/>
        public override void CopyPixels(Int32Rect sourceRect, Array pixels, int stride, int offset) => throw CreateFailure();

        /// <inheritdoc/>
        public override void CopyPixels(Int32Rect sourceRect, IntPtr buffer, int bufferSize, int stride) => throw CreateFailure();

        /// <inheritdoc/>
        public override void CopyPixels(Array pixels, int stride, int offset) => throw CreateFailure();

        /// <inheritdoc/>
        protected override Freezable CreateInstanceCore() => new ThrowingBitmapSource();

        /// <summary>Builds the failure the pixel-read routes report.</summary>
        /// <returns>The exception every read of this source throws.</returns>
        private static InvalidOperationException CreateFailure() =>
            new("Simulated: this source's pixels cannot be read.");
    }
}
