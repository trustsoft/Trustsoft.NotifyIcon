using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Sample;

/// <summary>
/// The live proof for S01 (D009): a WPF application with no window at all that owns a real icon in
/// the notification area, rotates that icon once per second, reports failures on the console and
/// removes the icon on the way out. Since S02 it also reports every click the shell delivers - the
/// decoded event and the raw callback that produced it - so both halves of the click contract are
/// observable on one console. Since S03 a right click opens a real context menu at the icon, and
/// since S04 a single left click shows a legacy Shell_NotifyIcon balloon whose severity and sound
/// behaviour the --balloon-* switches choose; each demonstration has its own remarks paragraph
/// below and its own docs/UAT-S03.md / docs/UAT-S04.md record.
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
/// <b>Per-monitor DPI (S03/T06).</b> This executable carries an application manifest
/// (<c>app.manifest</c>, wired in by <c>&lt;ApplicationManifest&gt;</c>) that declares
/// <c>dpiAwareness = PerMonitorV2</c>. That declaration is executable-level and manifest-only: the
/// programmatic API has to run before the process owns its first window handle, which a WPF library
/// can never guarantee, so the library never touches process DPI awareness (it reads the icon's
/// monitor with <c>GetDpiForMonitor</c> instead, which is awareness-independent). The sample prints
/// its own <c>GetProcessDpiAwareness</c> value and the <c>isPerMonitorV2</c> comparison at startup,
/// and every monitor with its effective DPI and scale, so the configuration behind each live reading
/// in <c>docs/UAT-S03.md</c> is part of the capture. A consumer that wants the same behaviour copies
/// this file and that csproj line.
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
/// <b>Balloon demonstration (S04).</b> A single left click on the icon shows a legacy
/// <c>Shell_NotifyIcon</c> balloon, and clicking that balloon prints one
/// <c>[sample] balloon clicked: ...</c> line next to the raw <c>NIN_BALLOONUSERCLICK</c> callback the
/// hook already prints - the pairing that shows the shell sent the code and the library decoded it
/// into the routed event. The severity and the sound behaviour are chosen by switches
/// (<c>--balloon-icon info|warning|error|none</c>, <c>--balloon-nosound</c>,
/// <c>--balloon-realtime</c>, <c>--balloon-respect-quiet-time</c>) and the effective configuration is
/// printed at startup, so a capture records what was asked for next to what happened. Because the
/// balloon is shown from the main click handler, <c>--cancel-preview left</c> suppresses it too: the
/// Preview handler sets <see cref="System.Windows.RoutedEventArgs.Handled"/> and the main click event
/// - with the balloon call inside it - never reaches the sample. <c>--show-balloon-after [seconds]</c>
/// shows one balloon with no click injected, which is what separates "the click path is broken" from
/// "the balloon path is broken" in a single capture.
/// </para>
/// <para>
/// <b>Toast demonstration (M002/S01; its shows moved onto the public API in M002/S02/T06).</b>
/// <c>--toast</c> runs the slice's exit condition from this windowless process: it builds a
/// <see cref="ToastContent"/> and shows it through the public <see cref="ToastNotifier"/>, which
/// registers the AppUserModelID on its first show (D060). The sample prints the identity and the
/// shortcut path up front, prints the exact content it built before each show, and - after the first
/// show - reads the shortcut back from a <em>fresh</em> shell link so the run's own capture shows the
/// identity was written and is readable. Each toast carries a launch argument naming the show
/// (<c>sample-toast-N</c>); since S03 the sample subscribes the notifier's three public events, so
/// every activation, dismissal and non-fatal error the shell delivers is printed, and the activation
/// line classifies the argument it carried against the arguments this run actually sent
/// (<c>element=button-1|button-2|body|unknown</c> - never a guess, because per-click attribution is
/// not available to an automated instrument). <c>--toast-buttons</c> gives every toast the two action
/// buttons whose arguments are <c>sample-button-1</c> and <c>sample-button-2</c>, and
/// <c>--toast-dispose-after</c> disposes the notifier while the process keeps pumping, so the
/// post-teardown window line is a measurement of silence rather than an assumption. <c>Dispose</c>
/// removes the shortcut again on the way out, and the read-back that follows
/// measures that removal. <c>--toast-severity</c> chooses the scenario
/// (<c>Default|Reminder|Alarm|Urgent</c>), <c>--toast-repeat</c> keeps a banner available to click for
/// the whole run, and <c>--toast-skip-register</c> is the negative control: the public path always
/// registers on its first show, so that control keeps driving the library's internal seam and runs
/// the identical show path for an identity that was never registered. The <c>InternalsVisibleTo</c>
/// grant therefore survives one more slice, for the fresh-link read-back, that control and the Verbose
/// trace attachment that puts the library's per-step lines (including the exact XML handed to
/// <c>IXmlDocumentIO.LoadXml</c>) into this run's capture - none of the three has a public equivalent,
/// and S05's consumer proof retires it. See
/// <c>docs/TOAST-MEASUREMENT.md</c> for what each line was measured to mean, including the finding
/// that <c>Show</c> succeeds for an identity that was never registered.
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

    /// <summary>The severity keyword <c>--balloon-icon</c> selects when none is named.</summary>
    /// <remarks>
    /// Unlike <c>--cancel-preview</c>, an <em>invalid</em> severity is never silently mapped here: the
    /// parser rejects it, because the severity decides what the shell is asked to draw and a
    /// substituted value would make a capture prove the wrong thing.
    /// </remarks>
    private const string DefaultBalloonIconKeyword = "info";

    /// <summary>
    /// The delay <c>--show-balloon-after</c> uses when it is given without a value.
    /// </summary>
    /// <remarks>
    /// Five seconds, the same reasoning as <see cref="DefaultOpenMenuDelay"/>: long enough for the
    /// startup lines - including the balloon configuration line - to be on the console before the
    /// balloon appears, which is what keeps a capture readable.
    /// </remarks>
    private static readonly TimeSpan DefaultShowBalloonDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The delay <c>--open-menu-after</c> uses when it is given without a value.
    /// </summary>
    /// <remarks>
    /// Five seconds: long enough for the startup lines (the DPI readings and the registration) to be
    /// on the console before the menu opens, which is what keeps a capture readable.
    /// </remarks>
    private static readonly TimeSpan DefaultOpenMenuDelay = TimeSpan.FromSeconds(5);

    /// <summary>How long a self-opened menu stays open before the sample closes it again.</summary>
    /// <remarks>
    /// The hold exists so a run produces both lines - an opened menu that was never closed would show
    /// placement but no dismissal, and the dismissal is the half of the contract that fails for the
    /// obvious implementation.
    /// </remarks>
    private static readonly TimeSpan SelfOpenedMenuHold = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The delay <c>--toast-after</c> uses when it is given without a value: the M002/S01 toast
    /// demonstration's first show.
    /// </summary>
    /// <remarks>
    /// Three seconds, shorter than the menu and balloon defaults because the toast's own banner is
    /// short-lived: the startup lines are still on the console before it appears, which is all a
    /// capture needs, and a shorter delay leaves more of the run's wall time available for a click on
    /// the banner.
    /// </remarks>
    private static readonly TimeSpan DefaultToastDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The repeat interval <c>--toast-repeat</c> uses when it is given without a value, and the
    /// interval that makes the slice's human half practical.
    /// </summary>
    /// <remarks>
    /// A toast banner is on screen for seconds; a person who has to run the sample, find the banner
    /// and click it cannot reliably do that inside one banner's lifetime. Re-showing the toast keeps a
    /// banner in the notification area's corner, so the click is a click on <em>a</em> toast of this
    /// registration rather than a race. Five seconds is the measurement's own banner lifetime
    /// (<c>docs/TOAST-MEASUREMENT.md</c>), so the refresh is not faster than the platform's own
    /// dismissal.
    /// </remarks>
    private static readonly TimeSpan DefaultToastRepeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The delay <c>--toast-dispose-after</c> uses when it is given without a value: the point at
    /// which the demonstration disposes its notifier while the process keeps pumping.
    /// </summary>
    /// <remarks>
    /// Five seconds, two seconds after the default first show (see <see cref="DefaultToastDelay"/>):
    /// long enough that the default show has appeared before the teardown, and short enough that a
    /// short run still spends most of its wall time in the post-teardown window the shutdown line
    /// measures.
    /// </remarks>
    private static readonly TimeSpan DefaultToastDisposeDelay = TimeSpan.FromSeconds(5);

    /// <summary>The S01 toast demonstration's title line.</summary>
    private const string ToastTitle = "Trustsoft.NotifyIcon sample toast";

    /// <summary>
    /// The S01 toast demonstration's body line.
    /// </summary>
    /// <remarks>
    /// It names the action the demonstration measures - clicking the body - because the body is the
    /// only part of an unpackaged process's toast that produces an activation with an argument, and
    /// because a capture should say what the person was asked to click.
    /// </remarks>
    private const string ToastBody = "Click this banner's body: the sample prints the activation it receives.";

    /// <summary>
    /// The prefix of the toast's <c>launch</c> argument, suffixed with the show's number.
    /// </summary>
    /// <remarks>
    /// A distinct argument per show is what makes a captured activation attributable: with
    /// <c>--toast-repeat</c> several banners are alive for the same identity, and the argument the
    /// sample prints names the show it came from.
    /// </remarks>
    private const string ToastLaunchPrefix = "sample-toast-";

    /// <summary>
    /// The argument the first action button carries when <c>--toast-buttons</c> is set; the shell
    /// reports it verbatim when that button is activated.
    /// </summary>
    /// <remarks>
    /// Fixed rather than per-show on purpose: a button argument identifies the <em>element</em> and
    /// the launch argument identifies the <em>show</em>, so one delivered string carries both axes.
    /// The sample's classifier compares a delivered argument against this constant and against the
    /// launch arguments of the shows it actually issued, and reports <c>unknown</c> rather than a
    /// guess when neither - or both - of two candidates match.
    /// </remarks>
    private const string ToastButton1Argument = "sample-button-1";

    /// <inheritdoc cref="ToastButton1Argument"/>
    private const string ToastButton2Argument = "sample-button-2";

    /// <summary>
    /// The visible label of the first <c>--toast-buttons</c> button. Two distinguishable labels, so
    /// a person reading the banner knows which button the sample is asking about.
    /// </summary>
    private const string ToastButton1Text = "Button 1";

    /// <inheritdoc cref="ToastButton1Text"/>
    private const string ToastButton2Text = "Button 2";

    /// <summary>
    /// The severity keyword <c>--toast-severity</c> starts from: the ordinary toast, which writes no
    /// <c>scenario</c> attribute at all.
    /// </summary>
    private const string DefaultToastSeverityKeyword = "Default";

    /// <summary>The exit code an unrecognized command-line argument produces.</summary>
    /// <remarks>
    /// Distinct from the <c>1</c> a refused registration returns, so a caller can tell "this build
    /// never started" from "the shell refused the icon".
    /// </remarks>
    private const int UsageErrorExitCode = 2;

    /// <summary>The one-line usage text printed when an argument is not understood.</summary>
    private const string SampleUsage =
        "[sample] usage: Trustsoft.NotifyIcon.Sample [--xaml] [--run-seconds N] [--cancel-preview [left|double|right|middle]] [--open-menu-after [seconds]] "
        + "[--balloon-icon none|info|warning|error] [--balloon-nosound] [--balloon-realtime] [--balloon-respect-quiet-time] [--show-balloon-after [seconds]] "
        + "[--toast] [--toast-aumid <id>] [--toast-severity Default|Reminder|Alarm|Urgent] [--toast-skip-register] [--toast-after [seconds]] [--toast-repeat [seconds]] [--toast-buttons] [--toast-dispose-after [seconds]]";

    /// <summary><c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>: the pseudo-handle the manifest asks for.</summary>
    /// <remarks>
    /// The value is documented (not measured): the context constants are negative pseudo-handles
    /// defined in <c>windef.h</c>, and this one is <c>-4</c>. It is compared with
    /// <see cref="AreDpiAwarenessContextsEqual"/> rather than dereferenced, which is the only legal
    /// use of a pseudo-handle.
    /// </remarks>
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    /// <summary><c>MDT_EFFECTIVE_DPI</c>: the DPI the monitor is actually being driven at.</summary>
    private const int EffectiveDpi = 0;

    private SampleTrayIcon? _sampleTrayIcon;
    private TrayIcon? _trayIcon;
    private DispatcherTimer? _rotationTimer;
    private DispatcherTimer? _shutdownTimer;
    private DispatcherTimer? _menuOpenTimer;
    private DispatcherTimer? _menuHoldTimer;
    private DispatcherTimer? _balloonShowTimer;
    private TraceSource? _libraryTrace;
    private bool _observersDetached;
    private int _frameIndex;

    /// <summary>
    /// The severity the demonstration's balloons are shown with, selected by <c>--balloon-icon</c>.
    /// </summary>
    private BalloonTipIcon _balloonIcon = BalloonTipIcon.Info;

    /// <summary>
    /// The optional behaviours the demonstration's balloons are shown with, combined from
    /// <c>--balloon-nosound</c>, <c>--balloon-realtime</c> and <c>--balloon-respect-quiet-time</c>.
    /// </summary>
    private BalloonTipOptions _balloonOptions = BalloonTipOptions.None;

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

    /// <summary>
    /// Balloon show requests made to the library, for the shutdown total - from clicks and, when
    /// <c>--show-balloon-after</c> was given, from the sample itself.
    /// </summary>
    /// <remarks>
    /// A <em>request</em>, deliberately not a confirmation: whether the shell actually showed the
    /// balloon is the shell's side of the protocol, reported by the <c>NIN_BALLOONSHOW</c> callback
    /// the raw hook prints. A refusal arrives on the <see cref="TrayIcon.TrayError"/> channel instead.
    /// </remarks>
    private int _balloonShowRequestCount;

    /// <summary>How many of the show requests the sample made without any click, for the totals.</summary>
    private int _selfBalloonShowCount;

    /// <summary>Delivered tunnel-phase balloon click events, for the shutdown total.</summary>
    private int _balloonPreviewCount;

    /// <summary>Delivered main-phase balloon click events, for the shutdown total.</summary>
    private int _balloonClickCount;

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

    /// <summary>
    /// Lines the library's own source delivered once the toast demonstration raised it to Verbose,
    /// for the toast totals.
    /// </summary>
    /// <remarks>
    /// Counted separately from <see cref="_libraryTraceLineCount" /> on purpose: that total is the
    /// measurement of the documented consumer path (which receives nothing), this one is the toast run's
    /// instrument, and folding them together would destroy the finding. See
    /// <see cref="AttachLibraryInternalTraceListener"/>.
    /// </remarks>
    private int _libraryInternalTraceLineCount;

    // ---------------------------------------------------------------------------------------------
    // M002/S01 toast demonstration state (--toast); the show step moved onto the public API in S02/T06.
    //
    // The normal run builds a ToastContent and shows it through the public ToastNotifier, which
    // registers the identity on its first show. The library's internals are still reached in three
    // places, all with no public equivalent (see the InternalsVisibleTo note in the library's
    // AssemblyInfo.cs): the fresh-link read-back the run prints as its identity evidence, the
    // --toast-skip-register negative control, which exists precisely to measure the case the public
    // path cannot express, and the Verbose trace attachment that puts the library's own per-step lines
    // into the capture. Everything else about the sample - the tray icon, the menu, the balloon and the
    // raw instruments - stays on the public surface.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The toast seam the demonstration drives, or <see langword="null"/> when <c>--toast</c> was not
    /// given.
    /// </summary>
    /// <remarks>
    /// One instance serves both halves: <see cref="ToastIdentity"/> writes and reads back the shortcut,
    /// and each <see cref="ToastShow"/> renders and shows one payload through the same seam. The
    /// production seam is one object over one set of raw vtables, so a wrong slot or GUID has a single
    /// home to inspect (T04).
    /// </remarks>
    private ToastApi? _toastApi;

    /// <summary>The identity service over <see cref="_toastApi"/>, or <see langword="null"/> without <c>--toast</c>.</summary>
    /// <remarks>
    /// Used for the two readings the public surface does not expose: the fresh-link read-back printed
    /// as this run's identity evidence, and the negative control's read of an identity that was never
    /// registered.
    /// </remarks>
    private ToastIdentity? _toastIdentity;

    /// <summary>
    /// The public notifier the normal run shows through, or <see langword="null"/> for the
    /// <c>--toast-skip-register</c> control, which drives the internal seam instead.
    /// </summary>
    /// <remarks>
    /// One notifier serves every show: S02 establishes that one notifier showing many toasts is the
    /// lifetime, so <c>--toast-repeat</c> reuses this instance rather than constructing one per show.
    /// Registration happens inside its first <see cref="ToastNotifier.Show"/> (D060), so the sample no
    /// longer pre-registers: it prints the identity and shortcut up front and reads the shortcut back
    /// after the first show.
    /// </remarks>
    private ToastNotifier? _toastNotifier;

    /// <summary>
    /// The scenario every show in this run asks for (<c>--toast-severity</c>); defaults to
    /// <see cref="ToastSeverity.Default"/>, which writes no scenario attribute at all.
    /// </summary>
    /// <remarks>
    /// A non-default severity is a request the shell may decline: the reminder scenario is silently
    /// ignored unless the toast carries a background-activation action, and this demonstration has
    /// none. The line printed before each show therefore names the value that was asked for, and the
    /// load-bearing evidence is the <c>scenario</c> attribute in the Verbose <c>LoadXml</c> line - not
    /// a claim that the toast looked or behaved differently.
    /// </remarks>
    private ToastSeverity _toastSeverity = ToastSeverity.Default;

    /// <summary>
    /// Whether every show in this run carries the two action buttons (<c>--toast-buttons</c>).
    /// </summary>
    /// <remarks>
    /// A flag rather than a value, and off by default so a plain <c>--toast</c> capture stays
    /// comparable with the S01 and S02 captures quoted in <c>docs/TOAST-MEASUREMENT.md</c>. It also
    /// decides which arguments the classifier treats as button arguments: with the flag off a
    /// delivered <c>sample-button-1</c> is <c>unknown</c>, because this run never sent it.
    /// </remarks>
    private bool _toastButtons;

    /// <summary>
    /// Every show this run created and has not disposed; all of them are disposed at shutdown.
    /// </summary>
    /// <remarks>
    /// A <see cref="ToastShow"/> owns one shown toast and its subscription, so a repeated
    /// demonstration needs one instance per show. They are kept alive until shutdown on purpose: the
    /// activation the demonstration measures is delivered to the object that created the notification,
    /// and disposing a show is what unsubscribes it - so a run that disposed the previous show on every
    /// repeat would leave the on-screen banners with no live subscription to deliver to, which is the
    /// opposite of what the demonstration is for.
    /// </remarks>
    private readonly List<ToastShow> _toastShows = [];

    /// <summary>The timer that shows the first toast, or <see langword="null"/>.</summary>
    private DispatcherTimer? _toastShowTimer;

    /// <summary>The timer that re-shows the toast (<c>--toast-repeat</c>), or <see langword="null"/>.</summary>
    private DispatcherTimer? _toastRepeatTimer;

    /// <summary>
    /// The timer that disposes the notifier mid-run (<c>--toast-dispose-after</c>), or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Its only job is to call <see cref="StopToastDemonstration"/> while the process keeps pumping -
    /// which is what turns "after disposal nothing fires" from a claim made at process exit into a
    /// window measured while there is still time for a callback to arrive.
    /// </remarks>
    private DispatcherTimer? _toastDisposeTimer;

    /// <summary>
    /// The identity this run used, or <see langword="null"/> without <c>--toast</c>.
    /// </summary>
    /// <remarks>
    /// The override (<c>--toast-aumid</c>) when given, otherwise the library's own default for this
    /// process - which is the entry assembly's simple name, so the sample registers and shows under
    /// <c>Trustsoft.NotifyIcon.Sample</c> unless a caller says otherwise. Both are printed at startup.
    /// </remarks>
    private string? _toastAppUserModelId;

    /// <summary>The shortcut path the demonstration registered (or would have), or <see langword="null"/>.</summary>
    private string? _toastShortcutPath;

    /// <summary>Whether this run created the shortcut (false for the <c>--toast-skip-register</c> control).</summary>
    private bool _toastRegistered;

    /// <summary>Shows this run asked the shell for, for the totals.</summary>
    private int _toastShowCount;

    /// <summary>
    /// Shows the shell accepted (<c>Show</c> returned <c>S_OK</c>), for the totals.
    /// </summary>
    /// <remarks>
    /// A <em>request</em>, deliberately not a confirmation: the measurement recorded in
    /// <c>docs/TOAST-MEASUREMENT.md</c> showed that <c>Show</c> succeeds even for an identity that was
    /// never registered, so this number says what the shell accepted, not that a banner appeared.
    /// </remarks>
    private int _toastShowAcceptedCount;

    /// <summary>Activation callbacks the sample received, for the totals - the slice's exit condition.</summary>
    private int _toastActivationCount;

    /// <summary>Dismissal callbacks the sample received, for the totals.</summary>
    private int _toastDismissedCount;

    /// <summary>Delivery-failure callbacks the sample received, for the totals.</summary>
    private int _toastFailedCount;

    /// <summary>
    /// Non-fatal failures the public path reported through <see cref="ToastNotifier.ToastError"/>,
    /// for the totals and the post-teardown window.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="_toastFailedCount"/>, which counts the
    /// <c>--toast-skip-register</c> control's <c>ToastShow.Failed</c> callbacks: one is the public
    /// event, the other the seam callback of the control, and folding them together would hide which
    /// channel delivered what.
    /// </remarks>
    private int _toastErrorCount;

    /// <summary>
    /// Shows the shell refused (a <see cref="ToastException"/>, or a failed
    /// <see cref="ToastShowResult"/> on the control's seam path), for the totals.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="_toastFailedCount"/>, which counts the shell's own
    /// delivery-failure callbacks: a refused show never produced a toast, so counting it as a delivery
    /// failure would conflate "the shell would not accept this" with "the shell accepted it and could
    /// not deliver it".
    /// </remarks>
    private int _toastRefusedCount;

    /// <summary>The argument string of the most recent activation, or <see langword="null"/>.</summary>
    private string? _toastLastActivationArguments;

    /// <summary>
    /// Whether the toast demonstration has been torn down; the guard that makes
    /// <see cref="StopToastDemonstration"/> idempotent across the sample's several exit paths.
    /// </summary>
    private bool _toastStopped;

    /// <summary>Activations seen when the teardown snapshot was taken; the post-teardown window is measured against it.</summary>
    private int _toastActivationsAtTeardown;

    /// <summary>Dismissals seen when the teardown snapshot was taken; the post-teardown window is measured against it.</summary>
    private int _toastDismissalsAtTeardown;

    /// <summary>Non-fatal errors seen when the teardown snapshot was taken; the post-teardown window is measured against it.</summary>
    private int _toastErrorsAtTeardown;

    /// <summary>
    /// Whether the teardown snapshot above has been taken, so the post-teardown window line is only
    /// printed for a run whose teardown actually happened.
    /// </summary>
    private bool _toastTeardownRecorded;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryParseArguments(e.Args, out SampleArguments arguments, out string? argumentError))
        {
            // Before any shell state exists, and with no icon created: an argument this sample does
            // not understand must be visible (on stderr, with a non-zero exit code) rather than
            // silently ignored, because a run whose switch was a typo would otherwise look exactly
            // like a run that proved the opposite of what the caller asked for.
            Console.Error.WriteLine(argumentError);
            Console.Error.WriteLine(SampleUsage);
            Shutdown(UsageErrorExitCode);
            return;
        }

        // Dispose the icon on every normal exit path. SessionEnding is the logoff/shutdown case;
        // Exit covers an explicit Shutdown as well as the end of Run(). Subscribed after the
        // argument check so a usage error produces no icon and no totals line.
        SessionEnding += (_, _) => ShutdownSample();
        Exit += (_, _) => ShutdownSample();

        // First, because these read the configuration every later number depends on: the process's
        // own DPI awareness (which is what the app.manifest decides) and the display configuration
        // the placement is measured against.
        ReportDpiAwareness();
        ReportDisplayConfiguration();

        _cancelledClickType = arguments.CancelledClickType;

        Console.WriteLine(
            _cancelledClickType is null
                ? "[sample] preview cancellation mode: off - every click reaches its main handler."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] preview cancellation mode: {_cancelledClickType} - the Preview handler sets Handled and the main handler must stay silent."));

        // S04: the balloon demonstration's configuration, applied before anything can show a balloon
        // and printed here so a capture records what was asked for next to what the shell did with
        // it - the same discipline as the DPI and menu-timing startup lines.
        _balloonIcon = ToBalloonTipIcon(arguments.BalloonIconKeyword);
        _balloonOptions = BalloonTipOptions.None;

        if (arguments.BalloonNoSound)
        {
            _balloonOptions |= BalloonTipOptions.NoSound;
        }

        if (arguments.BalloonRealtime)
        {
            _balloonOptions |= BalloonTipOptions.Realtime;
        }

        if (arguments.BalloonRespectQuietTime)
        {
            _balloonOptions |= BalloonTipOptions.RespectQuietTime;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] balloon demonstration: severity={_balloonIcon.ToString().ToLowerInvariant()} {DescribeBalloonOptions(_balloonOptions)} - a single left click on the icon shows this balloon, and clicking the balloon must print a balloon clicked line; --cancel-preview left suppresses both."));

        AttachLibraryTraceListener();

        // Printed before anything is created so a capture says which construction path produced every
        // line that follows. The two modes print the same shapes on purpose: a diff between them is
        // what shows the declarative path reaches the same states.
        Console.WriteLine(arguments.Declarative
            ? "[sample] declaration mode: XAML - the icon, its menu and its image are declared in Application.Resources and wired by markup; nothing in this file assigns a property or subscribes to an event on the icon."
            : "[sample] declaration mode: code-first - the icon is constructed here and every property and event is wired in C#.");

        // The instance. In declarative mode it comes from Application.Resources, and the lookup is
        // what instantiates the declaration - which is also when the shell registration happens,
        // because the markup carries Visible="True". Nothing is assigned or subscribed below in that
        // mode: markup carries the whole declaration, which is what this mode exists to prove.
        //
        // What the markup declares is SampleTrayIcon, the sample's own subclass, and only because
        // --open-menu-after has to open the *declared* menu with no shell click - that path needs the
        // protected OnTrayClick hook, which the library deliberately does not expose publicly. Every
        // attribute the declaration writes belongs to the library type, so the markup compiled here is
        // the library's own surface; the plain library type declared in Application.Resources is pinned
        // headlessly by TrayIconXamlContractTests, and the namespace a consumer writes is proven to
        // resolve by that same file. See App.xaml for why the local namespace must not be
        // assembly-qualified.
        TrayIcon? trayIcon = arguments.Declarative ? null : new SampleTrayIcon();

        if (trayIcon is not null)
        {
            _sampleTrayIcon = (SampleTrayIcon)trayIcon;

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

            // S04: the balloon click pair. The tunnel line shows the Preview phase arriving; the bubble
            // line is the event half of this slice's exit condition. Subscribed before the first
            // registration, with the click events, so even a balloon click during startup is observed.
            trayIcon.PreviewBalloonTipClicked += OnPreviewBalloonTipClicked;
            trayIcon.BalloonTipClicked += OnBalloonTipClicked;
        }

        try
        {
            if (trayIcon is null)
            {
                trayIcon = DeclarativeIcon();
                _trayIcon = trayIcon;

                // The declaration names SampleTrayIcon, so the no-click menu instrument is available in
                // this mode too. The cast is a guard rather than an assumption: a consumer whose own
                // App.xaml declares the plain library type gets a TrayIcon here, and the self-open timer
                // then reports that it cannot reach the hook instead of failing on an invalid cast.
                _sampleTrayIcon = trayIcon as SampleTrayIcon;
            }
            else
            {
                _trayIcon = trayIcon;

                trayIcon.ToolTipText = "Trustsoft.NotifyIcon sample - the icon changes every second";

                // Visible first, then the image: this exercises "an icon with no image yet" followed by
                // the NIM_MODIFY replacement path that the rotation keeps using.
                trayIcon.Visible = true;
                trayIcon.IconSource = Frames[0];
            }
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
            arguments.Declarative
                ? "[sample] tray icon registered from markup (Visible=\"True\" and the image come from Application.Resources; no rotation runs, because nothing in C# may replace a declared value)."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] tray icon registered, rotating {Frames.Length} frames every {RotationInterval.TotalSeconds:0.#}s."));
        Console.WriteLine("[sample] no window is shown - check the notification area, not the taskbar.");

        // S03: the menu. Code-first builds it here; declarative takes the one markup assigned, so the
        // item count printed below becomes a reading about the declaration rather than about this
        // file. The window can never assign a menu in declarative mode, which is why the count is
        // printed from whichever source produced it.
        if (arguments.Declarative)
        {
            _menu = trayIcon.ContextMenu;
        }
        else
        {
            _menu = CreateTrayMenu();
            trayIcon.ContextMenu = _menu;
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] context menu {(arguments.Declarative ? "taken from markup" : "assigned")} with {_menu?.Items.Count ?? 0} item(s); a right click on the icon must open it at the icon."));

        // The pump-level control, subscribed in the same run as the hook so the two counts are
        // directly comparable. It counts only; the hook is what prints.
        ComponentDispatcher.ThreadFilterMessage += OnPumpMessage;

        // Only now does the library's host window exist: it is created lazily by the first
        // registration, so enumerating the thread's windows any earlier would hook nothing.
        AttachRawCallbackHooks();

        if (arguments.Declarative)
        {
            // The rotation would replace the value markup declared, which is precisely the thing this
            // mode is meant to keep honest, so it is off. It also makes the probe's GDI series for a
            // declarative run directly comparable to a code-first one with no churn in it.
            Console.WriteLine("[sample] rotation disabled in declaration mode: the markup's image is what the icon shows.");
        }
        else
        {
            _rotationTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = RotationInterval };
            _rotationTimer.Tick += OnRotationTick;
            _rotationTimer.Start();
        }

        if (arguments.RunSeconds is TimeSpan runSeconds)
        {
            Console.WriteLine($"[sample] will shut down by itself after {runSeconds.TotalSeconds:0.#}s (graceful close check).");

            _shutdownTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = runSeconds };
            _shutdownTimer.Tick += OnShutdownTick;
            _shutdownTimer.Start();
        }

        if (arguments.OpenMenuAfter is TimeSpan openMenuDelay)
        {
            // The no-click demonstration: the menu is opened through the library's own click path
            // after a delay, and closed again by the consumer's own menu. It exists because the
            // injector cannot always click (the icon may be in a flyout it cannot reach), and
            // because a menu that fails to open must be visible in the console rather than inferred
            // from a screen no instrument is reading.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] menu self-open requested: the assigned menu will open once after {openMenuDelay.TotalSeconds:0.#}s and be closed again {SelfOpenedMenuHold.TotalSeconds:0.#}s later (--open-menu-after). No shell click is injected for this."));

            _menuOpenTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = openMenuDelay };
            _menuOpenTimer.Tick += OnMenuOpenRequestTick;
            _menuOpenTimer.Start();
        }

        if (arguments.ShowBalloonAfter is TimeSpan showBalloonDelay)
        {
            // The no-click demonstration, in the shape of --open-menu-after: one balloon is shown
            // through the library's public ShowBalloonTip path after a delay, with no shell click
            // injected. A run that shows a balloon this way but shows none on a click localises the
            // fault to the click path; a run that shows no balloon either way points at the balloon
            // path itself. That is why this switch exists next to the click-driven demonstration.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] balloon self-show requested: a balloon will be shown once after {showBalloonDelay.TotalSeconds:0.#}s (--show-balloon-after). No shell click is injected for this."));

            _balloonShowTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = showBalloonDelay };
            _balloonShowTimer.Tick += OnBalloonShowRequestTick;
            _balloonShowTimer.Start();
        }

        if (arguments.Toast)
        {
            // M002/S01's live end-to-end run. It is started last so every configuration line a capture
            // needs to attribute the run is already on the console when the first toast appears.
            StartToastDemonstration(arguments);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // M002/S01 toast demonstration (--toast)
    //
    // The windowless form of the slice's exit condition: register an identity, show a toast from a
    // process with no window, subscribe to activation, and print what arrives when the user clicks the
    // banner's body. What each line is for, and what the measurement already fixed about this path, is
    // in docs/TOAST-MEASUREMENT.md.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the toast demonstration: derives the identity, constructs the public notifier (or the
    /// negative control's seam state), and schedules the first show.
    /// </summary>
    /// <param name="arguments">The parsed switches; <see cref="SampleArguments.Toast"/> is true.</param>
    /// <remarks>
    /// <para>
    /// <b>The normal run does not register here.</b> Since D060 the notifier registers on its first
    /// <c>Show</c>, so this method only says which identity the run will use and where its shortcut
    /// lives; the identity evidence - the read-back from a <em>fresh</em> shell link, not the writer's
    /// own object - is printed after that first show, once there is something to read. The measured trap
    /// (<c>docs/TOAST-MEASUREMENT.md</c>, Contract 1) is that a shortcut can exist and still carry no
    /// property, which is why the read-back prints the value it read next to the comparison.
    /// </para>
    /// <para>
    /// <b>The control is not a "registration failed" case.</b> With <c>--toast-skip-register</c> the
    /// sample never writes the shortcut and runs the show path directly over the internal seam, because
    /// the public notifier always registers on its first show and the measurement recorded that
    /// <c>Show</c> succeeds anyway. What differs without registration is the routing of an activation,
    /// so the control's verdict is the absence of an activation, and the sample prints the identity's
    /// read-back state up front so the control's cleanliness is a reading rather than an assumption.
    /// </para>
    /// </remarks>
    private void StartToastDemonstration(SampleArguments arguments)
    {
        _toastApi = new ToastApi();
        _toastIdentity = new ToastIdentity(_toastApi);
        _toastSeverity = ToToastSeverity(arguments.ToastSeverityKeyword);
        _toastButtons = arguments.ToastButtons;

        // Before the notifier exists, so the first show's registration and show steps are all captured.
        AttachLibraryInternalTraceListener();

        string appUserModelId = string.IsNullOrEmpty(arguments.ToastAppUserModelId)
            ? ToastIdentity.DeriveDefaultAppUserModelId()
            : arguments.ToastAppUserModelId!;

        _toastAppUserModelId = appUserModelId;
        _toastShortcutPath = ToastIdentity.DefaultShortcutPath(appUserModelId);

        string surface;

        if (arguments.ToastSkipRegister)
        {
            surface = "this is the negative control, so no shortcut is created anywhere: the shows go through the library's internal seam (ToastShow directly), which is the only path that can show for an identity that was never registered.";
        }
        else
        {
            // AppUserModelId stays null when --toast-aumid was not given, which is exactly the
            // documented meaning of the property: the library derives the entry assembly's default.
            _toastNotifier = new ToastNotifier { AppUserModelId = arguments.ToastAppUserModelId };

            // S03's public activation surface: the three typed events. They are subscribed before the
            // first show, so a delivery that races the display is classified rather than dropped. The
            // --toast-skip-register control never reaches this branch - it keeps driving ToastShow
            // directly, which is the only path that can show for an identity that was never registered.
            _toastNotifier.Activated += OnNotifierActivated;
            _toastNotifier.Dismissed += OnNotifierDismissed;
            _toastNotifier.ToastError += OnNotifierToastError;

            surface = "the shows go through the public ToastNotifier, which registers the identity on its first show and raises the three S03 events; the sample's InternalsVisibleTo grant survives this slice for the fresh-link read-back, the --toast-skip-register control and the Verbose trace attachment, none of which has a public equivalent (S05's consumer proof retires it).";
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast demonstration: identity='{appUserModelId}' shortcut='{_toastShortcutPath}' register={!arguments.ToastSkipRegister} severity={_toastSeverity} repeat={DescribeToastRepeat(arguments.ToastRepeat)} - {surface}"));

        ToastIdentity identity = _toastIdentity;
        string shortcutPath = _toastShortcutPath;

        if (arguments.ToastSkipRegister)
        {
            ToastIdentityResult existing = identity.ReadBack(shortcutPath);
            bool carriesTheIdentity = existing.Success
                && string.Equals(existing.AppUserModelId, appUserModelId, StringComparison.Ordinal);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast demonstration negative control: no shortcut is created; read-back success={existing.Success} operation='{existing.Operation}' code=0x{existing.Code:X8} value='{existing.AppUserModelId ?? "(null)"}' carriesTheIdentity={carriesTheIdentity}"));

            if (carriesTheIdentity)
            {
                Console.Error.WriteLine("[sample] toast demonstration negative control is NOT clean: this identity already carries the AppUserModelID, so an activation delivered in this run would not distinguish a registered identity from an unregistered one. Register under a different identity or remove that shortcut.");
            }
        }

        TimeSpan delay = arguments.ToastAfter ?? DefaultToastDelay;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast show scheduled: first show after {delay.TotalSeconds:0.#}s (--toast-after); the toast's launch argument is '{ToastLaunchPrefix}N', so the activation a click delivers names the show it came from."));

        _toastShowTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = delay };
        _toastShowTimer.Tick += OnToastShowTick;
        _toastShowTimer.Start();

        if (arguments.ToastRepeat is TimeSpan repeatInterval)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast repeat requested: the toast is re-shown every {repeatInterval.TotalSeconds:0.#}s (--toast-repeat), so a banner is available to click for as long as the run lasts."));

            _toastRepeatTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = repeatInterval };
            _toastRepeatTimer.Tick += OnToastRepeatTick;
            _toastRepeatTimer.Start();
        }

        if (arguments.ToastDisposeAfter is TimeSpan disposeDelay)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast dispose scheduled: the notifier is disposed after {disposeDelay.TotalSeconds:0.#}s (--toast-dispose-after) while this run keeps pumping, so an activation, dismissal or error arriving after the teardown line is measured rather than assumed."));

            _toastDisposeTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = disposeDelay };
            _toastDisposeTimer.Tick += OnToastDisposeTick;
            _toastDisposeTimer.Start();
        }

        Console.Out.Flush();
    }

    /// <summary>Shows the first toast once the scheduled delay has elapsed.</summary>
    private void OnToastShowTick(object? sender, EventArgs e)
    {
        _toastShowTimer?.Stop();
        _toastShowTimer = null;
        ShowToastOnce();
    }

    /// <summary>Re-shows the toast on the repeat interval, so a banner stays available to click.</summary>
    private void OnToastRepeatTick(object? sender, EventArgs e) => ShowToastOnce();

    /// <summary>
    /// Disposes the demonstration mid-run (<c>--toast-dispose-after</c>): the timers stop and the
    /// notifier is disposed, but the process keeps pumping so the silence that follows is a window
    /// rather than an instant.
    /// </summary>
    /// <remarks>
    /// It calls <see cref="StopToastDemonstration"/> rather than duplicating its steps, so the disposal
    /// that leaves the post-teardown window open is byte-for-byte the disposal the normal exit path
    /// performs. Because <see cref="StopToastDemonstration"/> stops the show and repeat timers first,
    /// <c>--toast-repeat</c> cannot re-show through a disposed notifier either.
    /// </remarks>
    private void OnToastDisposeTick(object? sender, EventArgs e)
    {
        _toastDisposeTimer?.Stop();
        _toastDisposeTimer = null;
        StopToastDemonstration();
    }

    /// <summary>
    /// Builds one <see cref="ToastContent"/> for the show, prints exactly what it built, and shows it -
    /// through the public <see cref="ToastNotifier"/> on the normal run, or through a fresh internal
    /// <see cref="ToastShow"/> on the <c>--toast-skip-register</c> control, exactly as S01 did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One content, printed before it is shown.</b> The line names the title, the body, the severity,
    /// the launch argument and - when <c>--toast-buttons</c> is set - the two button arguments the
    /// consumer built, so a capture can compare it with the XML the library renders from it and with
    /// the scenario the shell receives. The severity is printed as the request that was made - never as
    /// a claim about what the shell did with it (a reminder scenario is silently ignored without a
    /// background-activation action, and this demonstration has none).
    /// </para>
    /// <para>
    /// <b>The normal run reuses one notifier.</b> It was constructed once in
    /// <see cref="StartToastDemonstration"/>, and every show goes through it: one notifier showing many
    /// toasts is the lifetime S02 establishes. A <see cref="ToastException"/> is the documented failure,
    /// not a crash - on the public path it now covers only a <em>registration</em> failure (the
    /// documented throw, printed as REFUSED, counted as refused and never printed as a success), because
    /// a failure the show path reports after a successful registration arrives through
    /// <see cref="ToastNotifier.ToastError"/> instead (see <see cref="OnNotifierToastError"/>).
    /// </para>
    /// <para>
    /// <b>The control uses the seam.</b> <c>--toast-skip-register</c> must show without an identity and
    /// the public notifier always registers on its first show, so the control keeps S01's
    /// <see cref="ToastShow"/> path and its <c>setting=...</c> acceptance line unchanged; its verdict is
    /// the absence of an activation.
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
            Severity = _toastSeverity,
        };

        if (_toastButtons)
        {
            // The slice's two-button toast: fixed element arguments that the classifier can recognise
            // in a delivered activation, and distinguishable labels so a person knows what to click.
            content.Buttons.Add(new ToastButton { Text = ToastButton1Text, Arguments = ToastButton1Argument });
            content.Buttons.Add(new ToastButton { Text = ToastButton2Text, Arguments = ToastButton2Argument });
        }

        // The exact content the consumer built, before either path is chosen: this is the line the
        // XML contract and the live run are compared through. The buttons fragment is written only when
        // the flag was set, so a plain --toast content line stays byte-comparable with S01 and S02.
        string buttonsReading = _toastButtons
            ? $" buttons=[{ToastButton1Argument},{ToastButton2Argument}]"
            : string.Empty;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast content: title='{content.Title}' body='{content.Body}' severity={content.Severity} launch='{content.Launch}'{buttonsReading}"));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast show #{_toastShowCount}: asking the shell for identity='{_toastAppUserModelId}' launch='{launch}' title='{content.Title}'"));

        if (_toastNotifier is ToastNotifier notifier)
        {
            try
            {
                notifier.Show(content);
            }
            catch (ToastException exception)
            {
                // The documented failure, not a crash. On the public path this catch now covers only a
                // registration failure: the notifier registers inside its first Show (D060) and throws
                // when that fails, while a failure the show path reports after registration arrives
                // through ToastError and does not throw (the D055/D061 split). The exception names the
                // failing operation and carries the code, and this show is counted as refused - never as
                // accepted, and nothing about it is printed as success.
                _toastRefusedCount++;
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] toast show #{_toastShowCount}: REFUSED operation='{exception.Operation}' code=0x{exception.ErrorCode:X8} - the shell did not accept a toast for identity='{_toastAppUserModelId}'"));
                Console.Out.Flush();
                return;
            }

            _toastShowAcceptedCount++;

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast show #{_toastShowCount}: the shell accepted it (ToastNotifier.Show returned; S_OK inside it is acceptance, not visibility); delivery is judged out of process with scripts/probe-toast --history '{_toastAppUserModelId}'"));

            if (_toastShowCount == 1)
            {
                // Registration happened inside this first Show (D060), and the public call only returns
                // when its own register/read-back pair agreed - which is why this is the first moment a
                // read-back can show anything, and why the run's identity evidence lives here now.
                _toastRegistered = true;
                ReportToastIdentityReadBack();
            }

            Console.Out.Flush();
            return;
        }

        // The negative control's path, unchanged from S01: no registration anywhere, so the identity is
        // whatever the shortcut store happens to hold.
        var payload = new ToastPayload(content);
        var show = new ToastShow(_toastApi!, payload)
        {
            Activated = OnToastActivated,
            Dismissed = OnToastDismissed,
            Failed = OnToastFailed,
        };

        ToastShowResult result = show.Show(_toastAppUserModelId);

        if (result.Success)
        {
            _toastShowAcceptedCount++;
            _toastShows.Add(show);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast show #{_toastShowCount}: the shell accepted it (setting={result.NotificationSetting} raw; S_OK is acceptance, not visibility); delivery is judged out of process with scripts/probe-toast --history '{_toastAppUserModelId}'"));
        }
        else
        {
            // The show path unwinds itself on failure; Dispose here is the belt to that brace and is
            // documented idempotent, so a failed show leaves no handle and no subscription behind.
            _toastRefusedCount++;
            show.Dispose();
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast show #{_toastShowCount}: REFUSED operation='{result.Operation}' code=0x{result.Code:X8} - the shell did not accept a toast for identity='{_toastAppUserModelId}'"));
        }

        Console.Out.Flush();
    }

    /// <summary>
    /// Reads the registered shortcut back from a <em>fresh</em> shell link and prints the identity
    /// evidence line S01 printed, in the same shape.
    /// </summary>
    /// <remarks>
    /// The notifier registered the identity inside its first <see cref="ToastNotifier.Show"/> (D060) and
    /// its own registration already includes a read-back check, but that check is the library's internal
    /// one. This reading is the sample's own: it opens a fresh shell link and prints the value it carries,
    /// so "the identity was written and is readable" is a measurement in this run's capture rather than a
    /// claim about the notifier.
    /// </remarks>
    private void ReportToastIdentityReadBack()
    {
        if (_toastIdentity is not ToastIdentity identity || _toastShortcutPath is not string shortcutPath)
        {
            return;
        }

        ToastIdentityResult readBack = identity.ReadBack(shortcutPath);
        bool matches = readBack.Success
            && string.Equals(readBack.AppUserModelId, _toastAppUserModelId, StringComparison.Ordinal);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast registration read-back (fresh shell link): success={readBack.Success} operation='{readBack.Operation}' code=0x{readBack.Code:X8} value='{readBack.AppUserModelId ?? "(null)"}' matchesExpected={matches}"));
    }

    /// <summary>
    /// Prints an activation - the slice's exit condition: an activation of one of this run's toasts
    /// arrives here as the argument it carries.
    /// </summary>
    /// <param name="arguments">The argument string Windows reported, or <see langword="null"/>.</param>
    /// <remarks>
    /// This is the <c>--toast-skip-register</c> control's callback (the <see cref="ToastShow"/> seam it
    /// drives directly). The public path's equivalent is <see cref="OnNotifierActivated"/>; both share
    /// <see cref="ReportToastActivation"/>, so the two paths print one line shape and classify one way.
    /// The measured delivery scope is D054: an activation arrives only while this process is running
    /// and pumping, and the argument string is the only payload an unpackaged process receives.
    /// </remarks>
    private void OnToastActivated(string? arguments) => ReportToastActivation(arguments);

    /// <summary>
    /// Prints an activation delivered by the public <see cref="ToastNotifier.Activated"/> event.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload, whose <see cref="ToastActivatedEventArgs.Arguments"/> is the delivered argument.</param>
    private void OnNotifierActivated(object? sender, ToastActivatedEventArgs e) => ReportToastActivation(e.Arguments);

    /// <summary>
    /// Counts one delivered activation and prints it with the element the argument classifies to.
    /// </summary>
    /// <param name="arguments">The argument Windows delivered, or <see langword="null"/>.</param>
    /// <remarks>
    /// The line reports the <em>argument</em> and the <em>element that argument names</em>; it does not
    /// report that a click on that element happened, because an activation carries nothing about which
    /// element produced it. See <see cref="ClassifyToastElement"/>.
    /// </remarks>
    private void ReportToastActivation(string? arguments)
    {
        _toastActivationCount++;
        _toastLastActivationArguments = arguments;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast activated: arguments='{arguments ?? "(null)"}' element={ClassifyToastElement(arguments)} (activation {_toastActivationCount} of {_toastShowCount} show(s))"));
        Console.Out.Flush();
    }

    /// <summary>
    /// Classifies a delivered activation argument as one of this run's known elements, or
    /// <c>unknown</c>.
    /// </summary>
    /// <param name="arguments">The argument Windows delivered, or <see langword="null"/>.</param>
    /// <returns><c>button-1</c>, <c>button-2</c>, <c>body</c> or <c>unknown</c>.</returns>
    /// <remarks>
    /// <para>
    /// The classification is a lookup in the arguments this run actually sent - never a guess from the
    /// string's shape. The candidates are the two button arguments (only when <c>--toast-buttons</c> was
    /// set, because a run that never sent a button argument cannot receive one of its own) and the
    /// <c>sample-toast-N</c> launch arguments of the shows this run actually issued. Anything else is
    /// <c>unknown</c>, and so is an <em>ambiguous</em> match: an argument two candidates both claim names
    /// neither.
    /// </para>
    /// <para>
    /// This is deliberately not click attribution. The measured limitation
    /// (<c>docs/TOAST-MEASUREMENT.md</c>) is that an activation can arrive with no instrumented click
    /// and cannot be credited to a specific click, so this classifies the argument and the console line
    /// says <c>element=</c> about the string, not about a click.
    /// </para>
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
    /// Prints a dismissal delivered by the public <see cref="ToastNotifier.Dismissed"/> event, with the
    /// raw numeric reason the public vocabulary was mapped from.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload.</param>
    /// <remarks>
    /// The cast back to the ordinal is deliberate: the capture keeps the <c>reason=N</c> shape the S01
    /// control quotes, so a reader compares the same number across captures while the event itself
    /// carries the typed vocabulary.
    /// </remarks>
    private void OnNotifierDismissed(object? sender, ToastDismissedEventArgs e) => OnToastDismissed((int)e.Reason);

    /// <summary>
    /// Prints a non-fatal toast failure delivered by the public
    /// <see cref="ToastNotifier.ToastError"/> event, and counts it.
    /// </summary>
    /// <param name="sender">The notifier that raised the event.</param>
    /// <param name="e">The event payload: the failing operation, the code and the optional exception.</param>
    /// <remarks>
    /// On stderr, like the other failure lines, and marked non-fatal because the process is still
    /// alive: this is the D055/D061 channel a show-path failure arrives on instead of an exception. The
    /// exception is rendered as a type name plus a one-line message, or <c>(null)</c> when the failure
    /// carried none - which is the normal case for the shell's asynchronous failure and for a show-path
    /// failure, both of which report a bare code.
    /// </remarks>
    private void OnNotifierToastError(object? sender, ToastErrorEventArgs e)
    {
        _toastErrorCount++;

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast error: operation='{e.Operation}' code=0x{e.ErrorCode:X8} exception='{DescribeToastException(e.Exception)}' (non-fatal)"));
        Console.Out.Flush();
    }

    /// <summary>Renders a toast failure's exception as a one-line reading, or the placeholder for none.</summary>
    /// <param name="exception">The exception the failure carried, or <see langword="null"/>.</param>
    /// <returns><c>(null)</c>, or <c>Type: message</c> with any line break collapsed to a space.</returns>
    private static string DescribeToastException(Exception? exception) =>
        exception is null
            ? "(null)"
            : $"{exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}";

    /// <summary>Prints a toast dismissal with the raw reason Windows reported.</summary>
    /// <param name="reason">The raw <c>ToastDismissedReason</c> (<c>0</c> = user canceled, <c>1</c> = hidden, <c>2</c> = timed out, <c>-1</c> = unreadable or unknown).</param>
    private void OnToastDismissed(int reason)
    {
        _toastDismissedCount++;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast dismissed: reason={reason} ({DescribeToastDismissalReason(reason)})"));
        Console.Out.Flush();
    }

    /// <summary>Names a raw dismissal reason in the sample's own vocabulary, totally.</summary>
    /// <param name="reason">The raw numeric reason.</param>
    /// <returns>The known name, or <c>unknown</c> for every other value - including the seam's <c>-1</c>.</returns>
    private static string DescribeToastDismissalReason(int reason) => reason switch
    {
        (int)ToastDismissalReason.UserCanceled => "user canceled",
        (int)ToastDismissalReason.ApplicationHidden => "application hidden",
        (int)ToastDismissalReason.TimedOut => "timed out",
        _ => "unknown",
    };

    /// <summary>Prints a toast delivery failure with the raw code Windows reported.</summary>
    /// <param name="errorCode">The raw error code from the <c>Failed</c> event.</param>
    private void OnToastFailed(int errorCode)
    {
        _toastFailedCount++;
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast failed: code=0x{errorCode:X8}"));
        Console.Out.Flush();
    }

    /// <summary>
    /// Stops the toast timers, unsubscribes and releases every show, and removes the shortcut this run
    /// created; safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The unregistration is part of the demonstration, not housekeeping.</b> The library's identity
    /// contract is register / read back / remove, so the run ends by removing what it created and then
    /// reading the shortcut back: the removal is a measurement (the read-back fails at the open step
    /// once the file is gone) rather than a claim, and the machine is left as the run found it. On the
    /// normal run the removal is <see cref="ToastNotifier.Dispose"/>'s work - the notifier removes the
    /// shortcut it registered - so the line reports what the read-back found after disposal, not a
    /// removal result the sample itself performed.
    /// </para>
    /// <para>
    /// <b>Disposal is what removes the subscriptions.</b> The notifier disposes every show it owns, and
    /// the control's own shows are disposed here, so the process exits with no live toast subscription -
    /// the teardown half of the measured sequence.
    /// </para>
    /// </remarks>
    private void StopToastDemonstration()
    {
        if (_toastStopped)
        {
            return;
        }

        _toastStopped = true;

        _toastShowTimer?.Stop();
        _toastShowTimer = null;
        _toastRepeatTimer?.Stop();
        _toastRepeatTimer = null;
        _toastDisposeTimer?.Stop();
        _toastDisposeTimer = null;

        // First, because it is the only step that also removes the shortcut: the read-back below is the
        // measurement of that removal, and it has to happen after the removal (not before it).
        _toastNotifier?.Dispose();

        if (_toastShows.Count > 0)
        {
            foreach (ToastShow show in _toastShows)
            {
                show.Dispose();
            }

            _toastShows.Clear();
        }

        if (_toastShowAcceptedCount > 0)
        {
            // The notifier disposes the shows it owns and the control's shows are disposed just above, so
            // the accepted count is the number of live subscriptions this process held at teardown.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast teardown: {_toastShowAcceptedCount} show(s) unsubscribed and released - no activation subscription outlives the process."));
        }

        // The post-teardown window starts here. Everything counted up to this point is pre-teardown,
        // and the remaining wall time of a --toast-dispose-after run is the silence DetachObservers
        // measures by subtracting these snapshots from the final totals: a callback that arrived after
        // the teardown line would make one of those differences non-zero, which is exactly what the
        // disposal guarantee has to make impossible.
        _toastActivationsAtTeardown = _toastActivationCount;
        _toastDismissalsAtTeardown = _toastDismissedCount;
        _toastErrorsAtTeardown = _toastErrorCount;
        _toastTeardownRecorded = true;

        if (_toastRegistered && _toastIdentity is not null && _toastShortcutPath is not null)
        {
            ToastIdentity identity = _toastIdentity;
            string shortcutPath = _toastShortcutPath;
            ToastIdentityResult after = identity.ReadBack(shortcutPath);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast unregistration: Dispose removed the shortcut this run registered; read-back after dispose success={after.Success} operation='{after.Operation}' code=0x{after.Code:X8} (a failure at OpenShellLink is the shortcut being gone)"));
        }

        Console.Out.Flush();
    }

    /// <summary>Names the effective repeat behaviour for the startup line.</summary>
    /// <param name="repeatInterval">The parsed interval, or <see langword="null"/>.</param>
    /// <returns><c>off</c>, or the interval in seconds.</returns>
    private static string DescribeToastRepeat(TimeSpan? repeatInterval) =>
        repeatInterval is TimeSpan interval
            ? string.Create(CultureInfo.InvariantCulture, $"every {interval.TotalSeconds:0.#}s")
            : "off";

    /// <summary>
    /// Opens the assigned menu once, on the sample's own initiative, through the library's click path.
    /// </summary>
    /// <param name="sender">The timer; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// The line before the call is printed on purpose: if the menu does not open, the absence of the
    /// <c>[sample] menu opened:</c> line that follows it is a measurement rather than a missing run.
    /// </remarks>
    private void OnMenuOpenRequestTick(object? sender, EventArgs e)
    {
        _menuOpenTimer?.Stop();
        _menuOpenTimer = null;

        SampleTrayIcon? icon = _sampleTrayIcon;

        if (icon is null)
        {
            // The declaration did not name a type that reaches OnTrayClick (a plain library TrayIcon
            // does not), so the self-open path is not available. Said plainly rather than left as the
            // generic "no tray icon exists" line, which would be wrong here - the icon does exist, the
            // instrument does not.
            Console.WriteLine("[sample] --open-menu-after: unavailable - the declared instance is not a "
                + nameof(SampleTrayIcon)
                + ", so there is no route to the menu-open hook; open the menu with a real right click instead.");
            return;
        }

        Console.WriteLine("[sample] --open-menu-after: requesting the menu now, with no click injected.");
        Console.Out.Flush();

        // The interop stays in the application: the subclass is handed the anchor it should report.
        GetCursorPos(out POINT cursor);
        icon.RequestMenuOpen(new Point(cursor.X, cursor.Y));

        _menuHoldTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = SelfOpenedMenuHold };
        _menuHoldTimer.Tick += OnMenuHoldElapsedTick;
        _menuHoldTimer.Start();
    }

    /// <summary>
    /// Closes the menu the sample opened itself, so the run reports an open and a dismissal.
    /// </summary>
    /// <param name="sender">The timer; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// This is the consumer-driven close (<c>ContextMenu.IsOpen = false</c> on the consumer's own
    /// menu), not the OS-driven one an outside click causes: the outside click is measured by the
    /// <c>menu</c> scenario of the click injector, and the two are recorded separately in the
    /// checklist. Both must arrive at the library's <c>Closed</c> teardown, which is what the
    /// <c>[sample] menu dismissed.</c> line reports.
    /// </remarks>
    private void OnMenuHoldElapsedTick(object? sender, EventArgs e)
    {
        _menuHoldTimer?.Stop();
        _menuHoldTimer = null;

        ContextMenu? menu = _menu;

        if (menu is null || !menu.IsOpen)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] --open-menu-after: nothing to close after the hold (menu assigned={menu is not null}, open={menu?.IsOpen == true}) - no open line can have preceded this."));
            return;
        }

        Console.WriteLine("[sample] --open-menu-after: closing the menu the sample opened (consumer-driven close, not an outside click).");
        menu.IsOpen = false;
    }

    /// <summary>The <c>x:Key</c> the declarative declaration is stored under in <c>App.xaml</c>.</summary>
    /// <remarks>
    /// Held as a constant rather than an inline string so the lookup and the markup cannot drift apart
    /// silently: a renamed resource would otherwise turn the declarative mode into a run that reports
    /// success while showing nothing.
    /// </remarks>
    private const string DeclarativeIconKey = "TrayIcon";

    /// <summary>
    /// Looks the declarative declaration up from the application's resources.
    /// </summary>
    /// <returns>The icon the markup declared, already registered because the markup says so.</returns>
    /// <exception cref="InvalidOperationException">
    /// The key is absent, or it holds something that is not a <see cref="TrayIcon"/>.
    /// </exception>
    /// <remarks>
    /// <b>The lookup is what instantiates the declaration.</b> BAML defers value creation until the
    /// first lookup, which is why a declaration nobody asks for costs nothing - and why this call is
    /// the moment the icon appears and registers. Nothing here assigns a property or subscribes to an
    /// event: in this mode the markup carries the whole declaration, including <c>Visible</c>, the
    /// tooltip, the menu and every handler attribute.
    /// </remarks>
    private static TrayIcon DeclarativeIcon()
    {
        if (Application.Current.Resources[DeclarativeIconKey] is not TrayIcon icon)
        {
            throw new InvalidOperationException(
                $"Application.Resources['{DeclarativeIconKey}'] did not yield a {nameof(TrayIcon)}. "
                + "A declarative run needs App.xaml to declare that key, declared after the menu and image it references.");
        }

        return icon;
    }

    /// <summary>
    /// Handles the declared menu's first item, which the markup names by attribute.
    /// </summary>
    /// <param name="sender">The item; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// The code-first menu can attach a lambda to this item, but markup cannot: an event attribute has
    /// to name a method. This is that method, and it prints the same line the lambda prints so the two
    /// modes stay comparable line for line - which is the whole point of running both.
    /// </remarks>
    private void OnDeclaredMenuItemClick(object sender, RoutedEventArgs e)
    {
        Console.WriteLine("[sample] menu item clicked: header=Sample menu item");
        Console.Out.Flush();
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
        var secondItem = new MenuItem();

        firstItem.Click += (_, _) =>
        {
            Console.WriteLine("[sample] menu item clicked: header=Sample menu item");
            Console.Out.Flush();
        };

        // T06: the same mechanism the declarative mode writes in markup, expressed in C# - the
        // consumer sets the menu's own DataContext, and the item binds against it. The two modes must
        // not diverge, and neither may rely on the icon's DataContext reaching the menu: the library
        // never writes a menu's DataContext, and a menu declared in a resource dictionary has no
        // logical parent for one to flow through (pinned by TrayIconMenuDataContextTests).
        menu.DataContext = new SampleMenuData { Label = CodeFirstMenuDataLabel };
        secondItem.SetBinding(MenuItem.HeaderProperty, new Binding(nameof(SampleMenuData.Label)));

        menu.Items.Add(firstItem);
        menu.Items.Add(secondItem);

        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;

        return menu;
    }

    /// <summary>
    /// The label the code-first menu's own <c>DataContext</c> carries, so its bound item resolves to
    /// text that names the mode it came from.
    /// </summary>
    /// <remarks>
    /// Spelled out here rather than taken from the declared resource, because the code-first mode's
    /// whole point is that nothing in it comes from markup: a code-first run that reached into
    /// <c>Application.Resources</c> for its data would be measuring the wrong path. The declarative
    /// mode takes the same label out of <c>App.xaml</c>, and the two labels differing by mode is what
    /// makes the live "menu data context:" line attributable at a glance.
    /// </remarks>
    private const string CodeFirstMenuDataLabel = "code-first menu data context";

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

            // The cursor is printed beside the popup because it is what tells the two placement
            // sources apart after the fact: the shell's icon rectangle (the menu sits at the icon) and
            // the library's documented cursor fallback (the menu sits at the pointer).
            bool cursorRead = GetCursorPos(out POINT cursor);

            // The popup's lower-left corner in the DIP space the placement offsets live in, derived
            // from the measured rectangle and the measured DPI rather than read from the library:
            // scale is applied exactly once, here, in the other direction. It is the number to compare
            // against the icon rectangle, in DIP.
            string bottomLeftDip = measured && dpi > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{rectangle.Left / (dpi / 96.0):0.###},{rectangle.Bottom / (dpi / 96.0):0.###}")
                : "unreadable";

            measurement = string.Create(
                CultureInfo.InvariantCulture,
                $"popup=0x{popup.ToInt64():X} class={GetWindowClass(popup)} rect={(measured ? $"{rectangle.Left},{rectangle.Top} {rectangle.Right - rectangle.Left}x{rectangle.Bottom - rectangle.Top}" : "unreadable")} dpi={dpi} scale={dpi / 96.0:0.###} owner=0x{owner.ToInt64():X} cursor={(cursorRead ? $"{cursor.X},{cursor.Y}" : "unreadable")} bottomLeftDip={bottomLeftDip}");
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[sample] menu opened: {measurement}"));
        Console.WriteLine($"[sample] menu data context: {DescribeMenuDataContext(sender as ContextMenu)}");
        Console.Out.Flush();
    }

    /// <summary>
    /// Describes where the opening menu's bindings resolve from, and what its bound item resolved to.
    /// </summary>
    /// <param name="menu">The menu that just opened, or <see langword="null"/>.</param>
    /// <returns>One line naming the menu's own data context and every item whose header is bound.</returns>
    /// <remarks>
    /// <para>
    /// T06's observability surface, and deliberately about the <em>menu's</em> data context rather
    /// than the icon's: a menu declared in <c>Application.Resources</c> has no logical parent and the
    /// library points its <c>PlacementTarget</c> at its own anchor window rather than at the icon, so
    /// the icon's <c>DataContext</c> never reaches the menu - the consumer sets the menu's own. The
    /// value is read from the live object graph as the menu opens, so a binding that did not resolve
    /// appears here as <c>&lt;null&gt;</c> instead of being inferred from a screenshot, and the two
    /// modes' lines are directly comparable because the same shape is printed in both.
    /// </para>
    /// <para>
    /// The bound items are found by their binding expression rather than by index, so the line stays a
    /// measurement if an item is added, removed or reordered in either mode.
    /// </para>
    /// </remarks>
    private static string DescribeMenuDataContext(ContextMenu? menu)
    {
        if (menu is null)
        {
            return "menu=none (the opened event carried no menu)";
        }

        object? dataContext = menu.DataContext;
        string source = dataContext is null ? "none" : dataContext.GetType().Name;
        List<string> boundItems = [];

        for (int index = 0; index < menu.Items.Count; index++)
        {
            if (menu.Items[index] is MenuItem item
                && BindingOperations.GetBindingExpression(item, MenuItem.HeaderProperty) is not null)
            {
                string header = item.Header as string is string text ? $"'{text}'" : "<null>";

                boundItems.Add(string.Create(CultureInfo.InvariantCulture, $"item[{index}].Header={header}"));
            }
        }

        string resolved = boundItems.Count == 0 ? "no bound item" : string.Join(", ", boundItems);

        return string.Create(CultureInfo.InvariantCulture, $"menu.DataContext={source} (set by the consumer) -> {resolved}");
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

        // S04: the exit condition's first half. The balloon belongs to the single left click only,
        // and it is shown here - inside the main click handler - rather than from a separate
        // subscription, because that is exactly what makes --cancel-preview left suppress it: the
        // Preview handler has already set Handled, so this method never runs and no balloon can be
        // requested. A double click raises TrayLeftDoubleClick and deliberately shows no balloon.
        if (ReferenceEquals(e.RoutedEvent, TrayIcon.TrayLeftClickEvent))
        {
            TrayIcon? trayIcon = _trayIcon;

            if (trayIcon is not null)
            {
                ShowConfiguredBalloon(trayIcon, "single left click");
            }
        }
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
    /// Observes the tunnel phase of a balloon click, before any handler could cancel it.
    /// </summary>
    /// <param name="sender">The <see cref="TrayIcon"/> that raised the event.</param>
    /// <param name="e">The routed payload; no balloon payload exists by design (D031).</param>
    /// <remarks>
    /// Nothing is cancelled here - the cancellation contract for balloons is demonstrated the same
    /// way the click contract's is: a consumer's Preview handler sets
    /// <see cref="System.Windows.RoutedEventArgs.Handled"/> and the main line then never appears.
    /// This line exists so a capture can tell "the tunnel phase ran and the main phase was
    /// suppressed" from "no callback arrived at all".
    /// </remarks>
    private void OnPreviewBalloonTipClicked(object? sender, RoutedEventArgs e)
    {
        _balloonPreviewCount++;

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] balloon preview clicked: event={e.RoutedEvent.Name} phase=tunnel - the main phase must follow unless a handler sets Handled.");

        Console.WriteLine(message);
        Trace.WriteLine(message, SampleTraceSourceName);
        Console.Out.Flush();
    }

    /// <summary>
    /// Prints the line this slice's exit condition is read from: the main (bubble) phase of a
    /// balloon click, delivered as the <see cref="TrayIcon.BalloonTipClicked"/> routed event.
    /// </summary>
    /// <param name="sender">The <see cref="TrayIcon"/> that raised the event.</param>
    /// <param name="e">The routed payload.</param>
    /// <remarks>
    /// The raw <c>NIN_BALLOONUSERCLICK</c> callback that produced this event is printed by the hook
    /// (<see cref="OnRawHostMessage"/>) - before or after this line depending on the order WPF
    /// invokes the hooks in, which the checklist reads off the capture rather than assumes - so the
    /// pairing shows the shell's message and the library's decode as one interaction. Flushed per
    /// line: this stream is the evidence.
    /// </remarks>
    private void OnBalloonTipClicked(object? sender, RoutedEventArgs e)
    {
        _balloonClickCount++;

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] balloon clicked: event={e.RoutedEvent.Name} phase=bubble - the shell accepted a click on the balloon.");

        Console.WriteLine(message);
        Trace.WriteLine(message, SampleTraceSourceName);
        Console.Out.Flush();
    }

    /// <summary>
    /// Shows one balloon with the configuration the switches selected, through the library's public
    /// <see cref="TrayIcon.ShowBalloonTip"/> exactly as a consumer would call it.
    /// </summary>
    /// <param name="trayIcon">The registered icon the balloon is shown for.</param>
    /// <param name="source">What asked for the balloon, named in the balloon text so a capture
    /// can tell a click-driven balloon from a self-shown one.</param>
    /// <remarks>
    /// <para>
    /// The balloon's text repeats the effective configuration, so what the shell was asked to render
    /// is visible inside the rendering itself. The severity keyword spelling is the same one the
    /// startup line and the switches use, so the three can be compared in one capture.
    /// </para>
    /// <para>
    /// The two argument/state exceptions are caught and written where a human can read them rather
    /// than allowed to escape from a click event handler into an invisible unhandled exception - the
    /// same policy as the startup failure path. A shell refusal never reaches this catch: the library
    /// reports it through <see cref="TrayIcon.TrayError"/> and never throws for one.
    /// </para>
    /// </remarks>
    private void ShowConfiguredBalloon(TrayIcon trayIcon, string source)
    {
        _balloonShowRequestCount++;

        string title = "Trustsoft.NotifyIcon sample";
        string text = string.Create(
            CultureInfo.InvariantCulture,
            $"Balloon from the {source}. severity={_balloonIcon.ToString().ToLowerInvariant()} {DescribeBalloonOptions(_balloonOptions)}. Click this balloon - the sample must print a balloon clicked line.");

        try
        {
            trayIcon.ShowBalloonTip(title, text, _balloonIcon, _balloonOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] balloon request refused before any shell call: {ex.GetType().Name}: {ex.Message}");

            Console.Error.WriteLine(message);
            Trace.TraceError(message);
        }
    }

    /// <summary>
    /// Shows the demonstration balloon once, on the sample's own initiative, with no click injected.
    /// </summary>
    /// <param name="sender">The timer; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    /// <remarks>
    /// The line before the call is printed on purpose, in the shape of
    /// <see cref="OnMenuOpenRequestTick"/>: if the balloon does not appear, the absence of the
    /// <c>NIN_BALLOONSHOW</c> raw callback line that would follow it is a measurement rather than a
    /// missing run.
    /// </remarks>
    private void OnBalloonShowRequestTick(object? sender, EventArgs e)
    {
        _balloonShowTimer?.Stop();
        _balloonShowTimer = null;

        TrayIcon? trayIcon = _trayIcon;

        if (trayIcon is null)
        {
            Console.WriteLine("[sample] --show-balloon-after: no tray icon exists - no balloon was requested.");
            return;
        }

        _selfBalloonShowCount++;

        Console.WriteLine("[sample] --show-balloon-after: showing a balloon now, with no click injected.");
        Console.Out.Flush();

        ShowConfiguredBalloon(trayIcon, "--show-balloon-after (no click injected)");
    }

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
    /// Attaches this sample's listener to the library's own Verbose trace source and raises that source
    /// to <see cref="SourceLevels.Verbose"/>, so the library's per-step lines - including the exact XML
    /// handed to <c>IXmlDocumentIO.LoadXml</c> - appear in this run's capture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An instrument, not the consumer path.</b> The source is internal and is reached through the
    /// sample's <c>InternalsVisibleTo</c> grant. <see cref="AttachLibraryTraceListener"/> stays attached
    /// to the same-named source created from the documented consumer spelling and keeps its own total,
    /// because the measured finding is that this subscription delivers nothing (docs/UAT-S02.md): a
    /// <see cref="TraceSource"/> created with an existing source's name gets its own switch and its own
    /// listener list. That is why the level has to be raised on the library's own object, and why the
    /// two totals are reported separately rather than folded into one number.
    /// </para>
    /// <para>
    /// Raising the level changes nothing about the library's shipped default: a consumer's run still
    /// emits nothing above <see cref="SourceLevels.Warning"/> unless the consumer opts in, and this
    /// sample opts in for its toast demonstration only, which is what makes the per-step evidence - and
    /// with it the exact payload the shell accepted - part of a single capture.
    /// </para>
    /// </remarks>
    private void AttachLibraryInternalTraceListener()
    {
        NotifyIconTrace.Source.Switch.Level = SourceLevels.Verbose;
        NotifyIconTrace.Source.Listeners.Add(new FlushingConsoleTraceListener(() => _libraryInternalTraceLineCount++));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] toast library trace: the library's own source '{LibraryTraceSourceName}' was raised to {NotifyIconTrace.Source.Switch.Level} through the sample's InternalsVisibleTo grant, so its per-step toast lines - including the exact XML handed to LoadXml - print below as [trace] lines."));
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
    /// Reads and validates every command-line argument of the sample.
    /// </summary>
    /// <param name="args">The startup arguments.</param>
    /// <param name="arguments">The parsed switches.</param>
    /// <param name="error">The message naming the first argument that was not understood.</param>
    /// <returns><see langword="true"/> when every argument was understood.</returns>
    /// <remarks>
    /// <para>
    /// Both spellings are accepted for every switch with a value - <c>--run-seconds 20</c> and
    /// <c>--run-seconds=20</c> - following the convention the first switch established.
    /// </para>
    /// <para>
    /// <b>An unknown argument is an error, never a silence.</b> This sample is an instrument: a run
    /// whose switch was misspelled would otherwise look exactly like a run that proved the opposite
    /// of what the caller asked for, which is the one failure mode an instrument must not have. The
    /// rejection happens before any shell state exists, and it exits <see cref="UsageErrorExitCode"/>.
    /// </para>
    /// <para>
    /// A bare <c>--cancel-preview</c> is documented behaviour rather than a missing value: it cancels
    /// <see cref="DefaultCancelPreviewType"/>, and the effective mode is printed at startup, so a typo
    /// cannot make the demonstration quietly prove the wrong thing. A bare <c>--open-menu-after</c>
    /// takes the default delay for the same reason; a <em>malformed</em> delay, by contrast, is
    /// rejected, because the delay decides when the menu opens and a silently substituted value would
    /// make a capture's timing unexplainable. A bare <c>--show-balloon-after</c> behaves the same way.
    /// </para>
    /// <para>
    /// <b>The balloon switches are stricter, on purpose.</b> <c>--balloon-icon</c> has no silent
    /// fallback at all: a value that is not one of the four keywords is rejected, because the severity
    /// decides what the shell draws and a substituted value would make a capture prove the wrong
    /// thing. The three behaviour switches are pure flags and reject a <c>=value</c> spelling, which
    /// would otherwise be swallowed without any effect - the one failure mode an instrument must not
    /// have.
    /// </para>
    /// </remarks>
    private static bool TryParseArguments(string[] args, out SampleArguments arguments, out string? error)
    {
        TimeSpan? runSeconds = null;
        TimeSpan? openMenuAfter = null;
        string? cancelledClickType = null;
        string balloonIconKeyword = DefaultBalloonIconKeyword;
        bool balloonNoSound = false;
        bool balloonRealtime = false;
        bool balloonRespectQuietTime = false;
        TimeSpan? showBalloonAfter = null;
        bool declarative = false;
        bool toast = false;
        string? toastAppUserModelId = null;
        string? toastSeverityKeyword = null;
        bool toastSkipRegister = false;
        TimeSpan? toastAfter = null;
        TimeSpan? toastRepeat = null;
        bool toastButtons = false;
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
                case "--run-seconds":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? runSecondsValue))
                    {
                        error = $"[sample] '{argument}' needs a number of seconds: use --run-seconds N or --run-seconds=N.";
                        return false;
                    }

                    if (!double.TryParse(runSecondsValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds <= 0)
                    {
                        error = $"[sample] '--run-seconds' needs a positive number of seconds, got '{runSecondsValue}'.";
                        return false;
                    }

                    runSeconds = TimeSpan.FromSeconds(seconds);
                    break;

                case "--cancel-preview":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? cancelValue))
                    {
                        cancelledClickType = DefaultCancelPreviewType;
                        break;
                    }

                    cancelledClickType = IsCancelPreviewType(cancelValue) ? cancelValue : DefaultCancelPreviewType;
                    break;

                case "--open-menu-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? openMenuValue))
                    {
                        openMenuAfter = DefaultOpenMenuDelay;
                        break;
                    }

                    if (!double.TryParse(openMenuValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double delaySeconds) || delaySeconds < 0)
                    {
                        error = $"[sample] '--open-menu-after' needs a delay in seconds, got '{openMenuValue}'.";
                        return false;
                    }

                    openMenuAfter = TimeSpan.FromSeconds(delaySeconds);
                    break;

                case "--balloon-icon":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? balloonIconValue))
                    {
                        error = $"[sample] '{argument}' needs a severity: use --balloon-icon none|info|warning|error or --balloon-icon=info.";
                        return false;
                    }

                    if (!IsBalloonIconKeyword(balloonIconValue))
                    {
                        error = $"[sample] '--balloon-icon' needs one of none|info|warning|error, got '{balloonIconValue}'.";
                        return false;
                    }

                    balloonIconKeyword = balloonIconValue;
                    break;

                case "--balloon-nosound":
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --balloon-nosound.";
                        return false;
                    }

                    balloonNoSound = true;
                    break;

                case "--balloon-realtime":
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --balloon-realtime.";
                        return false;
                    }

                    balloonRealtime = true;
                    break;

                case "--balloon-respect-quiet-time":
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --balloon-respect-quiet-time.";
                        return false;
                    }

                    balloonRespectQuietTime = true;
                    break;

                case "--show-balloon-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? showBalloonValue))
                    {
                        showBalloonAfter = DefaultShowBalloonDelay;
                        break;
                    }

                    if (!double.TryParse(showBalloonValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double balloonDelaySeconds) || balloonDelaySeconds < 0)
                    {
                        error = $"[sample] '--show-balloon-after' needs a delay in seconds, got '{showBalloonValue}'.";
                        return false;
                    }

                    showBalloonAfter = TimeSpan.FromSeconds(balloonDelaySeconds);
                    break;

                case "--toast":
                    // A flag, like --xaml: the demonstration is the sample's own, so there is nothing
                    // for the caller to pass and a '=value' spelling would be swallowed silently.
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --toast.";
                        return false;
                    }

                    toast = true;
                    break;

                case "--toast-aumid":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastAumidValue))
                    {
                        error = $"[sample] '{argument}' needs an identity: use --toast-aumid <id> or --toast-aumid=<id>.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(toastAumidValue))
                    {
                        error = "[sample] '--toast-aumid' needs a non-empty identity.";
                        return false;
                    }

                    toastAppUserModelId = toastAumidValue;
                    break;

                case "--toast-severity":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastSeverityValue))
                    {
                        error = $"[sample] '{argument}' needs a severity: use --toast-severity Default|Reminder|Alarm|Urgent or --toast-severity=Reminder.";
                        return false;
                    }

                    if (!IsToastSeverityKeyword(toastSeverityValue))
                    {
                        error = $"[sample] '--toast-severity' needs one of Default|Reminder|Alarm|Urgent, got '{toastSeverityValue}'.";
                        return false;
                    }

                    toastSeverityKeyword = toastSeverityValue;
                    break;

                case "--toast-skip-register":
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --toast-skip-register.";
                        return false;
                    }

                    toastSkipRegister = true;
                    break;

                case "--toast-buttons":
                    // A flag, like --toast and the balloon behaviour switches: the two-button content is
                    // the sample's own, so a '=value' spelling would be swallowed silently.
                    if (inlineValue is not null)
                    {
                        error = $"[sample] '{argument}' takes no value: write it as --toast-buttons.";
                        return false;
                    }

                    toastButtons = true;
                    break;

                case "--toast-dispose-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastDisposeValue))
                    {
                        toastDisposeAfter = DefaultToastDisposeDelay;
                        break;
                    }

                    if (!double.TryParse(toastDisposeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double toastDisposeSeconds) || toastDisposeSeconds < 0)
                    {
                        error = $"[sample] '--toast-dispose-after' needs a delay in seconds, got '{toastDisposeValue}'.";
                        return false;
                    }

                    toastDisposeAfter = TimeSpan.FromSeconds(toastDisposeSeconds);
                    break;

                case "--toast-after":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastAfterValue))
                    {
                        toastAfter = DefaultToastDelay;
                        break;
                    }

                    if (!double.TryParse(toastAfterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double toastDelaySeconds) || toastDelaySeconds < 0)
                    {
                        error = $"[sample] '--toast-after' needs a delay in seconds, got '{toastAfterValue}'.";
                        return false;
                    }

                    toastAfter = TimeSpan.FromSeconds(toastDelaySeconds);
                    break;

                case "--toast-repeat":
                    if (!TryTakeValue(inlineValue, args, ref i, out string? toastRepeatValue))
                    {
                        toastRepeat = DefaultToastRepeatInterval;
                        break;
                    }

                    // Zero is rejected rather than read as "show once": a repeat interval of zero would
                    // re-show the toast in a tight loop, and a caller who wants one show writes --toast
                    // without this switch.
                    if (!double.TryParse(toastRepeatValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double toastRepeatSeconds) || toastRepeatSeconds <= 0)
                    {
                        error = $"[sample] '--toast-repeat' needs a positive interval in seconds, got '{toastRepeatValue}'.";
                        return false;
                    }

                    toastRepeat = TimeSpan.FromSeconds(toastRepeatSeconds);
                    break;

                case "--xaml":
                    // A flag, not a switch with a value: the declaration lives in App.xaml, so there is
                    // nothing for the caller to pass. Combining it with the wiring switches is legal
                    // and useful - the balloon and menu demonstrations work in both modes.
                    declarative = true;
                    break;

                default:
                    error = $"[sample] unknown argument '{argument}' - this sample does not ignore arguments it does not understand.";
                    return false;
            }
        }

        // The toast switches select behaviour inside the demonstration, so one of them without
        // --toast would be a switch that does nothing - the one failure mode this parser refuses.
        if (!toast && (toastAppUserModelId is not null || toastSeverityKeyword is not null || toastSkipRegister || toastAfter is not null || toastRepeat is not null || toastButtons || toastDisposeAfter is not null))
        {
            error = "[sample] --toast-aumid, --toast-severity, --toast-skip-register, --toast-after, --toast-repeat, --toast-buttons and --toast-dispose-after only apply to the toast demonstration: add --toast.";
            return false;
        }

        arguments = new SampleArguments(runSeconds, cancelledClickType, openMenuAfter, balloonIconKeyword, balloonNoSound, balloonRealtime, balloonRespectQuietTime, showBalloonAfter, declarative, toast, toastAppUserModelId, toastSeverityKeyword ?? DefaultToastSeverityKeyword, toastSkipRegister, toastAfter, toastRepeat, toastButtons, toastDisposeAfter);
        return true;
    }

    /// <summary>
    /// Reads the value of one switch, in either its separated or its <c>=</c>-joined form.
    /// </summary>
    /// <param name="inlineValue">The value from the <c>=</c>-joined form, or <see langword="null"/>.</param>
    /// <param name="args">The startup arguments.</param>
    /// <param name="index">The current argument index; advanced when the separated form supplies the value.</param>
    /// <param name="value">The value, when one was supplied.</param>
    /// <returns>
    /// <see langword="true"/> when a value was supplied, <see langword="false"/> for a bare switch
    /// (including one followed by another switch, which must not be read as its value).
    /// </returns>
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

    /// <summary>
    /// Reports whether a value names one of the four cancelable click types.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for the four click-type keywords.</returns>
    private static bool IsCancelPreviewType(string? value) => value is "left" or "double" or "right" or "middle";

    /// <summary>
    /// Reports whether a value names one of the four balloon severities.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for the four severity keywords. The
    /// <see cref="NotNullWhenAttribute"/> on the parameter is what lets the parser's flow analysis
    /// treat the value as non-null after a positive answer, so no suppression is needed at the
    /// assignment site.</returns>
    private static bool IsBalloonIconKeyword([NotNullWhen(true)] string? value) => value is "none" or "info" or "warning" or "error";

    /// <summary>
    /// Whether the value is one of the severity keywords <c>--toast-severity</c> accepts.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for one of the four enum member names, compared case-insensitively.</returns>
    /// <remarks>
    /// The accepted vocabulary is exactly <see cref="ToastSeverity"/>'s member names, so the switch and
    /// the enum cannot drift. The comparison is written as a name lookup rather than
    /// <c>Enum.TryParse</c> on purpose: <c>TryParse</c> also accepts numeric strings and out-of-range
    /// numbers, which would let <c>--toast-severity 99</c> through as a value nothing renders.
    /// </remarks>
    private static bool IsToastSeverityKeyword([NotNullWhen(true)] string? value)
    {
        foreach (string name in Enum.GetNames<ToastSeverity>())
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps a severity keyword to its <see cref="ToastSeverity"/> value.
    /// </summary>
    /// <param name="keyword">A keyword <see cref="IsToastSeverityKeyword"/> accepted.</param>
    /// <returns>The matching severity.</returns>
    /// <remarks>
    /// Case-insensitive because a caller may be copying a lower-case scenario name out of a toast
    /// document; the parser has already rejected anything that is not one of the names, so the fallback
    /// exists for the compiler and can never run.
    /// </remarks>
    private static ToastSeverity ToToastSeverity(string keyword) => keyword switch
    {
        var name when string.Equals(name, nameof(ToastSeverity.Default), StringComparison.OrdinalIgnoreCase) => ToastSeverity.Default,
        var name when string.Equals(name, nameof(ToastSeverity.Reminder), StringComparison.OrdinalIgnoreCase) => ToastSeverity.Reminder,
        var name when string.Equals(name, nameof(ToastSeverity.Alarm), StringComparison.OrdinalIgnoreCase) => ToastSeverity.Alarm,
        var name when string.Equals(name, nameof(ToastSeverity.Urgent), StringComparison.OrdinalIgnoreCase) => ToastSeverity.Urgent,
        _ => ToastSeverity.Default,
    };

    /// <summary>
    /// Maps a severity keyword to its <see cref="BalloonTipIcon"/> value.
    /// </summary>
    /// <param name="keyword">One of the four keywords <see cref="IsBalloonIconKeyword"/> accepts.</param>
    /// <returns>The matching severity.</returns>
    /// <remarks>
    /// The mapping is total over the keywords the parser accepts; the fallback exists for the
    /// compiler and can never run.
    /// </remarks>
    private static BalloonTipIcon ToBalloonTipIcon(string keyword) => keyword switch
    {
        "none" => BalloonTipIcon.None,
        "info" => BalloonTipIcon.Info,
        "warning" => BalloonTipIcon.Warning,
        "error" => BalloonTipIcon.Error,
        _ => BalloonTipIcon.Info,
    };

    /// <summary>
    /// Names the balloon behaviours a flags value selects, in the vocabulary the startup line, the
    /// switches and the balloon text share, so all three can be compared in one capture.
    /// </summary>
    /// <param name="options">The flags value in effect.</param>
    /// <returns>A <c>sound=... realtime=... respectQuietTime=...</c> fragment.</returns>
    private static string DescribeBalloonOptions(BalloonTipOptions options) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"sound={((options & BalloonTipOptions.NoSound) == 0 ? "on" : "off")} realtime={((options & BalloonTipOptions.Realtime) != 0 ? "on" : "off")} respectQuietTime={((options & BalloonTipOptions.RespectQuietTime) != 0 ? "on" : "off")}");

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

    /// <summary>The current-process pseudo-handle <c>GetProcessDpiAwareness</c> accepts.</summary>
    /// <returns>The pseudo-handle, which must not be closed.</returns>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>Reads a process's DPI awareness; Windows 8.1 and later.</summary>
    /// <param name="process">The process handle, or the current-process pseudo-handle.</param>
    /// <param name="awareness">Receives the <c>PROCESS_DPI_AWARENESS</c> value.</param>
    /// <returns><c>S_OK</c> (0) on success, or the failure <c>HRESULT</c>.</returns>
    /// <remarks>
    /// This is the reading that says whether the manifest was applied, and it is taken from outside
    /// the process as well (<c>.gsd/measure-dpi-awareness.ps1</c>), so the sample's own line and an
    /// external observer's line can be compared. It reports per-monitor awareness as a single value:
    /// it does not distinguish V1 from V2, which is what <c>GetThreadDpiAwarenessContext</c> is for.
    /// </remarks>
    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

    /// <summary>Reads the calling thread's DPI awareness context; Windows 10 1607 and later.</summary>
    /// <returns>The context, which is only ever compared, never dereferenced.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    /// <summary>Reduces an awareness context to its <c>DPI_AWARENESS</c> value.</summary>
    /// <param name="context">The context handle.</param>
    /// <returns>The <c>DPI_AWARENESS</c> value.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    /// <summary>Compares two DPI awareness contexts, which is the only legal use of a pseudo-handle.</summary>
    /// <param name="first">One context.</param>
    /// <param name="second">The other context.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    /// <remarks>
    /// This is what separates PerMonitorV2 from PerMonitorV1: <c>GetProcessDpiAwareness</c> reports
    /// <c>2</c> for both, while comparing the thread's context against
    /// <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c> answers the question the manifest actually
    /// declares.
    /// </remarks>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    /// <summary>Reads the cursor position in physical screen pixels.</summary>
    /// <param name="point">Receives the position.</param>
    /// <returns><see langword="true"/> when it was read.</returns>
    /// <remarks>
    /// Printed with every menu-open line, because it is what separates "the shell located the icon"
    /// from the library's documented cursor fallback when a menu lands somewhere unexpected.
    /// </remarks>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    /// <summary>Reads a system metric; used only for the display-configuration line.</summary>
    /// <param name="index">The metric index.</param>
    /// <returns>The metric's value in physical pixels (or a count, for the monitor metric).</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Enumerates the display monitors.</summary>
    /// <param name="deviceContext">A device context to intersect with; <see cref="IntPtr.Zero"/> for all.</param>
    /// <param name="clip">A clip rectangle to intersect with; <see cref="IntPtr.Zero"/> for all.</param>
    /// <param name="callback">Invoked once per monitor.</param>
    /// <param name="parameter">The caller's opaque value.</param>
    /// <returns><see langword="true"/> when the enumeration completed.</returns>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, EnumDisplayMonitorsCallback callback, IntPtr parameter);

    /// <summary>Reads a monitor's bounds, work area, flags and device name.</summary>
    /// <param name="monitor">The monitor handle from the enumeration.</param>
    /// <param name="info">The structure, with <c>CbSize</c> already filled in.</param>
    /// <returns><see langword="true"/> when it was read.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    /// <summary>Reads a monitor's DPI for one <c>MONITOR_DPI_TYPE</c>; Windows 8.1 and later.</summary>
    /// <param name="monitor">The monitor handle.</param>
    /// <param name="dpiType"><see cref="EffectiveDpi"/>.</param>
    /// <param name="dpiX">Receives the horizontal DPI.</param>
    /// <param name="dpiY">Receives the vertical DPI.</param>
    /// <returns><c>S_OK</c> (0) on success, or the failure <c>HRESULT</c>.</returns>
    /// <remarks>
    /// Awareness-independent by design, which is why it is the reader the library's placement path
    /// uses as well (D026): the answer does not change with the process's own awareness.
    /// </remarks>
    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>The callback <see cref="EnumDisplayMonitors"/> invokes once per display monitor.</summary>
    /// <param name="monitor">The monitor handle.</param>
    /// <param name="deviceContext">The device context, unused here.</param>
    /// <param name="rect">The monitor rectangle, unused here (the callback reads the full info).</param>
    /// <param name="parameter">The caller's opaque value; unused here.</param>
    /// <returns><see langword="true"/> to continue the enumeration.</returns>
    private delegate bool EnumDisplayMonitorsCallback(IntPtr monitor, IntPtr deviceContext, ref RECT rect, IntPtr parameter);

    /// <summary><c>MONITORINFOF_PRIMARY</c>: the monitor is the primary one.</summary>
    private const uint MonitorInfoPrimary = 0x00000001;

    /// <summary><c>SM_CXSCREEN</c>: the primary monitor's width in physical pixels.</summary>
    private const int SmCxScreen = 0;

    /// <summary><c>SM_CYSCREEN</c>: the primary monitor's height in physical pixels.</summary>
    private const int SmCyScreen = 1;

    /// <summary><c>SM_XVIRTUALSCREEN</c>: the virtual screen's left edge.</summary>
    private const int SmXVirtualScreen = 76;

    /// <summary><c>SM_YVIRTUALSCREEN</c>: the virtual screen's top edge.</summary>
    private const int SmYVirtualScreen = 77;

    /// <summary><c>SM_CXVIRTUALSCREEN</c>: the virtual screen's width.</summary>
    private const int SmCxVirtualScreen = 78;

    /// <summary><c>SM_CYVIRTUALSCREEN</c>: the virtual screen's height.</summary>
    private const int SmCyVirtualScreen = 79;

    /// <summary><c>SM_CMONITORS</c>: the number of display monitors.</summary>
    private const int SmCMonitors = 80;

    /// <summary>A screen point, in the layout <c>GetCursorPos</c> writes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        /// <summary>The x coordinate, negative on a monitor left of the primary one.</summary>
        public int X;

        /// <summary>The y coordinate, negative on a monitor above the primary one.</summary>
        public int Y;
    }

    /// <summary>
    /// The <c>MONITORINFOEX</c> layout: the monitor rectangle, the work area, the flags and the
    /// device name, in that order, with the name as an inline 32-character array (hence the
    /// <see cref="CharSet.Unicode"/> on the enclosing structure).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        /// <summary>The structure size, which <c>GetMonitorInfo</c> requires the caller to fill in.</summary>
        public uint CbSize;

        /// <summary>The monitor's full rectangle in physical virtual-screen coordinates.</summary>
        public RECT Monitor;

        /// <summary>The monitor's work area: the rectangle minus any taskbars and appbars.</summary>
        public RECT Work;

        /// <summary>The <c>MONITORINFOF_*</c> flags.</summary>
        public uint Flags;

        /// <summary>The device name, for example <c>\\.\DISPLAY1</c>.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

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
        _menuOpenTimer?.Stop();
        _menuOpenTimer = null;
        _menuHoldTimer?.Stop();
        _menuHoldTimer = null;
        _balloonShowTimer?.Stop();
        _balloonShowTimer = null;

        // Before the icon is disposed, so the toast totals DetachObservers prints are the settled
        // ones: the teardown below unsubscribes every handler, and a callback arriving after the totals
        // line would be an activation the capture reports nowhere.
        StopToastDemonstration();

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
            trayIcon.PreviewBalloonTipClicked -= OnPreviewBalloonTipClicked;
            trayIcon.BalloonTipClicked -= OnBalloonTipClicked;

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
            $"[sample] totals: raw callback lines={_rawMessageCount}, pump-observed private-range messages={_pumpMessageCount}, library trace lines={_libraryTraceLineCount}, clicks={_clickCount}, cancelled by a Preview handler={_cancelledClickCount}, balloon show requests={_balloonShowRequestCount} (self={_selfBalloonShowCount}), balloon clicked deliveries={_balloonClickCount}, balloon preview deliveries={_balloonPreviewCount}, menu opens={_menuOpenCount}, menu dismissals={_menuDismissedCount}."));

        if (_toastApi is not null)
        {
            // The disposal guarantee measured rather than claimed: every toast callback that arrived
            // after the teardown line would have incremented one of these counters, and the snapshot
            // taken in StopToastDemonstration is what makes the reading a window (a --toast-dispose-after
            // run keeps pumping for the rest of the run) instead of a single instant.
            if (_toastTeardownRecorded)
            {
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sample] toast post-teardown window: activations={_toastActivationCount - _toastActivationsAtTeardown} dismissals={_toastDismissedCount - _toastDismissalsAtTeardown} errors={_toastErrorCount - _toastErrorsAtTeardown} after the teardown line (0 is the disposal guarantee)"));
            }

            // Its own line rather than a longer totals line, so a capture of the S01 toast run can be
            // compared with the M001-era captures that quote the totals line verbatim.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] toast totals: shows={_toastShowCount}, accepted={_toastShowAcceptedCount}, activations={_toastActivationCount} (last arguments='{_toastLastActivationArguments ?? "(none)"}'), dismissals={_toastDismissedCount}, failures={_toastFailedCount}, errors={_toastErrorCount}, refused={_toastRefusedCount}, libraryTrace={_libraryInternalTraceLineCount}, registered={_toastRegistered}, identity='{_toastAppUserModelId}'."));
        }

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
    /// The switches this sample understands; every other argument is rejected at startup.
    /// </summary>
    /// <param name="RunSeconds">How long the sample runs before shutting itself down, or <see langword="null"/>.</param>
    /// <param name="CancelledClickType">The click type a Preview handler cancels, or <see langword="null"/>.</param>
    /// <param name="OpenMenuAfter">The delay after which the sample opens its own menu once, or <see langword="null"/>.</param>
    /// <param name="BalloonIconKeyword">The balloon severity keyword in effect; always set, defaulting to <see cref="DefaultBalloonIconKeyword"/>.</param>
    /// <param name="BalloonNoSound">Whether the balloon is shown without the notification sound (<c>--balloon-nosound</c>).</param>
    /// <param name="BalloonRealtime">Whether the balloon is shown immediately rather than queued (<c>--balloon-realtime</c>).</param>
    /// <param name="BalloonRespectQuietTime">Whether the balloon claims to honour quiet time (<c>--balloon-respect-quiet-time</c>).</param>
    /// <param name="ShowBalloonAfter">The delay after which the sample shows one balloon with no click injected, or <see langword="null"/>.</param>
    /// <param name="Declarative">
    /// Whether the icon, its menu and its image come from <c>Application.Resources</c> markup
    /// (<c>--xaml</c>) instead of being constructed and wired here. In that mode nothing in this file
    /// assigns a property or subscribes to an event on the icon: markup carries the whole declaration,
    /// which is the thing this mode exists to prove.
    /// </param>
    /// <param name="Toast">Whether the M002/S01 toast demonstration runs (<c>--toast</c>).</param>
    /// <param name="ToastAppUserModelId">
    /// The identity the demonstration registers and shows under (<c>--toast-aumid</c>), or
    /// <see langword="null"/> to take the library's default for this process (the entry assembly's
    /// simple name).
    /// </param>
    /// <param name="ToastSeverityKeyword">
    /// The scenario the demonstration's shows ask for (<c>--toast-severity</c>), one of the
    /// <see cref="ToastSeverity"/> member names; always set, defaulting to
    /// <see cref="DefaultToastSeverityKeyword"/>. A non-<c>Default</c> value is a request the shell may
    /// decline - the reminder scenario is silently ignored unless the toast carries a
    /// background-activation action, which this sample does not emit - so the demonstration reports the
    /// attribute it asked for and never claims a visible difference.
    /// </param>
    /// <param name="ToastSkipRegister">
    /// Whether the demonstration never creates the shortcut (<c>--toast-skip-register</c>) - the
    /// negative control, which runs the same show path with an identity that was never registered.
    /// </param>
    /// <param name="ToastAfter">
    /// The delay after which the demonstration shows its first toast, or <see langword="null"/> when
    /// <c>--toast-after</c> was not given (the first show then uses the default delay).
    /// </param>
    /// <param name="ToastRepeat">
    /// The interval at which the demonstration re-shows the toast, or <see langword="null"/> for a
    /// single show.
    /// </param>
    /// <param name="ToastButtons">
    /// Whether every show in this run carries the two action buttons (<c>--toast-buttons</c>) with the
    /// arguments <see cref="ToastButton1Argument"/> and <see cref="ToastButton2Argument"/>.
    /// </param>
    /// <param name="ToastDisposeAfter">
    /// The delay after which the demonstration disposes its notifier while the process keeps pumping
    /// (<c>--toast-dispose-after</c>), or <see langword="null"/> when the switch was not given (the
    /// teardown then happens only at shutdown, which is the S01/S02 behaviour).
    /// </param>
    private sealed record SampleArguments(
        TimeSpan? RunSeconds,
        string? CancelledClickType,
        TimeSpan? OpenMenuAfter,
        string BalloonIconKeyword,
        bool BalloonNoSound,
        bool BalloonRealtime,
        bool BalloonRespectQuietTime,
        TimeSpan? ShowBalloonAfter,
        bool Declarative,
        bool Toast,
        string? ToastAppUserModelId,
        string ToastSeverityKeyword,
        bool ToastSkipRegister,
        TimeSpan? ToastAfter,
        TimeSpan? ToastRepeat,
        bool ToastButtons,
        TimeSpan? ToastDisposeAfter);

    /// <summary>
    /// Prints what this process's DPI awareness actually is, and whether the manifested PerMonitorV2
    /// mode is the one in effect.
    /// </summary>
    /// <remarks>
    /// <b>This is the measurement that turns "the executable manifests PerMonitorV2" into a
    /// reading.</b> A missing or stale manifest shows up here as <c>value=1</c>
    /// (DPI_AWARENESS_SYSTEM_AWARE) and as <c>isPerMonitorV2=False</c>, and every Display Scale in the
    /// same capture is then the system's rather than the monitor's - which is why the warning line is
    /// printed next to the reading instead of being left to the reader.
    /// </remarks>
    private void ReportDpiAwareness()
    {
        int processHresult = GetProcessDpiAwareness(GetCurrentProcess(), out int processAwareness);
        bool perMonitorV2 = false;
        string contextReading;

        try
        {
            IntPtr threadContext = GetThreadDpiAwarenessContext();
            int contextAwareness = GetAwarenessFromDpiAwarenessContext(threadContext);
            perMonitorV2 = AreDpiAwarenessContextsEqual(threadContext, DpiAwarenessContextPerMonitorAwareV2);

            contextReading = string.Create(
                CultureInfo.InvariantCulture,
                $"threadContextAwareness={contextAwareness} ({DescribeAwareness(contextAwareness)}) isPerMonitorV2={perMonitorV2}");
        }
        catch (EntryPointNotFoundException)
        {
            // The DPI awareness contexts arrived in Windows 10 1607. An older OS is a configuration
            // this sample reports, not one it crashes on.
            contextReading = "threadContextAwareness=UNKNOWN (this OS does not export GetThreadDpiAwarenessContext)";
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] process DPI awareness: GetProcessDpiAwareness=0x{processHresult:X8} value={processAwareness} ({DescribeAwareness(processAwareness)}); {contextReading}."));

        if (!perMonitorV2)
        {
            Console.WriteLine("[sample] WARNING: this executable did not report PerMonitorV2 - app.manifest is missing, or the binary is stale (rebuild before reading any Display Scale in this capture). The per-monitor claims in docs/UAT-S03.md depend on this line.");
        }
    }

    /// <summary>
    /// Prints the session's display configuration: every monitor with its rectangle, work area,
    /// effective DPI and scale, and which one carries the notification area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DPI comes from <c>GetDpiForMonitor(MDT_EFFECTIVE_DPI)</c>, which is awareness-independent
    /// and therefore the same reader the library's menu placement uses. That is deliberate: the two
    /// readings are then comparable, and the popup's own <c>dpi=</c> reading in the menu-open line is
    /// the third, per-window one.
    /// </para>
    /// <para>
    /// The tray monitor is reported as the primary monitor, because the notification area lives on
    /// the primary monitor's taskbar. The authoritative per-open value is the popup's own DPI in the
    /// menu-open line, and the checklist says so.
    /// </para>
    /// </remarks>
    private void ReportDisplayConfiguration()
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] display configuration: monitors={GetSystemMetrics(SmCMonitors)} virtualScreen={GetSystemMetrics(SmXVirtualScreen)},{GetSystemMetrics(SmYVirtualScreen)} {GetSystemMetrics(SmCxVirtualScreen)}x{GetSystemMetrics(SmCyVirtualScreen)} physical, primaryPhysical={GetSystemMetrics(SmCxScreen)}x{GetSystemMetrics(SmCyScreen)}"));

        int index = 0;
        string trayMonitor = "no monitor enumerated";

        EnumDisplayMonitorsCallback callback = (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { CbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            bool hasInfo = GetMonitorInfo(monitor, ref info);
            int dpiHresult = GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out uint dpiY);
            bool primary = hasInfo && (info.Flags & MonitorInfoPrimary) != 0;

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[sample] monitor {index}: device={info.DeviceName} primary={primary} rect={info.Monitor.Left},{info.Monitor.Top} {info.Monitor.Right - info.Monitor.Left}x{info.Monitor.Bottom - info.Monitor.Top} work={info.Work.Left},{info.Work.Top} {info.Work.Right - info.Work.Left}x{info.Work.Bottom - info.Work.Top} dpi={dpiX} scale={(dpiHresult == 0 ? (dpiX / 96.0).ToString("0.###", CultureInfo.InvariantCulture) : "unreadable")} hr=0x{dpiHresult:X8}"));

            if (primary)
            {
                trayMonitor = string.Create(
                    CultureInfo.InvariantCulture,
                    $"primary monitor device={info.DeviceName} dpi={dpiX} scale={(dpiHresult == 0 ? (dpiX / 96.0).ToString("0.###", CultureInfo.InvariantCulture) : "unreadable")}");
            }

            index++;
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[sample] tray monitor: {trayMonitor} - the notification area lives on the primary monitor's taskbar; the popup's own dpi= reading in the menu-open line is the authoritative per-open value."));
    }

    /// <summary>Names a <c>DPI_AWARENESS</c> value.</summary>
    /// <param name="awareness">The raw value.</param>
    /// <returns>The documented constant name, or <c>unknown</c>.</returns>
    private static string DescribeAwareness(int awareness) => awareness switch
    {
        0 => "DPI_AWARENESS_UNAWARE",
        1 => "DPI_AWARENESS_SYSTEM_AWARE",
        2 => "DPI_AWARENESS_PER_MONITOR_AWARE",
        _ => "unknown",
    };
}

