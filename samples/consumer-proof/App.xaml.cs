using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon;

namespace ConsumerProof;

/// <summary>
/// The consumer proof: a windowless WPF application that owns nothing but one <c>PackageReference</c>
/// to the packed library, and shows a working tray icon on every target framework.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this project exists.</b> "Installable and usable from a consumer project without
/// additional setup" (R010) and "works on all three target frameworks" (R012) cannot be shown from
/// inside the library's own solution, where the library is a project reference and the repository's
/// <c>Directory.Build.props</c> supplies every setting. This application is the thing a package user
/// actually has: one <c>&lt;PackageReference Include="Trustsoft.NotifyIcon" Version="1.0.0" /&gt;</c>
/// restored from the local folder feed that <c>dotnet pack</c> writes to, its own
/// <c>nuget.config</c> with every inherited source cleared, and its own empty
/// <c>Directory.Build.props</c> that stops the MSBuild walk before it reaches the repository's.
/// </para>
/// <para>
/// <b>The shape it exercises is the README's.</b> No window at all, an icon registered, a menu
/// assigned, click handlers wired, a balloon shown through <c>ShowBalloonTip</c>, and a disposal
/// path that removes the icon - so the proof covers the surface a consumer reads about, not only the
/// constructor. Running it with no arguments runs that whole shape once and exits, which is what
/// <c>scripts/probe-live</c> watches from outside the process.
/// </para>
/// <para>
/// <b>What this application deliberately is not.</b> It does not reference the sample, the tests or
/// the probe, and it is absent from <c>Trustsoft.NotifyIcon.sln</c>: a consumer cannot use any of
/// them, so needing one would falsify the claim this project makes.
/// </para>
/// <para>
/// <b>The one instrument it carries.</b> <c>--self-open-menu-after</c> runs the menu-open path
/// without a shell click, through the <c>OnTrayClick</c> hook a subclass is documented to call (the
/// same instrument the in-repo sample uses, re-implemented here rather than shared). It exists
/// because the shell click this slice would otherwise rely on was never observed to arrive in
/// <c>docs/UAT-S06.md</c> (F1): without it, a run could only report that the icon was present, never
/// that the menu opens from a consumer's own code.
/// </para>
/// <para>
/// <b>The toast path (<c>--toast</c>, M002/S05).</b> The other half of what a consumer reads
/// about: it builds one <see cref="ToastContent"/> with a title and a body, gives it two
/// <see cref="ToastButton"/>s when <c>--toast-buttons</c> is set, subscribes the three typed S03
/// events (<see cref="ToastNotifier.Activated"/>, <see cref="ToastNotifier.Dismissed"/> and
/// <see cref="ToastNotifier.ToastError"/>), shows it once through the public
/// <see cref="ToastNotifier"/> and prints what arrived - with the platform's notification setting
/// read as an outcome and the post-teardown window measured rather than assumed. Every toast switch
/// is off by default, so a run without them is the tray proof this project carried before, line for
/// line.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>Exit code for a registration the shell refused, or any other startup failure.</summary>
    private const int StartupFailureExitCode = 1;

    /// <summary>Exit code for a command line this application does not understand.</summary>
    /// <remarks>
    /// An argument that is silently ignored would make a run whose switch was misspelled look
    /// exactly like a run that proved the opposite of what the caller asked for, which is the one
    /// failure mode an instrument must not have.
    /// </remarks>
    private const int UsageErrorExitCode = 2;

    /// <summary>Exit code for a package whose public surface is not the documented one.</summary>
    private const int SurfaceMismatchExitCode = 3;

    /// <summary>How long the run lasts when <c>--run-seconds</c> is not given.</summary>
    private const double DefaultRunSeconds = 20;

    /// <summary>How long the self-opened menu is left open before the run closes it again.</summary>
    private const double MenuHoldSeconds = 4;

    /// <summary>The delay <c>--toast-after</c> uses when it is given without a value.</summary>
    private static readonly TimeSpan DefaultToastDelay = TimeSpan.FromSeconds(3);

    /// <summary>The delay <c>--toast-dispose-after</c> uses when it is given without a value.</summary>
    private static readonly TimeSpan DefaultToastDisposeDelay = TimeSpan.FromSeconds(5);

    /// <summary>This consumer's toast title, its own vocabulary rather than the sample's.</summary>
    private const string ToastTitle = "Trustsoft.NotifyIcon consumer proof toast";

    /// <summary>This consumer's toast body, its own wording rather than the sample's.</summary>
    private const string ToastBody = "This toast came from the packaged library, installed into a windowless WPF application. Click the body or a button.";

    /// <summary>
    /// The prefix of the toast's launch argument, suffixed with the show's number.
    /// </summary>
    /// <remarks>
    /// An activation arrives as the argument the toast or button carried and nothing else, so an
    /// argument that names the show is the only thing that makes a delivered activation attributable
    /// to a show of this run rather than to some other process's toast.
    /// </remarks>
    private const string ToastLaunchPrefix = "consumer-toast-";

    /// <summary>The argument the first action button carries when <c>--toast-buttons</c> is set.</summary>
    private const string ToastButton1Argument = "consumer-button-1";

    /// <inheritdoc cref="ToastButton1Argument"/>
    private const string ToastButton2Argument = "consumer-button-2";

    /// <summary>The visible label of the first <c>--toast-buttons</c> button.</summary>
    private const string ToastButton1Text = "Button 1";

    /// <inheritdoc cref="ToastButton1Text"/>
    private const string ToastButton2Text = "Button 2";

    /// <summary>The accepted command line, printed on every usage error.</summary>
    private const string Usage =
        "usage: ConsumerProof [--run-seconds <seconds>] [--self-open-menu-after <seconds>] "
        + "[--show-balloon-after <seconds>] [--surface-only] [--toast] [--toast-buttons] "
        + "[--toast-aumid <id>] [--toast-after [seconds]] [--toast-dispose-after [seconds]]";

    /// <summary>
    /// The twenty public types the README documents, as the package's surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out here - in the consumer's own source, from the consumer's own reading of the
    /// documentation - rather than read from the library's test project, because the claim being
    /// checked is what a package user sees. The first seven are the M001 tray surface
    /// (<see cref="TrayIcon"/>, <see cref="TrayIconException"/>, <see cref="TrayErrorEventArgs"/>,
    /// <see cref="TrayIconClickEventArgs"/>, <see cref="TrayMenuActivation"/>,
    /// <see cref="BalloonTipIcon"/> and <see cref="BalloonTipOptions"/>) and the remaining thirteen
    /// are the M002 toast surface: the <see cref="ToastNotifier"/> entry point, its four typed event
    /// payloads and the content vocabulary the caller builds a toast from.
    /// </para>
    /// <para>
    /// The list is the consumer-side copy of
    /// <c>PackagePurityTests.Public_surface_is_only_the_documented_types</c>: that test asserts the
    /// library's view of itself, this one asserts the same set from an assembly that only ever
    /// installed a nupkg. A widened surface fails here even if nobody updated the library's test,
    /// which is what makes it a consumer-side assertion rather than a restatement. M002/S05 widened
    /// it from the seven M001 tray types to the twenty the M002 package exports - the stale list is
    /// exactly what made every pre-S05 run of this proof fail with exit 3.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedPublicTypes =
    [
        "Trustsoft.NotifyIcon.BalloonTipIcon",
        "Trustsoft.NotifyIcon.BalloonTipOptions",
        "Trustsoft.NotifyIcon.ToastActivatedEventArgs",
        "Trustsoft.NotifyIcon.ToastButton",
        "Trustsoft.NotifyIcon.ToastContent",
        "Trustsoft.NotifyIcon.ToastDismissalReason",
        "Trustsoft.NotifyIcon.ToastDismissedEventArgs",
        "Trustsoft.NotifyIcon.ToastErrorEventArgs",
        "Trustsoft.NotifyIcon.ToastException",
        "Trustsoft.NotifyIcon.ToastImage",
        "Trustsoft.NotifyIcon.ToastImagePlacement",
        "Trustsoft.NotifyIcon.ToastNotificationSetting",
        "Trustsoft.NotifyIcon.ToastNotifier",
        "Trustsoft.NotifyIcon.ToastSeverity",
        "Trustsoft.NotifyIcon.ToastSound",
        "Trustsoft.NotifyIcon.TrayErrorEventArgs",
        "Trustsoft.NotifyIcon.TrayIcon",
        "Trustsoft.NotifyIcon.TrayIconClickEventArgs",
        "Trustsoft.NotifyIcon.TrayIconException",
        "Trustsoft.NotifyIcon.TrayMenuActivation",
    ];

    /// <summary>The icon this run created, or <see langword="null"/> before startup or after disposal.</summary>
    private TrayIcon? _trayIcon;

    /// <summary>The consumer's own menu instance, assigned to the icon and never cloned by the library.</summary>
    private ContextMenu? _menu;

    /// <summary>The icon as this application's own subclass, for the no-click menu instrument.</summary>
    private ConsumerTrayIcon? _instrumentedIcon;

    private DispatcherTimer? _runTimer;
    private DispatcherTimer? _selfOpenTimer;
    private DispatcherTimer? _menuHoldTimer;
    private DispatcherTimer? _balloonTimer;
    private bool _shutdownDone;
    private bool _surfaceOk = true;
    private int _exitCode;
    private int _clickCount;
    private int _previewClickCount;
    private int _menuOpenCount;
    private int _menuDismissedCount;
    private int _menuItemClickCount;
    private int _balloonShowCount;
    private int _balloonClickCount;

    /// <summary>
    /// The toast notifier this run configured, or <see langword="null"/> without <c>--toast</c> or
    /// after its teardown.
    /// </summary>
    /// <remarks>
    /// A field rather than a local because it is the thing <see cref="ShutdownConsumer"/> disposes:
    /// the notifier's own <c>Dispose</c> removes the shortcut its registration wrote, so the one
    /// existing exit path unregisters the identity on every framework's run.
    /// </remarks>
    private ToastNotifier? _toastNotifier;

    /// <summary>The identity this run asked the shell to show toasts for, or <see langword="null"/> without <c>--toast</c>.</summary>
    /// <remarks>
    /// The override (<c>--toast-aumid</c>) when given, otherwise the default the library documents:
    /// the entry assembly's simple name. Recorded here because <see cref="ToastNotifier.AppUserModelId"/>
    /// keeps the override and nothing else, so a run that did not pass one could not otherwise print
    /// the identity it used.
    /// </remarks>
    private string? _toastIdentity;

    /// <summary>Whether this run's toast carries the two action buttons (<c>--toast-buttons</c>).</summary>
    private bool _toastButtons;

    private DispatcherTimer? _toastTimer;
    private DispatcherTimer? _toastDisposeTimer;

    /// <summary>Whether the toast path was started at all (<c>--toast</c>), so a run without it neither tears down nor reports one.</summary>
    private bool _toastStarted;

    private bool _toastStopped;
    private bool _toastTeardownRecorded;
    private int _toastShowCount;
    private int _toastAcceptedCount;
    private int _toastActivationCount;
    private int _toastDismissedCount;
    private int _toastErrorCount;
    private int _toastRefusedCount;

    /// <summary>
    /// Toast failures that arrived outside the two documented failure channels.
    /// </summary>
    /// <remarks>
    /// The public surface owns exactly two documented failure channels - a <see cref="ToastException"/>
    /// from <see cref="ToastNotifier.Show"/> (counted as refused) and the typed
    /// <see cref="ToastNotifier.ToastError"/> event (counted as errors) - so this counter only moves
    /// when a show raises something the documentation does not describe. It exists because an
    /// instrument that quietly swallowed such a failure would report a run that produced no toast as a
    /// green one, and because the in-repo sample prints the same field name in its toast totals, so the
    /// two lines stay comparable. A healthy run reads <c>failures=0</c>.
    /// </remarks>
    private int _toastFailureCount;

    private string? _toastLastActivationArguments;
    private string? _toastLastSettingName;
    private int _toastActivationsAtTeardown;
    private int _toastDismissalsAtTeardown;
    private int _toastErrorsAtTeardown;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryParseArguments(e.Args, out ConsumerArguments arguments, out string? argumentError))
        {
            Console.Error.WriteLine(argumentError);
            Console.Error.WriteLine(Usage);
            Shutdown(UsageErrorExitCode);
            return;
        }

        // Disposal runs on every normal exit path: SessionEnding is the logoff/shutdown case, Exit
        // covers an explicit Shutdown as well as the end of Run(). Subscribed after the argument
        // check, so a usage error produces no icon and no totals.
        SessionEnding += (_, _) => ShutdownConsumer();
        Exit += (_, _) => ShutdownConsumer();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] ConsumerProof start {DateTime.Now:yyyy-MM-dd HH:mm:ss}; machine={Environment.MachineName}; os={Environment.OSVersion.VersionString}"));

        // First, because it is the claim a package user would notice first, and because it is cheap:
        // the exported surface of the assembly this process actually loaded out of the package.
        _surfaceOk = ReportPackageSurface();

        if (arguments.SurfaceOnly)
        {
            Console.Out.Flush();
            Shutdown(_surfaceOk ? 0 : SurfaceMismatchExitCode);
            return;
        }

        _exitCode = _surfaceOk ? 0 : SurfaceMismatchExitCode;

        // Before the tray icon, so the toast evidence is never gated on the tray registration
        // outcome. The notifier is a field, so the single ShutdownConsumer exit path below disposes
        // it - and therefore unregisters its identity - whatever ends the run.
        if (arguments.Toast)
        {
            StartToastPath(arguments);
        }

        try
        {
            StartTrayIcon(arguments);
        }
        catch (TrayIconException ex)
        {
            // A registration the shell refused is reported and named, not retried and not swallowed:
            // a WinExe would otherwise die in an invisible unhandled exception, which is exactly how
            // a missing interactive desktop turns into a silent "the icon never appeared".
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] startup failure: operation={ex.Operation} win32Error={ex.Win32ErrorCode}: {ex.Message}"));
            Console.Error.WriteLine("[consumer] no icon will be shown. Is an interactive desktop session with a notification area available?");

            Shutdown(StartupFailureExitCode);
            return;
        }

        Console.WriteLine("[consumer] no window is shown - check the notification area, not the taskbar.");

        StartRunTimers(arguments);

        Console.Out.Flush();
    }

    /// <summary>
    /// Creates the icon, assigns the consumer's own menu, wires the handlers and shows the first
    /// balloon request, which is the whole documented code-first surface.
    /// </summary>
    /// <param name="arguments">The parsed command line.</param>
    private void StartTrayIcon(ConsumerArguments arguments)
    {
        // A plain WPF drawing, frozen, so the icon needs no image file and no decoder: the package's
        // IconSource accepts any ImageSource, and this is the vector case the README documents.
        var image = new DrawingImage(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0xA0)),
            null,
            new EllipseGeometry(new Point(8, 8), 7, 7)));
        image.Freeze();

        var trayIcon = new ConsumerTrayIcon();

        // Subscribed before the first registration, so a failure during registration is reported
        // rather than escaping as an unhandled exception from the startup path.
        trayIcon.TrayError += OnTrayError;

        // The four click types the shell reports, with one Preview counterpart to show the tunnel
        // phase: a consumer's handlers, not the sample's.
        trayIcon.TrayLeftClick += OnClick;
        trayIcon.TrayLeftDoubleClick += OnClick;
        trayIcon.TrayRightClick += OnClick;
        trayIcon.TrayMiddleClick += OnClick;
        trayIcon.PreviewTrayLeftClick += OnPreviewClick;

        // The balloon click pair, the half of ShowBalloonTip a consumer subscribes to.
        trayIcon.PreviewBalloonTipClicked += (_, _) => Say("[consumer] balloon preview clicked (tunnel phase).");
        trayIcon.BalloonTipClicked += OnBalloonTipClicked;

        trayIcon.ToolTipText = "ConsumerProof - Trustsoft.NotifyIcon from the local package feed";

        // Visible first, then the image: the icon exists before it has a picture, which is the
        // documented order a windowless application can use.
        trayIcon.Visible = true;
        trayIcon.IconSource = image;

        // The menu is the consumer's own object graph. The library opens it, points its
        // PlacementTarget at its own anchor window and never writes its DataContext, so the
        // DataContext is set here - once, by the caller, exactly as the README's interaction-model
        // section says.
        var menu = new ContextMenu { DataContext = new ConsumerMenuData { Label = "consumer data context" } };
        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;
        menu.Items.Add(CreateMenuItem("ConsumerProof menu item"));
        menu.Items.Add(new MenuItem { Header = new Binding("Label") });

        trayIcon.ContextMenu = menu;

        _trayIcon = trayIcon;
        _instrumentedIcon = trayIcon;
        _menu = menu;

        Say($"[consumer] tray icon registered from code; tooltip and a {menu.Items.Count}-item menu assigned.");
        Say("[consumer] interaction model: a right click opens the assigned menu, the four click types are routed events, the menu is this application's own instance, and a balloon is a method call rather than a property.");

        if (arguments.SelfOpenMenuAfter is TimeSpan selfOpenDelay)
        {
            _selfOpenTimer = CreateTimer(
                selfOpenDelay,
                () =>
                {
                    Say("[consumer] opening the assigned menu now with no shell click injected (OnTrayClick on the consumer's own subclass).");
                    _instrumentedIcon?.RequestMenuOpen(ReadCursorPosition());
                });
        }

        if (arguments.ShowBalloonAfter is TimeSpan balloonDelay)
        {
            _balloonTimer = CreateTimer(balloonDelay, ShowBalloon);
        }
    }

    /// <summary>Starts the run's own timers: the balloon, if requested, and the shutdown deadline.</summary>
    /// <param name="arguments">The parsed command line.</param>
    private void StartRunTimers(ConsumerArguments arguments)
    {
        // The default balloon: a package user who installs the library and runs the documented
        // example should see a balloon without having to pass a switch.
        if (arguments.ShowBalloonAfter is null)
        {
            _balloonTimer ??= CreateTimer(TimeSpan.FromSeconds(arguments.RunSeconds / 2), ShowBalloon);
        }

        _runTimer = CreateTimer(
            TimeSpan.FromSeconds(arguments.RunSeconds),
            () =>
            {
                Say(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[consumer] run deadline reached after {arguments.RunSeconds:0.#}s - shutting down and disposing the icon."));
                Shutdown(_exitCode);
            });

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] run plan: {arguments.RunSeconds:0.#}s, self-open menu {(arguments.SelfOpenMenuAfter is TimeSpan open ? $"after {open.TotalSeconds:0.#}s" : "off")}, balloon {(arguments.ShowBalloonAfter is TimeSpan balloon ? $"after {balloon.TotalSeconds:0.#}s" : "shown once mid-run")}."));
    }

    /// <summary>
    /// Shows one balloon through the public method and records the request.
    /// </summary>
    /// <remarks>
    /// The balloon is a method call, not a dependency property (D031), so this is the whole API a
    /// consumer uses for one - and the request count is what makes "a balloon was requested" a
    /// reading rather than an inference from the absence of an exception.
    /// </remarks>
    private void ShowBalloon()
    {
        TrayIcon? trayIcon = _trayIcon;

        if (trayIcon is null)
        {
            return;
        }

        _balloonShowCount++;

        Say($"[consumer] balloon show request #{_balloonShowCount} through ShowBalloonTip(title, text, BalloonTipIcon.Info).");

        trayIcon.ShowBalloonTip(
            "ConsumerProof",
            "This balloon came from the packaged library, installed into a windowless WPF application.",
            BalloonTipIcon.Info);
    }

    // ---------------------------------------------------------------------------------------------
    // M002 toast path (--toast)
    //
    // The consumer half of the milestone's second and third criteria: a real toast built from the
    // packaged library's own vocabulary, shown through the public ToastNotifier, with the three typed
    // S03 events subscribed. What each line is for, and what the measurement already fixed about this
    // path, is in docs/TOAST-MEASUREMENT.md; the identities the S05 runner uses are
    // Trustsoft.NotifyIcon.ConsumerProof.<tfm>, one per target framework.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the toast path: derives the identity, constructs the public notifier, subscribes the
    /// three typed events and schedules the one show.
    /// </summary>
    /// <param name="arguments">The parsed command line; <see cref="ConsumerArguments.Toast"/> is true.</param>
    /// <remarks>
    /// <para>
    /// <b>This run does not register here.</b> Since D060 the notifier registers on its first
    /// <c>Show</c>, so this method only says which identity the run will use; registration, the
    /// show and the events all happen inside the timer that fires at <c>--toast-after</c>.
    /// </para>
    /// <para>
    /// <b>The override is assigned before the first show.</b> <see cref="ToastNotifier.AppUserModelId"/>
    /// is settable only until registration has been attempted and throws afterwards rather than
    /// ignoring a late value, so the value must be set here - and a null value is left null, which
    /// is exactly the documented meaning of the property: derive the entry assembly's default.
    /// </para>
    /// </remarks>
    private void StartToastPath(ConsumerArguments arguments)
    {
        _toastStarted = true;
        _toastButtons = arguments.ToastButtons;
        _toastIdentity = EffectiveAppUserModelId(arguments.ToastAppUserModelId);

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast demonstration: identity='{_toastIdentity}' buttons={_toastButtons} - the show goes through the public ToastNotifier, which registers the identity on its first show (D060) and raises the three typed events."));

        var notifier = new ToastNotifier { AppUserModelId = arguments.ToastAppUserModelId };

        // Subscribed before the first show, so a delivery that races the display is classified rather
        // than dropped.
        notifier.Activated += OnToastActivated;
        notifier.Dismissed += OnToastDismissed;
        notifier.ToastError += OnToastError;

        _toastNotifier = notifier;

        TimeSpan delay = arguments.ToastAfter ?? DefaultToastDelay;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast show scheduled: first show after {delay.TotalSeconds:0.#}s; the toast's launch argument is '{ToastLaunchPrefix}N', so the activation a click delivers names the show it came from."));

        _toastTimer = CreateTimer(delay, ShowToastOnce);

        if (arguments.ToastDisposeAfter is TimeSpan disposeDelay)
        {
            Say(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] toast dispose scheduled: the notifier is disposed after {disposeDelay.TotalSeconds:0.#}s while this run keeps pumping, so an activation, dismissal or error arriving after the teardown line is measured rather than assumed."));

            _toastDisposeTimer = CreateTimer(disposeDelay, StopToastPath);
        }
    }

    /// <summary>
    /// Builds one <see cref="ToastContent"/>, prints exactly what this consumer built and shows it
    /// through the public notifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One content, printed before it is shown.</b> The line names the identity, the title, the
    /// body, the launch argument and - when <c>--toast-buttons</c> is set - the two button arguments
    /// this consumer built, so a capture can compare what was asked for with what arrived. The
    /// identity is part of the line because the activation's argument names a show, while the
    /// shortcut that routes it names an identity; a reader needs both to place a delivery.
    /// </para>
    /// <para>
    /// <b>A refusal is exit code 1, not a crash.</b> A <see cref="ToastException"/> is the documented
    /// failure of the first <c>Show</c> (registration failed), and a WinExe would otherwise die in an
    /// invisible unhandled exception; the show is counted as refused, never as accepted, and nothing
    /// about it is printed as success. A failure the show path reports after a successful
    /// registration arrives on <see cref="ToastNotifier.ToastError"/> instead (the D055/D061 split)
    /// and never changes the exit code.
    /// </para>
    /// </remarks>
    private void ShowToastOnce()
    {
        _toastShowCount++;

        string launch = ToastLaunchPrefix + _toastShowCount.ToString(CultureInfo.InvariantCulture);
        var content = new ToastContent
        {
            Title = ToastTitle,
            Body = ToastBody,
            Launch = launch,
            Severity = ToastSeverity.Default,
        };

        if (_toastButtons)
        {
            // The slice's two-button toast: distinguishable labels for a person, and arguments this
            // consumer can recognise in a delivered activation.
            content.Buttons.Add(new ToastButton { Text = ToastButton1Text, Arguments = ToastButton1Argument });
            content.Buttons.Add(new ToastButton { Text = ToastButton2Text, Arguments = ToastButton2Argument });
        }

        // Written only when the matching switch was set, so a plain --toast content line stays
        // comparable with the M001-era captures of this project.
        string buttonsReading = _toastButtons
            ? $" buttons=[{ToastButton1Argument},{ToastButton2Argument}]"
            : string.Empty;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast content: identity='{_toastIdentity}' title='{content.Title}' body='{content.Body}' severity={content.Severity} launch='{launch}'{buttonsReading}"));

        ToastNotifier? notifier = _toastNotifier;

        if (notifier is null)
        {
            return;
        }

        try
        {
            notifier.Show(content);
        }
        catch (ToastException exception)
        {
            _toastRefusedCount++;
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] toast show #{_toastShowCount}: REFUSED operation='{exception.Operation}' code=0x{exception.ErrorCode:X8} - the shell did not accept a toast for identity='{_toastIdentity}'"));
            Console.Error.Flush();
            Console.Out.Flush();

            Shutdown(StartupFailureExitCode);
            return;
        }
        catch (Exception exception)
        {
            // A failure type the public surface does not document. Counted, named in full and ended
            // the same way a refusal is, because the run did not produce a toast and must not read as
            // green - and because letting it escape would put it back in the invisible unhandled
            // exception a WinExe hides.
            _toastFailureCount++;
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] toast failure: the show raised {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")} - this is not one of the documented failure types, and the run ends without a toast."));
            Console.Error.Flush();
            Console.Out.Flush();

            Shutdown(StartupFailureExitCode);
            return;
        }

        _toastAcceptedCount++;

        // The platform's setting is an outcome, never a failure: a disabled value is reported here and
        // never raised on ToastError, and it never makes Show throw. Null means "no show in this run
        // reached the setting step", which is the distinction the property is nullable for.
        ReportToastSetting(notifier.NotificationSetting);

        Console.Out.Flush();
    }

    /// <summary>
    /// Prints the notification setting the show read, as the documented outcome of that show rather
    /// than as a failure.
    /// </summary>
    /// <param name="setting">
    /// The notifier's <see cref="ToastNotifier.NotificationSetting"/> after the show, or
    /// <see langword="null"/> when no show in this run reached the setting step.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Null means "not read", never "enabled".</b> A show that failed before the setting step
    /// (a refused registration, or an image the library could not resolve) leaves the property null,
    /// so the line says so instead of guessing.
    /// </para>
    /// <para>
    /// <b>A disabled value is recorded, not treated as a failure.</b> The show was accepted
    /// (<c>S_OK</c> is acceptance, not visibility) and the documented meaning is that the shell will
    /// not render it, so the run says that in its own line; the measured lag of that value (S04: it
    /// can lag a registry change by one process) is why the reading is dated by the run rather than
    /// treated as a live view.
    /// </para>
    /// </remarks>
    private void ReportToastSetting(ToastNotificationSetting? setting)
    {
        if (setting is null)
        {
            Say("[consumer] toast setting: (not read) - no show in this run reached the platform's setting step, so this run says nothing about whether the shell will show the toast.");
            return;
        }

        string meaning = setting switch
        {
            ToastNotificationSetting.Enabled =>
                "the shell will show this application's toasts",
            ToastNotificationSetting.DisabledForApplication =>
                "the shell will not show this application's toasts, because notifications are disabled for this application",
            ToastNotificationSetting.DisabledForUser =>
                "the shell will not show this application's toasts, because notifications are disabled for the user",
            ToastNotificationSetting.DisabledByGroupPolicy =>
                "the shell will not show this application's toasts, because notifications are disabled by group policy",
            ToastNotificationSetting.DisabledByManifest =>
                "the shell will not show this application's toasts, because the application's manifest disables them",
            _ => "the shell will not show this application's toasts",
        };

        // The name the totals repeat. Recorded here rather than read back from the notifier at
        // shutdown, so the totals quote the exact reading the per-show line printed.
        _toastLastSettingName = setting.Value.ToString();

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast setting: {setting} ({(int)setting.Value}) - {meaning}."));

        if (setting is not ToastNotificationSetting.Enabled)
        {
            // The outcome, separated from the acceptance so a capture cannot read "the shell accepted
            // it" as "it was shown": this run's toast is accepted and will not be rendered.
            Say(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] toast outcome: the platform reports {setting} ({(int)setting.Value}) for identity='{_toastIdentity}', so this run's toast is accepted but not shown - an outcome, not a failure."));
        }
    }

    /// <summary>
    /// Prints an activation delivered by the public <see cref="ToastNotifier.Activated"/> event.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload, whose <see cref="ToastActivatedEventArgs.Arguments"/> is the delivered argument.</param>
    /// <remarks>
    /// The line reports the <em>argument</em> and the <em>element that argument names</em>; it does
    /// not report that a click on that element happened, because an activation carries nothing about
    /// which element produced it (S01/D054: an activation is an arrival report, never click
    /// attribution).
    /// </remarks>
    private void OnToastActivated(object? sender, ToastActivatedEventArgs e)
    {
        _toastActivationCount++;
        _toastLastActivationArguments = e.Arguments;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast activated: arguments='{e.Arguments ?? "(null)"}' element={ClassifyToastElement(e.Arguments)} (activation {_toastActivationCount} of {_toastShowCount} show(s))"));
    }

    /// <summary>
    /// Classifies a delivered activation argument as one of the elements this run actually sent, or
    /// <c>unknown</c>.
    /// </summary>
    /// <param name="arguments">The argument Windows delivered, or <see langword="null"/>.</param>
    /// <returns><c>button-1</c>, <c>button-2</c>, <c>body</c> or <c>unknown</c>.</returns>
    /// <remarks>
    /// The classification is a lookup in the arguments this run actually sent - never a guess from
    /// the string's shape. The candidates are the two button arguments (only when
    /// <c>--toast-buttons</c> was set, because a run that never sent a button argument cannot
    /// receive one of its own) and the <c>consumer-toast-N</c> launch arguments of the shows this run
    /// actually issued. Anything else is <c>unknown</c>, and so is an <em>ambiguous</em> match: an
    /// argument two candidates both claim names neither.
    /// </remarks>
    private string ClassifyToastElement(string? arguments)
    {
        if (arguments is null)
        {
            return "unknown";
        }

        string? match = null;
        int matches = 0;

        if (_toastButtons)
        {
            AddMatch(ToastButton1Argument, "button-1");
            AddMatch(ToastButton2Argument, "button-2");
        }

        for (int show = 1; show <= _toastShowCount; show++)
        {
            AddMatch(ToastLaunchPrefix + show.ToString(CultureInfo.InvariantCulture), "body");
        }

        return matches == 1 ? match! : "unknown";

        void AddMatch(string candidate, string element)
        {
            if (string.Equals(candidate, arguments, StringComparison.Ordinal))
            {
                matches++;
                match = element;
            }
        }
    }

    /// <summary>
    /// Prints a dismissal delivered by the public <see cref="ToastNotifier.Dismissed"/> event, with
    /// the public vocabulary name and the ordinal it maps from.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload.</param>
    /// <remarks>
    /// <c>reason=N</c> keeps the numeric shape the earlier captures quote, while the name comes from
    /// the typed vocabulary the event carries - so the same number can be compared across captures
    /// and the reader still gets the library's own word for it.
    /// </remarks>
    private void OnToastDismissed(object? sender, ToastDismissedEventArgs e)
    {
        _toastDismissedCount++;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast dismissed: reason={(int)e.Reason} ({DescribeToastDismissalReason(e.Reason)})"));
    }

    /// <summary>Names a <see cref="ToastDismissalReason"/> in the consumer's own vocabulary, totally.</summary>
    /// <param name="reason">The typed reason the event carried.</param>
    /// <returns>The known name, or <c>unknown</c> for every other value.</returns>
    private static string DescribeToastDismissalReason(ToastDismissalReason reason) => reason switch
    {
        ToastDismissalReason.UserCanceled => "user canceled",
        ToastDismissalReason.ApplicationHidden => "application hidden",
        ToastDismissalReason.TimedOut => "timed out",
        _ => "unknown",
    };

    /// <summary>
    /// Prints a non-fatal toast failure delivered by the public
    /// <see cref="ToastNotifier.ToastError"/> event, and counts it.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload: the failing operation, the code and the optional exception.</param>
    /// <remarks>
    /// On stderr, like the tray failure line, and marked non-fatal because the process is still
    /// alive: this is the D055/D061 channel a show-path failure arrives on instead of an exception,
    /// and it never changes the exit code. The exception is rendered as a type name plus a one-line
    /// message, or <c>(null)</c> when the failure carried none - the normal case for the shell's
    /// asynchronous failure.
    /// </remarks>
    private void OnToastError(object? sender, ToastErrorEventArgs e)
    {
        _toastErrorCount++;

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] toast error: operation='{e.Operation}' code=0x{e.ErrorCode:X8} exception='{DescribeToastException(e.Exception)}' (non-fatal)"));
        Console.Error.Flush();
    }

    /// <summary>Renders a toast failure's exception as a one-line reading, or the placeholder for none.</summary>
    /// <param name="exception">The exception the failure carried, or <see langword="null"/>.</param>
    /// <returns><c>(null)</c>, or <c>Type: message</c> with any line break collapsed to a space.</returns>
    private static string DescribeToastException(Exception? exception) =>
        exception is null
            ? "(null)"
            : $"{exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}";

    /// <summary>
    /// Disposes the notifier, drops its subscriptions and takes the post-teardown snapshot; safe to
    /// call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Disposal is the unregistration.</b> The notifier removes the shortcut its own registration
    /// wrote, so a run that reached this method leaves the machine with nothing registered - which is
    /// what the S05 runner reads back from a second process after every framework's run, at both the
    /// mid-run point (<c>--toast-dispose-after</c>) and the exit path.
    /// </para>
    /// <para>
    /// <b>Calling it mid-run is what makes the post-teardown window a window.</b> The dispose timer
    /// calls this method and the process keeps pumping until its run deadline, so a callback that
    /// arrived after the teardown line would make the window line non-zero; calling it again from
    /// <see cref="ShutdownConsumer"/> is a no-op, so the snapshot stays where the teardown happened.
    /// </para>
    /// </remarks>
    private void StopToastPath()
    {
        if (_toastStopped)
        {
            return;
        }

        _toastStopped = true;

        StopTimer(ref _toastTimer);
        StopTimer(ref _toastDisposeTimer);

        ToastNotifier? notifier = _toastNotifier;
        _toastNotifier = null;

        if (notifier is not null)
        {
            notifier.Activated -= OnToastActivated;
            notifier.Dismissed -= OnToastDismissed;
            notifier.ToastError -= OnToastError;

            notifier.Dispose();
        }

        if (_toastAcceptedCount > 0)
        {
            // One line per teardown, not per show: the count is the number of accepted shows this
            // process held a subscription for, which is what the disposal guarantee has to make
            // silent.
            Say(string.Create(
                CultureInfo.InvariantCulture,
                $"[consumer] toast teardown: {_toastAcceptedCount} show(s) unsubscribed and released - no activation subscription outlives the process."));
        }

        // The post-teardown window starts here: everything counted up to this point is pre-teardown
        // and the remaining wall time of a --toast-dispose-after run is the silence the window line
        // measures.
        _toastActivationsAtTeardown = _toastActivationCount;
        _toastDismissalsAtTeardown = _toastDismissedCount;
        _toastErrorsAtTeardown = _toastErrorCount;
        _toastTeardownRecorded = true;
    }

    /// <summary>
    /// Names the identity a run will use: the <c>--toast-aumid</c> override when one was given,
    /// otherwise the library's documented default.
    /// </summary>
    /// <param name="overrideIdentity">The value of <c>--toast-aumid</c>, or <see langword="null"/>.</param>
    /// <returns>The identity this run asks the shell to show toasts for.</returns>
    /// <remarks>
    /// The default is re-derived here - the entry assembly's simple name, falling back to the
    /// library's own name - from the documented rule rather than read back from the notifier, because
    /// <see cref="ToastNotifier.AppUserModelId"/> keeps the override and nothing else. A run whose
    /// identity could not be named is a run whose evidence could not be attributed, so the fallback
    /// is spelled out rather than left as an empty string.
    /// </remarks>
    private static string EffectiveAppUserModelId(string? overrideIdentity) =>
        string.IsNullOrEmpty(overrideIdentity)
            ? Assembly.GetEntryAssembly()?.GetName().Name
                ?? typeof(TrayIcon).Assembly.GetName().Name
                ?? "(unknown)"
            : overrideIdentity;

    /// <summary>Creates one menu item whose click is counted and reported.</summary>
    /// <param name="header">The item's header.</param>
    /// <returns>The item, with its click handler attached.</returns>
    private MenuItem CreateMenuItem(string header)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) =>
        {
            _menuItemClickCount++;
            Say($"[consumer] menu item clicked: '{item.Header}' (item clicks={_menuItemClickCount}).");
        };

        return item;
    }

    /// <summary>Reports a click the shell delivered to this application.</summary>
    /// <param name="sender">The icon.</param>
    /// <param name="e">The click payload: the button, the count and the anchor.</param>
    private void OnClick(object? sender, TrayIconClickEventArgs e)
    {
        _clickCount++;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] click: button={e.Button} count={e.ClickCount} anchor={e.ScreenAnchor.X},{e.ScreenAnchor.Y} (clicks={_clickCount})."));
    }

    /// <summary>Reports the tunnel phase of a left click, so both phases are visible in the log.</summary>
    /// <param name="sender">The icon.</param>
    /// <param name="e">The click payload.</param>
    private void OnPreviewClick(object? sender, TrayIconClickEventArgs e)
    {
        _previewClickCount++;

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] preview click: button={e.Button} (preview deliveries={_previewClickCount})."));
    }

    /// <summary>Reports a balloon click delivered by the shell.</summary>
    /// <param name="sender">The icon.</param>
    /// <param name="e">The routed event arguments.</param>
    private void OnBalloonTipClicked(object? sender, RoutedEventArgs e)
    {
        _balloonClickCount++;
        Say($"[consumer] balloon clicked (deliveries={_balloonClickCount}) - the shell accepted a click on the balloon.");
    }

    /// <summary>Counts a menu open and schedules the close, which is what makes dismissal observable.</summary>
    /// <param name="sender">The menu.</param>
    /// <param name="e">The event arguments.</param>
    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        _menuOpenCount++;
        Say($"[consumer] menu opened (opens={_menuOpenCount}) - by the library from the assigned instance, at the icon.");

        _menuHoldTimer ??= CreateTimer(TimeSpan.FromSeconds(MenuHoldSeconds), () =>
        {
            if (_menu is { IsOpen: true } menu)
            {
                Say("[consumer] closing the menu held open by this run.");
                menu.IsOpen = false;
            }
        });
    }

    /// <summary>Counts a menu dismissal.</summary>
    /// <param name="sender">The menu.</param>
    /// <param name="e">The event arguments.</param>
    private void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        _menuDismissedCount++;
        Say($"[consumer] menu dismissed (dismissals={_menuDismissedCount}).");
    }

    /// <summary>Reports a runtime failure the library surfaced through <c>TrayError</c>.</summary>
    /// <param name="sender">The icon.</param>
    /// <param name="e">The failure, with its operation and Win32 error code.</param>
    private void OnTrayError(object? sender, TrayErrorEventArgs e)
    {
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] tray error: operation={e.Operation} win32Error={e.Win32ErrorCode} retried={e.Retried}: {e.Exception?.Message}"));
        Console.Error.Flush();
    }

    /// <summary>
    /// Removes the icon, destroys its handle and prints the run's totals; safe to call more than once.
    /// </summary>
    /// <remarks>
    /// This is the clean disposal path a windowless consumer must own: nothing else ends the process
    /// with the icon removed, and the totals are printed after disposal so a capture that lost the
    /// last lines of a run can be told apart from a run that never reached them.
    /// </remarks>
    private void ShutdownConsumer()
    {
        if (_shutdownDone)
        {
            return;
        }

        _shutdownDone = true;

        StopTimer(ref _runTimer);
        StopTimer(ref _selfOpenTimer);
        StopTimer(ref _menuHoldTimer);
        StopTimer(ref _balloonTimer);

        TrayIcon? trayIcon = _trayIcon;
        _trayIcon = null;
        _instrumentedIcon = null;

        if (trayIcon is not null)
        {
            trayIcon.TrayError -= OnTrayError;
            trayIcon.TrayLeftClick -= OnClick;
            trayIcon.TrayLeftDoubleClick -= OnClick;
            trayIcon.TrayRightClick -= OnClick;
            trayIcon.TrayMiddleClick -= OnClick;
            trayIcon.PreviewTrayLeftClick -= OnPreviewClick;
            trayIcon.BalloonTipClicked -= OnBalloonTipClicked;

            trayIcon.Dispose();
            Say("[consumer] tray icon disposed - it must have left the notification area.");
        }

        ContextMenu? menu = _menu;
        _menu = null;

        if (menu is not null)
        {
            menu.Opened -= OnMenuOpened;
            menu.Closed -= OnMenuClosed;
        }

        // The toast path's single teardown, on the same exit path that disposes the icon: idempotent,
        // so a run that already disposed its notifier mid-run (--toast-dispose-after) keeps the
        // snapshot it took then and this call only stops the timers again. Gated on the path having
        // been started, so a run without --toast prints no toast line at all.
        if (_toastStarted)
        {
            StopToastPath();

            if (_toastTeardownRecorded)
            {
                // The disposal guarantee measured rather than claimed: every toast callback that arrived
                // after the teardown line would have incremented one of these counters, and the snapshot
                // taken in StopToastPath is what makes the reading a window (a --toast-dispose-after run
                // keeps pumping for the rest of the run) instead of a single instant.
                Say(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[consumer] toast post-teardown window: activations={_toastActivationCount - _toastActivationsAtTeardown} dismissals={_toastDismissedCount - _toastDismissalsAtTeardown} errors={_toastErrorCount - _toastErrorsAtTeardown} after the teardown line"));
            }
        }

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] totals: clicks={_clickCount}, preview deliveries={_previewClickCount}, menu opens={_menuOpenCount}, menu dismissals={_menuDismissedCount}, menu item clicks={_menuItemClickCount}, balloon show requests={_balloonShowCount}, balloon clicked deliveries={_balloonClickCount}, toast shows={_toastShowCount}, toast accepted={_toastAcceptedCount}, toast activations={_toastActivationCount} (last arguments='{_toastLastActivationArguments ?? "(none)"}'), toast dismissals={_toastDismissedCount}, toast errors={_toastErrorCount}, toast failures={_toastFailureCount}, toast refused={_toastRefusedCount}, toast setting={_toastLastSettingName ?? "(none)"}, toast identity='{_toastIdentity ?? "(none)"}'."));
        Say($"[consumer] public surface of the installed package: {(_surfaceOk ? "PASS (exactly the twenty documented types: the tray surface and the toast surface)" : "FAIL (see the lines above)")}; exit code={_exitCode}.");

        Console.Out.Flush();
    }

    /// <summary>
    /// Checks the installed package's public surface and its assembly references from this
    /// assembly's point of view, and prints one line per finding.
    /// </summary>
    /// <returns><see langword="true"/> when the surface is exactly the documented one and the
    /// package references the platform only.</returns>
    private static bool ReportPackageSurface()
    {
        Assembly library = typeof(TrayIcon).Assembly;
        string[] observed =
        [
            .. library.GetExportedTypes()
                .Where(type => !IsCompilerGenerated(type))
                .Select(type => type.FullName ?? type.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        string[] expected = [.. ExpectedPublicTypes.OrderBy(name => name, StringComparer.Ordinal)];

        string? informational = library
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        string frameworkName = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName ?? "(unknown)";

        Say($"[consumer] package assembly: {library.Location}");
        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] package identity: {library.GetName().Name} assemblyVersion={library.GetName().Version} informationalVersion={informational}"));
        Say($"[consumer] this build targets: {frameworkName} (base directory {AppContext.BaseDirectory})");
        Say($"[consumer] public surface: {observed.Length} exported type(s): {string.Join(", ", observed)}");

        bool surfaceMatches = observed.SequenceEqual(expected, StringComparer.Ordinal);

        if (surfaceMatches)
        {
            Say($"[consumer] PASS the package surfaces exactly the {expected.Length} documented public types - the seven M001 tray types and the thirteen M002 toast types - and no others, checked from this consumer assembly rather than from the library's test project.");
        }
        else
        {
            string[] missing = [.. expected.Except(observed, StringComparer.Ordinal)];
            string[] unexpected = [.. observed.Except(expected, StringComparer.Ordinal)];

            Say($"[consumer] FAIL the package surface is not the documented one: {expected.Length} expected, {observed.Length} observed; missing=[{string.Join(", ", missing)}]; unexpected=[{string.Join(", ", unexpected)}]");
        }

        // The consumer-visible half of R011: the assembly the package delivered carries no
        // dependency the platform does not provide. A consumer notices this as "it just works"; a
        // missing dependency at runtime is exactly the setup step the claim says does not exist.
        string[] references =
        [
            .. library.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? "(unnamed)")
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Say($"[consumer] package assembly references: {string.Join(", ", references)}");

        string[] forbidden =
        [
            .. references.Where(name =>
                name.Contains("Windows.Forms", StringComparison.Ordinal)
                || name.Contains("Drawing", StringComparison.Ordinal)
                || (name.Contains("NotifyIcon", StringComparison.Ordinal)
                    && !string.Equals(name, "Trustsoft.NotifyIcon", StringComparison.Ordinal))),
        ];

        bool referencesArePlatformOnly = forbidden.Length == 0;

        if (referencesArePlatformOnly)
        {
            Say("[consumer] PASS the package's assembly references no WinForms, no System.Drawing and no other tray implementation.");
        }
        else
        {
            Say($"[consumer] FAIL the package's assembly references [{string.Join(", ", forbidden)}], which the package is not allowed to depend on.");
        }

        return surfaceMatches && referencesArePlatformOnly;
    }

    /// <summary>
    /// Reports whether a type was produced by the compiler rather than written by hand.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <returns><see langword="true"/> for compiler-generated types.</returns>
    /// <remarks>
    /// The same exclusion the library's own surface test applies. A nested compiler-generated type
    /// is reported as compiler-generated too, so filtering cannot hide a real public nested class
    /// behind a name that merely looks odd.
    /// </remarks>
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (type.IsNested && type.DeclaringType is not null && IsCompilerGenerated(type.DeclaringType));

    /// <summary>Creates a one-shot timer that stops itself inside the tick.</summary>
    /// <param name="interval">The delay before the action runs.</param>
    /// <param name="action">What to do.</param>
    /// <returns>The running timer.</returns>
    private static DispatcherTimer CreateTimer(TimeSpan interval, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = interval };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };

        timer.Start();

        return timer;
    }

    /// <summary>Stops and forgets a timer.</summary>
    /// <param name="timer">The timer, or <see langword="null"/>.</param>
    private static void StopTimer(ref DispatcherTimer? timer)
    {
        timer?.Stop();
        timer = null;
    }

    /// <summary>Writes one line to the evidence stream and flushes it.</summary>
    /// <param name="line">The line to write.</param>
    /// <remarks>
    /// Flushed per line because this console is the evidence: a line still sitting in a buffer when
    /// the process exits is a line the probe's capture cannot read.
    /// </remarks>
    private static void Say(string line)
    {
        Console.WriteLine(line);
        Console.Out.Flush();
    }

    /// <summary>Reads the cursor position, which is the anchor a self-opened menu reports.</summary>
    /// <returns>The cursor position in physical screen pixels.</returns>
    private static Point ReadCursorPosition()
    {
        return GetCursorPos(out POINT point) ? new Point(point.X, point.Y) : new Point(0, 0);
    }

    /// <summary>
    /// Parses the command line, rejecting anything it does not understand.
    /// </summary>
    /// <param name="args">The startup arguments.</param>
    /// <param name="arguments">The parsed values.</param>
    /// <param name="error">A message naming the first argument that was not understood.</param>
    /// <returns><see langword="true"/> when every argument was understood.</returns>
    private static bool TryParseArguments(string[] args, out ConsumerArguments arguments, out string? error)
    {
        double runSeconds = DefaultRunSeconds;
        bool runSecondsSpecified = false;
        TimeSpan? selfOpenMenuAfter = null;
        TimeSpan? showBalloonAfter = null;
        bool surfaceOnly = false;
        bool toast = false;
        bool toastButtons = false;
        string? toastAppUserModelId = null;
        TimeSpan? toastAfter = null;
        TimeSpan? toastDisposeAfter = null;
        error = null;
        arguments = default!;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            int separator = argument.IndexOf('=');
            string name = separator < 0 ? argument : argument[..separator];
            string? inlineValue = separator < 0 ? null : argument[(separator + 1)..];

            switch (name)
            {
                case "--surface-only":
                    if (inlineValue is not null)
                    {
                        error = $"[consumer] '{argument}' takes no value: write it as --surface-only.";
                        return false;
                    }

                    surfaceOnly = true;
                    break;

                case "--run-seconds":
                case "--self-open-menu-after":
                case "--show-balloon-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? rawValue))
                    {
                        error = $"[consumer] '{argument}' needs a number of seconds: use {name} N or {name}=N.";
                        return false;
                    }

                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
                    {
                        error = $"[consumer] '{name}' needs a non-negative number of seconds, got '{rawValue}'.";
                        return false;
                    }

                    if (name == "--run-seconds")
                    {
                        runSeconds = seconds == 0 ? DefaultRunSeconds : seconds;
                        runSecondsSpecified = true;
                    }
                    else if (name == "--self-open-menu-after")
                    {
                        selfOpenMenuAfter = TimeSpan.FromSeconds(seconds);
                    }
                    else
                    {
                        showBalloonAfter = TimeSpan.FromSeconds(seconds);
                    }

                    break;

                case "--toast":
                    // A flag, not a switch with a value: the content is this consumer's own, so a
                    // '=value' spelling would be swallowed silently.
                    if (inlineValue is not null)
                    {
                        error = $"[consumer] '{argument}' takes no value: write it as --toast.";
                        return false;
                    }

                    toast = true;
                    break;

                case "--toast-buttons":
                    // A flag, like --toast and --surface-only: the two-button content is this
                    // consumer's own, so a '=value' spelling would be swallowed silently.
                    if (inlineValue is not null)
                    {
                        error = $"[consumer] '{argument}' takes no value: write it as --toast-buttons.";
                        return false;
                    }

                    toastButtons = true;
                    break;

                case "--toast-aumid":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastAumidValue))
                    {
                        error = $"[consumer] '{argument}' needs an identity: use --toast-aumid <id> or --toast-aumid=<id>.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(toastAumidValue))
                    {
                        error = "[consumer] '--toast-aumid' needs a non-empty identity.";
                        return false;
                    }

                    toastAppUserModelId = toastAumidValue;
                    break;

                case "--toast-after":
                    // The value is optional, as in the in-repo sample: a bare switch means the
                    // default delay rather than a usage error, because the switch's own presence is
                    // the request and the delay is a detail.
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastAfterValue))
                    {
                        toastAfter = DefaultToastDelay;
                        break;
                    }

                    if (!double.TryParse(toastAfterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double toastDelaySeconds) || toastDelaySeconds < 0)
                    {
                        error = $"[consumer] '--toast-after' needs a delay in seconds, got '{toastAfterValue}'.";
                        return false;
                    }

                    toastAfter = TimeSpan.FromSeconds(toastDelaySeconds);
                    break;

                case "--toast-dispose-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastDisposeValue))
                    {
                        toastDisposeAfter = DefaultToastDisposeDelay;
                        break;
                    }

                    if (!double.TryParse(toastDisposeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double toastDisposeSeconds) || toastDisposeSeconds < 0)
                    {
                        error = $"[consumer] '--toast-dispose-after' needs a delay in seconds, got '{toastDisposeValue}'.";
                        return false;
                    }

                    toastDisposeAfter = TimeSpan.FromSeconds(toastDisposeSeconds);
                    break;

                default:
                    error = $"[consumer] unknown argument '{argument}' - this application does not ignore arguments it does not understand.";
                    return false;
            }
        }

        // The toast switches select behaviour inside the toast path, so one of them without --toast
        // would be a switch that does nothing - the one failure mode this parser refuses.
        if (!toast && (toastButtons || toastAppUserModelId is not null || toastAfter is not null || toastDisposeAfter is not null))
        {
            error = "[consumer] --toast-aumid, --toast-after, --toast-buttons and --toast-dispose-after only apply to the toast demonstration: add --toast.";
            return false;
        }

        if (surfaceOnly && (runSecondsSpecified || selfOpenMenuAfter is not null || showBalloonAfter is not null || toast))
        {
            error = "[consumer] '--surface-only' checks the package surface and exits before an icon or a toast exists, so it cannot be combined with a run switch.";
            return false;
        }

        arguments = new ConsumerArguments(runSeconds, selfOpenMenuAfter, showBalloonAfter, surfaceOnly, toast, toastButtons, toastAppUserModelId, toastAfter, toastDisposeAfter);
        return true;
    }

    /// <summary>Reads the value of one switch, in either its separated or its <c>=</c>-joined form.</summary>
    /// <param name="inlineValue">The value from the <c>=</c>-joined form, or <see langword="null"/>.</param>
    /// <param name="args">The startup arguments.</param>
    /// <param name="index">The current index; advanced when the separated form supplies the value.</param>
    /// <param name="value">The value, when one was supplied.</param>
    /// <returns><see langword="true"/> when a value was supplied.</returns>
    private static bool TryTakeValue(string? inlineValue, string[] args, ref int index, out string? value)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>The switches this application understands.</summary>
    /// <param name="RunSeconds">How long the run lasts before it shuts itself down.</param>
    /// <param name="SelfOpenMenuAfter">The delay after which the assigned menu is opened with no shell click, or <see langword="null"/>.</param>
    /// <param name="ShowBalloonAfter">The delay after which one balloon is shown, or <see langword="null"/>.</param>
    /// <param name="SurfaceOnly">Whether to check the package surface and exit without creating an icon.</param>
    /// <param name="Toast">Whether the toast path runs at all (<c>--toast</c>).</param>
    /// <param name="ToastButtons">Whether the toast carries the two action buttons (<c>--toast-buttons</c>).</param>
    /// <param name="ToastAppUserModelId">The identity override (<c>--toast-aumid</c>), or <see langword="null"/> for the library's default.</param>
    /// <param name="ToastAfter">The delay after which the toast is shown, or <see langword="null"/> when <c>--toast-after</c> was not given.</param>
    /// <param name="ToastDisposeAfter">The delay after which the notifier is disposed while the run keeps pumping, or <see langword="null"/>.</param>
    private sealed record ConsumerArguments(
        double RunSeconds,
        TimeSpan? SelfOpenMenuAfter,
        TimeSpan? ShowBalloonAfter,
        bool SurfaceOnly,
        bool Toast,
        bool ToastButtons,
        string? ToastAppUserModelId,
        TimeSpan? ToastAfter,
        TimeSpan? ToastDisposeAfter);

    /// <summary>A screen point, in the layout <c>GetCursorPos</c> writes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        /// <summary>The x coordinate.</summary>
        public int X;

        /// <summary>The y coordinate.</summary>
        public int Y;
    }

    /// <summary>Reads the cursor position; the consumer's own interop, not the library's.</summary>
    /// <param name="point">Receives the position.</param>
    /// <returns><see langword="true"/> when it was read.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
}

