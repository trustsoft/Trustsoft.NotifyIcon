using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S04 balloon surface: <see cref="TrayIcon.ShowBalloonTip"/> as one <c>NIM_MODIFY</c> carrying
/// <c>NIF_INFO</c>, the two severity/option enums as the shell's own bits in the right fields, the
/// surrogate-safe truncation of both balloon strings, the marshalling and refusal guards, and the
/// <see cref="TrayIcon.BalloonTipClickedEvent"/> routed-event pair's shape.
/// </summary>
/// <remarks>
/// <para>
/// The seam is where the balloon becomes observable: the shell has no notification area in a test
/// host and a real balloon could not be forced to render, so the assertions are about the exact
/// <see cref="NOTIFYICONDATAW"/> the shell is handed (<see cref="FakeShellApi.ShellNotifyIconDataSnapshots"/>)
/// and about the call log (<see cref="FakeShellApi.ShellNotifyIconCalls"/>). That is the same
/// evidence class S01's lifecycle tests use, and it is what makes "the severity reached the shell"
/// a fact rather than a screenshot.
/// </para>
/// <para>
/// Every test runs on an STA thread (<see cref="StaFactAttribute"/>) because the guard cases that
/// must reach the shell need a real registration, which creates a real hidden <c>HwndSource</c>
/// host window. The one test that needs a running dispatcher to serve a background thread's
/// <c>Dispatcher.Invoke</c> uses <see cref="DispatcherFactAttribute"/>.
/// </para>
/// </remarks>
public sealed class TrayIconBalloonTipTests
{
    /// <summary>The icon edge length used by the tests that need an icon.</summary>
    private const int IconSize = 16;

    /// <summary>An informational balloon is one <c>NIM_MODIFY</c> whose flags are exactly
    /// <c>NIF_INFO</c>, and the strings reach the shell as they were passed.</summary>
    /// <remarks>
    /// The exact-flag assertion is deliberate: <c>NIF_INFO</c> alone is what makes the balloon
    /// members valid, and a stray <c>NIF_TIP</c>/<c>NIF_SHOWTIP</c> on a balloon call would ask the
    /// shell to change the tooltip as a side effect of showing a balloon.
    /// </remarks>
    [StaFact]
    public void ShowBalloonTip_is_one_NIM_MODIFY_carrying_NIF_INFO_and_the_given_strings()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        int callsBefore = shell.ShellNotifyIconCalls.Count;

        trayIcon.ShowBalloonTip("Build finished", "3 projects built in 12.4s");

        ShellCall call = Assert.Single(shell.ShellNotifyIconCalls.Skip(callsBefore));

