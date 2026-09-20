using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon;

namespace Trustsoft.NotifyIcon.Sample;

/// <summary>
/// The live proof for S01 (D009): a WPF application with no window at all that owns a real icon in
/// the notification area, rotates that icon once per second, reports failures on the console and
/// removes the icon on the way out.
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

    private TrayIcon? _trayIcon;
    private DispatcherTimer? _rotationTimer;
    private DispatcherTimer? _shutdownTimer;
    private int _frameIndex;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dispose the icon on every normal exit path. SessionEnding is the logoff/shutdown case;
        // Exit covers an explicit Shutdown as well as the end of Run().
        SessionEnding += (_, _) => ShutdownSample();
        Exit += (_, _) => ShutdownSample();

        var trayIcon = new TrayIcon();

        // Subscribed before the first registration so a failure during registration is reported
        // rather than escaping as an unhandled exception from the startup path.
        trayIcon.TrayError += OnTrayError;
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

        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[sample] frame {_frameIndex + 1}/{Frames.Length} applied."), "Trustsoft.NotifyIcon.Sample");
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

        if (trayIcon is null)
        {
            return;
        }

        trayIcon.TrayError -= OnTrayError;
        trayIcon.Dispose();

        Console.WriteLine("[sample] tray icon disposed - it must have left the notification area.");
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
