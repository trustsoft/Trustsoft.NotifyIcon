using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon;

namespace Trustsoft.NotifyIcon.Sample;

/// <summary>
/// The live proof for S01 (D009): a WPF application with no window at all that owns a real icon in
/// the notification area, rotates that icon once per second, reports failures on the console and
/// removes the icon on the way out. Since S02 it also reports every click the shell delivers - the
/// decoded event and the raw callback that produced it - so both halves of the click contract are
/// observable on one console.
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately contains no <c>System.Windows.Forms</c> and no
/// <c>System.Drawing.Common</c> reference: the sample is the executable form of R011, and one
/// convenient <c>using</c> here would turn the demonstration into a counterexample. The assembly
/// purity guard (<c>PackagePurityTests</c>) watches the library; this file is the human-readable
/// half of the same promise.
/// </para>
/// <para>
/// Run it with <c>dotnet run --project samples/Trustsoft.NotifyIcon.Sample -c Release</c>. Add
/// <c>-- --run-seconds 20</c> to make it leave on its own after 20 seconds, which is how the
/// manual checklist observes a <em>graceful</em> shutdown (the icon must disappear from the tray
/// when the process exits normally). Without that argument the process runs until the session ends
/// or the user stops it.
/// </para>
/// <para>
/// <b>Click demonstration (S02).</b> The sample subscribes all four click events and writes one
/// console line per delivered click, so the four click types are visible as they arrive. Add
/// <c>-- --cancel-preview left|double|right|middle</c> to make the Preview handler of that one
/// click type set <see cref="System.Windows.RoutedEventArgs.Handled"/>; the matching main click
/// then prints nothing, which is how a human verifies the cancellation contract on screen.
/// </para>
/// <para>
/// <b>Menu demonstration (S03).</b> The icon is given a real two-item <c>ContextMenu</c> at startup,
/// and the sample writes one <c>[sample] menu opened: popup=0x... rect=... dpi=... scale=... owner=0x...</c>
/// line per open and one <c>[sample] menu dismissed.</c> line per close. The measurements are taken
/// from the OS - the popup window's rectangle, its owner and its monitor's DPI - which is what makes
/// the placement of a windowless process's context menu a machine read rather than a glance at the
/// screen, and the results are recorded in <c>docs/UAT-S03.md</c>.
/// </para>
/// <para>
/// <b>Raw callback trace (S02).</b> The sample prints the shell's undecoded callback messages next
/// to the decoded events, because the <c>NOTIFYICON_VERSION_4</c> encoding is documented but the
/// per-interaction <em>sequence</em> is not. The instrument is a read-only <c>HwndSource</c> hook:
/// the sample enumerates the top-level windows of its own thread and adds a hook to each one that
/// is a WPF <c>HwndSource</c> - which is how it reaches the library's hidden host window without
/// referencing a single library internal. A hook sees messages the WPF message pump never
/// retrieves, which is exactly what the shell's callback is (see the finding in
/// <c>docs/UAT-S02.md</c>: the pump-level view sees posted messages only). The hook never consumes a
/// message and never changes one.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>How often the icon is replaced, so a human can watch the replacement path work.</summary>
    private static readonly TimeSpan RotationInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Three visually distinct frames - a blue disc, a red square with a white band and a green
    /// triangle - so the rotation is unmistakable in a 16x16 notification-area slot. Vector
    /// drawings, not image files: the sample ships without binary assets.
    /// </summary>
    private static readonly ImageSource[] Frames = CreateFrames();

    /// <summary>
    /// The name of the library's trace channel (<c>NotifyIconTrace.SourceName</c>), and the source
    /// the sample subscribes to at Verbose so the library's own raw-callback lines reach this
    /// console.
    /// </summary>
    /// <remarks>
    /// The name is spelled here rather than referenced because the library keeps the constant
    /// internal; this is the documented consumer spelling. <b>Measured caveat:</b> in .NET 8 a
    /// <see cref="TraceSource"/> created with an existing source's name gets its own listener list,
    /// so this subscription receives nothing from the library's own source - the defect and its
    /// evidence are recorded in <c>docs/UAT-S02.md</c>. It is kept because it is the path the
    /// library's remarks tell a consumer to take, and the finding has to be reproducible from the
    /// sample itself.
    /// </remarks>
    private const string LibraryTraceSourceName = "Trustsoft.NotifyIcon";

    /// <summary>
    /// The application-private message range <c>WM_USER</c>..<c>WM_APP - 1</c>, where the library
    /// registers its tray callback (<c>WM_USER + 1</c>).
    /// </summary>
    /// <remarks>
    /// The raw filter prints this band rather than one exact message id: the id is an internal
    /// constant, and the band is the documented range for messages a window class owns - the
    /// non-private ranges are the window manager's, and WPF's own internal traffic (mouse input,
    /// <c>WM_TIMER</c>, and the registered-message range above <c>WM_APP</c>) is what would
    /// otherwise drown the stream.
    /// </remarks>
    private const int WmUser = 0x0400;

    /// <inheritdoc cref="WmUser"/>
    private const int WmAppExclusiveUpperBound = 0x8000;

    /// <summary>The sample's own trace source, distinct from the library's channel.</summary>
    private const string SampleTraceSourceName = "Trustsoft.NotifyIcon.Sample";

    /// <summary>The click type <c>--cancel-preview</c> cancels when no type is named.</summary>
    private const string DefaultCancelPreviewType = "right";

    private TrayIcon? _trayIcon;
    private DispatcherTimer? _rotationTimer;
    private DispatcherTimer? _shutdownTimer;
    private TraceSource? _libraryTrace;
    private bool _observersDetached;
    private int _frameIndex;

    /// <summary>
    /// The menu the icon opens on a right click (S03), or <see langword="null"/> before startup.
    /// </summary>
    /// <remarks>
    /// The sample owns the menu, which is the point of the contract: the library never clones it, so
    /// the instance assigned to <see cref="TrayIcon.ContextMenu"/> is the instance that opens and the
    /// handlers wired up here keep working. It carries real items because WPF suppresses a menu with
    /// an empty item collection - an empty menu would demonstrate nothing about placement.
    /// </remarks>
    private ContextMenu? _menu;

    /// <summary>How many times the menu opened, for the run's totals.</summary>
    private int _menuOpenCount;

    /// <summary>How many times the menu closed, for the run's totals.</summary>
    private int _menuDismissedCount;

    /// <summary>
    /// The raw hooks this sample added, held as strong references on purpose (MEM013).
    /// </summary>
    /// <remarks>
    /// WPF keeps <see cref="HwndSourceHook"/> delegates weakly, so a hook that exists only as the
    /// argument to <see cref="HwndSource.AddHook(HwndSourceHook)"/> can be collected at any time and
    /// the window then keeps working while silently never calling back - which looks exactly like a
    /// shell that stopped sending. Holding the pair here is what makes the raw stream reliable, and
    /// it is the same trap the library documents for its own host.
    /// </remarks>
    private readonly List<(HwndSource Source, HwndSourceHook Hook)> _rawHooks = [];

    /// <summary>
    /// The click type whose Preview handler cancels the click, or <see langword="null"/> when every
    /// click reaches its main handler.
    /// </summary>
    private string? _cancelledClickType;

    /// <summary>Delivered main click events, for the shutdown total.</summary>
    private int _clickCount;

    /// <summary>Clicks a Preview handler suppressed, for the shutdown total.</summary>
    private int _cancelledClickCount;

    /// <summary>Raw private-range messages printed, for the shutdown total.</summary>
    private int _rawMessageCount;

    /// <summary>
    /// Private-range messages the WPF pump itself saw, for the shutdown total - the control that
    /// shows what the hook adds.
    /// </summary>
    /// <remarks>
    /// Counted, never printed: the expected value is <c>0</c> for every tray interaction, and the
    /// number is what proves the hook is not decorative. See <c>AttachRawCallbackHooks</c>.
    /// </remarks>
    private int _pumpMessageCount;

    /// <summary>Lines the library's own trace source delivered, for the shutdown total.</summary>
    /// <remarks>
    /// Expected to stay <c>0</c> in .NET 8 - see <c>LibraryTraceSourceName</c>. Printing it at
    /// shutdown is what turns the documented-but-inert subscription into a measurement instead of a
    /// silent surprise.
    /// </remarks>
    private int _libraryTraceLineCount;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dispose the icon on every normal exit path. SessionEnding is the logoff/shutdown case;
        // Exit covers an explicit Shutdown as well as the end of Run().
        SessionEnding += (_, _) => ShutdownSample();
        Exit += (_, _) => ShutdownSample();

        _cancelledClickType = ParseCancelPreview(e.Args);

        Console.WriteLine(
            _cancelledClickType is null
                ? "[sample] preview cancellation mode: off - every click reaches its main handler."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] preview cancellation mode: {_cancelledClickType} - the Preview handler sets Handled and the main handler must stay silent."));

        AttachLibraryTraceListener();

        var trayIcon = new TrayIcon();

        // Subscribed before the first registration so a failure during registration is reported
        // rather than escaping as an unhandled exception from the startup path.
        trayIcon.TrayError += OnTrayError;

        // S02: the four click types the shell reports through the v4 callback, plus the four
        // Preview counterparts. The same handler serves all four main events because the arguments
        // carry the button and the click count and the routed event carries its own name, so one
        // line per click can name the click type without four near-identical methods.
        trayIcon.TrayLeftClick += OnClick;
        trayIcon.TrayLeftDoubleClick += OnClick;
        trayIcon.TrayRightClick += OnClick;
        trayIcon.TrayMiddleClick += OnClick;

        trayIcon.PreviewTrayLeftClick += OnPreviewClick;
        trayIcon.PreviewTrayLeftDoubleClick += OnPreviewClick;
        trayIcon.PreviewTrayRightClick += OnPreviewClick;
        trayIcon.PreviewTrayMiddleClick += OnPreviewClick;

        _trayIcon = trayIcon;

        trayIcon.ToolTipText = "Trustsoft.NotifyIcon sample - the icon changes every second";

        try
        {
            // Visible first, then the image: this exercises "an icon with no image yet" followed by
            // the NIM_MODIFY replacement path that the rotation keeps using.
            trayIcon.Visible = true;
            trayIcon.IconSource = Frames[0];
        }
        catch (TrayIconException ex)
        {
            // Startup policy: a registration the shell refused is reported and named, not retried
            // and not swallowed. Without this catch a WinExe would die in an invisible unhandled
            // exception, which is exactly how a missing interactive desktop turns into a silent
            // "the icon never appeared" - so the failure is written where a human can read it.
            Console.Error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] startup failure: operation={ex.Operation} win32Error={ex.Win32ErrorCode}: {ex.Message}"));
            Console.Error.WriteLine("[sample] no icon will be shown. Is an interactive desktop session with a notification area available?");

            ShutdownSample();
            Shutdown(1);
            return;
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] tray icon registered, rotating {Frames.Length} frames every {RotationInterval.TotalSeconds:0.#}s."));
        Console.WriteLine("[sample] no window is shown - check the notification area, not the taskbar.");

        // S03: the menu the icon opens on a right click. Assigned here rather than inside a click
        // handler, because that is the shape a consumer's resource dictionary produces (a value the
        // click path reads), and assigned before any click can arrive.
        _menu = CreateTrayMenu();
        trayIcon.ContextMenu = _menu;

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] context menu assigned with {_menu.Items.Count} item(s); a right click on the icon must open it at the icon."));

        // The pump-level control, subscribed in the same run as the hook so the two counts are
        // directly comparable. It counts only; the hook is what prints.
        ComponentDispatcher.ThreadFilterMessage += OnPumpMessage;

        // Only now does the library's host window exist: it is created lazily by the first
        // registration, so enumerating the thread's windows any earlier would hook nothing.
        AttachRawCallbackHooks();

        _rotationTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = RotationInterval };
        _rotationTimer.Tick += OnRotationTick;
        _rotationTimer.Start();

        if (TryParseRunSeconds(e.Args, out TimeSpan runSeconds))
        {
            Console.WriteLine($"[sample] will shut down by itself after {runSeconds.TotalSeconds:0.#}s (graceful close check).");

            _shutdownTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = runSeconds };
            _shutdownTimer.Tick += OnShutdownTick;
            _shutdownTimer.Start();
        }
    }

    /// <summary>
    /// Builds and wires the sample's context menu.
    /// </summary>
    /// <returns>The menu the icon is given.</returns>
    /// <remarks>
    /// Two real items, and the first one writes a line when it is chosen, so the item-click path is
    /// observable on the same console as the open and dismiss lines. The <c>Opened</c> and
    /// <c>Closed</c> handlers are the sample's own instruments: the library's Verbose trace lines are
    /// unreachable from a consumer assembly in .NET 8 (the finding recorded in
    /// <c>docs/UAT-S02.md</c>), so the popup's rectangle has to be measured from the outside - which
    /// is what the checklist needs, because the measurement is then independent of the library's own
    /// arithmetic.
    /// </remarks>
    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu();
        var firstItem = new MenuItem { Header = "Sample menu item" };
        var secondItem = new MenuItem { Header = "Second item" };

        firstItem.Click += (_, _) =>
        {
            Console.WriteLine("[sample] menu item clicked: header=Sample menu item");
            Console.Out.Flush();
        };

        menu.Items.Add(firstItem);
        menu.Items.Add(secondItem);

        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;

        return menu;
    }

    /// <summary>
    /// Measures and reports the popup that just opened.
    /// </summary>
    /// <param name="sender">The menu; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// <para>
    /// <b>Measured from the OS, not from the library.</b> The popup is found by enumerating this
    /// thread's top-level windows (the sample is windowless, and the library's zero-sized host and
    /// 1x1 anchor are both excluded by the size test), its rectangle is read with
    /// <c>GetWindowRect</c>, its owner with <c>GetWindow(GW_OWNER)</c> - which is the library's anchor
    /// window - and its monitor's DPI with <c>GetDpiForWindow</c>. The DPI reading is what makes "the
    /// menu landed where the icon's monitor scale says it should" a two-instrument measurement rather
    /// than the library's own word: the library resolves the same monitor's scale independently.
    /// </para>
    /// <para>
    /// A line is written even when no popup window is found: a menu that reports itself open with no
    /// window behind it is exactly the failure a checklist row has to record rather than infer.
    /// </para>
    /// </remarks>
    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        _menuOpenCount++;

        IntPtr popup = FindOwnPopupWindow();
        string measurement;

        if (popup == IntPtr.Zero)
        {
            measurement = "popup-window=NOT FOUND";
        }
        else
        {
            bool measured = GetWindowRect(popup, out RECT rectangle);
            uint dpi = GetDpiForWindow(popup);
            IntPtr owner = GetWindow(popup, GetWindowOwner);

            measurement = string.Create(
                CultureInfo.InvariantCulture,
                $"popup=0x{popup.ToInt64():X} class={GetWindowClass(popup)} rect={(measured ? $"{rectangle.Left},{rectangle.Top} {rectangle.Right - rectangle.Left}x{rectangle.Bottom - rectangle.Top}" : "unreadable")} dpi={dpi} scale={dpi / 96.0:0.###} owner=0x{owner.ToInt64():X}");
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[sample] menu opened: {measurement}"));
        Console.Out.Flush();
    }

    /// <summary>
    /// Reports that the menu is no longer showing, whoever dismissed it.
    /// </summary>
    /// <param name="sender">The menu; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// One line per close, with no attribution: the sample cannot tell an outside click from Escape, a
    /// chosen item, another window taking activation or the icon's own disposal. The checklist pairs
    /// this line with the injector's record of the outside click it injected, which is what makes the
    /// attribution a measurement instead of a story.
    /// </remarks>
    private void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        _menuDismissedCount++;

        Console.WriteLine("[sample] menu dismissed.");
        Console.Out.Flush();
    }

    /// <summary>
    /// Finds this process's popup window: the only visible top-level window of this thread that is
    /// larger than 20x20 physical pixels in both dimensions.
    /// </summary>
    /// <returns>The popup's handle, or <see cref="IntPtr.Zero"/> when no window matches.</returns>
    /// <remarks>
    /// The size, not the window class, is the discriminator - the class name of a WPF window is an
    /// implementation detail of the runtime, while "a visible window this large exists in this
    /// process" is a fact about it. Measured: the library's tray host is zero-sized and its menu
    /// anchor is 1x1, so neither can be mistaken for the popup, and the sample opens no window of its
    /// own.
    /// </remarks>
    private static IntPtr FindOwnPopupWindow()
    {
        const int minimumEdge = 20;
        IntPtr found = IntPtr.Zero;

        EnumThreadWindowsCallback callback = (IntPtr windowHandle, IntPtr _) =>
        {
            if (!IsWindowVisible(windowHandle)
                || !GetWindowRect(windowHandle, out RECT rectangle)
                || rectangle.Right - rectangle.Left <= minimumEdge
                || rectangle.Bottom - rectangle.Top <= minimumEdge)
            {
                return true;
            }

            found = windowHandle;
            return false;
        };

        EnumThreadWindows(GetCurrentThreadId(), callback, IntPtr.Zero);

        return found;
    }

    /// <summary>Reads a window's class name, for the report line only.</summary>
    /// <param name="windowHandle">The window handle.</param>
    /// <returns>The class name, or an empty string when it cannot be read.</returns>
    private static string GetWindowClass(IntPtr windowHandle)
    {
        var name = new System.Text.StringBuilder(256);

        return GetClassName(windowHandle, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    /// <summary>
    /// Replaces the icon with the next frame, which is the live exercise of the NIM_MODIFY path.
    /// </summary>
    private void OnRotationTick(object? sender, EventArgs e)
    {
        TrayIcon? trayIcon = _trayIcon;

        if (trayIcon is null)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % Frames.Length;
        trayIcon.IconSource = Frames[_frameIndex];

        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[sample] frame {_frameIndex + 1}/{Frames.Length} applied."), SampleTraceSourceName);
    }

    /// <summary>
    /// Ends the sample on its own so the manual checklist can watch a graceful shutdown.
    /// </summary>
    private void OnShutdownTick(object? sender, EventArgs e)
    {
        ShutdownSample();
        Shutdown();
    }

    /// <summary>
    /// Reports a runtime notification-area failure. The routed event is the only failure channel a
    /// windowless host has besides the library's trace source, so the payload is written out whole.
    /// </summary>
    private void OnTrayError(object? sender, TrayErrorEventArgs e)
    {
        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] TrayError operation={e.Operation} win32Error={e.Win32ErrorCode} retried={e.Retried} exception={e.Exception.GetType().Name}: {e.Exception.Message}");

        Console.Error.WriteLine(message);
        Trace.TraceError(message);
    }

    /// <summary>
    /// Writes one line per delivered click: which of the four click events fired, the button and
    /// click count the shell reported, and where it said the click was.
    /// </summary>
    /// <param name="sender">The <see cref="TrayIcon"/> that raised the event.</param>
    /// <param name="e">The click payload.</param>
    /// <remarks>
    /// The click type is read from <see cref="RoutedEventArgs.RoutedEvent"/>'s name rather than
    /// from the button alone, because a single left click and a double left click carry the same
    /// button and differ only in the count: naming the event is what makes the two distinguishable
    /// in a console log, and it is exactly what a consumer's handler would branch on.
    /// </remarks>
    private void OnClick(object? sender, TrayIconClickEventArgs e)
    {
        _clickCount++;

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] click type={e.RoutedEvent.Name} button={e.Button} count={e.ClickCount} anchor={e.ScreenAnchor.X:0.#},{e.ScreenAnchor.Y:0.#}");

        Console.WriteLine(message);
        Trace.WriteLine(message, SampleTraceSourceName);
    }

    /// <summary>
    /// Cancels the one click type <c>--cancel-preview</c> selected, and only that one.
    /// </summary>
    /// <param name="sender">The <see cref="TrayIcon"/> that raised the event.</param>
    /// <param name="e">The click payload of the Preview phase.</param>
    /// <remarks>
    /// This is the cancellation contract in one statement: the library raises the Preview event
    /// first and honours <see cref="RoutedEventArgs.Handled"/> before raising the main event, so the
    /// matching <see cref="OnClick"/> never runs for a cancelled click - which is what a human
    /// watching the console verifies by the absence of the <c>[sample] click</c> line.
    /// </remarks>
    private void OnPreviewClick(object? sender, TrayIconClickEventArgs e)
    {
        string? cancelledClickType = _cancelledClickType;

        if (cancelledClickType is null || !IsCancelledType(cancelledClickType, e.RoutedEvent))
        {
            // Either no cancellation was requested or this is a different click type: the click
            // must reach its main handler untouched.
            return;
        }

        e.Handled = true;
        _cancelledClickCount++;

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] click cancelled by Preview handler: previewType={e.RoutedEvent.Name} button={e.Button} count={e.ClickCount} anchor={e.ScreenAnchor.X:0.#},{e.ScreenAnchor.Y:0.#} (no [sample] click line for this click)");

        Console.WriteLine(message);
        Trace.WriteLine(message, SampleTraceSourceName);
    }

    /// <summary>
    /// Reports whether the requested cancellation mode names the click type of a Preview event.
    /// </summary>
    /// <param name="cancelledClickType">One of <c>left</c>, <c>double</c>, <c>right</c>, <c>middle</c>.</param>
    /// <param name="previewEvent">The routed event a Preview handler was invoked for.</param>
    /// <returns><see langword="true"/> when this event is the one to cancel.</returns>
    /// <remarks>
    /// The comparison is by routed-event identity, not by name or by button, so an unknown mode or
    /// a renamed event cannot silently cancel the wrong click type.
    /// </remarks>
    private static bool IsCancelledType(string cancelledClickType, RoutedEvent previewEvent) =>
        cancelledClickType switch
        {
            "left" => ReferenceEquals(previewEvent, TrayIcon.PreviewTrayLeftClickEvent),
            "double" => ReferenceEquals(previewEvent, TrayIcon.PreviewTrayLeftDoubleClickEvent),
            "right" => ReferenceEquals(previewEvent, TrayIcon.PreviewTrayRightClickEvent),
            "middle" => ReferenceEquals(previewEvent, TrayIcon.PreviewTrayMiddleClickEvent),
            _ => false,
        };

    /// <summary>
    /// Adds a read-only hook to every WPF <see cref="HwndSource"/> this thread created, which is
    /// how the sample sees the shell's undecoded callback before any library code decodes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The enumeration is deliberately structural rather than name-based: the library keeps the
    /// host window's title and its callback message id internal, so identifying the host by a
    /// string would couple this sample to a private detail. Every top-level window of this thread
    /// that WPF still knows as an <see cref="HwndSource"/> gets a hook and the message band does the
    /// filtering - the host is simply whichever of them receives the callback.
    /// </para>
    /// <para>
    /// Called after the icon is registered, because the host window is created lazily by that first
    /// registration. The hooks are kept in <see cref="_rawHooks"/> for the reason documented there.
    /// </para>
    /// </remarks>
    private void AttachRawCallbackHooks()
    {
        int hooked = 0;

        // Held in a local for the duration of the call: the delegate must stay alive while the
        // enumeration is running. It is not stored afterwards - the hooks are what must survive.
        EnumThreadWindowsCallback callback = (IntPtr windowHandle, IntPtr _) =>
        {
            HwndSource? source = HwndSource.FromHwnd(windowHandle);

            if (source is null)
            {
                // Not a window WPF still tracks: nothing this sample could hook.
                return true;
            }

            var hook = new HwndSourceHook(OnRawHostMessage);
            source.AddHook(hook);
            _rawHooks.Add((source, hook));
            hooked++;

            return true;
        };

        EnumThreadWindows(GetCurrentThreadId(), callback, IntPtr.Zero);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] raw callback trace: hooked {hooked} HwndSource(s) owned by this thread, watching 0x{WmUser:X4}..0x{WmAppExclusiveUpperBound - 1:X4}; the pump counter is armed as the control."));
    }

    /// <summary>
    /// Counts the private-range messages the WPF message pump retrieves, and prints nothing.
    /// </summary>
    /// <param name="msg">The message the pump is about to dispatch.</param>
    /// <param name="handled">Never modified.</param>
    /// <remarks>
    /// This is the sample's control instrument. Both counters run in the same process and the same
    /// session, so "hook saw 6, pump saw 0" is a measurement rather than two runs that might not be
    /// comparable. Calibration for what the pump <em>can</em> see - messages that are genuinely
    /// posted - is recorded in <c>docs/UAT-S02.md</c>; the tray callback is delivered
    /// synchronously and is therefore never retrieved from the queue.
    /// </remarks>
    private void OnPumpMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message >= WmUser && msg.message < WmAppExclusiveUpperBound)
        {
            _pumpMessageCount++;
        }
    }

    /// <summary>
    /// Prints every message in the application-private range that reaches one of the hooked
    /// windows, in raw form.
    /// </summary>
    /// <param name="windowHandle">The window the message was sent to; the library's host is the one that matters.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="wParam">The first message parameter: the anchor point.</param>
    /// <param name="lParam">The second message parameter: the event code and the icon id.</param>
    /// <param name="handled">Always left untouched: the hook observes, it does not consume.</param>
    /// <returns>Always <see cref="IntPtr.Zero"/>, so default processing still runs.</returns>
    /// <remarks>
    /// <para>
    /// Nothing is decoded here beyond the two documented halves of the payload, so the line stays a
    /// <em>raw</em> record next to the decoded click line. Whether it lands before or after that
    /// decoded line depends on the order WPF invokes the hooks, which is why the checklist reads
    /// the order off the console instead of assuming one.
    /// </para>
    /// <para>
    /// <b>Why a hook and not the message pump.</b> The shell delivers this callback synchronously,
    /// so the message is never retrieved from the thread's queue and is invisible to pump-level
    /// observation (<c>ComponentDispatcher.ThreadFilterMessage</c>). That measurement, and the
    /// calibration showing the pump does see messages that are genuinely posted, are recorded in
    /// <c>docs/UAT-S02.md</c>.
    /// </para>
    /// </remarks>
    private IntPtr OnRawHostMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (messageId < WmUser || messageId >= WmAppExclusiveUpperBound)
        {
            // WPF's own traffic - input, timers and the registered-message range - lives outside
            // the private band. Returning immediately keeps the hook cheap, because it runs for
            // every message of every hooked window.
            return IntPtr.Zero;
        }

        ulong payload = unchecked((ulong)lParam.ToInt64());
        ulong anchor = unchecked((ulong)wParam.ToInt64());
        _rawMessageCount++;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] raw callback hwnd=0x{windowHandle.ToInt64():X} msg=0x{messageId:X4} event=0x{payload & 0xFFFF:X4} iconId={(payload >> 16) & 0xFFFF} wParam=0x{anchor:X16} lParam=0x{payload:X16}"));

        // Flushed per line: this stream is the evidence, and a line still sitting in a buffer when
        // the run ends is a line the checklist cannot read.
        Console.Out.Flush();

        // Read-only, and explicit about it: a hook that set Handled - or returned a non-zero value
        // - would swallow the callback and the library would never raise a click at all.
        handled = false;
        return IntPtr.Zero;
    }

    /// <summary>
    /// Attaches a Verbose listener to the library's trace channel so the library's own
    /// raw-callback lines would reach this console.
    /// </summary>
    /// <remarks>
    /// See <c>LibraryTraceSourceName</c> for the measured caveat: the subscription is the
    /// documented consumer path and currently delivers nothing. The shutdown total of received
    /// lines is what records that as a measurement.
    /// </remarks>
    private void AttachLibraryTraceListener()
    {
        var source = new TraceSource(LibraryTraceSourceName, SourceLevels.Verbose);
        source.Switch.Level = SourceLevels.Verbose;
        source.Listeners.Add(new FlushingConsoleTraceListener(() => _libraryTraceLineCount++));
        _libraryTrace = source;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] library trace listener attached to source '{LibraryTraceSourceName}', level {source.Switch.Level}."));
    }

    /// <summary>
    /// Reads <c>--cancel-preview</c> from the command line.
    /// </summary>
    /// <param name="args">The startup arguments.</param>
    /// <returns>
    /// The click type to cancel - <c>left</c>, <c>double</c>, <c>right</c> or <c>middle</c> - or
    /// <see langword="null"/> when no cancellation was requested.
    /// </returns>
    /// <remarks>
    /// Both spellings of the existing <c>--run-seconds</c> argument are accepted, and a bare
    /// <c>--cancel-preview</c> (or one with a value that is not a click type) selects
    /// <see cref="DefaultCancelPreviewType"/> rather than failing the run: the effective mode is
    /// always printed at startup, so a typo cannot make the demonstration quietly prove the wrong
    /// thing.
    /// </remarks>
    private static string? ParseCancelPreview(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cancel-preview")
            {
                return i + 1 < args.Length && IsCancelPreviewType(args[i + 1])
                    ? args[i + 1]
                    : DefaultCancelPreviewType;
            }

            const string prefix = "--cancel-preview=";

            if (args[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                string raw = args[i][prefix.Length..];
                return IsCancelPreviewType(raw) ? raw : DefaultCancelPreviewType;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether a value names one of the four cancelable click types.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for the four click-type keywords.</returns>
    private static bool IsCancelPreviewType(string? value) => value is "left" or "double" or "right" or "middle";

    /// <summary>The <c>GW_OWNER</c> selector: the popup's owner window, which is the library's anchor.</summary>
    private const uint GetWindowOwner = 4;

    /// <summary>A screen rectangle, in the layout <c>GetWindowRect</c> writes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        /// <summary>The left edge, in physical screen pixels.</summary>
        public int Left;

        /// <summary>The top edge, in physical screen pixels.</summary>
        public int Top;

        /// <summary>The exclusive right edge, in physical screen pixels.</summary>
        public int Right;

        /// <summary>The exclusive bottom edge, in physical screen pixels.</summary>
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out RECT rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    /// <summary>Reads the DPI of the monitor a window is on; Windows 10 1607 and later.</summary>
    /// <param name="windowHandle">The window handle.</param>
    /// <returns>The effective DPI of the window's monitor.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, System.Text.StringBuilder name, int count);

    /// <summary>
    /// The callback <see cref="EnumThreadWindows"/> invokes once per top-level window of a thread.
    /// </summary>
    /// <param name="windowHandle">The window handle.</param>
    /// <param name="parameter">The caller's opaque value; unused here.</param>
    /// <returns><see langword="true"/> to continue the enumeration.</returns>
    private delegate bool EnumThreadWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsCallback callback, IntPtr parameter);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Pumps the dispatcher queue for a moment so work WPF schedules asynchronously gets a chance to run
    /// before the process exits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, and the honest result of the measurement.</b> The library destroys an open menu's
    /// anchor window synchronously inside <see cref="TrayIcon.Dispose"/> - the in-repo test
    /// <c>Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent</c> asserts that immediately
    /// afterwards - but the <see cref="ContextMenu.Closed"/> event a consumer subscribes to is raised by
    /// WPF, and a disposal-driven close did not deliver it before the process exited (<c>menu opens=1,
    /// menu dismissals=0</c>, measured in two runs, with this pump in place in the second one). An
    /// OS-driven dismissal - an outside click - does deliver it (same runs). The sample therefore
    /// reports its per-close line only for dismissals that happen while the application is still
    /// running, and the disposal case is covered by the in-repo test rather than by this line.
    /// </para>
    /// <para>
    /// The pump is kept because it is correct in intent and harmless: it drains the thread's queue,
    /// including posted messages, using the same shape as the library's own menu tests.
    /// </para>
    /// </remarks>
    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();

        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }

    /// <summary>
    /// Removes the icon and destroys its handle; safe to call more than once because the sample
    /// calls it from several exit paths.
    /// </summary>
    private void ShutdownSample()
    {
        _rotationTimer?.Stop();
        _rotationTimer = null;
        _shutdownTimer?.Stop();
        _shutdownTimer = null;

        TrayIcon? trayIcon = _trayIcon;
        _trayIcon = null;

        if (trayIcon is not null)
        {
            trayIcon.TrayError -= OnTrayError;
            trayIcon.TrayLeftClick -= OnClick;
            trayIcon.TrayLeftDoubleClick -= OnClick;
            trayIcon.TrayRightClick -= OnClick;
            trayIcon.TrayMiddleClick -= OnClick;
            trayIcon.PreviewTrayLeftClick -= OnPreviewClick;
            trayIcon.PreviewTrayLeftDoubleClick -= OnPreviewClick;
            trayIcon.PreviewTrayRightClick -= OnPreviewClick;
            trayIcon.PreviewTrayMiddleClick -= OnPreviewClick;

            // The icon is disposed first, while the menu's Closed handler is still attached: the
            // library closes an open menu as part of disposal, and that close is what the
            // "[sample] menu dismissed." line has to report. Detaching the handlers first made the
            // dismissal unobservable - measured: a run that opened one menu and never clicked outside
            // reported "menu opens=1, menu dismissals=0" - which is exactly the kind of measurement
            // order this checklist must not get wrong.
            trayIcon.Dispose();

            Console.WriteLine("[sample] tray icon disposed - it must have left the notification area.");

            // See DrainDispatcher's remarks for the measurement behind this call: it drains the queue
            // before the totals are printed, and it is the point at which the disposal-driven close was
            // expected - but did not arrive - so the sample's per-close line covers the OS-driven
            // dismissals only.
            DrainDispatcher();

            ContextMenu? menu = _menu;

            if (menu is not null)
            {
                menu.Opened -= OnMenuOpened;
                menu.Closed -= OnMenuClosed;
                _menu = null;
            }
        }

        DetachObservers();
    }

    /// <summary>
    /// Detaches the raw instruments and prints the run's totals; safe to call more than once.
    /// </summary>
    /// <remarks>
    /// The totals exist so a run is self-describing after the fact: a captured stream that ends
    /// without them cannot be told apart from one whose last lines were lost, and a
    /// <c>library trace lines=0</c> total is the measurement behind the finding that the documented
    /// channel is unreachable from a consumer.
    /// </remarks>
    private void DetachObservers()
    {
        if (_observersDetached)
        {
            return;
        }

        _observersDetached = true;

        // The hooks are not removed by hand: they hang off the library's own host window, which
        // ShutdownSample has already destroyed by the time this runs, and WPF drops a window's
        // hooks with the window. Clearing the list is what guarantees none of it can be invoked
        // afterwards, and it releases the strong references that kept the delegates alive (MEM013).
        _rawHooks.Clear();

        ComponentDispatcher.ThreadFilterMessage -= OnPumpMessage;

        TraceSource? libraryTrace = _libraryTrace;
        _libraryTrace = null;

        if (libraryTrace is not null)
        {
            libraryTrace.Flush();
            libraryTrace.Close();
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] totals: raw callback lines={_rawMessageCount}, pump-observed private-range messages={_pumpMessageCount}, library trace lines={_libraryTraceLineCount}, clicks={_clickCount}, cancelled by a Preview handler={_cancelledClickCount}, menu opens={_menuOpenCount}, menu dismissals={_menuDismissedCount}."));

        // Flushed by hand rather than left to the process exit path: the whole point of the raw
        // capture is that the last lines of a run survive it.
        Console.Out.Flush();
    }

    /// <summary>
    /// Writes each line of the library's trace channel to the console and flushes it immediately.
    /// </summary>
    /// <param name="onLine">Called once per delivered line, for the run's total.</param>
    /// <remarks>
    /// The flush is the reason this type exists rather than a stock
    /// <see cref="ConsoleTraceListener"/>: the checklist asks what the shell sent per interaction,
    /// and a process that exits with its last raw lines still in a buffer would answer that
    /// question with a truncated stream.
    /// </remarks>
    private sealed class FlushingConsoleTraceListener(Action onLine) : TraceListener
    {
        /// <inheritdoc />
        public override void Write(string? message) => Console.Out.Write(message);

        /// <inheritdoc />
        public override void WriteLine(string? message)
        {
            Console.WriteLine($"[trace] {message}");
            Console.Out.Flush();
            onLine();
        }
    }

    /// <summary>
    /// Builds the three frames. Each is a <see cref="DrawingImage"/> in a 16x16 coordinate space,
    /// so nothing in the sample depends on a file on disk or on an image decoder.
    /// </summary>
    private static ImageSource[] CreateFrames()
    {
        return
        [
            Frame(new EllipseGeometry(new Point(8, 8), 7, 7), Colors.DodgerBlue, null),
            Frame(new RectangleGeometry(new Rect(1, 1, 14, 14)), Colors.Firebrick, new GeometryGroup
            {
                Children =
                {
                    new RectangleGeometry(new Rect(1, 6, 14, 4)),
                },
            }),
            Frame(
                new PathGeometry(
                [
                    new PathFigure(
                        new Point(8, 1),
                        [
                            new LineSegment(new Point(15, 15), true),
                            new LineSegment(new Point(1, 15), true),
                        ],
                        true),
                ]),
                Colors.SeaGreen,
                null),
        ];
    }

    /// <summary>
    /// Builds one frame: a filled shape, optionally with a contrasting cut-out drawn on top of it.
    /// </summary>
    /// <param name="shape">The silhouette.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="cutOut">The contrasting geometry drawn over the fill, or <see langword="null"/>.</param>
    /// <returns>A frozen-or-freezable drawing source the library can rasterize.</returns>
    private static DrawingImage Frame(Geometry shape, Color color, Geometry? cutOut)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, shape));

        if (cutOut is not null)
        {
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Colors.White), null, cutOut));
        }

        var image = new DrawingImage(group);

        // Freezing is what makes the frame safe to hand to the conversion path from any thread and
        // removes any doubt about who owns the drawing. DrawingImage.Freeze() recurses into the
        // drawing graph and throws if anything in it is not freezable, which is exactly the
        // property worth asserting here rather than discovering at runtime.
        image.Freeze();

        return image;
    }

    /// <summary>
    /// Reads <c>--run-seconds N</c> (or <c>--run-seconds=N</c>) from the command line.
    /// </summary>
    /// <param name="args">The startup arguments.</param>
    /// <param name="runSeconds">The requested run time.</param>
    /// <returns><see langword="true"/> when a positive run time was requested.</returns>
    private static bool TryParseRunSeconds(string[] args, out TimeSpan runSeconds)
    {
        runSeconds = TimeSpan.Zero;
        string? raw = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--run-seconds" && i + 1 < args.Length)
            {
                raw = args[i + 1];
                break;
            }

            const string prefix = "--run-seconds=";

            if (args[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                raw = args[i][prefix.Length..];
                break;
            }
        }

        if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds <= 0)
        {
            return false;
        }

        runSeconds = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
