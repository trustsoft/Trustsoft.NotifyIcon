using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The per-show half of the S04 image contract, over the recording seam and with no shell: the
/// notifier resolves <see cref="ToastImage.Source"/> into <see cref="ToastImageFile"/>, the payload
/// the show hands the shell names that resolved <c>file:///</c> reference, the show owns the temp
/// file from <c>Show</c> to teardown and deletes it in its single unwind path - on disposal, on a
/// failed show and never twice - and a source that cannot be encoded is a named, file-free failure
/// that never reaches the seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own collection, because it shares a folder.</b> These tests and
/// <c>ToastApiContractTests</c>' two temp-file tests all exercise the one fixed folder the library
/// writes to (<see cref="ToastImageFile.DefaultFolder"/>), so they are placed in the same xUnit
/// collection: classes in one collection run sequentially, which is what makes the folder listings
/// these tests compare meaningful rather than racy.
/// </para>
/// <para>
/// <b>Why the notifier and not the show path directly.</b> The resolution is a notifier-side step
/// (it happens between the registration and the payload construction), so these tests drive
/// <see cref="ToastNotifier.Show"/> over <see cref="FakeToastApi"/>: that is the only way to prove
/// the ordering claims - after registration, before the seam - and the file lifetime together.
/// </para>
/// <para>
/// <b>The file is found, not guessed.</b> The resolved path is read back out of the exact XML the
/// show handed to the seam (<c>xml.&lt;image src="..."&gt;</c>), decoded with
/// <c>new Uri(reference).LocalPath</c>, so the test proves the reference the shell received really
/// names the file that exists - rather than asserting on a path it constructed itself.
/// </para>
/// <para>
/// <b>Only <c>BitmapSource</c> inputs (MEM040).</b> A drawing source needs WPF's compositor and a
/// pumping dispatcher; the lone non-bitmap input below is a double that fails, which needs no
/// dispatcher to be interesting. The vector path is exercised live by the windowless sample.
/// </para>
/// <para>
/// <b>Cleanup.</b> Every test works against the library's real fixed temp folder
/// (<see cref="ToastImageFile.DefaultFolder"/>) - the production code path, not an injected folder -
/// so each test disposes the notifier and then removes anything left in a <c>finally</c>. The folder
/// itself is the library's and is never the test's to delete.
/// </para>
/// </remarks>
[Collection("Toast image temp folder")]
public sealed class ToastImageResolutionTests
{
    /// <summary>The identity override the fake reads back, so registration succeeds deterministically.</summary>
    private const string AppUserModelId = "Vendor.Toast.Image";

    /// <summary>The measured show half, in order, for a content with no tag, group or expiry.</summary>
    private static readonly string[] ShowHalfSequence =
    [
        nameof(IToastApi.GetToastNotificationManagerStatics),
        nameof(IToastApi.GetToastNotificationFactory),
        nameof(IToastApi.ActivateXmlDocument),
        nameof(IToastApi.CreateToastNotifier),
        nameof(IToastApi.GetNotifierSetting),
        nameof(IToastApi.LoadXml),
        nameof(IToastApi.CreateToastNotification),
        nameof(IToastApi.SubscribeActivated),
        nameof(IToastApi.SubscribeDismissed),
        nameof(IToastApi.SubscribeFailed),
        nameof(IToastApi.Show),
    ];

    /// <summary>The three unsubscribes of the teardown, in the measured order.</summary>
    private static readonly string[] UnsubscribeSequence =
    [
        nameof(IToastApi.UnsubscribeActivated),
        nameof(IToastApi.UnsubscribeDismissed),
        nameof(IToastApi.UnsubscribeFailed),
    ];

    /// <summary>The five seam members that hand a handle to the caller and must therefore be released.</summary>
    private const int HandleReturningOperations = 5;

