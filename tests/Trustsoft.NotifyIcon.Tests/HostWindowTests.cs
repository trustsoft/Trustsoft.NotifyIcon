using System.Reflection;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves the properties of the hidden tray host window that nothing else in the slice can prove:
/// that it really is a <b>top-level</b> window (the empirical retirement of the open risk behind
/// D011), that it is invisible and absent from the taskbar, that its <c>TaskbarCreated</c> id was
/// resolved for real, and that its message hook is genuinely alive.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on an STA thread (<see cref="StaFactAttribute"/>), because creating an
/// <c>HwndSource</c> on an MTA thread fails for reasons unrelated to the code under test.
/// </para>
/// <para>
/// <b>Why these are measurements, not assertions about our own code.</b> The claim
/// "a zero parent yields a top-level window" is inferred - <c>HwndSourceParameters.ParentWindow</c>
/// has no reference documentation - and if it were false, D011's recovery premise would be dead
/// while every other test in the slice still passed. So the tests ask the OS, through
/// <c>GetAncestor</c>/<c>GA_ROOT</c>, <c>IsWindowVisible</c>, <c>IsWindow</c> and
/// <c>GetWindowLongPtr</c>, rather than re-reading the values we passed in.
/// </para>
/// <para>
/// <b>The hook assertion is behavioural.</b> WPF holds <c>HwndSourceHook</c> delegates weakly, so
/// a hook that is only a temporary gets collected and the window silently stops calling back. A
/// reflection check cannot detect that; sending a message and observing the callback can
/// (a same-thread <c>SendMessage</c> runs the window procedure synchronously), so the reflection
/// check is only a supplement.
/// </para>
/// </remarks>
public sealed class HostWindowTests
{
    /// <summary>
    /// The single assertion this task exists for: the host is a top-level window, so
    /// <c>HWND_BROADCAST</c> - and therefore <c>TaskbarCreated</c> - can reach it.
    /// </summary>
    [StaFact]
    public void Host_is_a_top_level_window()
    {
        using var host = CreateHost();

        Assert.NotEqual(IntPtr.Zero, host.Handle);

        // GetAncestor(hwnd, GA_ROOT) == hwnd is the definition of "this window is top-level".
        // A message-only host (the invalidated D003 design) fails exactly here.
        Assert.Equal(host.Handle, Win32.GetAncestor(host.Handle, Win32.GA_ROOT));
    }

    /// <summary>
    /// The host must never be on screen: the library's premise is an application with no window.
    /// </summary>
    [StaFact]
    public void Host_is_not_visible()
    {
        using var host = CreateHost();

        Assert.False(Win32.IsWindowVisible(host.Handle));
    }

    /// <summary>
    /// The host carries <c>WS_EX_TOOLWINDOW</c> and must not carry <c>WS_EX_APPWINDOW</c>, which
    /// together are what keeps a phantom taskbar button and Alt-Tab entry from appearing.
    /// </summary>
    /// <remarks>
    /// The style is read back from the created window rather than from the parameters we handed to
    /// WPF, because <c>HwndSource</c> creates the window through <c>CreateWindowEx</c> and could in
    /// principle adjust what it was asked for.
    /// </remarks>
    [StaFact]
    public void Host_has_toolwindow_extended_style_and_not_appwindow()
    {
        using var host = CreateHost();

        long extendedStyle = Win32.GetWindowLongPtr(host.Handle, Win32.GWL_EXSTYLE);

        // Measured on this machine: WPF passes the requested extended style through and adds
        // WS_EX_WINDOWEDGE (0x100), so the created window reports 0x180 - the tool-window bit is
        // ours (0x80) and the app-window bit (0x40000) is absent. The assertions below are
        // deliberately semantic rather than a pin on 0x180, because WS_EX_WINDOWEDGE is a WPF
        // choice that could legitimately change; what must hold is the tool-window bit being set
        // and the taskbar-forcing bit being clear.
        Assert.True(
            (extendedStyle & Win32.WS_EX_TOOLWINDOW) != 0,
            $"The host must be a tool window so it stays off the taskbar and out of Alt-Tab; "
            + $"measured extended style 0x{extendedStyle:X}.");

        Assert.True(
            (extendedStyle & Win32.WS_EX_APPWINDOW) == 0,
            $"The host must not force itself onto the taskbar; measured extended style 0x{extendedStyle:X}.");

        // Binding evidence, independent of the style bits: whatever the extended style says, the
        // window is invisible and is its own root window, which is what the "no visible artifact"
        // and broadcast-reception premises actually require.
        Assert.False(Win32.IsWindowVisible(host.Handle));
        Assert.Equal(host.Handle, Win32.GetAncestor(host.Handle, Win32.GA_ROOT));
    }

    /// <summary>
    /// The <c>TaskbarCreated</c> id is resolved once, at construction, and is a usable (non-zero)
    /// id - the recovery point S05 depends on.
    /// </summary>
    [StaFact]
    public void TaskbarCreated_is_registered_once_and_is_not_zero()
    {
        var shell = new FakeShellApi();
        using var host = CreateHost(shell);

        Assert.True(host.TaskbarCreatedAvailable);
        Assert.Equal(0, host.TaskbarCreatedRegistrationError);

        string registeredName = Assert.Single(shell.RegisteredMessages);
        Assert.Equal("TaskbarCreated", registeredName);

        Assert.Equal(1, shell.Calls.Count(call => call.Operation == nameof(IShellApi.RegisterWindowMessage)));

        Assert.True(host.IsTaskbarCreatedMessage(FakeShellApi.DefaultRegisteredMessageId));
        Assert.False(host.IsTaskbarCreatedMessage(FakeShellApi.DefaultRegisteredMessageId + 1));
    }

