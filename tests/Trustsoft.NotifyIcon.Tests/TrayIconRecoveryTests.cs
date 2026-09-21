using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S05 vertical slice: the shell's <c>TaskbarCreated</c> broadcast arriving at the real host
/// window becomes a re-registration of the icon the consumer already asked for.
/// </summary>
/// <remarks>
/// <para>
/// The broadcast is injected exactly the way S02 injects a click - a same-thread
/// <see cref="Win32.SendMessage"/> into the window the icon really registered
/// (<see cref="TrayIcon.HostHandle"/>) - so the whole path from the host's message id to the sink to
/// the recovery branch to the recorded shell calls runs synchronously before the send returns.
/// Nothing is pumped and nothing but the shell seam is faked.
/// </para>
/// <para>
/// <b>Why the sequence is the contract, not the returned value.</b> A re-registration that speaks
/// the legacy protocol, or one that forgets <c>NIF_TIP</c>/<c>NIF_SHOWTIP</c>, produces an icon that
/// looks present and behaves wrongly - click payloads decode against the wrong layout and the
/// tooltip silently does nothing. Neither failure raises anything a consumer could catch, so both
/// are asserted as recorded call sequences (D012).
/// </para>
/// <para>
/// <b>Why this class is in the trace-channel collection.</b> One test observes the Verbose recovery
/// line by raising the library's process-wide trace switch, and
/// <see cref="NotifyIconTraceTests"/> asserts that the same switch defaults to
/// <see cref="SourceLevels.Warning"/>. Serialising the two classes keeps that assertion from
/// observing a level this one deliberately changed.
/// </para>
/// <para>
/// <b>Requirements proven here:</b> R005 (after Explorer restarts the icon returns by itself and
/// stays interactive, with <see cref="TrayIcon.Visible"/> preserved), the handle-ownership half of
/// R007 (recovery reuses the retained <c>HICON</c> instead of converting
/// <see cref="TrayIcon.IconSource"/> again), and R013's runtime half for the recovery path (a
/// refused recovery retries once, traces and raises <c>TrayError</c> without throwing out of the
/// window procedure).
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class TrayIconRecoveryTests
{
    /// <summary>The icon edge length used by the tests that need a real icon image.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// The tooltip capacity recovery has to respect: <c>szTip</c> is <c>WCHAR[128]</c> including the
    /// terminator, so at most 127 characters reach the shell.
    /// </summary>
    private const int MaxToolTipLength = 127;

    /// <summary>
    /// The First Proof: a broadcast on a registered icon re-issues the add pair with the handle that
    /// is already owned.
    /// </summary>
    /// <remarks>
    /// This is the slice's highest-risk assertion. Recovery has to be a re-add - the registration
    /// disappears with the shell process while the consumer's intent and the retained handle do not
    /// - and the tempting shortcut (calling the ordinary registration path again) would either
    /// no-op, throw inside a window procedure, or silently build a second handle over the first.
    /// </remarks>
    [DispatcherFact]
    public void TaskbarCreated_broadcast_re_adds_with_the_retained_handle()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
            ToolTipText = "Trustsoft.NotifyIcon",
        };

        trayIcon.Visible = true;

        uint iconIdBefore = shell.ShellNotifyIconDataSnapshots[0].uID;
        IntPtr handleBefore = trayIcon.RegisteredIconHandle;
        int callsBefore = shell.ShellNotifyIconCalls.Count;

        Assert.NotEqual(IntPtr.Zero, handleBefore);
        Assert.True(trayIcon.IsRegistered);
        Assert.True(trayIcon.Visible);

        SendTaskbarCreated(trayIcon);

        IReadOnlyList<ShellCall> recovery = [.. shell.ShellNotifyIconCalls.Skip(callsBefore)];

        // The order is the contract: version 4 must be selected on the registration that just
        // succeeded, and nothing else may be sent in between.
        Assert.Equal(2, recovery.Count);
        Assert.Equal(ShellConstants.NIM_ADD, recovery[0].Message);
        Assert.Equal(ShellConstants.NIM_SETVERSION, recovery[1].Message);

        IReadOnlyList<NOTIFYICONDATAW> data = shell.ShellNotifyIconDataSnapshots;

        // The re-add carries the same identity - the shell reports the id back in HIWORD(lParam),
        // so a different id would orphan the click pipeline S02 installed - and the same window.
        Assert.Equal(iconIdBefore, data[callsBefore].uID);
        Assert.Equal(trayIcon.HostHandle, data[callsBefore].hWnd);
        Assert.Equal(ShellConstants.TrayCallbackMessage, data[callsBefore].uCallbackMessage);

        // The retained handle is reused, not rebuilt: the icon the shell now holds is the icon this
        // instance still owns, which is what keeps the GDI handle count flat across a restart.
        Assert.Equal(handleBefore, data[callsBefore].hIcon);
        Assert.Equal(handleBefore, trayIcon.RegisteredIconHandle);

        Assert.Equal(ShellConstants.NOTIFYICON_VERSION_4, data[callsBefore + 1].uTimeoutOrVersion);

        // Nothing was destroyed on the way through.
        Assert.Empty(shell.DestroyedIconHandles);

        // The registration the shell refused to keep is now back, and the consumer's intent is
        // unchanged: recovery is not a visibility change.
        Assert.True(trayIcon.IsRegistered);
        Assert.True(trayIcon.Visible);
    }

    /// <summary>
    /// The re-add carries exactly the registration flags and the tooltip text a fresh registration
    /// carries, and never a balloon field.
    /// </summary>
    /// <remarks>
    /// The two silent-regression traps this pins: a re-add without <c>NIF_TIP</c> plus a filled
    /// <c>szTip</c> leaves a recovered icon that shows no tooltip until the text happens to change
    /// (D012), and a re-add that carried <c>NIF_INFO</c> would ask the shell to treat stale balloon
    /// members from a fresh structure as a balloon show. The no-image case is covered too, because
    /// <c>NIF_ICON</c> with a zero <c>hIcon</c> asks the shell to display nothing.
    /// </remarks>
    [DispatcherFact]
    public void Re_add_carries_the_registration_flags_and_the_tooltip_but_never_a_balloon_flag()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
            ToolTipText = new string('t', MaxToolTipLength + 33),
        };

        trayIcon.Visible = true;

        int callsBefore = shell.ShellNotifyIconCalls.Count;

        SendTaskbarCreated(trayIcon);

        NOTIFYICONDATAW add = shell.ShellNotifyIconDataSnapshots[callsBefore];

        uint expected = ShellConstants.NIF_MESSAGE
            | ShellConstants.NIF_TIP
            | ShellConstants.NIF_SHOWTIP
            | ShellConstants.NIF_ICON;

        Assert.Equal(expected, add.uFlags);
        Assert.Equal(0u, add.uFlags & ShellConstants.NIF_INFO);

        // Truncated exactly as a fresh registration truncates it, so a recovered icon's tooltip is
        // byte-for-byte the tooltip the consumer asked for.
        Assert.Equal(MaxToolTipLength, add.szTip.Length);
        Assert.Equal(new string('t', MaxToolTipLength), add.szTip);

        // Every recorded structure has to be a full-size one: a re-add that forgot cbSize would be
        // rejected by the shell in a way that looks like a refused add rather than a library bug.
        Assert.All(
            shell.ShellNotifyIconDataSnapshots,
            data => Assert.Equal((uint)NOTIFYICONDATAW.SizeOf(), data.cbSize));
    }

    /// <summary>
    /// An icon registered without any image still recovers, with <c>NIF_SHOWTIP</c> set and
    /// <c>NIF_ICON</c> clear - the same shape a fresh registration of such an icon has.
    /// </summary>
    [DispatcherFact]
    public void Re_add_of_an_icon_without_an_image_keeps_showtip_and_omits_the_icon_flag()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell) { ToolTipText = "No image yet" };

        trayIcon.Visible = true;

        int callsBefore = shell.ShellNotifyIconCalls.Count;

        SendTaskbarCreated(trayIcon);

        NOTIFYICONDATAW add = shell.ShellNotifyIconDataSnapshots[callsBefore];

        Assert.True((add.uFlags & ShellConstants.NIF_SHOWTIP) != 0);
        Assert.Equal(0u, add.uFlags & ShellConstants.NIF_ICON);
        Assert.Equal(IntPtr.Zero, add.hIcon);
        Assert.Equal(IntPtr.Zero, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);
    }

    /// <summary>
    /// A broadcast on an instance whose icon is currently not shown is a silent no-op: the
    /// application never asked for an icon, so recovery must not create one.
    /// </summary>
    /// <remarks>
    /// The reachable form of this case is a registration that was deliberately removed - the host
    /// window is still alive after <c>Visible = false</c> (only disposal destroys it), so the
    /// broadcast genuinely arrives at the sink and the guard is what stops it. That is a different
    /// situation from an instance that was never shown at all, which has no host window and
    /// therefore cannot receive the broadcast.
    /// </remarks>
    [DispatcherFact]
    public void Broadcast_after_the_icon_was_removed_is_a_silent_no_op()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
        };

        trayIcon.Visible = true;
        trayIcon.Visible = false;

        Assert.False(trayIcon.IsRegistered);

        // The window outlives the registration, which is what makes the guard reachable.
        IntPtr host = trayIcon.HostHandle;

        Assert.NotEqual(IntPtr.Zero, host);
        Assert.True(Win32.IsWindow(host));

        int callsBefore = shell.ShellNotifyIconCalls.Count;
        int destroyedBefore = shell.DestroyedIcons;

        Win32.SendMessage(host, FakeShellApi.DefaultRegisteredMessageId, IntPtr.Zero, IntPtr.Zero);

        Assert.Equal(callsBefore, shell.ShellNotifyIconCalls.Count);
        Assert.Equal(destroyedBefore, shell.DestroyedIcons);
        Assert.False(trayIcon.IsRegistered);
        Assert.False(trayIcon.Visible);
    }

    /// <summary>
    /// A broadcast on a disposed instance does nothing: disposal is terminal, and a recovery that
    /// resurrected the icon would outlive the element that owned it.
    /// </summary>
    [DispatcherFact]
    public void Broadcast_after_disposal_is_a_no_op()
    {
        var shell = new FakeShellApi();
        var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;

        IntPtr host = trayIcon.HostHandle;

        trayIcon.Dispose();

        Assert.False(trayIcon.IsRegistered);
        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);

        int callsBefore = shell.ShellNotifyIconCalls.Count;

        // The host is gone with the element, so the broadcast can no longer reach the sink at all;
        // the send is kept so the assertion covers the window handle the host used to own.
        Win32.SendMessage(host, FakeShellApi.DefaultRegisteredMessageId, IntPtr.Zero, IntPtr.Zero);

        Assert.Equal(callsBefore, shell.ShellNotifyIconCalls.Count);
        Assert.False(trayIcon.IsRegistered);
    }

    /// <summary>
    /// Two restarts in a row each recover independently, and neither one destroys anything.
    /// </summary>
    /// <remarks>
    /// Recovery is deliberately not gated on a flag of its own: every broadcast means the
    /// registration is gone, so every broadcast has to re-add. A "already recovered" latch would
    /// pass the single-restart test and leave the icon missing after the second restart.
    /// </remarks>
    [DispatcherFact]
    public void Each_broadcast_recovers_and_nothing_is_destroyed()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
        };

        trayIcon.Visible = true;

        IntPtr handle = trayIcon.RegisteredIconHandle;
        int createdBefore = shell.CreatedIcons;

        SendTaskbarCreated(trayIcon);
        SendTaskbarCreated(trayIcon);

        IReadOnlyList<ShellCall> recovery = [.. shell.ShellNotifyIconCalls.Skip(2)];

        Assert.Equal(4, recovery.Count);
        Assert.Equal(ShellConstants.NIM_ADD, recovery[0].Message);
        Assert.Equal(ShellConstants.NIM_SETVERSION, recovery[1].Message);
        Assert.Equal(ShellConstants.NIM_ADD, recovery[2].Message);
        Assert.Equal(ShellConstants.NIM_SETVERSION, recovery[3].Message);

        // The GDI accounting: no conversion ran and no handle was released. Asserting the identity
        // and the two counters rather than a zero outstanding count, because a registered icon
        // legitimately keeps exactly one live HICON.
        Assert.Equal(createdBefore, shell.CreatedIcons);
        Assert.Empty(shell.DestroyedIconHandles);
        Assert.Equal(handle, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);
    }

    /// <summary>
    /// A refused re-add is retried exactly once, then traced and raised through
    /// <see cref="TrayIcon.TrayError"/> - without an exception escaping the window procedure.
    /// </summary>
    /// <remarks>
    /// This is the runtime policy (D008) applied to the recovery path. It matters more here than
    /// anywhere else in the library: the failing call happens inside a window procedure that the
    /// shell invoked, so a thrown exception would not be a test failure, it would be a crash in the
    /// host application, triggered by a condition - Explorer restarting - that the application
    /// cannot prevent.
    /// </remarks>
    [DispatcherFact]
    public void Failed_re_add_retries_once_then_raises_TrayError_without_throwing()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
        };

        trayIcon.Visible = true;

        IntPtr handleBefore = trayIcon.RegisteredIconHandle;
        int callsBefore = shell.ShellNotifyIconCalls.Count;

        var observed = new List<TrayErrorEventArgs>();
        trayIcon.TrayError += (_, args) => observed.Add(args);

        shell.FailAlways(ShellOperation.ShellNotifyIcon);
        shell.LastErrorToReport = 5;

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(listener);

            SendTaskbarCreated(trayIcon);

            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }

        IReadOnlyList<ShellCall> attempts = [.. shell.ShellNotifyIconCalls.Skip(callsBefore)];

        // Exactly two attempts at the add: the original and the single retry.
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, call => Assert.Equal(ShellConstants.NIM_ADD, call.Message));

        TrayErrorEventArgs error = Assert.Single(observed);

        Assert.True(error.Retried);
        Assert.Equal(TrayIconException.OperationAdd, error.Operation);
        Assert.Equal(5, error.Win32ErrorCode);

        // The consumer can find the failure in a support log without wiring up the event.
        Assert.Contains("failed (Win32 error 5); retried=True", writer.ToString(), StringComparison.Ordinal);

        // Nothing was destroyed and the consumer's intent is unchanged: the icon is simply absent
        // until the next broadcast, and the retained handle is still this instance's to reuse.
        Assert.Empty(shell.DestroyedIconHandles);
        Assert.Equal(handleBefore, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);
        Assert.True(trayIcon.Visible);
    }

    /// <summary>
    /// When the re-add succeeds but the version call is refused, the half-registered icon is removed
    /// again while the retained handle and the registration flag survive.
    /// </summary>
    /// <remarks>
    /// The decision this pins: an icon that is present but speaking the legacy protocol would
    /// misdecode every click S02 delivers, so it is worse than no icon. But the retained handle must
    /// not be destroyed and the flag must not be cleared, because a set flag over a live handle is
    /// recoverable while the reverse makes the next registration build a second handle over the
    /// field and orphan the first.
    /// </remarks>
    [DispatcherFact]
    public void Failed_SetVersion_during_recovery_rolls_back_and_keeps_the_handle()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
        };

        trayIcon.Visible = true;

        IntPtr handleBefore = trayIcon.RegisteredIconHandle;
        int callsBefore = shell.ShellNotifyIconCalls.Count;

        var observed = new List<TrayErrorEventArgs>();
        trayIcon.TrayError += (_, args) => observed.Add(args);

        // Call ordinals are per operation, so the registration consumed 1 (add) and 2 (setversion),
        // and recovery consumes 3 (add), 4 (setversion) and 5 (the single retry of that setversion).
        shell.FailCallNumber(ShellOperation.ShellNotifyIcon, 4);
        shell.FailCallNumber(ShellOperation.ShellNotifyIcon, 5);
        shell.LastErrorToReport = 87;

        SendTaskbarCreated(trayIcon);

        uint[] expected =
        [
            ShellConstants.NIM_ADD,
            ShellConstants.NIM_SETVERSION,
            ShellConstants.NIM_SETVERSION,
            ShellConstants.NIM_DELETE,
        ];

        Assert.Equal(expected, [.. shell.ShellNotifyIconCalls.Skip(callsBefore).Select(call => call.Message)]);

        TrayErrorEventArgs error = Assert.Single(observed);

        Assert.True(error.Retried);
        Assert.Equal(TrayIconException.OperationSetVersion, error.Operation);
        Assert.Equal(87, error.Win32ErrorCode);

        Assert.Empty(shell.DestroyedIconHandles);
        Assert.Equal(handleBefore, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);
        Assert.True(trayIcon.Visible);
    }

    /// <summary>
    /// A successful recovery announces itself on the trace channel at Verbose level.
    /// </summary>
    /// <remarks>
    /// The observability half of the contract, and the one place it is measurable: a consumer who
    /// raised the level to diagnose "my icon disappeared after Explorer restarted" needs the line to
    /// exist and to name the broadcast. It is written at <see cref="TraceEventType.Verbose"/> on
    /// purpose, so a normal run stays silent and the failure channel stays reserved for failures
    /// (MEM026).
    /// </remarks>
    [DispatcherFact]
    public void Successful_recovery_writes_one_verbose_line_naming_the_broadcast()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0),
        };

        trayIcon.Visible = true;

        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        try
        {
            source.Switch.Level = SourceLevels.Verbose;
            source.Listeners.Add(recorder);

            SendTaskbarCreated(trayIcon);

            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(recorder);
            source.Switch.Level = previousLevel;
        }

        (TraceEventType EventType, int EventId, string? Message) line = Assert.Single(recorder.Events);

        Assert.Equal(TraceEventType.Verbose, line.EventType);
        Assert.Equal(NotifyIconTrace.VerboseEventId, line.EventId);
        Assert.NotNull(line.Message);
        Assert.Contains("TaskbarCreated", line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sends the shell's taskbar-created broadcast to the icon's own host window, synchronously.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the broadcast.</param>
    /// <returns>The value the window procedure returned.</returns>
    /// <remarks>
    /// The message id comes from the seam that resolved it (<see cref="FakeShellApi"/> returns
    /// <see cref="FakeShellApi.DefaultRegisteredMessageId"/> for every registration), so the test
    /// sends the id the host really registered rather than a hardcoded one. Both parameters are
    /// zero: the broadcast carries no payload, and the recovery branch must not read them.
    /// </remarks>
    private static IntPtr SendTaskbarCreated(TrayIcon trayIcon) =>
        Win32.SendMessage(trayIcon.HostHandle, FakeShellApi.DefaultRegisteredMessageId, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Builds a solid square bitmap source for the tests that need a convertible icon.
    /// </summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <param name="b">The blue channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="r">The red channel.</param>
    /// <returns>A frozen, readable <see cref="BitmapSource"/>.</returns>
    /// <remarks>
    /// The array overload of <c>BitmapSource.Create</c> rather than a live visual, because the
    /// conversion path reads pixels synchronously and a render-target-backed source would need a
    /// pumping dispatcher.
    /// </remarks>
    private static BitmapSource CreateSolid(int size, byte b, byte g, byte r)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    /// <summary>
    /// Captures the trace events written to the library's own source.
    /// </summary>
    /// <remarks>
    /// A listener attached to <see cref="NotifyIconTrace.Source"/> rather than a second
    /// <see cref="TraceSource"/> built from the same name: a source created from a name gets its own
    /// listener list, so the by-name route would observe nothing and the test would pass for the
    /// wrong reason.
    /// </remarks>
    private sealed class RecordingTraceListener : TraceListener
    {
        /// <summary>Gets the events this listener received, in order.</summary>
        internal List<(TraceEventType EventType, int EventId, string? Message)> Events { get; } = [];

        /// <inheritdoc />
        public override void Write(string? message)
        {
        }

        /// <inheritdoc />
        public override void WriteLine(string? message)
        {
        }

        /// <inheritdoc />
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            Events.Add((eventType, id, message));
        }
    }
}