    /// <summary>
    /// A content with a source shows an image element whose <c>src</c> is the resolved
    /// <c>file:///</c> reference, and the file the reference names exists while the show is live.
    /// </summary>
    [StaFact]
    public void A_content_with_a_source_shows_the_resolved_file_uri_and_the_file_exists_while_live()
    {
        var fake = RegisteredFake();
        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        string[] before = TempFiles();

        try
        {
            notifier.Show(ContentWithSource());

            string reference = ResolvedReferenceOf(fake);

            Assert.StartsWith("file:///", reference, StringComparison.Ordinal);

            string path = new Uri(reference).LocalPath;

            Assert.Equal(ToastImageFile.DefaultFolder, Path.GetDirectoryName(path));
            Assert.True(File.Exists(path), "the resolved PNG must exist while the show is live");
            Assert.Equal(1, fake.CallCount(ToastOperation.LoadXml));

            // Exactly one new file, and it is the one the reference named.
            Assert.Equal(new[] { path }, TempFiles().Except(before).ToArray());
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>
    /// Disposal deletes the temp file the show resolved, and the teardown still records exactly the
    /// measured eleven show operations plus three unsubscribes plus five releases: the delete is the
    /// library's own file handling and adds no seam call.
    /// </summary>
    [StaFact]
    public void Dispose_removes_the_temp_file_and_keeps_the_measured_eleven_plus_three_sequence()
    {
        var fake = RegisteredFake();
        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        string[] before = TempFiles();

        try
        {
            notifier.Show(ContentWithSource());

            string path = new Uri(ResolvedReferenceOf(fake)).LocalPath;

            Assert.True(File.Exists(path));

            int firstShowOperation = IndexOf(fake, nameof(IToastApi.GetToastNotificationManagerStatics));

            notifier.Dispose();

            Assert.False(File.Exists(path), "disposal must delete the temp file the show owns");

            int showStart = firstShowOperation;

            Assert.Equal(ShowHalfSequence, fake.Operations.Skip(showStart).Take(ShowHalfSequence.Length).ToArray());
            Assert.Equal(
                UnsubscribeSequence,
                fake.Operations.Skip(showStart + ShowHalfSequence.Length).Take(UnsubscribeSequence.Length).ToArray());
            Assert.Equal(
                Enumerable.Repeat(nameof(IToastApi.ReleaseHandle), HandleReturningOperations).ToArray(),
                fake.Operations
                    .Skip(showStart + ShowHalfSequence.Length + UnsubscribeSequence.Length)
                    .Take(HandleReturningOperations)
                    .ToArray());
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>
    /// A show that fails after the image was resolved deletes the file in the same unwind path a
    /// disposal uses, and the failure still arrives through <see cref="ToastNotifier.ToastError"/>.
    /// </summary>
    /// <remarks>
    /// The exactly-once half of "the show owns the file" is pinned where it can be stated sharply:
    /// <c>ToastApiContractTests.A_failed_show_deletes_the_temp_file_once_in_the_same_unwind_path</c>
    /// re-creates a file at the same path after the unwind and shows that the disposal which follows
    /// does not remove it, which the failure path here reaches through the notifier and no other.
    /// </remarks>
    [StaFact]
    public void A_scripted_show_failure_leaves_no_temp_file_behind_and_still_reports_through_ToastError()
    {
        var fake = RegisteredFake();
        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        var errors = new List<ToastErrorEventArgs>();

        notifier.ToastError += (_, args) => errors.Add(args);

        string[] before = TempFiles();

        try
        {
            // Register first, on a content with no image: the failing show is the one that resolves.
            notifier.Show(new ToastContent { Title = "registration only" });

            string[] beforeFailingShow = TempFiles();

            fake.FailNext(ToastOperation.LoadXml);
            notifier.Show(ContentWithSource());

            ToastErrorEventArgs error = Assert.Single(errors);

            Assert.Equal(nameof(IToastApi.LoadXml), error.Operation);
            Assert.Equal(FakeToastApi.DefaultFailureHResult, error.ErrorCode);

            // The resolved PNG is gone: the failed show unwound through the same single path a
            // disposal uses, before the notifier reported the failure.
            Assert.Empty(TempFiles().Except(beforeFailingShow));
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>
    /// A source that cannot be encoded throws the resolver's own named
    /// <see cref="ToastException"/>, records no seam call at all and leaves no file behind: the show
    /// path is never entered, exactly as for a refused title.
    /// </summary>
    [StaFact]
    public void A_source_that_cannot_be_encoded_throws_named_and_records_no_seam_call()
    {
        var fake = RegisteredFake();
        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        string[] before = TempFiles();

        try
        {
            notifier.Show(new ToastContent { Title = "registration only" });

            int seamCallsAfterRegistration = fake.Operations.Count;
            string[] beforeResolution = TempFiles();

            var content = new ToastContent
            {
                Title = "unencodable",
                Image = new ToastImage { Source = new ThrowingBitmapSource() },
            };

            ToastException exception = Assert.Throws<ToastException>(() => notifier.Show(content));

            Assert.Equal(ToastException.OperationImageResolution, exception.Operation);
            Assert.IsNotType<TrayIconException>(exception);
            Assert.Contains(nameof(ThrowingBitmapSource), exception.Message, StringComparison.Ordinal);
            Assert.Equal(seamCallsAfterRegistration, fake.Operations.Count);
            Assert.Empty(TempFiles().Except(beforeResolution));
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>
    /// The typed source wins over a pre-formed reference when both are set, and a
    /// <see cref="ToastImage.Reference"/>-only content still renders exactly the S02 string.
    /// </summary>
    [StaFact]
    public void A_source_wins_over_a_reference_and_a_reference_only_content_renders_the_s02_string()
    {
        var fake = RegisteredFake();
        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        string[] before = TempFiles();

        try
        {
            notifier.Show(new ToastContent
            {
                Title = "both",
                Image = new ToastImage
                {
                    Reference = "https://example.com/ignored.png",
                    Source = CreateSolid(4, 1, 2, 3, 255),
                },
            });

            string resolvedXml = XmlOf(fake);

            Assert.Contains("<image src=\"file:///", resolvedXml, StringComparison.Ordinal);
            Assert.DoesNotContain("example.com", resolvedXml, StringComparison.Ordinal);
            Assert.True(File.Exists(new Uri(ResolvedReferenceOf(fake)).LocalPath));

            notifier.Show(new ToastContent
            {
                Title = "Title",
                Image = new ToastImage { Reference = "file:///C:/images/logo.png" },
            });

            Assert.Equal(
                "<toast><visual><binding template=\"ToastGeneric\"><text>Title</text><image src=\"file:///C:/images/logo.png\" placement=\"appLogoOverride\"/></binding></visual></toast>",
                XmlOf(fake, index: 1));
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>
    /// A failed registration leaves no temp file, because resolution deliberately runs after the
    /// registration: the identity failure throws before any image is written.
    /// </summary>
    [StaFact]
    public void A_registration_failure_leaves_no_temp_file_because_resolution_runs_after_it()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = AppUserModelId };
        fake.FailNext(ToastOperation.SaveShortcut);

        var notifier = new ToastNotifier(fake) { AppUserModelId = AppUserModelId };
        string[] before = TempFiles();

        try
        {
            ToastException exception = Assert.Throws<ToastException>(() => notifier.Show(ContentWithSource()));

            Assert.Equal(nameof(IToastApi.SaveShortcut), exception.Operation);
            Assert.Empty(TempFiles().Except(before));
            Assert.DoesNotContain(nameof(IToastApi.LoadXml), fake.Operations);
        }
        finally
        {
            notifier.Dispose();
            RemoveTempFiles(before);
        }
    }

    /// <summary>Builds a fake that completes registration, so a show reaches the resolution step.</summary>
    /// <returns>The recording seam, configured so the identity read-back matches.</returns>
    private static FakeToastApi RegisteredFake() => new() { AppUserModelIdToReadBack = AppUserModelId };

    /// <summary>A content whose image is a real, encodable bitmap source.</summary>
    /// <returns>The content to show.</returns>
    private static ToastContent ContentWithSource() => new()
    {
        Title = "Image toast",
        Image = new ToastImage { Source = CreateSolid(4, 12, 34, 56, 200) },
    };

    /// <summary>
    /// Reads the <c>src</c> attribute of the image element out of the XML the show handed to the
    /// shell, so the reference under test is the one the shell actually received.
    /// </summary>
    /// <param name="fake">The recording fake.</param>
    /// <returns>The reference string from the payload's image element.</returns>
    private static string ResolvedReferenceOf(FakeToastApi fake)
    {
        string xml = XmlOf(fake);
        int start = xml.IndexOf("src=\"", StringComparison.Ordinal) + "src=\"".Length;
        int end = xml.IndexOf('"', start);

        return xml[start..end];
    }

    /// <summary>
    /// Reads the exact XML string the show handed to <c>LoadXml</c>.
    /// </summary>
    /// <param name="fake">The recording fake.</param>
    /// <param name="index">Which <c>LoadXml</c> call to read; the default is the first.</param>
    /// <returns>The XML, exactly as the payload rendered it.</returns>
    private static string XmlOf(FakeToastApi fake, int index = 0)
    {
        string detail = fake.Calls
            .Where(call => call.Operation == nameof(IToastApi.LoadXml))
            .ElementAt(index)
            .Detail;

        int start = detail.IndexOf("xml=\"", StringComparison.Ordinal) + "xml=\"".Length;

        return detail[start..^1];
    }

    /// <summary>
    /// Lists the library's own temp files, so a test can compare the folder before and after a show.
    /// </summary>
    /// <returns>The absolute paths of the <c>toast-*.png</c> files, or an empty array.</returns>
    private static string[] TempFiles() =>
        Directory.Exists(ToastImageFile.DefaultFolder)
            ? Directory.GetFiles(ToastImageFile.DefaultFolder, "toast-*.png")
            : [];

    /// <summary>
    /// Removes every temp file the library's folder holds that was not there before the test.
    /// </summary>
    /// <param name="before">The folder listing taken before the test ran.</param>
    private static void RemoveTempFiles(string[] before)
    {
        foreach (string file in TempFiles().Except(before))
        {
            try
            {
                File.Delete(file);
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
    }

    /// <summary>Finds the first recorded call to one operation.</summary>
    /// <param name="fake">The recording fake.</param>
    /// <param name="operation">The seam member name.</param>
    /// <returns>The index of the first matching call, or <c>-1</c> when it was never called.</returns>
    private static int IndexOf(FakeToastApi fake, string operation)
    {
        for (int i = 0; i < fake.Operations.Count; i++)
        {
            if (string.Equals(fake.Operations[i], operation, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
    /// The same safe throwing double T02's <c>ToastImageFileTests</c> uses, kept here so the
    /// notifier-side translation contract can be driven without depending on the encoder suite. The
    /// extent is describable, so the failure happens while the encoder produces the frame - after the
    /// output file has been opened - which is exactly the case the resolver's cleanup branch exists
    /// for.
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
