using System.Diagnostics;
using System.Windows;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S04 balloon half of the callback sink: a <c>NIN_BALLOON*</c> callback arriving at the real
/// host window becomes the cancellable <see cref="TrayIcon.PreviewBalloonTipClicked"/> /
/// <see cref="TrayIcon.BalloonTipClicked"/> pair, or a Verbose-only lifecycle line that raises no
/// public event at all.
/// </summary>
/// <remarks>
/// <para>
/// Same injection discipline as <see cref="TrayIconClickEventsTests"/>: a same-thread
/// <c>SendMessage</c> into the window the icon really registered
/// (<see cref="TrayIcon.HostHandle"/>) runs the real window procedure, the real decoder, the real
/// icon-id filter and the real routed-event raiser synchronously before the call returns; only the
/// shell seam is a fake. Values observed inside a handler are recorded and asserted after the send,
/// never asserted inside the handler: it runs inside a window procedure, where a thrown assertion
/// would be an unhandled exception in the message path rather than a test failure.
/// </para>
/// <para>
/// This class belongs to the non-parallel trace collection for the same reason the click tests do:
/// one test raises the process-wide trace level, and <c>NotifyIconTraceTests</c> asserts the default.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class BalloonCallbackTests
{
    /// <summary>
    /// A <c>NIN_BALLOONUSERCLICK</c> callback carrying the icon's own id raises
    /// <c>PreviewBalloonTipClicked</c> before <c>BalloonTipClicked</c>, each exactly once, and each
    /// carrying its own routed event - two instances, never one raised twice.
    /// </summary>
    [StaFact]
    public void A_balloon_user_click_raises_the_preview_before_the_bubble_event_once_each()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var order = new List<string>();
        RoutedEvent? previewRoutedEvent = null;
        RoutedEvent? mainRoutedEvent = null;

        trayIcon.PreviewBalloonTipClicked += (_, e) =>
        {
            order.Add(e.RoutedEvent.Name);
            previewRoutedEvent = e.RoutedEvent;
        };

        trayIcon.BalloonTipClicked += (_, e) =>
        {
            order.Add(e.RoutedEvent.Name);
            mainRoutedEvent = e.RoutedEvent;
        };

        SendCallback(trayIcon, ShellNotifications.NIN_BALLOONUSERCLICK, iconId);

        Assert.Equal(
            [TrayIcon.PreviewBalloonTipClickedEventName, TrayIcon.BalloonTipClickedEventName],
            order);
        Assert.Same(TrayIcon.PreviewBalloonTipClickedEvent, previewRoutedEvent);
        Assert.Same(TrayIcon.BalloonTipClickedEvent, mainRoutedEvent);
    }

    /// <summary>
    /// A Preview handler that marks the balloon click handled suppresses
    /// <c>BalloonTipClicked</c> entirely - the cancellation contract the milestone's criterion and
    /// the markup path both rely on.
    /// </summary>
    [StaFact]
    public void A_handled_preview_suppresses_the_balloon_clicked_event()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var order = new List<string>();

        trayIcon.PreviewBalloonTipClicked += (_, e) =>
        {
            order.Add(e.RoutedEvent.Name);
            e.Handled = true;
        };

        trayIcon.BalloonTipClicked += (_, e) => order.Add(e.RoutedEvent.Name);

        SendCallback(trayIcon, ShellNotifications.NIN_BALLOONUSERCLICK, iconId);

        Assert.Equal([TrayIcon.PreviewBalloonTipClickedEventName], order);
    }

    /// <summary>
    /// The three balloon lifecycle codes raise no balloon event and no click event, are therefore
    /// reported nowhere but the Verbose trace, and leave the S02 click pipeline they share the sink
    /// with completely untouched.
    /// </summary>
    /// <param name="eventCode">The lifecycle code the shell reports for this phase.</param>
    [StaTheory]
    [InlineData(ShellNotifications.NIN_BALLOONSHOW)]
    [InlineData(ShellNotifications.NIN_BALLOONHIDE)]
    [InlineData(ShellNotifications.NIN_BALLOONTIMEOUT)]
    public void A_balloon_lifecycle_code_raises_neither_event_and_leaves_the_click_pipeline_working(uint eventCode)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var balloonEvents = new List<string>();
        var clickEvents = new List<string>();

        trayIcon.PreviewBalloonTipClicked += (_, e) => balloonEvents.Add(e.RoutedEvent.Name);
        trayIcon.BalloonTipClicked += (_, e) => balloonEvents.Add(e.RoutedEvent.Name);
        SubscribeToAllClicks(
            trayIcon,
            (_, e) => clickEvents.Add(e.RoutedEvent.Name),
            (_, e) => clickEvents.Add(e.RoutedEvent.Name));

        SendCallback(trayIcon, eventCode, iconId);

        Assert.Empty(balloonEvents);
        Assert.Empty(clickEvents);

        // The sink survived the lifecycle code: the next real click is delivered whole in both
        // phases, so the balloon branch changed nothing about the pipeline it shares the method
        // with. This is the D033 contract: lifecycle news is traced, not raised.
        SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId);

        Assert.Equal(
            [TrayIcon.PreviewTrayLeftClickEventName, TrayIcon.TrayLeftClickEventName],
            clickEvents);
    }

    /// <summary>
    /// A balloon code encoding another icon's id raises nothing on either icon: the icon-id filter
    /// applies to balloon callbacks exactly as it does to clicks.
    /// </summary>
    /// <param name="eventCode">The balloon event code to place in <c>LOWORD(lParam)</c>.</param>
    [StaTheory]
    [InlineData(ShellNotifications.NIN_BALLOONUSERCLICK)]
    [InlineData(ShellNotifications.NIN_BALLOONSHOW)]
    [InlineData(ShellNotifications.NIN_BALLOONHIDE)]
    [InlineData(ShellNotifications.NIN_BALLOONTIMEOUT)]
    public void A_balloon_code_encoded_for_another_icon_raises_nothing(uint eventCode)
    {
        using TrayIcon first = CreateRegisteredIcon(out _, out uint firstIconId);
        using TrayIcon second = CreateRegisteredIcon(out _, out uint secondIconId);

        Assert.NotEqual(firstIconId, secondIconId);

        var raised = new List<string>();

        first.PreviewBalloonTipClicked += (_, e) => raised.Add(e.RoutedEvent.Name);
        first.BalloonTipClicked += (_, e) => raised.Add(e.RoutedEvent.Name);
        second.PreviewBalloonTipClicked += (_, e) => raised.Add(e.RoutedEvent.Name);
        second.BalloonTipClicked += (_, e) => raised.Add(e.RoutedEvent.Name);

        SendCallback(first, eventCode, secondIconId);

        Assert.Empty(raised);

        // The filter is a filter, not a broken sink: the icon's own balloon click still arrives.
        SendCallback(first, ShellNotifications.NIN_BALLOONUSERCLICK, firstIconId);

        Assert.Equal(
            [TrayIcon.PreviewBalloonTipClickedEventName, TrayIcon.BalloonTipClickedEventName],
            raised);
    }

    /// <summary>
    /// A balloon code delivered on a message that is not this icon's callback message raises
    /// nothing: the callback-id check happens before either parameter is decoded.
    /// </summary>
    /// <param name="message">The message id to send to the host window.</param>
    [StaTheory]
    [InlineData(ShellConstants.WM_USER)]
    [InlineData(ShellConstants.WM_USER + 7)]
    [InlineData(ShellConstants.TrayCallbackMessage + 1)]
    public void A_balloon_code_on_a_message_that_is_not_the_callback_message_raises_nothing(uint message)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var balloonEvents = new List<string>();
        var clickEvents = new List<string>();

        trayIcon.PreviewBalloonTipClicked += (_, e) => balloonEvents.Add(e.RoutedEvent.Name);
        trayIcon.BalloonTipClicked += (_, e) => balloonEvents.Add(e.RoutedEvent.Name);
        SubscribeToAllClicks(
            trayIcon,
            (_, e) => clickEvents.Add(e.RoutedEvent.Name),
            (_, e) => clickEvents.Add(e.RoutedEvent.Name));

        Win32.SendMessage(
            trayIcon.HostHandle,
            message,
            Anchor(100, 200),
            Payload(ShellNotifications.NIN_BALLOONUSERCLICK, iconId));

        Assert.Empty(balloonEvents);
        Assert.Empty(clickEvents);

        // The sink is still alive and still decodes its own callback message.
        SendCallback(trayIcon, ShellNotifications.NIN_BALLOONUSERCLICK, iconId);

        Assert.Equal(
            [TrayIcon.PreviewBalloonTipClickedEventName, TrayIcon.BalloonTipClickedEventName],
            balloonEvents);
    }

    /// <summary>
    /// The three lifecycle codes are each traced with exactly one Verbose line naming the code, the
    /// icon id and the phase's meaning; none of them is ever an Error line; and the user click -
    /// being a mapped event - produces no trace line at all.
    /// </summary>
    /// <remarks>
    /// The severity is asserted structurally, on the recorded
    /// <see cref="TraceEventType"/> and event id, not by hoping the text reads right: the point is
    /// that the line sits at Verbose and can never be mistaken for a failure (MEM026).
    /// </remarks>
    [StaFact]
    public void Balloon_lifecycle_codes_are_traced_at_verbose_never_as_errors_and_a_user_click_traces_nothing()
    {
        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        // Registered before the level is raised, so the registration contributes no line of its own.
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        int balloonClicks = 0;

        trayIcon.BalloonTipClicked += (_, _) => balloonClicks++;

        try
        {
            source.Switch.Level = SourceLevels.Verbose;
            source.Listeners.Add(recorder);

            SendCallback(trayIcon, ShellNotifications.NIN_BALLOONSHOW, iconId);
            SendCallback(trayIcon, ShellNotifications.NIN_BALLOONHIDE, iconId);
            SendCallback(trayIcon, ShellNotifications.NIN_BALLOONTIMEOUT, iconId);

            // The user click is a mapped event: it raises the routed pair, not a trace line.
            SendCallback(trayIcon, ShellNotifications.NIN_BALLOONUSERCLICK, iconId);

            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(recorder);
            source.Switch.Level = previousLevel;
        }

        Assert.Equal(1, balloonClicks);
        Assert.Equal(3, recorder.Events.Count);

        Assert.All(
            recorder.Events,
            line =>
            {
                Assert.Equal(TraceEventType.Verbose, line.EventType);
                Assert.Equal(NotifyIconTrace.VerboseEventId, line.EventId);
                Assert.NotNull(line.Message);
                Assert.Contains($"icon id {iconId}", line.Message, StringComparison.Ordinal);
            });

        Assert.Contains(
            $"0x{ShellNotifications.NIN_BALLOONSHOW:X4}",
            recorder.Events[0].Message,
            StringComparison.Ordinal);
        Assert.Contains("about to show", recorder.Events[0].Message, StringComparison.Ordinal);

        Assert.Contains(
            $"0x{ShellNotifications.NIN_BALLOONHIDE:X4}",
            recorder.Events[1].Message,
            StringComparison.Ordinal);
        Assert.Contains("being hidden", recorder.Events[1].Message, StringComparison.Ordinal);

        Assert.Contains(
            $"0x{ShellNotifications.NIN_BALLOONTIMEOUT:X4}",
            recorder.Events[2].Message,
            StringComparison.Ordinal);
        Assert.Contains("timed out", recorder.Events[2].Message, StringComparison.Ordinal);

        // The level this class borrowed is not left raised for whatever runs next.
        Assert.Equal(SourceLevels.Warning, NotifyIconTrace.Source.Switch.Level);
    }

    /// <summary>
    /// Creates a <see cref="TrayIcon"/> registered over a scripted shell, and reports the 16-bit
    /// icon id the shell was told about.
    /// </summary>
    /// <param name="shell">Receives the fake seam the icon talks to.</param>
    /// <param name="iconId">Receives the icon id the registration carried.</param>
    /// <returns>The registered icon, owned by the caller.</returns>
    private static TrayIcon CreateRegisteredIcon(out FakeShellApi shell, out uint iconId)
    {
        shell = new FakeShellApi();

        var trayIcon = new TrayIcon(shell) { Visible = true };

        // The id the shell reports in HIWORD(lParam) is the id this registration used, so the tests
        // encode the real one instead of a guess.
        iconId = shell.ShellNotifyIconDataSnapshots[0].uID;

        Assert.NotEqual(0u, iconId);
        Assert.NotEqual(IntPtr.Zero, trayIcon.HostHandle);

        return trayIcon;
    }

    /// <summary>
    /// Sends one version-4 callback message to the icon's own host window, synchronously.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the callback.</param>
    /// <param name="eventCode">The event code for <c>LOWORD(lParam)</c>.</param>
    /// <param name="iconId">The icon id for <c>HIWORD(lParam)</c>.</param>
    /// <param name="x">The anchor x coordinate for <c>wParam</c>.</param>
    /// <param name="y">The anchor y coordinate for <c>wParam</c>.</param>
    /// <returns>The value the window procedure returned.</returns>
    private static IntPtr SendCallback(TrayIcon trayIcon, uint eventCode, uint iconId, int x = 100, int y = 200) =>
        Win32.SendMessage(trayIcon.HostHandle, ShellConstants.TrayCallbackMessage, Anchor(x, y), Payload(eventCode, iconId));

    /// <summary>
    /// Packs two screen coordinates into the <c>wParam</c> anchor the way the shell does: two
    /// <c>short</c>-sized halves, low word first.
    /// </summary>
    /// <param name="x">The x coordinate, possibly negative.</param>
    /// <param name="y">The y coordinate, possibly negative.</param>
    /// <returns>The packed parameter with a zero upper half.</returns>
    private static IntPtr Anchor(int x, int y)
    {
        uint packed = (uint)(ushort)x | ((uint)(ushort)y << 16);
        return new IntPtr((long)packed);
    }

    /// <summary>
    /// Packs an event code and a 16-bit icon id into the <c>lParam</c> payload the way the shell
    /// does: event in the low word, id in the high word.
    /// </summary>
    /// <param name="eventCode">The event code, taken from the low 16 bits.</param>
    /// <param name="iconId">The icon id, taken from the low 16 bits.</param>
    /// <returns>The packed parameter with a zero upper half.</returns>
    private static IntPtr Payload(uint eventCode, uint iconId)
    {
        uint packed = (eventCode & 0xFFFF) | ((iconId & 0xFFFF) << 16);
        return new IntPtr((long)packed);
    }

    /// <summary>
    /// Subscribes one handler to all four main click events and another to all four Preview events,
    /// so a test can assert that the balloon branch of the sink left the click pipeline untouched.
    /// </summary>
    /// <param name="trayIcon">The icon to subscribe on.</param>
    /// <param name="onMain">The handler for the four main events.</param>
    /// <param name="onPreview">The handler for the four Preview events.</param>
    private static void SubscribeToAllClicks(
        TrayIcon trayIcon,
        EventHandler<TrayIconClickEventArgs> onMain,
        EventHandler<TrayIconClickEventArgs> onPreview)
    {
        trayIcon.TrayLeftClick += onMain;
        trayIcon.TrayLeftDoubleClick += onMain;
        trayIcon.TrayRightClick += onMain;
        trayIcon.TrayMiddleClick += onMain;

        trayIcon.PreviewTrayLeftClick += onPreview;
        trayIcon.PreviewTrayLeftDoubleClick += onPreview;
        trayIcon.PreviewTrayRightClick += onPreview;
        trayIcon.PreviewTrayMiddleClick += onPreview;
    }

    /// <summary>
    /// Captures the structured trace events a listener receives, so severity and event id can be
    /// asserted rather than inferred from formatted text.
    /// </summary>
    private sealed class RecordingTraceListener : TraceListener
    {
        /// <summary>Gets the sequence of trace events this listener has received.</summary>
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
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            Events.Add((eventType, id, message));
        }
    }
}
