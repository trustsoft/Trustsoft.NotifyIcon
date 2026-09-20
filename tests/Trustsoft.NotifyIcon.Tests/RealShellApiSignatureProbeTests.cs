using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves that the <b>real</b> <see cref="ShellApi"/> P/Invoke declarations agree with the seam
/// every other test runs against, using <see cref="RecordingShellApi"/> wrapped around a real
/// <see cref="ShellApi"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every lifecycle, conversion and negative test in this slice drives
/// <see cref="FakeShellApi"/>. A wrong <c>EntryPoint</c> name, a missing <c>SetLastError</c>, a
/// <c>CharSet</c> that makes the runtime probe for a <c>W</c>-suffixed export that is not there,
/// or a <c>bool</c> marshalled as a one-byte type would pass all of them and fail only in a
/// consumer's process - usually as "the icon does not appear", with no exception anywhere. These
/// probes cross that gap with calls that are real but have no visible artifact.
/// </para>
/// <para>
/// <b>No icon is registered here.</b> A real <c>NIM_ADD</c> would put an icon in the notification
/// area of whoever runs the suite and could fail to clean it up, so it is deliberately excluded
/// from <c>dotnet test</c>: a successful registration is the manual UAT checklist's evidence
/// (<c>docs/UAT-S01.md</c>), and the only <c>Shell_NotifyIconW</c> call made here is one that
/// cannot register anything (an invalid operation code against an unregistered window/id pair).
/// </para>
/// <para>
/// <b>Serialised on purpose.</b> Two of the probes measure the process-wide GDI object count, so
/// the class joins the non-parallel GDI measurement collection
/// (<see cref="GdiCountCollection"/>) that keeps the counter attributable to the code under test.
/// </para>
/// </remarks>
[Collection(GdiCountCollection.Name)]
public sealed class RealShellApiSignatureProbeTests
{
    /// <summary>The icon edge length used by the real conversion round trip.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// A <c>dwMessage</c> value no <c>NIM_*</c> constant uses, so the real shell must refuse it.
    /// </summary>
    private const uint InvalidShellNotifyIconMessage = 0xDEADBEEF;

    /// <summary>
    /// <c>RegisterWindowMessageW</c> resolves and returns a stable, non-zero, session-unique id,
    /// and the host window computes the same availability from the same real resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call is side-effect-free and needs no desktop session: the message name is registered
    /// in the session's atom table and the same value comes back for the same name. That makes it
    /// the safest possible probe of a real entry point - a wrong entry point name surfaces here as
    /// an <c>EntryPointNotFoundException</c> rather than as a tray icon that silently never
    /// recovers from an Explorer restart.
    /// </para>
    /// <para>
    /// The host-window half is the S05-facing consequence: <see cref="TrayMessageWindow"/> treats
    /// a zero id as the named failure state <c>TaskbarCreatedAvailable = false</c>, so this probe
    /// asserts the same real id reaches the host and is recognised (
    /// <see cref="TrayMessageWindow.IsTaskbarCreatedMessage"/> would be false for any id the host
    /// did not actually receive).
    /// </para>
    /// </remarks>
    [StaFact]
    public void RegisterWindowMessage_TaskbarCreated_returns_a_nonzero_id_via_the_real_api()
    {
        var real = new ShellApi();

        uint first = real.RegisterWindowMessage("TaskbarCreated");
        uint second = real.RegisterWindowMessage("TaskbarCreated");

        Assert.NotEqual(0u, first);

        // A registered message id is a session-wide atom: registering the same name again must
        // return the identical id, or the definition of "the message you registered" is broken.
        Assert.Equal(first, second);

        // The host resolves the same id through the seam (here: the recording wrapper over the
        // real implementation), and reports it as available rather than caching a failure.
        var recording = new RecordingShellApi(real);
        using var host = new TrayMessageWindow(recording, (_, _, _) => { });

        Assert.True(host.TaskbarCreatedAvailable);
        Assert.Equal(0, host.TaskbarCreatedRegistrationError);

        // The host only recognises the id it actually registered: this passes only because the
        // real call above returned the same value.
        Assert.True(host.IsTaskbarCreatedMessage(first));

        // ...and the wrapper really delegated rather than answering from a cache.
        Assert.Contains(
            recording.Calls,
            call => call.Operation == nameof(IShellApi.RegisterWindowMessage));
    }