/// <summary>
/// The consumer's own <see cref="TrayIcon"/>, with one extra entry point: the documented
/// <c>OnTrayClick</c> hook, invoked directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subclass exists in a consumer proof.</b> <c>--self-open-menu-after</c> has to open the
/// assigned menu with no shell click, because the click injection S06 attempted was never observed
/// to arrive (F1 in <c>docs/UAT-S06.md</c>). The only supported way to run that path from outside
/// the library is the protected hook a subclass is documented to call, so the proof carries the same
/// small subclass the in-repo sample carries - re-implemented here, not shared, so this project
/// still takes no reference to the sample.
/// </para>
/// <para>
/// It reaches <c>base</c> by construction: it does not override the method, it invokes the base
/// implementation, so none of the library's policy is re-implemented here. Everything the run's log
/// attributes to the library (the menu activation, the shell icon rectangle, the monitor's DPI, the
/// placement, the anchor window, a real WPF popup) runs inside the package.
/// </para>
/// </remarks>
internal sealed class ConsumerTrayIcon : TrayIcon
{
    /// <summary>
    /// Runs the default action of a right click, as if the shell had delivered one.
    /// </summary>
    /// <param name="screenAnchor">The cursor position to report as the click's anchor, in physical screen pixels.</param>
    public void RequestMenuOpen(Point screenAnchor) =>
        OnTrayClick(new TrayIconClickEventArgs(MouseButton.Right, 1, screenAnchor, TrayIcon.TrayRightClickEvent));
}

/// <summary>
/// The object the consumer's menu data context points at, so the menu's bound item resolves.
/// </summary>
/// <remarks>
/// It exists because the data context of a context menu is the caller's business: the menu has no
/// logical parent and the library points its <c>PlacementTarget</c> at its own anchor window, so the
/// icon's data context never reaches the menu and the library never writes one. A consumer that
/// wants a bound item sets the menu's own <c>DataContext</c>, which is what this run does.
/// </remarks>
internal sealed class ConsumerMenuData
{
    /// <summary>Gets or sets the text the menu's bound item resolves to.</summary>
    public string Label { get; set; } = string.Empty;
}