/// <summary>
/// The sample's own <see cref="TrayIcon"/>, with one extra entry point: the documented
/// <see cref="TrayIcon.OnTrayClick"/> hook, invoked directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a top-level public type because XAML has to be able to name it.</b> A nested or internal
/// type cannot be referenced from markup, and S06's declarative mode declares this class in
/// <c>Application.Resources</c> - so the promotion is a requirement of the declarative surface, not
/// a stylistic preference.
/// </para>
/// <para>
/// <b>What this is for.</b> <c>--open-menu-after</c> has to open the menu without a shell click: the
/// icon may live in a flyout no injector can reach, and a menu that fails to open has to be visible
/// in the console rather than inferred from a screen nothing is reading. Deriving from
/// <see cref="TrayIcon"/> and calling the protected hook runs the *production* open path - menu
/// activation, the assigned menu, the shell's icon rectangle, the monitor's DPI,
/// <c>TrayIconPlacement</c>, the anchor window and a real WPF popup. What it bypasses is exactly one
/// step: the shell's own callback and its decode, which the sample demonstrates live by having real
/// clicks injected into it.
/// </para>
/// <para>
/// It reaches <c>base</c> by construction: it does not override the method, it invokes the base
/// implementation that a subclass is documented to call, so none of the library's policy is
/// re-implemented (or can drift) in the sample.
/// </para>
/// </remarks>
public sealed class SampleTrayIcon : TrayIcon
{
    /// <summary>
    /// Runs the default action of a right click, as if the shell had delivered one.
    /// </summary>
    /// <param name="screenAnchor">
    /// The cursor position to report as the click's anchor, in physical screen pixels. It is passed
    /// in rather than read here because the interop that reads it belongs to the application, and
    /// because a caller that supplies it makes the instrument's own input explicit.
    /// </param>
    /// <remarks>
    /// The click payload is a real one - a right button, a single click, a real cursor position and
    /// the right-click routed event - rather than an empty placeholder, because a future change that
    /// made <see cref="TrayIcon.OnTrayClick"/> read the payload would otherwise be invisible to this
    /// instrument. The placement path itself is shell-rect driven and deliberately ignores the anchor
    /// point, which the checklist records rather than assumes.
    /// </remarks>
    public void RequestMenuOpen(Point screenAnchor) =>
        OnTrayClick(new TrayIconClickEventArgs(MouseButton.Right, 1, screenAnchor, TrayIcon.TrayRightClickEvent));
}

