using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S02 vertical slice: a <c>NOTIFYICON_VERSION_4</c> callback message arriving at the real host
/// window becomes a typed, cancellable routed event on a parentless <see cref="TrayIcon"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every test injects the shell's message with a same-thread <c>SendMessage</c> into the window the
/// icon really registered (<see cref="TrayIcon.HostHandle"/>), which runs the window procedure - and
/// therefore the whole pipeline from constants to sink to decoder to icon-id filter to args to
/// Tunnel delivery to raiser-implemented suppression to Bubble delivery - synchronously before the
/// call returns. Nothing is pumped and nothing is mocked along that path; only the shell seam is a
/// fake, which is the seam S01 already established for the same reason.
/// </para>
/// <para>
/// Values observed inside a click handler are recorded and asserted <em>after</em> the send, never
/// asserted inside the handler: the handler runs inside a window procedure, where a thrown
/// assertion would be an unhandled exception in the message path rather than a test failure.
/// </para>
/// <para>
/// <b>Why this class is in a non-parallel collection.</b> One test raises the process-wide
/// <see cref="System.Diagnostics.TraceSource"/> level of the library's trace channel to observe the
/// Verbose click-stream line, and <c>NotifyIconTraceTests</c> asserts that the same channel defaults
/// to <see cref="System.Diagnostics.SourceLevels.Warning"/>. A collection that disables
/// parallelization keeps those two classes from running at the same time; without it the level
/// assertion could observe a moment that belongs to this class.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class TrayIconClickEventsTests
{
    /// <summary>
    /// A Tunnel probe event owned by this test class, used by the WPF-premise test so it measures the
    /// routing behaviour itself instead of borrowing the product's events.
    /// </summary>
    private static readonly RoutedEvent TunnelProbeEvent = EventManager.RegisterRoutedEvent(
        "TrayIconClickEventsTestsTunnelProbe",
        RoutingStrategy.Tunnel,
        typeof(EventHandler<RoutedEventArgs>),
        typeof(TrayIconClickEventsTests));

    /// <summary>
    /// The Bubble partner of <see cref="TunnelProbeEvent"/>: a pair like the one the product raises,
    /// with the suppression implemented by the raiser in the same shape.
    /// </summary>
    private static readonly RoutedEvent BubbleProbeEvent = EventManager.RegisterRoutedEvent(
        "TrayIconClickEventsTestsBubbleProbe",
        RoutingStrategy.Bubble,
        typeof(EventHandler<RoutedEventArgs>),
        typeof(TrayIconClickEventsTests));

    /// <summary>
    /// The First Proof, and the reason this task's tests were written before its implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slice's one architectural unknown is whether a manually raised
    /// <see cref="RoutingStrategy.Tunnel"/> event delivers to the handlers of an element that has no
    /// parent and no visual tree, and whether the raiser - not the framework - can implement
    /// Preview-to-Bubble suppression for such a pair. WPF pairs a Preview with its Bubble twin only
    /// for input it stages itself, so if a manual Tunnel raise silently delivered nothing, every
    /// Preview event this slice ships would be decorative and the cancellation contract would be
    /// false while every other test still passed.
    /// </para>
    /// <para>
    /// The assertions are therefore about order, payload and cancellation, not about the events
    /// being registered: preview handler first, bubble handler second, both on the click that was
    /// sent, and no bubble handler at all once a preview handler marks the args handled.
    /// </para>
    /// </remarks>
    [StaFact]
    public void First_proof_a_parentless_element_delivers_preview_then_bubble_and_a_handled_preview_suppresses_the_main_event()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        int owningThreadId = Environment.CurrentManagedThreadId;
        var phases = new List<string>();
        (MouseButton Button, int ClickCount, Point ScreenAnchor)? previewPayload = null;
        (MouseButton Button, int ClickCount, Point ScreenAnchor)? mainPayload = null;
        int previewThreadId = 0;
        int mainThreadId = 0;
        RoutedEvent? previewRoutedEvent = null;
        RoutedEvent? mainRoutedEvent = null;

        void OnPreview(object? sender, TrayIconClickEventArgs e)
        {
            phases.Add("preview");
            previewPayload = (e.Button, e.ClickCount, e.ScreenAnchor);
            previewThreadId = Environment.CurrentManagedThreadId;
            previewRoutedEvent = e.RoutedEvent;
        }

        void OnMain(object? sender, TrayIconClickEventArgs e)
        {
            phases.Add("bubble");
            mainPayload = (e.Button, e.ClickCount, e.ScreenAnchor);
            mainThreadId = Environment.CurrentManagedThreadId;
            mainRoutedEvent = e.RoutedEvent;
        }

        trayIcon.PreviewTrayLeftClick += OnPreview;
        trayIcon.TrayLeftClick += OnMain;

        SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId, x: 100, y: 200);

        Assert.Equal(["preview", "bubble"], phases);
        Assert.Equal((MouseButton.Left, 1, new Point(100, 200)), previewPayload);
        Assert.Equal((MouseButton.Left, 1, new Point(100, 200)), mainPayload);

        // The typed events are the registered ones, so a handler's args route to the event it was
        // subscribed for.
        Assert.Same(TrayIcon.PreviewTrayLeftClickEvent, previewRoutedEvent);
        Assert.Same(TrayIcon.TrayLeftClickEvent, mainRoutedEvent);

        // Same-thread injection, not pumping: the sink runs on the thread that created the host,
        // which is the thread running this test.
        Assert.Equal(owningThreadId, previewThreadId);
        Assert.Equal(owningThreadId, mainThreadId);

        // The suppression half: a fresh pair of handlers, the preview one cancelling.
        trayIcon.PreviewTrayLeftClick -= OnPreview;
        trayIcon.TrayLeftClick -= OnMain;

        int previewAfterCancel = 0;
        int mainAfterCancel = 0;

        trayIcon.PreviewTrayLeftClick += (_, e) =>
        {
            previewAfterCancel++;
            e.Handled = true;
        };

        trayIcon.TrayLeftClick += (_, _) => mainAfterCancel++;

        SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId, x: 100, y: 200);

        Assert.Equal(1, previewAfterCancel);
        Assert.Equal(0, mainAfterCancel);
    }

    /// <summary>
    /// The WPF premise the First Proof rests on, measured on its own: a manually raised
    /// <see cref="RoutingStrategy.Tunnel"/> event reaches the handlers of an element with no parent,
    /// and WPF does <em>not</em> raise the Bubble twin for it.
    /// </summary>
    /// <remarks>
    /// Separating this from the tray test is what makes the risk retirement durable: if a future
    /// runtime changed how a manual Tunnel raise routes, this test would fail with "the premise"
    /// rather than with a click that quietly stopped arriving, and the raiser-implemented suppression
    /// in <see cref="TrayIcon"/> would be shown to be the load-bearing part rather than an accident of
    /// ordering.
    /// </remarks>
    [StaFact]
    public void The_WPF_premise_a_manually_raised_tunnel_event_delivers_on_a_parentless_element()
    {
        var element = new FrameworkElement();
        var order = new List<string>();

        element.AddHandler(TunnelProbeEvent, new EventHandler<RoutedEventArgs>((_, _) => order.Add("tunnel")));
        element.AddHandler(BubbleProbeEvent, new EventHandler<RoutedEventArgs>((_, _) => order.Add("bubble")));

        // A Tunnel raise on its own delivers to the element's own handlers - and does not raise the
        // Bubble event, because WPF pairs the two only for input it stages itself. That absence is
        // why TrayIcon.OnHostMessage raises the main event explicitly and honours Handled itself.
        element.RaiseEvent(new RoutedEventArgs(TunnelProbeEvent, element));

        Assert.Equal(["tunnel"], order);

        // The raiser-implemented suppression, in the same shape the tray pipeline uses.
        var previewArgs = new RoutedEventArgs(TunnelProbeEvent, element);
        element.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            element.RaiseEvent(new RoutedEventArgs(BubbleProbeEvent, element));
        }

        Assert.Equal(["tunnel", "tunnel", "bubble"], order);

        order.Clear();

        // A preview handler that cancels: the main event is not raised at all, because the raiser
        // checked. Nothing in WPF would have done this for a manual pair.
        element.AddHandler(TunnelProbeEvent, new EventHandler<RoutedEventArgs>((_, e) => e.Handled = true));

        var cancelledArgs = new RoutedEventArgs(TunnelProbeEvent, element);
        element.RaiseEvent(cancelledArgs);

        if (!cancelledArgs.Handled)
        {
            element.RaiseEvent(new RoutedEventArgs(BubbleProbeEvent, element));
        }

        Assert.Equal(["tunnel"], order);
    }

    /// <summary>
    /// Each of the four click types raises exactly its own typed event, carrying the button, the click
    /// count and the anchor the shell reported - including a sign-extended negative anchor, which is
    /// the notification area on a monitor left of the primary one.
    /// </summary>
    /// <param name="eventCode">The event code the shell sends for this click type.</param>
    /// <param name="expectedEventName">The routed event that must fire, and only it.</param>
    /// <param name="expectedButton">The button the payload must report.</param>
    /// <param name="expectedClickCount">The click count the payload must report.</param>
    [StaTheory]
    [InlineData(ShellConstants.WM_LBUTTONUP, TrayIcon.TrayLeftClickEventName, MouseButton.Left, 1)]
    [InlineData(ShellConstants.WM_LBUTTONDBLCLK, TrayIcon.TrayLeftDoubleClickEventName, MouseButton.Left, 2)]
    [InlineData(ShellConstants.WM_CONTEXTMENU, TrayIcon.TrayRightClickEventName, MouseButton.Right, 1)]
    [InlineData(ShellConstants.WM_MBUTTONUP, TrayIcon.TrayMiddleClickEventName, MouseButton.Middle, 1)]
    public void Each_click_type_raises_exactly_its_typed_event_with_the_reported_payload(
        uint eventCode,
        string expectedEventName,
        MouseButton expectedButton,
        int expectedClickCount)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var fired = new List<(string EventName, MouseButton Button, int ClickCount, Point Anchor, int ThreadId)>();

        void Record(object? _, TrayIconClickEventArgs e) =>
            fired.Add((e.RoutedEvent.Name, e.Button, e.ClickCount, e.ScreenAnchor, Environment.CurrentManagedThreadId));

        SubscribeToAllClicks(trayIcon, Record, (_, _) => { });

        SendCallback(trayIcon, eventCode, iconId, x: -800, y: 640);

        (string EventName, MouseButton Button, int ClickCount, Point Anchor, int ThreadId) single = Assert.Single(fired);

        Assert.Equal(expectedEventName, single.EventName);
        Assert.Equal(expectedButton, single.Button);
        Assert.Equal(expectedClickCount, single.ClickCount);
        Assert.Equal(new Point(-800, 640), single.Anchor);
        Assert.Equal(Environment.CurrentManagedThreadId, single.ThreadId);
    }

    /// <summary>
    /// A double click is distinguishable from a single one by the payload as well as by the event:
    /// the double-click event reports a click count of 2 and the single-click event does not fire for
    /// the same message.
    /// </summary>
    [StaFact]
    public void A_double_click_reports_a_click_count_of_two()
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        int singleClicks = 0;
        int doubleClicks = 0;
        int reportedCount = 0;

        trayIcon.TrayLeftClick += (_, _) => singleClicks++;
        trayIcon.TrayLeftDoubleClick += (_, e) =>
        {
            doubleClicks++;
            reportedCount = e.ClickCount;
        };

        SendCallback(trayIcon, ShellConstants.WM_LBUTTONDBLCLK, iconId, x: 40, y: 50);

        Assert.Equal(0, singleClicks);
        Assert.Equal(1, doubleClicks);
        Assert.Equal(2, reportedCount);
    }

    /// <summary>
    /// Every click type is cancellable from its own Preview event: the preview fires, and the main
    /// event never does.
    /// </summary>
    /// <param name="eventCode">The event code the shell sends for this click type.</param>
    /// <param name="expectedPreviewName">The Preview event that must fire, and only it.</param>
    [StaTheory]
    [InlineData(ShellConstants.WM_LBUTTONUP, TrayIcon.PreviewTrayLeftClickEventName)]
    [InlineData(ShellConstants.WM_LBUTTONDBLCLK, TrayIcon.PreviewTrayLeftDoubleClickEventName)]
    [InlineData(ShellConstants.WM_CONTEXTMENU, TrayIcon.PreviewTrayRightClickEventName)]
    [InlineData(ShellConstants.WM_MBUTTONUP, TrayIcon.PreviewTrayMiddleClickEventName)]
    public void Preview_cancellation_suppresses_the_main_event_for_every_click_type(uint eventCode, string expectedPreviewName)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var previews = new List<string>();
        var mainEvents = new List<string>();

        void Cancel(object? _, TrayIconClickEventArgs e)
        {
            previews.Add(e.RoutedEvent.Name);
            e.Handled = true;
        }

        SubscribeToAllClicks(trayIcon, (_, e) => mainEvents.Add(e.RoutedEvent.Name), Cancel);

        SendCallback(trayIcon, eventCode, iconId);

        Assert.Equal([expectedPreviewName], previews);
        Assert.Empty(mainEvents);
    }

    /// <summary>
    /// A callback that encodes another icon's id raises nothing on this icon, even though the payload
    /// is otherwise a perfect left click.
    /// </summary>
    /// <remarks>
    /// Today each icon owns its own host window so this cannot happen through the shell; the filter is
    /// what keeps a future shared host from delivering one icon's clicks to another icon's handlers.
    /// The test also proves the icon's own clicks still arrive afterwards, so a mismatch is not
    /// mistaken for a permanently broken sink.
    /// </remarks>
    [StaFact]
    public void A_click_encoded_for_another_icon_raises_nothing()
    {
        using TrayIcon first = CreateRegisteredIcon(out _, out uint firstIconId);
        using TrayIcon second = CreateRegisteredIcon(out _, out uint secondIconId);

        Assert.NotEqual(firstIconId, secondIconId);

        var fired = new List<MouseButton>();

        first.TrayLeftClick += (_, e) => fired.Add(e.Button);
        second.TrayLeftClick += (_, e) => fired.Add(e.Button);

        SendCallback(first, ShellConstants.WM_LBUTTONUP, secondIconId);

        Assert.Empty(fired);

        SendCallback(first, ShellConstants.WM_LBUTTONUP, firstIconId);
        SendCallback(second, ShellConstants.WM_LBUTTONUP, secondIconId);

        Assert.Equal([MouseButton.Left, MouseButton.Left], fired);
    }

    /// <summary>
    /// A message that is not this icon's callback message raises nothing, even when its payload would
    /// otherwise decode as a click.
    /// </summary>
    /// <remarks>
    /// This is the separation MEM021 names: the <c>NIN_*</c> codes live in the same
    /// <c>WM_USER</c> neighbourhood as the callback message id, so a sink that matched on the payload
    /// alone would convert unrelated messages into clicks - and the <c>TaskbarCreated</c> broadcast
    /// S05 recovers on must stay untouched.
    /// </remarks>
    /// <param name="message">The message id to send to the host window.</param>
    [StaTheory]
    [InlineData(ShellConstants.WM_USER + 7)]
    [InlineData(ShellConstants.TrayCallbackMessage + 1)]
    [InlineData(ShellConstants.WM_USER)]
    [InlineData(0x8000u)]
    public void A_message_that_is_not_the_callback_message_raises_nothing(uint message)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        int mainEvents = 0;
        int previewEvents = 0;

        SubscribeToAllClicks(trayIcon, (_, _) => mainEvents++, (_, _) => previewEvents++);

        Win32.SendMessage(trayIcon.HostHandle, message, Anchor(100, 200), Payload(ShellConstants.WM_LBUTTONUP, iconId));

        Assert.Equal(0, mainEvents);
        Assert.Equal(0, previewEvents);

        // The sink is still alive and still decodes its own callback: one click, delivered in both phases.
        SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId);

        Assert.Equal(1, mainEvents);
        Assert.Equal(1, previewEvents);
    }

    /// <summary>
    /// An event code this library does not map raises nothing, never throws, and leaves the sink
    /// working.
    /// </summary>
    /// <remarks>
    /// These codes are ordinary traffic, not failures: pointer motion, the pre-v4 right-button
    /// message that version 4 replaces with <c>WM_CONTEXTMENU</c>, the keyboard and popup events, and
    /// the balloon codes that stay unmapped until the balloon slice lands. "Never throws" is asserted
    /// behaviourally - the window procedure survives and delivers the next real click - because an
    /// exception out of that procedure is not a test failure but an application crash.
    /// </remarks>
    /// <param name="eventCode">The event code to place in <c>LOWORD(lParam)</c>.</param>
    [StaTheory]
    [InlineData(ShellConstants.WM_MOUSEMOVE)]
    [InlineData(ShellConstants.WM_RBUTTONUP)]
    [InlineData(0x0201u)]
    [InlineData(ShellNotifications.NIN_SELECT)]
    [InlineData(ShellNotifications.NIN_KEYSELECT)]
    [InlineData(ShellNotifications.NIN_BALLOONUSERCLICK)]
    [InlineData(ShellNotifications.NIN_POPUPOPEN)]
    [InlineData(0x00ABu)]
    public void An_unknown_event_code_raises_nothing_and_leaves_the_pipeline_working(uint eventCode)
    {
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        var mainEvents = new List<string>();
        var previews = new List<string>();

        SubscribeToAllClicks(trayIcon, (_, e) => mainEvents.Add(e.RoutedEvent.Name), (_, e) => previews.Add(e.RoutedEvent.Name));

        SendCallback(trayIcon, eventCode, iconId);

        Assert.Empty(mainEvents);
        Assert.Empty(previews);

        // The window procedure survived the unmapped code: the next real click is delivered whole, so
        // the sink was not silently disabled by traffic it did not recognise.
        SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId);

        Assert.Equal([TrayIcon.TrayLeftClickEventName], mainEvents);
        Assert.Equal([TrayIcon.PreviewTrayLeftClickEventName], previews);
    }

    /// <summary>
    /// By default the click stream writes nothing at all to the trace channel: no click line for a
    /// mapped click, and no error line for an unmapped code.
    /// </summary>
    /// <remarks>
    /// This is the MEM026 half of the observability design, and it is a negative test on purpose: the
    /// error channel is reserved for failures, so an unmapped event code must be invisible at the
    /// default level rather than reported as something going wrong.
    /// </remarks>
    [StaFact]
    public void The_click_stream_writes_nothing_to_the_trace_channel_at_the_default_level()
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            Assert.Equal(SourceLevels.Warning, source.Switch.Level);

            source.Listeners.Add(listener);

            using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

            SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId);
            SendCallback(trayIcon, ShellConstants.WM_RBUTTONUP, iconId);
            SendCallback(trayIcon, ShellConstants.WM_MOUSEMOVE, iconId);
            SendCallback(trayIcon, ShellNotifications.NIN_BALLOONSHOW, iconId);

            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(listener);
            listener.Dispose();
        }

        Assert.Equal(string.Empty, writer.ToString());
    }

    /// <summary>
    /// Raising the level exposes the raw click stream: one Verbose line naming the event code and icon
    /// id of a callback this library raised nothing for, and no line at all for a callback it did raise.
    /// </summary>
    /// <remarks>
    /// This is the instrument the live checklist reads to answer the open question about the real
    /// per-interaction message sequence, so it is asserted here rather than assumed. The level is
    /// process-wide and is restored in <c>finally</c>; the collection this class belongs to keeps that
    /// moment from overlapping the class that asserts the default level.
    /// </remarks>
    [StaFact]
    public void The_verbose_click_stream_line_names_the_unmapped_event_code_when_the_level_is_raised()
    {
        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        // Registered before the level is raised, so the registration contributes no line of its own.
        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        try
        {
            source.Switch.Level = SourceLevels.Verbose;
            source.Listeners.Add(recorder);

            // Unmapped: WM_RBUTTONUP is deliberately not a click under version 4.
            SendCallback(trayIcon, ShellConstants.WM_RBUTTONUP, iconId);

            // Mapped: a real click raises events, not trace lines.
            SendCallback(trayIcon, ShellConstants.WM_LBUTTONUP, iconId);

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
        Assert.Contains("0x0205", line.Message, StringComparison.Ordinal);
        Assert.Contains($"icon id {iconId}", line.Message, StringComparison.Ordinal);

        // The level this class borrowed is not left raised for whatever runs next.
        Assert.Equal(SourceLevels.Warning, NotifyIconTrace.Source.Switch.Level);
    }

    /// <summary>
    /// A trace listener that fails the way a broken sink does must not turn ordinary click traffic
    /// into an exception out of the window procedure.
    /// </summary>
    /// <remarks>
    /// The Verbose writer sits on the message path, so it inherits the channel's never-throw policy:
    /// a file listener on a full disk must not be able to take the host application down over an
    /// unmapped event code. The assertion is the absence of an exception, not a caught one - control
    /// reaching the end of the send is the evidence.
    /// </remarks>
    [StaFact]
    public void A_throwing_trace_listener_does_not_escape_the_click_path()
    {
        var throwing = new ThrowingTraceListener();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        using TrayIcon trayIcon = CreateRegisteredIcon(out _, out uint iconId);

        try
        {
            source.Switch.Level = SourceLevels.Verbose;
            source.Listeners.Add(throwing);

            SendCallback(trayIcon, ShellConstants.WM_RBUTTONUP, iconId);
            source.Flush();
        }
        finally
        {
            source.Listeners.Remove(throwing);
            source.Switch.Level = previousLevel;
        }

        Assert.True(throwing.WasCalled, "The throwing listener must actually have been invoked.");
        Assert.Equal(SourceLevels.Warning, NotifyIconTrace.Source.Switch.Level);
    }

    /// <summary>
    /// The per-click hook runs after the main event, receives the click, and does not run for a click
    /// that was cancelled in Preview or already handled by a main-event handler.
    /// </summary>
    /// <remarks>
    /// This is the seam the context-menu behaviour overrides, so its timing and its suppression rules
    /// are part of the contract S03 builds on rather than an implementation detail.
    /// </remarks>
    [StaFact]
    public void The_click_hook_runs_after_the_main_event_and_not_for_a_handled_click()
    {
        var shell = new FakeShellApi();
        var probe = new HookProbeTrayIcon(shell) { Visible = true };
        using (probe)
        {
            uint iconId = shell.ShellNotifyIconDataSnapshots[0].uID;
            var order = new List<string>();
            var hookPayloads = new List<(MouseButton Button, int ClickCount, Point Anchor, RoutedEvent? RoutedEvent)>();

            probe.OnHookCalled = e =>
            {
                order.Add("hook");
                hookPayloads.Add((e.Button, e.ClickCount, e.ScreenAnchor, e.RoutedEvent));
            };

            probe.TrayLeftClick += (_, _) => order.Add("main");

            SendCallback(probe, ShellConstants.WM_LBUTTONUP, iconId, x: 300, y: 400);

            Assert.Equal(["main", "hook"], order);

            (MouseButton Button, int ClickCount, Point Anchor, RoutedEvent? RoutedEvent) hookPayload = Assert.Single(hookPayloads);

            Assert.Equal(MouseButton.Left, hookPayload.Button);
            Assert.Equal(1, hookPayload.ClickCount);
            Assert.Equal(new Point(300, 400), hookPayload.Anchor);
            Assert.Same(TrayIcon.TrayLeftClickEvent, hookPayload.RoutedEvent);

            // A main-event handler that marks the click handled suppresses the default action too.
            order.Clear();
            probe.TrayLeftClick += (_, e) => e.Handled = true;

            SendCallback(probe, ShellConstants.WM_LBUTTONUP, iconId);

            Assert.Equal(["main"], order);

            // And so does a Preview handler that cancels the click outright: neither the main event
            // nor the hook runs, so nothing at all is recorded in this phase.
            order.Clear();
            probe.PreviewTrayLeftClick += (_, e) => e.Handled = true;

            SendCallback(probe, ShellConstants.WM_LBUTTONUP, iconId);

            Assert.Empty(order);
        }
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
    /// Subscribes one handler to all four main click events and another to all four Preview events.
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
    /// A <see cref="TrayIcon"/> that reports when its per-click hook runs, so the seam the
    /// context-menu behaviour overrides can be observed without adding a public test surface to the
    /// product.
    /// </summary>
    private sealed class HookProbeTrayIcon : TrayIcon
    {
        /// <summary>Initializes a new instance over a scripted shell seam.</summary>
        /// <param name="shell">The seam to talk to the shell through.</param>
        internal HookProbeTrayIcon(IShellApi shell)
            : base(shell)
        {
        }

        /// <summary>Gets or sets the callback invoked when the hook runs.</summary>
        internal Action<TrayIconClickEventArgs>? OnHookCalled { get; set; }

        /// <inheritdoc />
        protected override void OnTrayClick(TrayIconClickEventArgs e)
        {
            OnHookCalled?.Invoke(e);
            base.OnTrayClick(e);
        }
    }

    /// <summary>
    /// A listener that fails the way a broken sink does, used to prove the click path cannot escalate
    /// a listener failure.
    /// </summary>
    private sealed class ThrowingTraceListener : TraceListener
    {
        /// <summary>Gets a value indicating whether the listener was invoked.</summary>
        internal bool WasCalled { get; private set; }

        /// <inheritdoc />
        public override void Write(string? message) => WasCalled = true;

        /// <inheritdoc />
        public override void WriteLine(string? message) => WasCalled = true;

        /// <inheritdoc />
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            WasCalled = true;

            throw new IOException("the trace sink is unavailable");
        }
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

/// <summary>
/// The collection definition that keeps the trace-channel classes from running at the same time.
/// </summary>
/// <remarks>
/// The library's <c>TraceSource</c> is process-wide, so a test that raises its level to observe a
/// Verbose line and a test that asserts the default level cannot be allowed to overlap. Tests in a
/// collection never run in parallel with each other, and a collection that disables parallelization
/// does not run beside other collections either.
/// </remarks>
[CollectionDefinition(TraceChannelCollection.Name, DisableParallelization = true)]
public sealed class TraceChannelCollection
{
    /// <summary>The collection name shared by the trace-channel test classes.</summary>
    public const string Name = "Trustsoft.NotifyIcon.TraceChannel";
}