        Assert.Equal(ShellConstants.NIM_MODIFY, call.Message);
        Assert.Equal(ShellConstants.NIF_INFO, call.Flags);

        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal("Build finished", data.szInfoTitle);
        Assert.Equal("3 projects built in 12.4s", data.szInfo);
        Assert.Equal((uint)NOTIFYICONDATAW.SizeOf(), data.cbSize);
    }

    /// <summary>
    /// Each <see cref="BalloonTipIcon"/> value lands in the low nibble of <c>dwInfoFlags</c>, which
    /// is the range <c>NIIF_ICON_MASK</c> selects.
    /// </summary>
    /// <remarks>
    /// The enum's values are the shell's own, so this is a cast at the call site; the test pins the
    /// numeric identity, which is the fact a future "helpful" translation table would silently
    /// break.
    /// </remarks>
    [StaFact]
    public void Each_severity_is_written_into_the_low_nibble_of_dwInfoFlags()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        BalloonTipIcon[] severities =
        [
            BalloonTipIcon.None,
            BalloonTipIcon.Info,
            BalloonTipIcon.Warning,
            BalloonTipIcon.Error,
        ];

        foreach (BalloonTipIcon severity in severities)
        {
            trayIcon.ShowBalloonTip("title", "text", severity);

            NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

            Assert.Equal((uint)severity, data.dwInfoFlags & ShellConstants.NIIF_ICON_MASK);
        }

        // The four values are exactly the four the mask selects: a fifth severity would be
        // unreachable through this surface.
        Assert.Equal(
            [0u, 1u, 2u, 3u],
            severities.Select(severity => (uint)severity));
    }

    /// <summary>
    /// <see cref="BalloonTipOptions.NoSound"/> and <see cref="BalloonTipOptions.RespectQuietTime"/>
    /// are <c>NIIF_*</c> bits and are OR-ed into <c>dwInfoFlags</c> next to the severity.
    /// </summary>
    [StaFact]
    public void NoSound_and_RespectQuietTime_set_their_NIIF_bits_in_dwInfoFlags()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        trayIcon.ShowBalloonTip(
            "title",
            "text",
            BalloonTipIcon.Warning,
            BalloonTipOptions.NoSound | BalloonTipOptions.RespectQuietTime);

        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal(
            (uint)BalloonTipIcon.Warning | ShellConstants.NIIF_NOSOUND | ShellConstants.NIIF_RESPECT_QUIET_TIME,
            data.dwInfoFlags);
        Assert.Equal(BalloonTipIcon.Warning, (BalloonTipIcon)(data.dwInfoFlags & ShellConstants.NIIF_ICON_MASK));
    }

    /// <summary>
    /// <see cref="BalloonTipOptions.Realtime"/> sets <c>NIF_REALTIME</c> in <c>uFlags</c> and
    /// <b>not</b> in <c>dwInfoFlags</c>.
    /// </summary>
    /// <remarks>
    /// This is the placement rule the whole options type documents, and the two fields share bit
    /// numbers, so a wrong placement cannot be spotted by reading the value alone: in
    /// <c>dwInfoFlags</c> that bit would ask the shell for a custom user icon (<c>NIIF_USER</c>)
    /// instead of a realtime balloon.
    /// </remarks>
    [StaFact]
    public void Realtime_sets_NIF_REALTIME_in_uFlags_and_never_in_dwInfoFlags()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        trayIcon.ShowBalloonTip("title", "text", BalloonTipIcon.Info, BalloonTipOptions.Realtime);

        ShellCall call = shell.ShellNotifyIconCalls[^1];
        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal(ShellConstants.NIF_INFO | ShellConstants.NIF_REALTIME, call.Flags);
        Assert.Equal(ShellConstants.NIF_INFO | ShellConstants.NIF_REALTIME, data.uFlags);
        Assert.Equal(ShellConstants.NIIF_INFO, data.dwInfoFlags);
        Assert.True((data.uFlags & ShellConstants.NIF_REALTIME) != 0);
        Assert.True((data.dwInfoFlags & ShellConstants.NIF_REALTIME) == 0);
    }

    /// <summary>
    /// The balloon call carries no timeout, no custom balloon icon and no protocol version: the
    /// union slot is left at the structure factory's initialisation and <c>hBalloonIcon</c> at zero.
    /// </summary>
    /// <remarks>
    /// The union slot is the interesting one. <c>uTimeout</c> is deprecated since Vista, so the
    /// balloon path deliberately never assigns the field; the observable statement is that the call
    /// carries the factory's zero rather than a value this library invented. The registration's own
    /// <c>NIM_SETVERSION(4)</c> call is asserted as the premise so the assertion cannot pass because
    /// the version was never written anywhere at all.
    /// </remarks>
    [StaFact]
    public void The_balloon_call_writes_neither_a_timeout_nor_a_custom_balloon_icon()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        Assert.Equal(ShellConstants.NOTIFYICON_VERSION_4, shell.ShellNotifyIconDataSnapshots[1].uTimeoutOrVersion);

        trayIcon.ShowBalloonTip("title", "text");

        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal(0u, data.uTimeoutOrVersion);
        Assert.Equal(IntPtr.Zero, data.hBalloonIcon);
    }

    /// <summary>
    /// An overlong body text is truncated to the <c>szInfo</c> capacity (255) and an overlong title
    /// to the <c>szInfoTitle</c> capacity (63).
    /// </summary>
    [StaFact]
    public void Overlong_text_and_title_are_truncated_to_the_field_capacities()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        string longText = new('x', 300);
        string longTitle = new('y', 90);

        trayIcon.ShowBalloonTip(longTitle, longText);

        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal(255, data.szInfo.Length);
        Assert.Equal(63, data.szInfoTitle.Length);
        Assert.Equal(longText[..255], data.szInfo);
        Assert.Equal(longTitle[..63], data.szInfoTitle);
    }

    /// <summary>
    /// A truncation cut never lands between the halves of a surrogate pair, in either balloon
    /// string: half a pair is an invalid string the shell renders as a replacement character.
    /// </summary>
    /// <remarks>
    /// The inputs are built so that the character at the cut index is the high half of a pair, which
    /// is the only way the rule can be exercised: a string whose 255th unit is a low surrogate or an
    /// ordinary character is cut normally.
    /// </remarks>
    [StaFact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        // Index 254 is the high half of the emoji, so the 255-unit cut would split it.
        string text = new string('a', 254) + char.ConvertFromUtf32(0x1F600) + new string('b', 10);

        // Index 62 plays the same role for the title.
        string title = new string('c', 62) + char.ConvertFromUtf32(0x1F600) + new string('d', 10);

        trayIcon.ShowBalloonTip(title, text);

        NOTIFYICONDATAW data = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.Equal(254, data.szInfo.Length);
        Assert.Equal(62, data.szInfoTitle.Length);
        Assert.False(char.IsHighSurrogate(data.szInfo[^1]));
        Assert.False(char.IsHighSurrogate(data.szInfoTitle[^1]));
        Assert.False(char.IsLowSurrogate(data.szInfo[^1]));
        Assert.False(char.IsLowSurrogate(data.szInfoTitle[^1]));
    }

    /// <summary>
    /// A <see langword="null"/> or empty body text is refused before anything reaches the shell, and
    /// the message says why: an empty <c>szInfo</c> under <c>NIF_INFO</c> removes the balloon that is
    /// showing, so an empty call would be a silent delete.
    /// </summary>
    [StaFact]
    public void Empty_or_null_text_is_refused_with_no_shell_call()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        int callsBefore = shell.Calls.Count;

        ArgumentException nullText = Assert.Throws<ArgumentException>(() => trayIcon.ShowBalloonTip("title", null!));
        ArgumentException emptyText = Assert.Throws<ArgumentException>(() => trayIcon.ShowBalloonTip("title", string.Empty));

        Assert.Equal("text", nullText.ParamName);
        Assert.Equal("text", emptyText.ParamName);
        Assert.Contains("removes", emptyText.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("balloon", emptyText.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(callsBefore, shell.Calls.Count);
    }

    /// <summary>
    /// The text guard runs before the registration guard: an instance that was never registered and
    /// an empty text report the argument problem, not the registration problem.
    /// </summary>
    /// <remarks>
    /// The ordering is part of the contract because the two refusals mean different things to a
    /// caller: "the call is malformed" is a bug in the call, "there is no icon to show a balloon
    /// for" is a lifecycle error.
    /// </remarks>
    [StaFact]
    public void The_text_guard_precedes_the_registration_guard()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => trayIcon.ShowBalloonTip("title", string.Empty));

        Assert.Equal("text", exception.ParamName);
        Assert.Empty(shell.Calls);
    }

    /// <summary>
    /// An instance that was never registered refuses the balloon with
    /// <see cref="InvalidOperationException"/>, names the missing registration, makes no shell call
    /// and is deliberately not a <see cref="TrayIconException"/>.
    /// </summary>
    [StaFact]
    public void A_detached_icon_is_refused_with_no_shell_call()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell);

        var exception = Assert.Throws<InvalidOperationException>(() => trayIcon.ShowBalloonTip("title", "text"));

        Assert.Contains("registered", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<TrayIconException>(exception);
        Assert.Empty(shell.Calls);
    }

    /// <summary>
    /// A disposed instance refuses the balloon the same way: disposal is the other way the
    /// registration guard is reached, so no separate disposed branch exists.
    /// </summary>
    [StaFact]
    public void A_disposed_icon_is_refused_with_no_shell_call()
    {
        TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        trayIcon.Dispose();

        int callsAfterDispose = shell.Calls.Count;

        var exception = Assert.Throws<InvalidOperationException>(() => trayIcon.ShowBalloonTip("title", "text"));

        Assert.Contains("registered", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(callsAfterDispose, shell.Calls.Count);
    }

    /// <summary>
    /// An instance created without a dispatcher refuses the balloon with the existing marshalling
    /// exception and makes no shell call - a balloon needs the thread that owns the host window.
    /// </summary>
    [StaFact]
    public void An_instance_without_a_dispatcher_is_refused()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell, dispatcher: null);

        var exception = Assert.Throws<InvalidOperationException>(() => trayIcon.ShowBalloonTip("title", "text"));

        Assert.Contains("Dispatcher", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<TrayIconException>(exception);
        Assert.Empty(shell.Calls);
    }

    /// <summary>
    /// A balloon shown from a background thread reaches the shell on the dispatcher thread, which is
    /// the only thread the host window and the shell registration belong to (R015).
    /// </summary>
    [DispatcherFact]
    public void A_background_thread_balloon_reaches_the_shell_on_the_dispatcher_thread()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        int dispatcherThread = Environment.CurrentManagedThreadId;
        int callsBefore = shell.ShellNotifyIconCalls.Count;

        RunOnBackgroundThread(
            () => trayIcon.ShowBalloonTip("from a worker", "marshalled"),
            out Exception? failure,
            out int backgroundThread);

        Assert.Null(failure);
        Assert.NotEqual(dispatcherThread, backgroundThread);

        ShellCall call = Assert.Single(shell.ShellNotifyIconCalls.Skip(callsBefore));

        Assert.Equal(ShellConstants.NIM_MODIFY, call.Message);
        Assert.Equal(dispatcherThread, call.ThreadId);
        Assert.Equal("marshalled", shell.ShellNotifyIconDataSnapshots[^1].szInfo);
    }

    /// <summary>
    /// A balloon the shell refuses is retried exactly once and then surfaced through
    /// <see cref="TrayIcon.TrayError"/> with <c>Retried = true</c>; nothing escapes
    /// <see cref="TrayIcon.ShowBalloonTip"/>.
    /// </summary>
    /// <remarks>
    /// This is D008's runtime policy applied to the balloon path, and the count assertion is the
    /// policy: one attempt plus exactly one retry, no loop.
    /// </remarks>
    [StaFact]
    public void A_refused_balloon_is_retried_once_then_surfaced_as_TrayError()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out FakeShellApi shell);

        var raised = new List<TrayErrorEventArgs>();
        trayIcon.TrayError += (_, e) => raised.Add(e);

        int callsBefore = shell.ShellNotifyIconCalls.Count;

        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        trayIcon.ShowBalloonTip("title", "text");

        ShellCall[] attempts = [.. shell.ShellNotifyIconCalls.Skip(callsBefore)];

        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, attempt => Assert.Equal(ShellConstants.NIM_MODIFY, attempt.Message));

        TrayErrorEventArgs observed = Assert.Single(raised);

        Assert.Equal(TrayIconException.OperationModify, observed.Operation);
        Assert.True(observed.Retried);
        Assert.Equal(shell.LastErrorToReport, observed.Win32ErrorCode);
    }

    /// <summary>
    /// The balloon routed events are a Bubble/Tunnel pair with <see cref="RoutedEventArgs"/> as the
    /// handler type and <see cref="TrayIcon"/> as the owner, and the CLR accessors subscribe and
    /// unsubscribe them.
    /// </summary>
    /// <remarks>
    /// This is the S04-to-S06 edge: markup wires <c>BalloonTipClicked="Handler"</c>, so the name,
    /// the handler type and the owner have to be the ones the XAML resolver expects. The raise here
    /// is a direct <see cref="UIElement.RaiseEvent"/> on the element, not the shell callback path -
    /// routing an actual balloon callback is the next task's contract.
    /// </remarks>
    [StaFact]
    public void The_balloon_click_events_are_a_bubble_and_tunnel_pair_with_CLR_accessors()
    {
        Assert.Equal("BalloonTipClicked", TrayIcon.BalloonTipClickedEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, TrayIcon.BalloonTipClickedEvent.RoutingStrategy);
        Assert.Equal(typeof(EventHandler<RoutedEventArgs>), TrayIcon.BalloonTipClickedEvent.HandlerType);
        Assert.Equal(typeof(TrayIcon), TrayIcon.BalloonTipClickedEvent.OwnerType);

        Assert.Equal("PreviewBalloonTipClicked", TrayIcon.PreviewBalloonTipClickedEvent.Name);
        Assert.Equal(RoutingStrategy.Tunnel, TrayIcon.PreviewBalloonTipClickedEvent.RoutingStrategy);
        Assert.Equal(typeof(EventHandler<RoutedEventArgs>), TrayIcon.PreviewBalloonTipClickedEvent.HandlerType);
        Assert.Equal(typeof(TrayIcon), TrayIcon.PreviewBalloonTipClickedEvent.OwnerType);

        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell);

        var observed = new List<RoutedEvent>();

        EventHandler<RoutedEventArgs> handler = (_, e) => observed.Add(e.RoutedEvent);

        trayIcon.BalloonTipClicked += handler;
        trayIcon.PreviewBalloonTipClicked += handler;

        trayIcon.RaiseEvent(new RoutedEventArgs(TrayIcon.PreviewBalloonTipClickedEvent));
        trayIcon.RaiseEvent(new RoutedEventArgs(TrayIcon.BalloonTipClickedEvent));

        Assert.Equal(
            [TrayIcon.PreviewBalloonTipClickedEvent, TrayIcon.BalloonTipClickedEvent],
            observed);

        trayIcon.BalloonTipClicked -= handler;
        trayIcon.PreviewBalloonTipClicked -= handler;

        observed.Clear();

        trayIcon.RaiseEvent(new RoutedEventArgs(TrayIcon.PreviewBalloonTipClickedEvent));
        trayIcon.RaiseEvent(new RoutedEventArgs(TrayIcon.BalloonTipClickedEvent));

        Assert.Empty(observed);
    }

    /// <summary>
    /// Creates a <see cref="TrayIcon"/> registered over a scripted shell, so the balloon guard cases
    /// and the marshal assertions have a real registration to work against.
    /// </summary>
    /// <param name="shell">Receives the fake seam the icon talks to.</param>
    /// <returns>The registered icon, owned by the caller.</returns>
    /// <remarks>
    /// The icon carries an <see cref="TrayIcon.IconSource"/> so the registration is the ordinary
    /// one an application produces; nothing here depends on the icon image itself.
    /// </remarks>
    private static TrayIcon CreateRegisteredIcon(out FakeShellApi shell)
    {
        shell = new FakeShellApi();

        var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(0x20, 0x60, 0xA0),
            Visible = true,
        };

        Assert.True(trayIcon.IsRegistered);
        Assert.NotEqual(IntPtr.Zero, trayIcon.HostHandle);

        return trayIcon;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a fresh MTA background thread while pumping the dispatcher
    /// until that thread has finished.
    /// </summary>
    /// <param name="action">The work to run on the background thread.</param>
    /// <param name="failure">The exception the work raised, if any.</param>
    /// <param name="threadId">The background thread's managed id, as observed by the work.</param>
    /// <remarks>
    /// The pump is what lets the background thread's synchronous <c>Dispatcher.Invoke</c> complete;
    /// without it the two threads would deadlock instead of failing, which is the least useful
    /// outcome. The shape is copied from <see cref="TrayIconMarshallingTests"/> deliberately, so both
    /// suites measure marshalling the same way.
    /// </remarks>
    private static void RunOnBackgroundThread(Action action, out Exception? failure, out int threadId)
    {
        Exception? captured = null;
        int id = 0;

        var thread = new Thread(() =>
        {
            id = Environment.CurrentManagedThreadId;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        DispatcherHarness.PumpUntil(() => !thread.IsAlive);
        thread.Join(TimeSpan.FromSeconds(10));

        failure = captured;
        threadId = id;
    }

    /// <summary>Builds a square, single-colour, fully opaque image.</summary>
    /// <param name="b">The blue channel value.</param>
    /// <param name="g">The green channel value.</param>
    /// <param name="r">The red channel value.</param>
    /// <returns>The image.</returns>
    private static BitmapSource CreateSolid(byte b, byte g, byte r)
    {
        var pixels = new byte[IconSize * IconSize * 4];

        for (int i = 0; i < IconSize * IconSize; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(IconSize, IconSize, 96, 96, PixelFormats.Bgra32, null, pixels, IconSize * 4);
    }
}
