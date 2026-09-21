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

    /// <summary>The accepted command line, printed on every usage error.</summary>
    private const string Usage =
        "usage: ConsumerProof [--run-seconds <seconds>] [--self-open-menu-after <seconds>] "
        + "[--show-balloon-after <seconds>] [--surface-only]";

    /// <summary>
    /// The seven public types the README documents, as the package's surface.
    /// </summary>
    /// <remarks>
    /// Written out here - in the consumer's own source, from the consumer's own reading of the
    /// documentation - rather than read from the library's test project, because the claim being
    /// checked is what a package user sees. The list is the consumer-side half of
    /// <c>PackagePurityTests.Public_surface_is_only_the_documented_types</c>: that test asserts the
    /// library's view of itself, this one asserts the same set from an assembly that only ever
    /// installed a nupkg. A widened surface fails here even if nobody updated the library's test,
    /// which is what makes it a consumer-side assertion rather than a restatement.
    /// </remarks>
    private static readonly string[] ExpectedPublicTypes =
    [
        "Trustsoft.NotifyIcon.BalloonTipIcon",
        "Trustsoft.NotifyIcon.BalloonTipOptions",
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

        Say(string.Create(
            CultureInfo.InvariantCulture,
            $"[consumer] totals: clicks={_clickCount}, preview deliveries={_previewClickCount}, menu opens={_menuOpenCount}, menu dismissals={_menuDismissedCount}, menu item clicks={_menuItemClickCount}, balloon show requests={_balloonShowCount}, balloon clicked deliveries={_balloonClickCount}."));
        Say($"[consumer] public surface of the installed package: {(_surfaceOk ? "PASS (exactly the seven documented types)" : "FAIL (see the lines above)")}; exit code={_exitCode}.");

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
            Say($"[consumer] PASS the package surfaces exactly the {expected.Length} documented public types and no others, checked from this consumer assembly rather than from the library's test project.");
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

                default:
                    error = $"[consumer] unknown argument '{argument}' - this application does not ignore arguments it does not understand.";
                    return false;
            }
        }

        if (surfaceOnly && (runSecondsSpecified || selfOpenMenuAfter is not null || showBalloonAfter is not null))
        {
            error = "[consumer] '--surface-only' checks the package surface and exits before an icon exists, so it cannot be combined with a run switch.";
            return false;
        }

        arguments = new ConsumerArguments(runSeconds, selfOpenMenuAfter, showBalloonAfter, surfaceOnly);
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
    private sealed record ConsumerArguments(
        double RunSeconds,
        TimeSpan? SelfOpenMenuAfter,
        TimeSpan? ShowBalloonAfter,
        bool SurfaceOnly);

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