    /// <summary>
    /// A failed registration is a <b>named failure state</b>, not a usable id: message id <c>0</c>
    /// can never match an incoming message, so a host that treated it as an id would silently stop
    /// seeing the broadcast and the failure would be invisible.
    /// </summary>
    /// <remarks>
    /// The host itself must still be usable - window creation is independent of the registration -
    /// because losing Explorer-restart recovery is a degradation, not a reason to refuse to show
    /// an icon at all.
    /// </remarks>
    [StaFact]
    public void A_failed_TaskbarCreated_registration_is_reported_as_a_named_state()
    {
        var shell = new FakeShellApi { LastErrorToReport = 5 };
        shell.FailNext(ShellOperation.RegisterWindowMessage);

        using var host = CreateHost(shell);

        Assert.False(host.TaskbarCreatedAvailable);
        Assert.Equal(5, host.TaskbarCreatedRegistrationError);

        // Id 0 must never match, or a failed registration would look like a broadcast.
        Assert.False(host.IsTaskbarCreatedMessage(0));

        Assert.NotEqual(IntPtr.Zero, host.Handle);
        Assert.True(Win32.IsWindow(host.Handle));
    }

    /// <summary>
    /// The hook is retained, and - more importantly - it actually runs when the window is sent a
    /// message.
    /// </summary>
    /// <remarks>
    /// The behavioural half is the real evidence: a hook that was garbage collected leaves a
    /// perfectly valid window that has simply gone silent, and only a message round trip reveals
    /// it. The reflection half documents the mechanism that keeps the delegate alive.
    /// </remarks>
    [StaFact]
    public void Hook_delegate_is_retained_and_receives_messages()
    {
        var received = new List<uint>();
        using var host = CreateHost(onMessage: (msg, _, _) => received.Add(msg));

        FieldInfo? hookField = typeof(TrayMessageWindow)
            .GetField("_hook", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(hookField);
        Assert.NotNull(hookField!.GetValue(host));

        // A same-thread SendMessage invokes the window procedure synchronously, so the callback
        // must have run by the time the call returns.
        Win32.SendMessage(host.Handle, ShellConstants.WM_USER + 7, new IntPtr(11), new IntPtr(22));

        Assert.Contains(ShellConstants.WM_USER + 7, received);
    }

    /// <summary>
    /// The callback message id the shell will be told to use is the one the host exposes, so the
    /// window and the icon data cannot drift apart.
    /// </summary>
    [StaFact]
    public void Callback_message_id_is_the_default_reserved_id_and_is_overridable()
    {
        using var defaultHost = CreateHost();
        Assert.Equal(ShellConstants.TrayCallbackMessage, defaultHost.CallbackMessageId);

        using var customHost = CreateHost(callbackMessageId: ShellConstants.WM_USER + 42);
        Assert.Equal(ShellConstants.WM_USER + 42, customHost.CallbackMessageId);
    }

    /// <summary>
    /// Disposal is idempotent: the shutdown path of a windowless host may be reached twice, and a
    /// second disposal must not throw.
    /// </summary>
    [StaFact]
    public void Dispose_is_idempotent()
    {
        var host = CreateHost();

        host.Dispose();
        host.Dispose();

        Assert.Equal(IntPtr.Zero, host.Handle);
    }

    /// <summary>
    /// Disposal actually destroys the window, so the host does not leak a window handle - the
    /// resource that would accumulate if an application created tray icons repeatedly.
    /// </summary>
    [StaFact]
    public void Dispose_destroys_the_window()
    {
        var host = CreateHost();

        // Capture the handle first: after disposal the property returns a null handle, and the
        // point of the test is the fate of the handle that existed.
        IntPtr handle = host.Handle;
        Assert.True(Win32.IsWindow(handle));

        host.Dispose();

        Assert.False(Win32.IsWindow(handle));
    }

    /// <summary>
    /// A construction that cannot possibly work is rejected before any seam call or window is
    /// created, so there is nothing to clean up and no half-built host to leak.
    /// </summary>
    [StaFact]
    public void Constructor_rejects_null_arguments_without_side_effects()
    {
        var shell = new FakeShellApi();

        Assert.Throws<ArgumentNullException>(() => new TrayMessageWindow(shell, null!));
        Assert.Empty(shell.Calls);

        Assert.Throws<ArgumentNullException>(() => new TrayMessageWindow(null!, (_, _, _) => { }));
        Assert.Empty(shell.Calls);
    }

    /// <summary>Creates a host window with a fake shell, a no-op sink and the default ids.</summary>
    /// <param name="shell">The seam to use; a fresh <see cref="FakeShellApi"/> when omitted.</param>
    /// <param name="onMessage">The message sink; a no-op when omitted.</param>
    /// <param name="callbackMessageId">The callback message id; the library default when omitted.</param>
    /// <returns>The host window, owned by the caller.</returns>
    private static TrayMessageWindow CreateHost(
        FakeShellApi? shell = null,
        Action<uint, IntPtr, IntPtr>? onMessage = null,
        uint? callbackMessageId = null) =>
        new(
            shell ?? new FakeShellApi(),
            onMessage ?? ((_, _, _) => { }),
            callbackMessageId ?? ShellConstants.TrayCallbackMessage);
}