    /// <summary>
    /// <c>GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS)</c> returns a plausible count and
    /// agrees exactly with the independent test-local declaration used by every GDI assertion in
    /// this suite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the function that produces all of R007's evidence. If its signature were wrong -
    /// the wrong <c>uiFlags</c> type, a truncated handle, the wrong calling convention - every
    /// leak assertion in the suite would still "pass" while measuring nonsense, so the signature
    /// has to be pinned against a second, independent declaration of the same call
    /// (<see cref="GdiHandles.Count"/>).
    /// </para>
    /// <para>
    /// <b>The assertion is a delta, not positivity.</b> A freshly started Windows process
    /// legitimately owns zero GDI objects (measured; see <see cref="GdiHandles"/>), so "greater
    /// than zero" is not a meaningful claim. Creating exactly one GDI object through
    /// <see cref="GdiHandles.CreateMemoryDc"/> and observing exactly <c>+1</c> through both
    /// instruments is: it fails for any marshalling mistake, and it fails if the seam and the
    /// test-local counter ever stop describing the same thing.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetGuiResources_GR_GDIOBJECTS_returns_a_plausible_count_via_the_real_api()
    {
        var real = new RecordingShellApi();
        IntPtr process = Win32.GetCurrentProcess();

        uint baseline = real.GetGuiResources(process, ShellConstants.GR_GDIOBJECTS);

        // Plausibility: a process is not expected to own four-figure numbers of GDI objects while
        // a unit test runs, and a marshalling mistake would show up as an absurd value.
        Assert.InRange(baseline, 0u, 4096u);

        using (GdiHandles.CreateMemoryDc())
        {
            uint withOneMoreObject = real.GetGuiResources(process, ShellConstants.GR_GDIOBJECTS);

            Assert.Equal(baseline + 1, withOneMoreObject);
            Assert.Equal((uint)GdiHandles.Count(), withOneMoreObject);
        }

        Assert.Equal(baseline, real.GetGuiResources(process, ShellConstants.GR_GDIOBJECTS));
    }

    /// <summary>
    /// A real icon built by the delivered conversion path can be destroyed by the real
    /// <c>DestroyIcon</c>, and doing so returns the process GDI count to where it started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the strongest R007 evidence obtainable without a live shell: real
    /// <c>CreateDIBSection</c> calls, a real <c>CreateIconIndirect</c>, a real <c>HICON</c>, a real
    /// <c>DestroyIcon</c>, with the process GDI counter as the instrument. Every one of those
    /// declarations is exercised against its DLL, and the two temporary <c>HBITMAP</c>s the
    /// factory releases show up as a flat counter rather than as a claim in a comment.
    /// </para>
    /// <para>
    /// One warm-up round trip is performed before the baseline so that lazily created
    /// process-wide GDI objects (WPF's imaging layer, the first DIB section) are not folded into
    /// the delta and misreported as a leak.
    /// </para>
    /// </remarks>
    [StaFact]
    public void DestroyIcon_on_a_real_icon_from_our_conversion_path_succeeds()
    {
        var shell = new RecordingShellApi(new ShellApi());
        BitmapSource source = CreateSolid(IconSize);

        // Warm-up: initialise whatever the conversion path creates once per process.
        IntPtr warmUp = HiconFactory.CreateIcon(shell, source, IconSize);

        Assert.NotEqual(IntPtr.Zero, warmUp);
        Assert.True(shell.DestroyIcon(warmUp));

        int before = GdiHandles.Count();

        IntPtr icon = HiconFactory.CreateIcon(shell, source, IconSize);

        Assert.NotEqual(IntPtr.Zero, icon);
        Assert.True(shell.DestroyIcon(icon));

        int after = GdiHandles.Count();

        Assert.Equal(before, after);

        // The destroy was addressed to the handle the factory returned - not to a zero or a
        // different handle - which is what a recording wrapper can say and a boolean cannot.
        Assert.Contains(
            $"hIcon=0x{icon.ToInt64():X}",
            shell.Calls.Select(call => call.Detail));

        Assert.Contains(
            shell.Calls,
            call => call.Operation == nameof(IShellApi.CreateIconIndirect));

        // Every bitmap the conversion created was released through the seam, so the flat counter
        // is not an accident of a non-allocating path.
        Assert.Equal(4, shell.Calls.Count(call => call.Operation == nameof(IShellApi.CreateDIBSection)));
        Assert.Equal(4, shell.Calls.Count(call => call.Operation == nameof(IShellApi.DeleteObject)));
    }

    /// <summary>
    /// The real <c>Shell_NotifyIconW</c> declaration reports failure through its return value and
    /// the captured last error instead of throwing out of managed code, for an operation code the
    /// shell does not recognise against a window/id pair that was never registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This pins the seam's raw-result contract (D008) against the real export: a failure is a
    /// <see langword="false"/> plus an error code, never an exception from inside the interop
    /// boundary, and it proves the <c>Shell_NotifyIconW</c> entry point (with its <c>ref</c>
    /// struct argument) really resolves at runtime.
    /// </para>
    /// <para>
    /// <b>Nothing is registered by this call.</b> The message code is not a valid <c>NIM_*</c>
    /// value, so the shell rejects the call before it can look at the icon data; no icon appears
    /// in anyone's notification area, and there is nothing to clean up.
    /// </para>
    /// </remarks>
    [Fact]
    public void ShellNotifyIcon_with_an_invalid_message_code_reports_failure_rather_than_throwing_from_managed_code()
    {
        var real = new RecordingShellApi();
        var data = NOTIFYICONDATAW.Create(IntPtr.Zero, 1);

        bool result = real.ShellNotifyIcon(InvalidShellNotifyIconMessage, ref data);

        Assert.False(result);

        int error = real.GetLastError();

        // Measured on this machine: the shell refuses the call and leaves a non-zero code behind
        // (E_FAIL, 0x80004005). Keep the assertion to "non-zero" rather than naming a code - the
        // value is the shell's to choose and other failures in this suite legitimately report 0.
        Assert.NotEqual(0, error);
    }