/// <summary>
/// The object the sample's menu data context points at, so a menu item bound with
/// <c>{Binding Label}</c> has something to resolve against in both run modes.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because of where the S06 acceptance line's bindings actually resolve from.</b> A
/// <see cref="ContextMenu"/> declared in <c>Application.Resources</c> has no logical parent, and the
/// library sets the menu's <c>PlacementTarget</c> to its own anchor window rather than to the icon,
/// so the icon's <c>DataContext</c> never reaches the menu and the library never writes the menu's
/// <c>DataContext</c> either. The consumer therefore sets the menu's own data context -
/// <c>DataContext="{StaticResource TrayMenuData}"</c> in the declarative mode, and
/// <c>menu.DataContext = new SampleMenuData { ... }</c> in the code-first one - which is the division
/// of responsibility <see cref="TrayIcon.ContextMenu"/>'s remarks document. Both halves are pinned
/// headlessly by <c>TrayIconMenuDataContextTests</c> and measured live in <c>docs/UAT-S06.md</c>
/// ("Menu data context and bindings (T06)").
/// </para>
/// <para>
/// <b>Public and top-level because XAML cannot reference anything else:</b> the declarative mode
/// declares an instance of this type in <c>App.xaml</c>, and the parser has to resolve the element
/// name to a type. It is an ordinary CLR object with no dependency property, which is the point - a
/// menu's data context is the caller's own object graph, with nothing of the library in it.
/// </para>
/// </remarks>
public sealed class SampleMenuData
{
    /// <summary>Gets or sets the text a menu item bound with <c>{Binding Label}</c> resolves to.</summary>
    public string Label { get; set; } = string.Empty;
}