    /// <summary>
    /// The real <c>Shell_NotifyIconGetRect</c> declaration resolves, returns a failing
    /// <c>HRESULT</c> for an unregistered icon instead of throwing out of managed code, and moves
    /// no GDI object count on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the real-entry-point proof for the export, the <c>ExactSpelling</c> flag and the
    /// <c>HRESULT</c> calling convention: a wrong entry point name surfaces here as an
    /// <c>EntryPointNotFoundException</c>, and reading the result as a <c>BOOL</c> would show up
    /// as the opposite of what this asserts. <c>Shell_NotifyIconGetRect</c> is the one member of
    /// the seam whose failure value is non-zero, so it is asserted as such and never against
    /// <see langword="false"/>.
    /// </para>
    /// <para>
    /// <b>Nothing is registered or altered by this call.</b> The window handle names no window at
    /// all, so the shell rejects the lookup before it can touch a notification area; the icon id
    /// was never registered for that handle. The GDI counter is read around the call because
    /// "locate an icon" creates no GDI object, which is the cheap half of "this call has no
    /// side effect": the class joins the non-parallel <see cref="GdiCountCollection"/> precisely
    /// so the counter stays attributable.
    /// </para>
    /// <para>
    /// The output rectangle is deliberately not asserted here. A failing call promises nothing
    /// about it, and pinning its content would pin an implementation detail of the marshalling
    /// stub rather than a contract; the empty-rectangle rule for a <em>failed</em> lookup is pinned
    /// on the fake in <c>ShellNotifyIconGetRectTests</c>, where "failed" is deterministic.
    /// </para>
    /// </remarks>
    [Fact]
    public void ShellNotifyIconGetRect_with_an_unregistered_identifier_returns_a_failing_HRESULT()
    {
        var real = new RecordingShellApi();
        IntPtr process = Win32.GetCurrentProcess();

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(new IntPtr(0x0000DEADBEEF), 0xBEEF);

        uint baseline = real.GetGuiResources(process, ShellConstants.GR_GDIOBJECTS);

        int result = real.ShellNotifyIconGetRect(ref identifier, out _);

        uint after = real.GetGuiResources(process, ShellConstants.GR_GDIOBJECTS);

        // Non-zero means "the shell could not locate the icon"; 0 would be S_OK. Measured on this
        // machine the shell answers a failure code for a handle that is not a window.
        Assert.NotEqual(0, result);

        // Locating an icon creates and frees no GDI object, so the process counter is unmoved.
        Assert.Equal(baseline, after);

        // The call really went through the wrapper (it is recorded after delegation, because the
        // record carries the HRESULT) and the identifier itself was not rewritten by the shell:
        // the export takes a const pointer.
        Assert.Contains(
            real.Calls,
            call => call.Operation == nameof(IShellApi.ShellNotifyIconGetRect));

        Assert.Equal(new IntPtr(0x0000DEADBEEF), identifier.hWnd);
        Assert.Equal(0xBEEFu, identifier.uID);
        Assert.Equal(Guid.Empty, identifier.guidItem);
    }

    /// <summary>
    /// A null window handle is refused by the <b>shell</b>, not by
    /// <see cref="NOTIFYICONIDENTIFIER.Create"/>: construction succeeds and the real export reports
    /// the failure through its <c>HRESULT</c>.
    /// </summary>
    /// <remarks>
    /// This pins which layer owns the "that window never registered this icon" decision. Moving it
    /// into the factory would turn a caller's bad handle into a managed exception inside a struct
    /// initializer and would leave the seam's failure path untestable.
    /// </remarks>
    [Fact]
    public void ShellNotifyIconGetRect_with_a_null_window_handle_is_refused_by_the_shell()
    {
        var real = new RecordingShellApi();

        NOTIFYICONIDENTIFIER identifier = NOTIFYICONIDENTIFIER.Create(IntPtr.Zero, 1);
        Assert.Equal(IntPtr.Zero, identifier.hWnd);
        Assert.Equal(NOTIFYICONIDENTIFIER.SizeOf(), identifier.cbSize);

        int result = real.ShellNotifyIconGetRect(ref identifier, out _);

        Assert.NotEqual(0, result);
    }

    /// <summary>Builds a square, single-colour, fully opaque image for the conversion path.</summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <returns>The image.</returns>
    private static BitmapSource CreateSolid(int size)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = 0x40;
            pixels[(i * 4) + 1] = 0x80;
            pixels[(i * 4) + 2] = 0xC0;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }
}
