using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The notification-area icon: a dependency-property driven element that owns the hidden host
/// window, the shell registration and the <c>HICON</c> lifetime behind one addressable object.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="FrameworkElement"/>, not a <see cref="DependencyObject"/> (D002).</b> The
/// element is never shown and never enters a visual tree, but it must be usable from XAML
/// markup: a <c>StaticResource</c> holding a <c>TrayIcon</c> subscribes to its routed event, and
/// the properties below are real dependency properties so that markup, styles and bindings work
/// against them the way they do for any other WPF element. A bare
/// <see cref="DependencyObject"/> would support neither the routed event nor resource lookup.
/// </para>
/// <para>
/// <b>One hidden top-level window per instance, created lazily.</b> The host
/// (<see cref="TrayMessageWindow"/>) is created on the first registration, on the dispatcher
/// thread, and destroyed by <see cref="Dispose"/>. Nothing is created until the icon is actually
/// shown, so an application that never sets <see cref="Visible"/> never creates a window.
/// </para>
/// <para>
/// <b>The registration sequence is a contract, not a preference.</b> The shell is told
/// <c>NIM_ADD</c> and then immediately <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c>, and the add
/// flags always include <c>NIF_SHOWTIP</c>. Version 4 makes the shell <em>suppress</em> the
/// standard tooltip unless that flag is present, so omitting it produces an icon whose
/// <see cref="ToolTipText"/> silently does nothing - no error, no exception, no trace (D012).
/// A <c>NIM_SETVERSION</c> failure deletes the registration that the successful add just
/// created: a half-registered icon (present, but with the old protocol) must never be left
/// behind.
/// </para>
/// <para>
/// <b>Two failure moments, two policies (D008).</b> A failure to <em>register</em> the icon is a
/// startup failure: it throws <see cref="TrayIconException"/> carrying the operation and the
/// Win32 error code, because an application whose only UI is the notification area would
/// otherwise degrade into a silently missing icon. A failure to <em>update</em> an already
/// registered icon is a runtime failure: the same call is retried exactly once
/// (<see cref="RuntimeRetryCount"/>) and, if the retry also fails, the failure is written to the
/// <see cref="NotifyIconTrace"/> channel and raised through the <see cref="TrayError"/> routed
/// event without terminating the process.
/// </para>
/// <para>
/// <b>Every click arrives as a routed event with a cancellable Preview twin.</b> All four click
/// types the notification area reports - left, double left, right and middle - are delivered as
/// <see cref="TrayIconClickEventArgs"/> routed events carrying the button, the click count and the
/// screen anchor, and each one has a <c>Preview...</c> counterpart registered with
/// <see cref="RoutingStrategy.Tunnel"/> while the main event bubbles. A Preview handler that sets
/// <see cref="RoutedEventArgs.Handled"/> suppresses the main event entirely: the framework pairs
/// Preview with Bubble only for input it stages itself, so a manually raised pair gets the
/// suppression from the raiser, which is what makes the cancellation real rather than decorative.
/// The pairs work on this element because a tray icon is standalone - the route has no ancestors
/// to walk, so "tunnelling" here means invoking the element's own Preview handlers first. A handler
/// that throws propagates, matching the <see cref="TrayError"/> contract: the library does not
/// swallow a caller's bug.
/// </para>
/// <para>
/// <b>Property state follows confirmed shell state (D008).</b> Registration state is only
/// updated in response to a result the shell confirmed, and a property change that could not be
/// applied at all is reverted before it is reported: a failed registration puts
/// <see cref="Visible"/> back to <see langword="false"/> and throws, and a change that no usable
/// dispatcher could carry out puts the property back to its previous value and throws
/// <see cref="InvalidOperationException"/>. The report is never silent, and the property never
/// claims a state that was not reached.
/// </para>
/// <para>
/// <b>HICON ownership is retain-and-destroy (R007).</b> The instance owns at most one icon
/// handle at a time: the one the shell has been given. A replacement destroys the <em>previous</em>
/// handle only after the shell accepted the new one, and a failed replacement destroys the
/// <em>new</em> handle instead - so neither a successful nor a failed change can leak an
/// <c>HICON</c>, and a failed change leaves the previously valid icon exactly as it was.
/// </para>
/// <para>
/// <b>Thread affinity is handled, not documented away (R015).</b> The assignment itself is
/// marshalled in the property accessor (<c>SetPropertyOnDispatcher</c>) - not in the change
/// callback, because WPF's own <c>SetValue</c> verifies thread access before any callback could
/// run - so an application may set these properties from a background thread. The single
/// documented exception is an instance that was created without any WPF dispatcher at all, which
/// has no thread to marshal to: that case raises <see cref="InvalidOperationException"/> and is
/// deliberately <em>not</em> a <see cref="TrayIconException"/>, because nothing about the
/// notification area failed.
/// </para>
/// <para>
/// <b>Disposal is mandatory and idempotent.</b> A tray-resident application must remove its icon
/// before it exits, or an orphaned icon stays in the notification area until the user hovers over
/// it. <see cref="Dispose"/> removes the registration, destroys the retained handle and destroys
/// the host window - and it also closes an open <see cref="ContextMenu"/> and destroys the anchor
/// window that menu was owned by, in that order, so nothing this element owns outlives it.
/// </para>
/// <para>
/// <b>A process that dies without disposing needs nothing from this library.</b> The registration
/// is bound to the host window, so process termination destroys that window and the shell drops
/// the icon with it (R006). That is why there is deliberately no <see cref="System.AppDomain.ProcessExit"/>
/// handler and no finalizer here: neither can run in the case they would have to cover, and a
/// force-killed process runs no managed code at all. The guarantee is proven live, against a real
/// notification area, rather than assumed (see <c>docs/UAT-S05.md</c>).
/// </para>
/// </remarks>
public class TrayIcon : FrameworkElement, IDisposable
{
    /// <summary>
    /// The name the <see cref="TrayErrorEvent"/> routed event is registered under, exposed as a
    /// constant so that markup, diagnostics and tests cannot spell it differently.
    /// </summary>
    public const string TrayErrorEventName = "TrayError";

    /// <summary>
    /// The routed event through which a runtime (that is, post-registration) notification-area
    /// failure is surfaced after the single retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bubbles, and it is raised on an element that normally has no parent and no visual
    /// tree - raising a routed event on a standalone element is supported and simply invokes the
    /// handlers attached to that element (asserted behaviourally by the lifecycle tests, because
    /// the whole product premise is "an application with no window").
    /// </para>
    /// <para>
    /// The event is the only user-visible failure channel a windowless host has besides the trace
    /// channel, which is why its payload carries the operation, the Win32 error code, the
    /// exception and the retry flag rather than a bare notification.
    /// </para>
    /// </remarks>
    public static readonly RoutedEvent TrayErrorEvent = EventManager.RegisterRoutedEvent(
        TrayErrorEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayErrorEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The name the <see cref="TrayLeftClickEvent"/> routed event is registered under, exposed as a
    /// constant so that markup, diagnostics and tests cannot spell it differently.
    /// </summary>
    public const string TrayLeftClickEventName = "TrayLeftClick";

    /// <summary>
    /// The name the <see cref="TrayLeftDoubleClickEvent"/> routed event is registered under.
    /// </summary>
    public const string TrayLeftDoubleClickEventName = "TrayLeftDoubleClick";

    /// <summary>
    /// The name the <see cref="TrayRightClickEvent"/> routed event is registered under.
    /// </summary>
    public const string TrayRightClickEventName = "TrayRightClick";

    /// <summary>
    /// The name the <see cref="TrayMiddleClickEvent"/> routed event is registered under.
    /// </summary>
    public const string TrayMiddleClickEventName = "TrayMiddleClick";

    /// <summary>
    /// The name the <see cref="PreviewTrayLeftClickEvent"/> routed event is registered under.
    /// </summary>
    public const string PreviewTrayLeftClickEventName = "PreviewTrayLeftClick";

    /// <summary>
    /// The name the <see cref="PreviewTrayLeftDoubleClickEvent"/> routed event is registered under.
    /// </summary>
    public const string PreviewTrayLeftDoubleClickEventName = "PreviewTrayLeftDoubleClick";

    /// <summary>
    /// The name the <see cref="PreviewTrayRightClickEvent"/> routed event is registered under.
    /// </summary>
    public const string PreviewTrayRightClickEventName = "PreviewTrayRightClick";

    /// <summary>
    /// The name the <see cref="PreviewTrayMiddleClickEvent"/> routed event is registered under.
    /// </summary>
    public const string PreviewTrayMiddleClickEventName = "PreviewTrayMiddleClick";

    /// <summary>
    /// The name the <see cref="BalloonTipClickedEvent"/> routed event is registered under, exposed as
    /// a constant so that markup, diagnostics and tests cannot spell it differently.
    /// </summary>
    public const string BalloonTipClickedEventName = "BalloonTipClicked";

    /// <summary>
    /// The name the <see cref="PreviewBalloonTipClickedEvent"/> routed event is registered under.
    /// </summary>
    public const string PreviewBalloonTipClickedEventName = "PreviewBalloonTipClicked";

    /// <summary>
    /// The routed event raised when the icon is single-clicked with the left mouse button.
    /// </summary>
    /// <remarks>
    /// It bubbles, it is raised on this element, and its payload is
    /// <see cref="TrayIconClickEventArgs"/>. The other three click types follow exactly this shape:
    /// <see cref="TrayLeftDoubleClickEvent"/>,
    /// <see cref="TrayRightClickEvent"/> and <see cref="TrayMiddleClickEvent"/>. Each one has a
    /// <c>Preview...</c> counterpart that is raised immediately before it, where a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> suppresses this event entirely - see the class remarks
    /// for why the raiser, not the framework, implements that suppression.
    /// </remarks>
    public static readonly RoutedEvent TrayLeftClickEvent = EventManager.RegisterRoutedEvent(
        TrayLeftClickEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The routed event raised when the icon is double-clicked with the left mouse button.
    /// </summary>
    /// <remarks>
    /// A double click is reported as its own event <em>and</em> as a click count of <c>2</c> in the
    /// payload, so a consumer that handles both left-click events from one handler can still tell
    /// them apart. Its cancellable counterpart is
    /// <see cref="PreviewTrayLeftDoubleClickEvent"/>.
    /// </remarks>
    public static readonly RoutedEvent TrayLeftDoubleClickEvent = EventManager.RegisterRoutedEvent(
        TrayLeftDoubleClickEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The routed event raised when the icon is clicked with the right mouse button.
    /// </summary>
    /// <remarks>
    /// Under <c>NOTIFYICON_VERSION_4</c> the shell reports this as <c>WM_CONTEXTMENU</c>, not as a
    /// button event. The anchor point in the payload is therefore <b>informational</b>: it is
    /// officially undefined for that message, so menu placement must not be derived from it. Its
    /// cancellable counterpart is <see cref="PreviewTrayRightClickEvent"/>.
    /// </remarks>
    public static readonly RoutedEvent TrayRightClickEvent = EventManager.RegisterRoutedEvent(
        TrayRightClickEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The routed event raised when the icon is clicked with the middle mouse button.
    /// </summary>
    /// <remarks>
    /// Its cancellable counterpart is <see cref="PreviewTrayMiddleClickEvent"/>.
    /// </remarks>
    public static readonly RoutedEvent TrayMiddleClickEvent = EventManager.RegisterRoutedEvent(
        TrayMiddleClickEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The tunnel-routed event raised before <see cref="TrayLeftClickEvent"/>.
    /// </summary>
    /// <remarks>
    /// A handler that sets <see cref="RoutedEventArgs.Handled"/> here prevents the main event from
    /// being raised at all. It is registered with <see cref="RoutingStrategy.Tunnel"/> because that
    /// is the strategy WPF's own <c>Preview...</c> events use, so the naming and the metadata agree
    /// with the framework's convention; on an element with no parent "tunnelling" means the
    /// element's own Preview handlers run first.
    /// </remarks>
    public static readonly RoutedEvent PreviewTrayLeftClickEvent = EventManager.RegisterRoutedEvent(
        PreviewTrayLeftClickEventName,
        RoutingStrategy.Tunnel,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The tunnel-routed event raised before <see cref="TrayLeftDoubleClickEvent"/>.
    /// </summary>
    /// <remarks>
    /// A handler that sets <see cref="RoutedEventArgs.Handled"/> here prevents the main event from
    /// being raised at all.
    /// </remarks>
    public static readonly RoutedEvent PreviewTrayLeftDoubleClickEvent = EventManager.RegisterRoutedEvent(
        PreviewTrayLeftDoubleClickEventName,
        RoutingStrategy.Tunnel,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The tunnel-routed event raised before <see cref="TrayRightClickEvent"/>.
    /// </summary>
    /// <remarks>
    /// This is the cancellation point for the menu: a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> here stops both the main event and the element's own
    /// right-click action.
    /// </remarks>
    public static readonly RoutedEvent PreviewTrayRightClickEvent = EventManager.RegisterRoutedEvent(
        PreviewTrayRightClickEventName,
        RoutingStrategy.Tunnel,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The tunnel-routed event raised before <see cref="TrayMiddleClickEvent"/>.
    /// </summary>
    /// <remarks>
    /// A handler that sets <see cref="RoutedEventArgs.Handled"/> here prevents the main event from
    /// being raised at all.
    /// </remarks>
    public static readonly RoutedEvent PreviewTrayMiddleClickEvent = EventManager.RegisterRoutedEvent(
        PreviewTrayMiddleClickEventName,
        RoutingStrategy.Tunnel,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The routed event raised when the user clicks a balloon notification this instance showed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bubbles, it is raised on this element, and its payload is a plain
    /// <see cref="RoutedEventArgs"/> whose <see cref="RoutedEventArgs.RoutedEvent"/> is this event.
    /// There is deliberately no balloon-specific args type in v1: a balloon click is a transient
    /// notification, not a payload-carrying click, so there is nothing for a dedicated type to
    /// carry that the routed event itself does not already name.
    /// </para>
    /// <para>
    /// The shell reports the click as the <c>NIN_BALLOONUSERCLICK</c> callback, whose
    /// <c>wParam</c> anchor is officially undefined - the balloon branch of the callback sink must
    /// not read one - so unlike the click events this family carries no coordinates. Its cancellable
    /// counterpart is <see cref="PreviewBalloonTipClickedEvent"/>.
    /// </para>
    /// </remarks>
    public static readonly RoutedEvent BalloonTipClickedEvent = EventManager.RegisterRoutedEvent(
        BalloonTipClickedEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<RoutedEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// The tunnel-routed event raised before <see cref="BalloonTipClickedEvent"/>.
    /// </summary>
    /// <remarks>
    /// A handler that sets <see cref="RoutedEventArgs.Handled"/> here prevents the main event from
    /// being raised at all, which is the same cancellation the click events implement in the raiser
    /// rather than in the framework (see the class remarks). It is registered with
    /// <see cref="RoutingStrategy.Tunnel"/> because that is the strategy WPF's own <c>Preview...</c>
    /// events use, so the naming and the metadata agree with the framework's convention; on an
    /// element with no parent "tunnelling" means the element's own Preview handlers run first.
    /// </remarks>
    public static readonly RoutedEvent PreviewBalloonTipClickedEvent = EventManager.RegisterRoutedEvent(
        PreviewBalloonTipClickedEventName,
        RoutingStrategy.Tunnel,
        typeof(EventHandler<RoutedEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// Identifies the <see cref="IconSource"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource),
        typeof(ImageSource),
        typeof(TrayIcon),
        new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Identifies the <see cref="ToolTipText"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToolTipTextProperty = DependencyProperty.Register(
        nameof(ToolTipText),
        typeof(string),
        typeof(TrayIcon),
        new PropertyMetadata(string.Empty, OnToolTipTextChanged, CoerceToolTipText));

    /// <summary>
    /// Identifies the <see cref="Visible"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty VisibleProperty = DependencyProperty.Register(
        nameof(Visible),
        typeof(bool),
        typeof(TrayIcon),
        new PropertyMetadata(false, OnVisibleChanged));

    /// <summary>
    /// Identifies the <see cref="MenuActivation"/> dependency property.
    /// </summary>
    /// <remarks>
    /// Registered with no change callback, because nothing is applied when the value is assigned:
    /// it is read at right-click time by the code that decides whether to open the menu. Unlike the
    /// three properties above, this one also deliberately does <em>not</em> go through
    /// <c>SetPropertyOnDispatcher</c> - see <see cref="MenuActivation"/> for why.
    /// </remarks>
    public static readonly DependencyProperty MenuActivationProperty = DependencyProperty.Register(
        nameof(MenuActivation),
        typeof(TrayMenuActivation),
        typeof(TrayIcon),
        new PropertyMetadata(TrayMenuActivation.RightClick));

    /// <summary>
    /// Identifies the <see cref="ContextMenu"/> dependency property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered with no change callback for the same reason <see cref="MenuActivationProperty"/> is:
    /// assigning a menu applies nothing eagerly - it is read when a right click arrives, and a
    /// notification-area click opens it there. Assigning it therefore does not go through
    /// <c>SetPropertyOnDispatcher</c> either; see <see cref="ContextMenu"/> for the full contract.
    /// </para>
    /// <para>
    /// <b>A library-owned property, not the inherited <see cref="FrameworkElement.ContextMenu"/>.</b>
    /// The base framework property is the same type with the same default, but it belongs to
    /// <c>ContextMenuService</c>'s automatic opening, which is driven by input events this element
    /// never receives. Reusing it would give an instance two independently settable menu values -
    /// one resolved by markup or a binding through the base property and one read by the click path -
    /// and nothing would say which one a consumer set. Owning the property makes "the menu" a single
    /// unambiguous value on this type, which is what the click path reads and what S06 resolves from
    /// a resource dictionary.
    /// </para>
    /// </remarks>
    public static new readonly DependencyProperty ContextMenuProperty = DependencyProperty.Register(
        nameof(ContextMenu),
        typeof(ContextMenu),
        typeof(TrayIcon),
        new PropertyMetadata(null));

    /// <summary>
    /// The number of times a failed runtime shell call is retried before the failure is surfaced.
    /// </summary>
    /// <remarks>
    /// A named constant rather than an emergent number: the policy is "retry exactly once, then
    /// report and continue", and a test asserts the count by counting seam calls.
    /// </remarks>
    private const int RuntimeRetryCount = 1;

    /// <summary>
    /// The edge length in pixels the icon is rasterized at.
    /// </summary>
    /// <remarks>
    /// A system-metric icon size (small icons are 16x16 at 100% DPI) is a fixed value for M001;
    /// DPI- and setting-aware size selection is S03's contract, which is why the resolution is
    /// not spread over the lifecycle code.
    /// </remarks>
    private const int IconPixelSize = 16;

    /// <summary>
    /// The longest tooltip the shell can hold: <c>WCHAR szTip[128]</c> in the header, and
    /// <see cref="char"/> is a UTF-16 code unit, so 127 characters fit next to the terminator.
    /// </summary>
    private const int MaxToolTipLength = 127;

    /// <summary>
    /// The longest balloon text the shell can hold: <c>WCHAR szInfo[256]</c> in the header, so 255
    /// characters fit next to the terminator.
    /// </summary>
    /// <remarks>
    /// Truncation to this capacity is deliberate rather than a passing of the problem to the
    /// marshaller: a longer string would be cut by the marshaller at an arbitrary point, which can
    /// land between the halves of a surrogate pair (see <see cref="TruncateToFieldCapacity"/>).
    /// </remarks>
    private const int MaxBalloonInfoLength = 255;

    /// <summary>
    /// The longest balloon title the shell can hold: <c>WCHAR szInfoTitle[64]</c> in the header, so
    /// 63 characters fit next to the terminator.
    /// </summary>
    /// <remarks>
    /// A truncated title still counts as a title, so the shell draws the severity icon; only an
    /// empty title suppresses it.
    /// </remarks>
    private const int MaxBalloonTitleLength = 63;

    /// <summary>
    /// The process-wide source of icon ids.
    /// </summary>
    /// <remarks>
    /// Process-unique and bounded to 16 bits because under <c>NOTIFYICON_VERSION_4</c> the shell
    /// reports the icon id in <c>HIWORD(lParam)</c> of the callback message (D012): an id above
    /// 0xFFFF would be truncated by the shell, and two icons sharing a truncated id would be
    /// indistinguishable to the event decoding in S02.
    /// </remarks>
    private static int s_nextIconId;

    /// <summary>The seam every shell call goes through; also the owner of the real GDI objects.</summary>
    private readonly IShellApi _shell;

    /// <summary>
    /// The dispatcher that owns this instance, or <see langword="null"/> when the instance was
    /// created without one.
    /// </summary>
    /// <remarks>
    /// Captured at construction instead of read from <see cref="DispatcherObject.Dispatcher"/>
    /// because WPF's own dispatcher is created on demand and therefore never null: the only way an
    /// instance can genuinely have no dispatcher is the internal verification constructor, and
    /// this field is what makes that case testable (R015).
    /// </remarks>
    private readonly Dispatcher? _dispatcher;

    /// <summary>The hidden host window, created on first registration and destroyed by disposal.</summary>
    private TrayMessageWindow? _host;

    /// <summary>The icon id this instance registered with the shell; 0 until the first registration.</summary>
    private uint _iconId;

    /// <summary>The <c>HICON</c> the shell currently has, or <see cref="IntPtr.Zero"/>.</summary>
    private IntPtr _registeredIcon;

    /// <summary>Whether the shell has confirmed the registration of this instance's icon.</summary>
    private bool _registered;

    /// <summary>Set once by <see cref="Dispose"/> so repeated disposal is a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// The menu currently opened by this instance, or <see langword="null"/> when none is open.
    /// </summary>
    /// <remarks>
    /// The same instance the consumer assigned, held so a second right click while it is open is a
    /// no-op instead of a second open, and so disposal can close what is showing. It is instance
    /// state on purpose: no menu state is static or process-wide, which is what lets S05's recovery
    /// path re-create the icon without disturbing a menu (D027).
    /// </remarks>
    private ContextMenu? _openMenu;

    /// <summary>
    /// The anchor window the open menu is owned by, or <see langword="null"/> when none is open.
    /// </summary>
    /// <remarks>
    /// Created when the menu opens and destroyed when it closes, so a consumer that never opens a
    /// menu never owns this window. It is deliberately not the tray host: the host is the shell
    /// registration window, and the measurement in M001/S03 showed a popup owned by it is ownerless
    /// and undismissable (<see cref="TrayMenuAnchorWindow"/>).
    /// </remarks>
    private TrayMenuAnchorWindow? _menuAnchor;

    /// <summary>
    /// The <c>HRESULT</c> of the last <see cref="IShellApi.ShellNotifyIconGetRect"/> call made for
    /// menu placement, or <see langword="null"/> when the call was not attempted because the icon
    /// was not registered.
    /// </summary>
    /// <remarks>
    /// Diagnostics and verification only; not part of the shipped public surface. It exists because
    /// the documented fallback ("the shell could not locate the icon, so the cursor is used") is
    /// otherwise indistinguishable from an implementation that never asked the shell at all.
    /// </remarks>
    private int? _lastIconRectHresult;

    /// <summary>
    /// Whether the last menu placement had to fall back to the cursor position.
    /// </summary>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    private bool _lastMenuPlacementUsedCursorFallback;

    /// <summary>
    /// The monitor reader the menu path asks for the icon monitor's work area and DPI, created on
    /// first use and injectable for verification.
    /// </summary>
    /// <remarks>
    /// Lazily created, like the host window: an instance that never opens a menu never asks a
    /// monitor question, so it never allocates the reader either. The verification constructor
    /// supplies one whose answers are scripted, which is the only way to exercise the documented
    /// "the DPI could not be read" degradation on a machine whose monitors do answer.
    /// </remarks>
    private MonitorInfoProvider? _monitorInfo;

    /// <summary>
    /// Set while a property is being put back to its previous value, so the revert itself does not
    /// re-enter the change callbacks.
    /// </summary>
    /// <remarks>
    /// Only an instance whose dispatcher cannot carry out changes needs this: it cannot apply the
    /// original change, and without the guard the revert would be another change it cannot apply.
    /// </remarks>
    private bool _suppressChangeCallbacks;

    /// <summary>
    /// Initializes a new instance that talks to the real shell and is owned by the calling
    /// thread's dispatcher.
    /// </summary>
    /// <remarks>
    /// Create the instance on the thread that owns the application's dispatcher (any WPF UI
    /// thread). It creates no window and registers no icon: both happen on the first
    /// <see cref="Visible"/> assignment.
    /// </remarks>
    public TrayIcon()
        : this(new ShellApi(), Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Initializes a new instance over the given seam, owned by the calling thread's dispatcher.
    /// </summary>
    /// <param name="shell">The seam to talk to the shell through.</param>
    /// <remarks>
    /// <b>Exists for verification (D009).</b> The shell call sequences, the retry policy and the
    /// handle-ownership rules cannot be exercised against a live notification area - a test host
    /// may not have one, and a real <c>Shell_NotifyIconW</c> failure cannot be forced - so the
    /// lifecycle tests construct the element over a scripted fake. The overload is
    /// <see langword="internal"/>; the shipped public surface is the parameterless constructor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    internal TrayIcon(IShellApi shell)
        : this(shell, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Initializes a new instance over the given seam and dispatcher.
    /// </summary>
    /// <param name="shell">The seam to talk to the shell through.</param>
    /// <param name="dispatcher">
    /// The dispatcher that owns the instance, or <see langword="null"/> to represent an instance
    /// created without any dispatcher - the case R015 requires to fail loudly. The null case is
    /// reachable <em>only</em> here: WPF creates a dispatcher on demand for every other
    /// construction path.
    /// </param>
    /// <remarks>
    /// Exists for verification (D009): the lifecycle tests pass a scripted seam, and the
    /// marshalling tests pass the dispatcher of the thread the element is meant to be bound to.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    internal TrayIcon(IShellApi shell, Dispatcher? dispatcher)
        : this(shell, dispatcher, monitorInfo: null)
    {
    }

    /// <summary>
    /// Initializes a new instance over the given seam, dispatcher and monitor reader.
    /// </summary>
    /// <param name="shell">The seam to talk to the shell through.</param>
    /// <param name="dispatcher">The dispatcher that owns the instance, or <see langword="null"/>; see the overload above.</param>
    /// <param name="monitorInfo">
    /// The monitor reader the menu path uses, or <see langword="null"/> to create the real one when
    /// a menu is first opened.
    /// </param>
    /// <remarks>
    /// Exists for verification (D009), exactly as the shell seam constructor does: the menu path's
    /// degradation rules ("no work area" and "no DPI reading") describe what happens when Windows
    /// cannot answer, which is not a state a test can produce on a live machine - so the reader is a
    /// seam too, and the tests script its answers. The shipped public surface is still the
    /// parameterless constructor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    internal TrayIcon(IShellApi shell, Dispatcher? dispatcher, MonitorInfoProvider? monitorInfo)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _dispatcher = dispatcher;
        _monitorInfo = monitorInfo;
    }

    /// <summary>
    /// Raised when a notification-area operation on an already registered icon failed and the
    /// single retry did not fix it.
    /// </summary>
    /// <remarks>
    /// A handler that throws propagates, like any routed event handler; the library does not
    /// swallow a caller's bug. Nothing in the notification-area path depends on the handler
    /// returning normally.
    /// </remarks>
    public event EventHandler<TrayErrorEventArgs> TrayError
    {
        add => AddHandler(TrayErrorEvent, value);
        remove => RemoveHandler(TrayErrorEvent, value);
    }

    /// <summary>
    /// Raised when the icon is single-clicked with the left mouse button.
    /// </summary>
    /// <remarks>
    /// Registers the handler for <see cref="TrayLeftClickEvent"/>. A handler that throws propagates,
    /// like any routed event handler; the library does not swallow a caller's bug.
    /// </remarks>
    public event EventHandler<TrayIconClickEventArgs> TrayLeftClick
    {
        add => AddHandler(TrayLeftClickEvent, value);
        remove => RemoveHandler(TrayLeftClickEvent, value);
    }

    /// <summary>
    /// Raised when the icon is double-clicked with the left mouse button.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="TrayLeftDoubleClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> TrayLeftDoubleClick
    {
        add => AddHandler(TrayLeftDoubleClickEvent, value);
        remove => RemoveHandler(TrayLeftDoubleClickEvent, value);
    }

    /// <summary>
    /// Raised when the icon is clicked with the right mouse button.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="TrayRightClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> TrayRightClick
    {
        add => AddHandler(TrayRightClickEvent, value);
        remove => RemoveHandler(TrayRightClickEvent, value);
    }

    /// <summary>
    /// Raised when the icon is clicked with the middle mouse button.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="TrayMiddleClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> TrayMiddleClick
    {
        add => AddHandler(TrayMiddleClickEvent, value);
        remove => RemoveHandler(TrayMiddleClickEvent, value);
    }

    /// <summary>
    /// Raised before <see cref="TrayLeftClickEvent"/>; a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> prevents the main event from being raised.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="PreviewTrayLeftClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> PreviewTrayLeftClick
    {
        add => AddHandler(PreviewTrayLeftClickEvent, value);
        remove => RemoveHandler(PreviewTrayLeftClickEvent, value);
    }

    /// <summary>
    /// Raised before <see cref="TrayLeftDoubleClickEvent"/>; a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> prevents the main event from being raised.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="PreviewTrayLeftDoubleClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> PreviewTrayLeftDoubleClick
    {
        add => AddHandler(PreviewTrayLeftDoubleClickEvent, value);
        remove => RemoveHandler(PreviewTrayLeftDoubleClickEvent, value);
    }

    /// <summary>
    /// Raised before <see cref="TrayRightClickEvent"/>; a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> prevents the main event from being raised.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="PreviewTrayRightClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> PreviewTrayRightClick
    {
        add => AddHandler(PreviewTrayRightClickEvent, value);
        remove => RemoveHandler(PreviewTrayRightClickEvent, value);
    }

    /// <summary>
    /// Raised before <see cref="TrayMiddleClickEvent"/>; a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> prevents the main event from being raised.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="PreviewTrayMiddleClickEvent"/>.</remarks>
    public event EventHandler<TrayIconClickEventArgs> PreviewTrayMiddleClick
    {
        add => AddHandler(PreviewTrayMiddleClickEvent, value);
        remove => RemoveHandler(PreviewTrayMiddleClickEvent, value);
    }

    /// <summary>
    /// Raised when the user clicks a balloon notification shown by this instance.
    /// </summary>
    /// <remarks>
    /// Registers the handler for <see cref="BalloonTipClickedEvent"/>. The payload is a plain
    /// <see cref="RoutedEventArgs"/>; a handler that throws propagates, like any routed event
    /// handler, because the library does not swallow a caller's bug.
    /// </remarks>
    public event EventHandler<RoutedEventArgs> BalloonTipClicked
    {
        add => AddHandler(BalloonTipClickedEvent, value);
        remove => RemoveHandler(BalloonTipClickedEvent, value);
    }

    /// <summary>
    /// Raised before <see cref="BalloonTipClickedEvent"/>; a handler that sets
    /// <see cref="RoutedEventArgs.Handled"/> prevents the main event from being raised.
    /// </summary>
    /// <remarks>Registers the handler for <see cref="PreviewBalloonTipClickedEvent"/>.</remarks>
    public event EventHandler<RoutedEventArgs> PreviewBalloonTipClicked
    {
        add => AddHandler(PreviewBalloonTipClickedEvent, value);
        remove => RemoveHandler(PreviewBalloonTipClickedEvent, value);
    }

    /// <summary>
    /// Gets or sets the image shown in the notification area.
    /// </summary>
    /// <value>
    /// The icon source, or <see langword="null"/> (the default) for "no icon yet". Setting the
    /// property from a background thread is supported and marshals to the UI thread.
    /// </value>
    /// <remarks>
    /// <para>
    /// Changing the value on a registered icon converts the image to an <c>HICON</c> and sends
    /// <c>NIM_MODIFY</c> with <c>NIF_ICON</c> (plus <c>NIF_TIP</c> when a tooltip is set: the
    /// shell does not persist <c>szTip</c> independently of the modify that carries it). The
    /// previous handle is destroyed only after the shell accepted the new one, so a failed change
    /// leaves the previously valid icon displayed (R007).
    /// </para>
    /// <para>
    /// Setting <see langword="null"/> does <b>not</b> clear the displayed icon: there is no shell
    /// operation that means "keep the registration but drop the image", so a null source is
    /// treated as "no change". Use <see cref="Visible"/> to hide the icon.
    /// </para>
    /// <para>
    /// An <see cref="ImageSource"/> that cannot be converted (a locked or foreign-thread drawing
    /// source, for example) raises <see cref="TrayIconException"/> with
    /// <see cref="TrayIconException.OperationConvertIcon"/>; that failure is a caller error rather
    /// than a shell failure, so it is reported rather than retried, and the registered icon is
    /// left untouched.
    /// </para>
    /// </remarks>
    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetPropertyOnDispatcher(IconSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip text the shell shows for the icon.
    /// </summary>
    /// <value>
    /// The tooltip, empty by default. <see langword="null"/> is normalised to the empty string.
    /// Setting the property from a background thread is supported and marshals to the UI thread.
    /// </value>
    /// <remarks>
    /// The text is truncated to <see cref="MaxToolTipLength"/> characters (127) before it reaches
    /// the shell, because <c>szTip</c> is <c>WCHAR[128]</c> including the terminator: a longer
    /// string would be silently cut by the marshaller at an arbitrary point. The truncation is
    /// deliberate and never splits a surrogate pair, and the modify that carries the new text also
    /// carries <c>NIF_SHOWTIP</c>, without which version 4 suppresses the tooltip entirely (D012).
    /// </remarks>
    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetPropertyOnDispatcher(ToolTipTextProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the icon is shown in the notification area.
    /// </summary>
    /// <value><see langword="false"/> by default. Setting the property from a background thread is
    /// supported and marshals to the UI thread.</value>
    /// <remarks>
    /// <para>
    /// Setting it to <see langword="true"/> registers the icon: the host window is created on
    /// first use, the icon is converted if <see cref="IconSource"/> is set, and the shell is told
    /// <c>NIM_ADD</c> and then <c>NIM_SETVERSION(4)</c>. Setting it to <see langword="false"/>
    /// removes the registration and destroys the retained <c>HICON</c>.
    /// </para>
    /// <para>
    /// A failed registration raises <see cref="TrayIconException"/> <em>and</em> leaves the
    /// property <see langword="false"/>, so the value always describes shell-confirmed state and a
    /// later assignment can retry the registration.
    /// </para>
    /// <para>
    /// <b>The value survives a taskbar restart (R005).</b> A restarted Explorer discards the
    /// notification-area registration - the registration lives in the shell's process, not in this
    /// one - but it does not change what the consumer asked for, so the icon is re-registered with
    /// the handle that is already retained and this property stays <see langword="true"/>. A
    /// recovery the shell refuses is reported through <see cref="TrayErrorEvent"/> and never flips
    /// this property, so the value keeps describing intent rather than a momentary shell state;
    /// assigning <see langword="false"/> and back to <see langword="true"/> remains the way to
    /// force a fresh registration.
    /// </para>
    /// </remarks>
    public bool Visible
    {
        get => (bool)GetValue(VisibleProperty);
        set => SetPropertyOnDispatcher(VisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets which click activates the icon's context menu.
    /// </summary>
    /// <value><see cref="TrayMenuActivation.RightClick"/> by default.</value>
    /// <remarks>
    /// <para>
    /// The value is read when a click arrives, so a click is always reported as an event and this
    /// property only decides whether the element should open the menu in response. Assigning it
    /// changes nothing that has already been applied to the shell and applies nothing eagerly.
    /// </para>
    /// <para>
    /// <b><see cref="TrayMenuActivation.None"/> opens nothing and raises everything.</b> The two
    /// right-click routed events - <see cref="PreviewTrayRightClickEvent"/> and
    /// <see cref="TrayRightClickEvent"/> - are raised exactly as they are for any other value,
    /// because they report what the shell delivered rather than what the library decided to do about
    /// it. With <see cref="TrayMenuActivation.None"/> the click simply has no library-provided
    /// default action, which is the opt-out a consumer that opens its own UI needs.
    /// </para>
    /// <para>
    /// <b>Deliberately not marshalled.</b> The other three dependency properties route their
    /// assignments through <c>SetPropertyOnDispatcher</c> because an assignment has to reach the
    /// shell on the thread that owns the host window (R015). This property applies nothing, so
    /// there is no work to place on that thread; copying the wrapper by reflex would only add a
    /// marshalling step that carries no change. Markup assigns it directly
    /// (<c>MenuActivation="None"</c>), and WPF evaluates markup on the owning thread, so the
    /// intended usage needs no wrapper at all. An assignment from a foreign thread is not
    /// marshalled either: WPF's own thread check refuses it, exactly as it does for any other
    /// dependency property.
    /// </para>
    /// </remarks>
    public TrayMenuActivation MenuActivation
    {
        get => (TrayMenuActivation)GetValue(MenuActivationProperty);
        set => SetValue(MenuActivationProperty, value);
    }

    /// <summary>
    /// Gets or sets the context menu a right click opens, or <see langword="null"/> (the default)
    /// for "this element has no menu".
    /// </summary>
    /// <value>
    /// The consumer's own <see cref="System.Windows.Controls.ContextMenu"/> instance, never a copy of
    /// it. <see langword="null"/> by default, and assignable from markup (a <c>StaticResource</c>
    /// resolves through a dependency property, which is why this is one).
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>It is the caller's menu, and it is never cloned (R009).</b> The instance assigned here is
    /// the instance that opens: a clone would break the caller's <c>DataContext</c>, its item
    /// templates and every handler it wired up, because a clone has none of them. Nothing in this
    /// library inspects, re-parents or mutates the menu's items; the menu's data context, its
    /// handlers and its items are entirely the caller's business.
    /// </para>
    /// <para>
    /// <b>The value is read when a right click arrives.</b> Assigning a different menu between two
    /// clicks takes effect on the next click, and assigning <see langword="null"/> means "no menu":
    /// a right click then raises the click events and does nothing else, which is exactly the
    /// behaviour of an element that never mentions a menu at all.
    /// </para>
    /// <para>
    /// <b>Opened when <see cref="MenuActivation"/> is
    /// <see cref="TrayMenuActivation.RightClick"/> (the default).</b> With
    /// <see cref="TrayMenuActivation.None"/>, or when a Preview handler cancels the click, or when a
    /// main-event handler marks it handled, an assigned menu stays closed and the click is report
    /// only. The library never opens a menu the consumer asked it not to open.
    /// </para>
    /// <para>
    /// <b>Assigning one instance to two <see cref="TrayIcon"/> instances is unsupported.</b> A WPF
    /// <see cref="System.Windows.Controls.ContextMenu"/> can only be open in one place at a time, so
    /// the second icon that tries to open it while the first has it open does not get a second
    /// popup - it reports a failure through <c>TrayError</c> and opens nothing, leaving the first
    /// icon's menu exactly where it is. Give each icon its own menu instance.
    /// </para>
    /// <para>
    /// <b>Not marshalled, unlike <see cref="IconSource"/>, <see cref="ToolTipText"/> and
    /// <see cref="Visible"/>.</b> Those three apply something to the shell, which must happen on the
    /// thread that owns the host window (D019). This property applies nothing: it is read at click
    /// time on the dispatcher thread that received the click, so there is no work to marshal. Markup
    /// assigns it directly, and WPF's own thread check governs an assignment from a foreign thread,
    /// exactly as it does for <see cref="MenuActivation"/>.
    /// </para>
    /// </remarks>
    public new ContextMenu? ContextMenu
    {
        get => (ContextMenu?)GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <summary>
    /// Shows a balloon notification for this icon.
    /// </summary>
    /// <param name="title">
    /// The balloon's title. <see langword="null"/> is legal and is normalised to the empty string;
    /// the shell then omits the severity icon entirely.
    /// </param>
    /// <param name="text">
    /// The balloon's body text. Must not be <see langword="null"/> or empty; see the exceptions.
    /// </param>
    /// <param name="icon">The severity icon to show. <see cref="BalloonTipIcon.Info"/> by default.</param>
    /// <param name="options">
    /// Optional shell behaviours, combined as flags. <see cref="BalloonTipOptions.None"/> by
    /// default.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is <see langword="null"/> or empty. An empty <c>szInfo</c> under
    /// <c>NIF_INFO</c> <em>removes</em> the balloon that is currently showing, so an "empty show"
    /// would be a silent delete and is refused instead.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The icon is not registered (either it was never made visible or it has been disposed), or the
    /// instance has no usable dispatcher to marshal to. In every one of those cases nothing has been
    /// sent to the shell, so the call is not a <see cref="TrayIconException"/>: no notification-area
    /// operation failed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is a command, not a property (D031).</b> A balloon is a transient message, so it is
    /// a method call rather than a set of dependency properties that would have to be kept in sync
    /// with an event that fires when the user types or clicks; there is deliberately no balloon
    /// property to bind.
    /// </para>
    /// <para>
    /// <b>The shell work is one <c>NIM_MODIFY</c> carrying <c>NIF_INFO</c>.</b> The title and the
    /// text are truncated to what the shell's fields can hold (<c>szInfoTitle</c> 63 and
    /// <c>szInfo</c> 255 characters) without ever splitting a surrogate pair, the severity is cast
    /// into the low nibble of <c>dwInfoFlags</c>, <see cref="BalloonTipOptions.NoSound"/> and
    /// <see cref="BalloonTipOptions.RespectQuietTime"/> are OR-ed into the same field, and
    /// <see cref="BalloonTipOptions.Realtime"/> becomes <c>NIF_REALTIME</c> in <c>uFlags</c> - never
    /// the other field, where that bit number would mean a custom user icon.
    /// </para>
    /// <para>
    /// <b>There is no timeout parameter, and none is written.</b> The structure's
    /// <c>uTimeout</c> reading of the <c>uTimeoutOrVersion</c> union is deprecated since Windows
    /// Vista; the display duration is the system accessibility setting, so the union slot keeps
    /// whatever registration put there (<c>NOTIFYICON_VERSION_4</c>) and this method never touches
    /// it. <c>hBalloonIcon</c> likewise stays zero, because <c>NIIF_USER</c> and a caller-supplied
    /// balloon icon are out of scope for this version.
    /// </para>
    /// <para>
    /// <b>A refused balloon follows the runtime policy (D008).</b> The call is retried once, and if
    /// the retry also fails the failure is written to the trace channel and raised through
    /// <see cref="TrayError"/> with <see cref="TrayErrorEventArgs.Retried"/> <see langword="true"/>.
    /// This method never throws because the shell refused: losing a balloon is strictly better than
    /// losing the application.
    /// </para>
    /// <para>
    /// <b>Marshalled like the property setters (R015).</b> A background-thread caller is moved to
    /// the dispatcher that owns the host window, because the shell call belongs to the thread that
    /// owns that window. An instance created without a dispatcher refuses with the existing
    /// <see cref="InvalidOperationException"/> from <c>ApplyOnDispatcher</c> and makes no shell call
    /// at all.
    /// </para>
    /// </remarks>
    public void ShowBalloonTip(
        string? title,
        string text,
        BalloonTipIcon icon = BalloonTipIcon.Info,
        BalloonTipOptions options = BalloonTipOptions.None)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException(
                "A balloon must carry text: an empty szInfo under NIF_INFO removes the balloon that is currently shown, "
                + "so this call would silently delete it instead of showing one. Pass the message to display.",
                nameof(text));
        }

        ApplyOnDispatcher(() =>
        {
            if (_disposed || !_registered || _host is null)
            {
                throw new InvalidOperationException(
                    "ShowBalloonTip requires a registered icon: a balloon can only be shown for an icon the shell currently "
                    + "holds. Set Visible to true (and keep it true) before showing a balloon, and do not call ShowBalloonTip "
                    + "after Dispose.");
            }

            NOTIFYICONDATAW data = CreateIconData(_host);

            // NIF_INFO is what makes the balloon members valid. NIF_REALTIME is a NIF_* bit and
            // therefore belongs here, in uFlags - see BalloonTipOptions for why placing it in
            // dwInfoFlags would be silently wrong rather than an error.
            data.uFlags = ShellConstants.NIF_INFO;

            if ((options & BalloonTipOptions.Realtime) != 0)
            {
                data.uFlags |= ShellConstants.NIF_REALTIME;
            }

            data.szInfo = TruncateToFieldCapacity(text, MaxBalloonInfoLength);
            data.szInfoTitle = TruncateToFieldCapacity(title, MaxBalloonTitleLength);

            // The severity is the shell's own NIIF_NONE..NIIF_ERROR value, so this is a cast and
            // not a lookup; the two option flags are single bits outside the severity's low nibble.
            data.dwInfoFlags = (uint)icon;

            if ((options & BalloonTipOptions.NoSound) != 0)
            {
                data.dwInfoFlags |= ShellConstants.NIIF_NOSOUND;
            }

            if ((options & BalloonTipOptions.RespectQuietTime) != 0)
            {
                data.dwInfoFlags |= ShellConstants.NIIF_RESPECT_QUIET_TIME;
            }

            // uTimeoutOrVersion is deliberately not written: the balloon timeout reading is
            // deprecated since Vista and the slot holds the registration's protocol version.
            // hBalloonIcon is deliberately not written: NIIF_USER is out of scope for v1.
            ApplyShellChange(ShellConstants.NIM_MODIFY, ref data, TrayIconException.OperationModify);
        });
    }

    /// <summary>
    /// Gets a value indicating whether the shell confirmed the registration of this instance's
    /// icon.
    /// </summary>
    /// <value><see langword="true"/> only between a confirmed <c>NIM_ADD</c> (plus
    /// <c>NIM_SETVERSION</c>) and a confirmed <c>NIM_DELETE</c>.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal bool IsRegistered => _registered;

    /// <summary>
    /// Gets the <c>HICON</c> the shell currently holds, or <see cref="IntPtr.Zero"/> when none is
    /// registered.
    /// </summary>
    /// <value>The retained handle, owned by this instance.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal IntPtr RegisteredIconHandle => _registeredIcon;

    /// <summary>
    /// Gets the host window handle, or <see cref="IntPtr.Zero"/> when no host has been created
    /// yet.
    /// </summary>
    /// <value>The <c>HWND</c> of the hidden top-level host window.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal IntPtr HostHandle => _host?.Handle ?? IntPtr.Zero;

    /// <summary>
    /// Gets the icon id this instance registered with the shell, or <c>0</c> before the first
    /// registration.
    /// </summary>
    /// <remarks>
    /// Diagnostics and verification only; not part of the shipped public surface. It is the id the
    /// shell reports in <c>HIWORD(lParam)</c> of a callback, so a test that injects a callback has to
    /// encode this value - and reading it from the instance is what keeps the test from guessing.
    /// </remarks>
    internal uint IconId => _iconId;

    /// <summary>
    /// Gets the menu currently opened by this instance, or <see langword="null"/> when none is open.
    /// </summary>
    /// <value>The consumer's own menu instance while it is showing.</value>
    /// <remarks>
    /// Diagnostics and verification only; not part of the shipped public surface. It is what a test
    /// reads to assert "the instance that opened is the instance that was assigned" and "disposal
    /// left nothing open", without reaching into the menu's own <c>IsOpen</c>.
    /// </remarks>
    internal ContextMenu? OpenContextMenu => _openMenu;

    /// <summary>
    /// Gets a value indicating whether a menu opened by this instance is currently showing.
    /// </summary>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal bool IsMenuOpen => _openMenu is not null;

    /// <summary>
    /// Gets the handle of the anchor window the open menu is owned by, or
    /// <see cref="IntPtr.Zero"/> when no menu is open.
    /// </summary>
    /// <value>The 1x1 <c>WS_POPUP</c> window the popup's owner relationship runs through.</value>
    /// <remarks>
    /// Diagnostics and verification only; not part of the shipped public surface. It turns the
    /// dismissal contract into an identity assertion - the popup's <c>GW_OWNER</c> must be this
    /// handle - and lets a test assert that the window is gone once the menu closed.
    /// </remarks>
    internal IntPtr MenuAnchorHandle => _menuAnchor?.Handle ?? IntPtr.Zero;

    /// <summary>
    /// Gets the <c>HRESULT</c> the last menu placement got from
    /// <see cref="IShellApi.ShellNotifyIconGetRect"/>, or <see langword="null"/> when that call was
    /// not made because the icon was not registered.
    /// </summary>
    /// <remarks>
    /// Diagnostics and verification only; not part of the shipped public surface. It is the recorded
    /// reason of the documented fallback: a test can tell "the shell refused to locate the icon"
    /// (a code) from "there was nothing to locate" (no call) from "the shell answered" (<c>0</c>).
    /// </remarks>
    internal int? LastIconRectHresult => _lastIconRectHresult;

    /// <summary>
    /// Gets a value indicating whether the last menu placement fell back to the cursor position.
    /// </summary>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal bool LastMenuPlacementUsedCursorFallback => _lastMenuPlacementUsedCursorFallback;

    /// <summary>
    /// Removes the icon from the notification area, releases the retained <c>HICON</c> and
    /// destroys the hidden host window. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marshalled to the owning dispatcher when the caller is on another thread, so an application
    /// may dispose from an exit handler on any thread. An instance created without a dispatcher
    /// disposes inline: there is no thread to marshal to, and refusing to dispose would leave the
    /// icon behind.
    /// </para>
    /// <para>
    /// A failing <c>NIM_DELETE</c> does not throw: it is retried once and then reported through
    /// the <see cref="TrayError"/> event and the trace channel, because disposal is typically the
    /// last thing a process does and there is no caller left to handle an exception. An exception
    /// thrown by a <see cref="TrayError"/> handler is the caller's own and propagates.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        Dispatcher? dispatcher = _dispatcher;

        if (dispatcher is null
            || dispatcher.HasShutdownStarted
            || dispatcher.HasShutdownFinished
            || dispatcher.CheckAccess())
        {
            // No dispatcher to marshal to, a dispatcher that can no longer execute anything, or
            // already on the owning thread: all three dispose inline. See the remarks.
            DisposeCore();
            return;
        }

        dispatcher.Invoke(DisposeCore);
    }

    /// <summary>
    /// Assigns a dependency property on the dispatcher that owns this instance (R015).
    /// </summary>
    /// <param name="property">The property to assign.</param>
    /// <param name="value">The new value.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the marshalling is here and not in the change callback.</b> WPF's own
    /// <c>DependencyObject.SetValue</c> verifies thread access <em>before</em> any change callback
    /// runs, and refuses with "The calling thread cannot access this object because a different
    /// thread owns it". A background-thread assignment that relied on the callback to marshal would
    /// therefore never reach the callback at all - it would fail inside WPF with a message about
    /// WPF's threading model instead of doing what the caller asked. Marshalling the whole
    /// assignment is also what makes the work the assignment triggers (the image conversion, the
    /// shell call, the ownership transfer, the host-window creation) happen on the owning thread,
    /// which is the only thread those things are legal on.
    /// </para>
    /// <para>
    /// Markup and bindings assign through <c>SetValue</c> directly and bypass this method. That is
    /// harmless - WPF evaluates them on the owning dispatcher thread - and the change callbacks
    /// keep an access check of their own, so a direct <c>SetValue</c> from a foreign thread is
    /// still refused by this library rather than by WPF.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The instance was created without a usable dispatcher; nothing is assigned in that case.
    /// </exception>
    private void SetPropertyOnDispatcher(DependencyProperty property, object? value) =>
        ApplyOnDispatcher(() => SetValue(property, value));

    /// <summary>
    /// Marshals <paramref name="action"/> onto the dispatcher that owns this instance (R015).
    /// </summary>
    /// <param name="action">The work to run on the owning thread.</param>
    /// <exception cref="InvalidOperationException">
    /// The instance was created without a WPF dispatcher, or its dispatcher has been shut down, so
    /// there is no thread that could own the host window. This is the documented carve-out of
    /// D008 and deliberately not a <see cref="TrayIconException"/>: no notification-area operation
    /// failed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// On the owning thread the action runs inline, so the common case costs a
    /// <see cref="Dispatcher.CheckAccess"/> and nothing else. From a background thread it runs
    /// through a synchronous <c>Dispatcher.Invoke</c>, which propagates an exception to the
    /// caller: a background-thread failure must be the background thread's exception, not a
    /// swallowed trace line.
    /// </para>
    /// <para>
    /// An instance whose <see cref="DispatcherObject.Dispatcher"/> has been shut down cannot accept
    /// work any more - WPF itself refuses with <see cref="InvalidOperationException"/> - so the
    /// refusal is made explicit here, where the message can name the requirement.
    /// </para>
    /// </remarks>
    private void ApplyOnDispatcher(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher? dispatcher = _dispatcher;

        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                "This TrayIcon instance was created without a WPF Dispatcher, so its properties cannot be marshalled to a UI thread. "
                + "Create the TrayIcon on a thread that owns a Dispatcher (any WPF UI thread does) and set its properties from there or from any other thread.");
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException(
                "This TrayIcon instance belongs to a Dispatcher that has already been shut down, so its properties can no longer be marshalled "
                + "to a UI thread and the hidden host window can no longer be created or destroyed.");
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>
    /// Applies a property change on the owning dispatcher, reverting the property when the change
    /// could not be applied at all.
    /// </summary>
    /// <param name="property">The property being changed.</param>
    /// <param name="previousValue">The value to restore; the change's old value.</param>
    /// <param name="action">The work to run on the owning thread.</param>
    /// <exception cref="InvalidOperationException">
    /// No usable dispatcher exists; it is raised <em>after</em> the property has been put back, so
    /// the caller sees both a loud failure and a property that still describes reality.
    /// </exception>
    /// <remarks>
    /// The revert only covers the case where nothing was applied. A <see cref="TrayIconException"/>
    /// is deliberately not caught here: each change owns its own rollback semantics (a failed icon
    /// replacement keeps the previous icon registered, a failed registration unregisters nothing)
    /// and reverts precisely the part of the state it touched.
    /// </remarks>
    private void ApplyOrRevert(DependencyProperty property, object? previousValue, Action action)
    {
        try
        {
            ApplyOnDispatcher(action);
        }
        catch (InvalidOperationException)
        {
            _suppressChangeCallbacks = true;

            try
            {
                SetCurrentValue(property, previousValue);
            }
            finally
            {
                _suppressChangeCallbacks = false;
            }

            throw;
        }
    }

    /// <summary>
    /// Normalises <see cref="ToolTipText"/> to a non-null string.
    /// </summary>
    /// <param name="d">The element whose property value is being resolved.</param>
    /// <param name="baseValue">The value being assigned.</param>
    /// <returns>The assigned value, or the empty string when it is <see langword="null"/>.</returns>
    /// <remarks>
    /// <c>null</c> is a legitimate way to say "no tooltip", but it is not a legitimate stored value
    /// for a property whose getter is typed as a non-nullable string: returning null would force
    /// every consumer to be nullable-aware for no reason. Coercion runs before the change callback,
    /// so the lifecycle only ever sees a real string.
    /// </remarks>
    private static object CoerceToolTipText(DependencyObject d, object baseValue) =>
        (string?)baseValue ?? string.Empty;

    /// <summary>
    /// The <see cref="IconSourceProperty"/> change callback: marshals, then applies the change.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        var source = (ImageSource?)e.NewValue;

        trayIcon.ApplyOrRevert(IconSourceProperty, e.OldValue, () => trayIcon.ApplyIconSource(source));
    }

    /// <summary>
    /// The <see cref="ToolTipTextProperty"/> change callback: marshals, then applies the change.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnToolTipTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        trayIcon.ApplyOrRevert(ToolTipTextProperty, e.OldValue, trayIcon.ApplyToolTipText);
    }

    /// <summary>
    /// The <see cref="VisibleProperty"/> change callback: marshals, then registers or removes.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        bool visible = (bool)e.NewValue;

        trayIcon.ApplyOrRevert(
            VisibleProperty,
            e.OldValue,
            visible ? trayIcon.RegisterOrRevert : trayIcon.Unregister);
    }

    /// <summary>
    /// Registers the icon, or puts <see cref="Visible"/> back to <see langword="false"/> when the
    /// shell refused.
    /// </summary>
    /// <remarks>
    /// The revert is what keeps the property meaningful: the value is only allowed to say
    /// <see langword="true"/> for a registration the shell confirmed (D008). It also keeps the
    /// property retryable - a failed registration leaves <see langword="false"/>, so raising the
    /// property again attempts the registration again, instead of doing nothing because the value
    /// never changed. The exception that follows is the report, so the revert is never silent.
    /// <c>DependencyObject.SetCurrentValue</c> is used rather than <c>SetValue</c> so that a
    /// binding or style that drives the property from markup is left intact.
    /// </remarks>
    private void RegisterOrRevert()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            EnsureRegistered();
        }
        catch (TrayIconException)
        {
            SetCurrentValue(VisibleProperty, false);
            throw;
        }
    }

    /// <summary>
    /// Runs the registration sequence: host window, icon handle, <c>NIM_ADD</c>,
    /// <c>NIM_SETVERSION(4)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the contract: <c>NIM_ADD</c> immediately followed by
    /// <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c>, with <c>NIF_SHOWTIP</c> among the add flags
    /// (D012). Failures follow the startup policy: the last error is read immediately after the
    /// failing call, and a thrown <see cref="TrayIconException"/> names the operation.
    /// </para>
    /// <para>
    /// Nothing is leaked on the failure paths: an <c>HICON</c> built for an add that the shell
    /// refused is destroyed before the throw, and the registration a successful add created is
    /// deleted when <c>NIM_SETVERSION</c> fails, so a half-registered icon is never left in the
    /// notification area.
    /// </para>
    /// </remarks>
    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        TrayMessageWindow host = EnsureHost();

        if (_iconId == 0)
        {
            _iconId = AllocateIconId();
        }

        IntPtr icon = IconSource is null ? IntPtr.Zero : HiconFactory.CreateIcon(_shell, IconSource, IconPixelSize);
        var data = CreateIconData(host);

        // NIF_MESSAGE carries uCallbackMessage, NIF_TIP carries szTip, and NIF_SHOWTIP is what
        // makes the shell show that tooltip at all under version 4 (D012). NIF_ICON is added only
        // when there is an icon to show: a zero hIcon with NIF_ICON set asks the shell to display
        // nothing.
        data.uFlags = ShellConstants.NIF_MESSAGE | ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
        data.uCallbackMessage = host.CallbackMessageId;

        // NIF_TIP is only meaningful with szTip filled in. This is the one call that establishes
        // the tooltip, so leaving szTip empty here would mean the icon has no tooltip until the
        // text happens to change - and the truncation rule applies exactly as it does on update.
        data.szTip = TruncateToolTipText(ToolTipText);

        if (icon != IntPtr.Zero)
        {
            data.hIcon = icon;
            data.uFlags |= ShellConstants.NIF_ICON;
        }

        if (!_shell.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data))
        {
            // Read the code first: it belongs to the call that just failed and is overwritten by
            // the next Win32 call (IShellApi.GetLastError).
            int error = _shell.GetLastError();

            if (icon != IntPtr.Zero)
            {
                _shell.DestroyIcon(icon);
            }

            throw new TrayIconException(TrayIconException.OperationAdd, error);
        }

        var versionData = CreateIconData(host);
        versionData.uTimeoutOrVersion = ShellConstants.NOTIFYICON_VERSION_4;

        if (!_shell.ShellNotifyIcon(ShellConstants.NIM_SETVERSION, ref versionData))
        {
            int error = _shell.GetLastError();

            // A half-registered icon is worse than none: the icon would be present but speaking
            // the old protocol, so S02's event decoding would be wrong. Remove it before reporting.
            var rollback = CreateIconData(host);
            _shell.ShellNotifyIcon(ShellConstants.NIM_DELETE, ref rollback);

            if (icon != IntPtr.Zero)
            {
                _shell.DestroyIcon(icon);
            }

            throw new TrayIconException(TrayIconException.OperationSetVersion, error);
        }

        _registered = true;
        _registeredIcon = icon;
    }

    /// <summary>
    /// Re-registers the icon after the shell announced that the taskbar was (re)created, which is
    /// what makes an icon reappear by itself after Explorer restarts (R005).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a re-add, not a second registration path.</b> The notification-area registration
    /// lives in the shell's process, so an Explorer restart discards it while everything this
    /// instance knows is still valid: the requested visibility, the retained <c>HICON</c> and the
    /// tooltip text. Recovering therefore means issuing the <c>NIM_ADD</c> plus
    /// <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c> pair again - the version must be selected on
    /// every add and is not persisted by the shell - with the handle that is already owned here.
    /// </para>
    /// <para>
    /// <b><see cref="EnsureRegistered"/> is deliberately not reused.</b> It returns immediately
    /// when the registration flag is set, it throws on failure (the startup policy, which is the
    /// wrong channel for a callback that arrives inside a window procedure), and it would convert
    /// <see cref="IconSource"/> again and overwrite <see cref="_registeredIcon"/>, leaking the handle
    /// that the next unregistration would have destroyed.
    /// </para>
    /// <para>
    /// <b><see cref="Visible"/> and the registration flag stay <see langword="true"/>.</b> A
    /// recovery is not a visibility change: the consumer still wants the icon, so a refused
    /// recovery is reported through <see cref="TrayErrorEvent"/> instead of quietly pretending the
    /// consumer asked for no icon. The next broadcast tries again, and the consumer can force a
    /// fresh attempt by toggling <see cref="Visible"/>.
    /// </para>
    /// <para>
    /// <b>Failure follows the runtime policy (D008).</b> Both calls go through
    /// <see cref="ApplyShellChange"/>, which retries once, traces and then raises
    /// <see cref="TrayErrorEvent"/> - nothing here throws. A failed re-add leaves the icon absent.
    /// When the re-add succeeds but the version call fails, the half-registered icon is removed
    /// again (a recovered icon speaking the legacy protocol would misdecode every click), while the
    /// retained handle is deliberately <em>not</em> destroyed and the registration flag is left
    /// alone: a set flag with a live handle is recoverable, whereas clearing it would make the next
    /// registration build a second handle over the field and leak the first.
    /// </para>
    /// <para>
    /// Nothing else is touched. An open context menu belongs to a process-local anchor window and
    /// survives an Explorer restart untouched, and no balloon state is re-sent, because a balloon is
    /// a transient <c>NIM_MODIFY</c> and never part of a registration.
    /// </para>
    /// </remarks>
    private void RecoverAfterTaskbarCreated()
    {
        TrayMessageWindow? host = _host;

        if (_disposed || !_registered || host is null)
        {
            // The broadcast reaches every top-level window, so an instance that never showed an
            // icon - or one that has been disposed - must not create one now: the application never
            // asked for this icon.
            return;
        }

        var data = CreateIconData(host);

        data.uFlags = ShellConstants.NIF_MESSAGE | ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
        data.uCallbackMessage = host.CallbackMessageId;

        // szTip is re-sent for the same reason EnsureRegistered sends it: NIF_TIP is only
        // meaningful with the text filled in, so a re-add that omitted it would produce an icon
        // whose tooltip silently does nothing until the text happens to change (D012).
        data.szTip = TruncateToolTipText(ToolTipText);

        if (_registeredIcon != IntPtr.Zero)
        {
            data.hIcon = _registeredIcon;
            data.uFlags |= ShellConstants.NIF_ICON;
        }

        if (!ApplyShellChange(ShellConstants.NIM_ADD, ref data, TrayIconException.OperationAdd))
        {
            // Already retried, traced and raised through TrayError. The icon stays absent and the
            // next broadcast tries again.
            return;
        }

        var versionData = CreateIconData(host);
        versionData.uTimeoutOrVersion = ShellConstants.NOTIFYICON_VERSION_4;

        if (!ApplyShellChange(ShellConstants.NIM_SETVERSION, ref versionData, TrayIconException.OperationSetVersion))
        {
            var rollback = CreateIconData(host);
            _shell.ShellNotifyIcon(ShellConstants.NIM_DELETE, ref rollback);

            NotifyIconTrace.Verbose(
                "TrayIcon removed the half-registered icon after a failed NIM_SETVERSION during "
                + "TaskbarCreated recovery; the retained HICON is kept for the next attempt.");
            return;
        }

        NotifyIconTrace.Verbose(
            "TrayIcon re-registered after the TaskbarCreated broadcast: NIM_ADD and "
            + "NIM_SETVERSION(NOTIFYICON_VERSION_4) were re-issued with the retained HICON.");
    }

    /// <summary>
    /// Removes the registration and releases the retained <c>HICON</c>.
    /// </summary>
    /// <remarks>
    /// Runs through <see cref="ApplyShellChange"/>, so a refused <c>NIM_DELETE</c> is retried once
    /// and then surfaced instead of throwing: this method is reached from disposal, and from a
    /// property assignment where an exception would be more alarming than the stale icon. When the
    /// delete does not succeed, the registration is left intact - the icon is still there, and
    /// pretending otherwise would lose the handle needed to remove it later.
    /// </remarks>
    private void Unregister()
    {
        if (!_registered || _host is null)
        {
            return;
        }

        var data = CreateIconData(_host);

        if (ApplyShellChange(ShellConstants.NIM_DELETE, ref data, TrayIconException.OperationRemove))
        {
            _registered = false;
            ReleaseRegisteredIcon();
        }
    }

    /// <summary>
    /// Applies a new <see cref="IconSource"/> to a registered icon.
    /// </summary>
    /// <param name="source">The new source, or <see langword="null"/> for "no change".</param>
    /// <remarks>
    /// The conversion happens first and can throw a <see cref="TrayIconException"/> naming
    /// <see cref="TrayIconException.OperationConvertIcon"/>; nothing has been sent to the shell at
    /// that point, so the registered icon is untouched. Once the new handle exists, the previous
    /// one is destroyed only if the shell accepted the replacement - a failed replacement destroys
    /// the new handle instead, which is what keeps both "the previous icon stays visible" (R007)
    /// and "no handle leaks" true at the same time.
    /// </remarks>
    private void ApplyIconSource(ImageSource? source)
    {
        if (_disposed || source is null || !_registered || _host is null)
        {
            return;
        }

        IntPtr replacement = HiconFactory.CreateIcon(_shell, source, IconPixelSize);
        var data = CreateIconData(_host);
        data.uFlags = ShellConstants.NIF_ICON;
        data.hIcon = replacement;

        string tip = TruncateToolTipText(ToolTipText);

        if (tip.Length > 0)
        {
            data.uFlags |= ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
            data.szTip = tip;
        }

        if (ApplyShellChange(ShellConstants.NIM_MODIFY, ref data, TrayIconException.OperationModify))
        {
            IntPtr previous = _registeredIcon;
            _registeredIcon = replacement;

            if (previous != IntPtr.Zero)
            {
                DestroyIcon(previous);
            }
        }
        else
        {
            // The shell kept the icon it had, so the handle built for the refused replacement is
            // not registered anywhere and must be released here or it leaks (R007).
            DestroyIcon(replacement);
        }
    }

    /// <summary>
    /// Applies the current <see cref="ToolTipText"/> to a registered icon.
    /// </summary>
    /// <remarks>
    /// <c>NIF_SHOWTIP</c> travels with <c>NIF_TIP</c> on purpose: the flag is what makes version 4
    /// show the standard tooltip, so a tooltip update that omitted it would appear to succeed and
    /// change nothing on screen.
    /// </remarks>
    private void ApplyToolTipText()
    {
        if (_disposed || !_registered || _host is null)
        {
            return;
        }

        var data = CreateIconData(_host);
        data.uFlags = ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
        data.szTip = TruncateToolTipText(ToolTipText);

        ApplyShellChange(ShellConstants.NIM_MODIFY, ref data, TrayIconException.OperationModify);
    }

    /// <summary>
    /// Sends one runtime shell operation under the retry-then-surface policy of D008.
    /// </summary>
    /// <param name="message">The <c>NIM_*</c> operation code.</param>
    /// <param name="data">The icon data to send, unchanged on the retry.</param>
    /// <param name="operation">The operation name for the failure report.</param>
    /// <returns><see langword="true"/> when the shell confirmed the operation.</returns>
    /// <remarks>
    /// The same call is repeated at most <see cref="RuntimeRetryCount"/> times - the retry exists
    /// because the shell occasionally rejects an update while Explorer is busy, and a single
    /// retry covers that without looping. When the retry also fails, the failure is traced and
    /// raised as <see cref="TrayErrorEvent"/> with <c>Retried = true</c> and this method returns
    /// <see langword="false"/>; it never throws and never terminates the process, because losing an
    /// icon update is strictly better than losing the application. The caller decides what an
    /// unconfirmed operation means for its own state.
    /// </remarks>
    private bool ApplyShellChange(uint message, ref NOTIFYICONDATAW data, string operation)
    {
        if (_shell.ShellNotifyIcon(message, ref data))
        {
            return true;
        }

        int error = _shell.GetLastError();

        for (int attempt = 0; attempt < RuntimeRetryCount; attempt++)
        {
            if (_shell.ShellNotifyIcon(message, ref data))
            {
                return true;
            }

            error = _shell.GetLastError();
        }

        var exception = new TrayIconException(
            operation,
            error,
            "The call was retried once and failed again, so the notification-area state may now be stale.");

        NotifyIconTrace.Error(operation, error, exception, retried: true);
        RaiseEvent(new TrayErrorEventArgs(operation, error, exception, retried: true, TrayErrorEvent));

        return false;
    }

    /// <summary>
    /// Destroys an owned <c>HICON</c>, tracing a failure instead of letting a handle leak silently.
    /// </summary>
    /// <param name="hIcon">The handle to release; <see cref="IntPtr.Zero"/> is ignored.</param>
    /// <remarks>
    /// A refused <c>DestroyIcon</c> cannot be retried usefully (the handle is either valid or it
    /// is not) and cannot be reported through <see cref="TrayError"/> without inventing an
    /// operation that did not fail. Tracing it keeps the leak visible to a support log instead of
    /// hiding it, which is what R007's no-leak claim needs in order to stay honest.
    /// </remarks>
    private void DestroyIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return;
        }

        if (_shell.DestroyIcon(hIcon))
        {
            return;
        }

        int error = _shell.GetLastError();

        NotifyIconTrace.Error(
            TrayIconException.OperationRemove,
            error,
            new TrayIconException(
                TrayIconException.OperationRemove,
                error,
                "An icon handle could not be destroyed and remains allocated for the lifetime of the process."),
            retried: false);
    }

    /// <summary>
    /// Destroys the retained <c>HICON</c> and clears the field.
    /// </summary>
    private void ReleaseRegisteredIcon()
    {
        IntPtr handle = _registeredIcon;
        _registeredIcon = IntPtr.Zero;

        DestroyIcon(handle);
    }

    /// <summary>
    /// Releases everything this instance owns: the registration, the retained <c>HICON</c>, an open
    /// context menu and its anchor window, and the host window.
    /// </summary>
    /// <remarks>
    /// Runs on the owning dispatcher thread (see <see cref="Dispose"/>). The order matters: the
    /// shell is told about the removal while the host window still exists, the icon handle is
    /// released next, the menu and its anchor are torn down, and the window is destroyed last. The
    /// retained handle is released even when the shell refused the removal: disposal is terminal, so
    /// holding on to the handle could only leak it, and the registration it belonged to is being
    /// abandoned either way. Every field is cleared before this method returns, so a second disposal
    /// cannot repeat any of it.
    /// </remarks>
    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TrayMessageWindow? host = _host;

        if (_registered && host is not null)
        {
            var data = CreateIconData(host);

            if (ApplyShellChange(ShellConstants.NIM_DELETE, ref data, TrayIconException.OperationRemove))
            {
                _registered = false;
            }
        }

        ReleaseRegisteredIcon();

        // The menu goes before the host window is destroyed: the popup is owned by the anchor rather
        // than by the host, but the click that opened it arrived through the host, so the resource
        // that can still call back into this instance is released while the shell registration that
        // named it is already gone - the same ordering discipline the three steps above follow.
        CloseMenu();

        _host = null;
        host?.Dispose();
    }

    /// <summary>
    /// Creates the host window on first use, on the thread that is executing this call.
    /// </summary>
    /// <returns>The host window.</returns>
    /// <remarks>
    /// Lazy on purpose: an instance that never becomes <see cref="Visible"/> never creates a
    /// window, which is what lets an application construct the element during startup without
    /// producing any OS resource at all.
    /// </remarks>
    private TrayMessageWindow EnsureHost() => _host ??= new TrayMessageWindow(_shell, OnHostMessage);

    /// <summary>
    /// Receives every message the host window is sent, including the shell's notification
    /// callbacks and the <c>TaskbarCreated</c> broadcast.
    /// </summary>
    /// <param name="message">The message id.</param>
    /// <param name="wParam">
    /// The first message parameter: the anchor point for a click, and deliberately never read for a
    /// balloon callback - see the branch comment in the body.
    /// </param>
    /// <param name="lParam">The second message parameter: the event code and the icon id.</param>
    /// <remarks>
    /// <para>
    /// <b>This is where a shell callback becomes a click event.</b> The message is matched against
    /// the callback id the host registered and then classified by
    /// <see cref="TrayEventDecoder.Classify"/> - the pure function that owns the
    /// <c>NOTIFYICON_VERSION_4</c> payload layout - and a decoded click is raised as the pair of
    /// routed events that belongs to it: the Tunnel <c>Preview...</c> event first, then, unless a
    /// Preview handler cancelled it, the Bubble main event.
    /// </para>
    /// <para>
    /// <b>A balloon callback takes its own branch.</b> <c>NIN_BALLOONUSERCLICK</c> is raised as the
    /// cancellable <see cref="PreviewBalloonTipClickedEvent"/> /
    /// <see cref="BalloonTipClickedEvent"/> pair with the same raiser-implemented suppression a
    /// click uses, and the three lifecycle codes (<c>NIN_BALLOONSHOW</c>, <c>NIN_BALLOONHIDE</c>,
    /// <c>NIN_BALLOONTIMEOUT</c>) raise no public event and are reported at Verbose only (D033).
    /// <paramref name="wParam"/> is never read on the balloon branch: the header leaves the anchor
    /// undefined for every balloon code.
    /// </para>
    /// <para>
    /// <b>The suppression is implemented here, on purpose.</b> WPF pairs a Preview event with its
    /// Bubble twin only for input it stages itself; a manually raised pair has no such pairing, so a
    /// Preview handler's <see cref="RoutedEventArgs.Handled"/> would have no effect on the main raise
    /// unless the raiser honours it. The check below is that honouring, and deleting it as
    /// "redundant" would silently turn every Preview event of this class into decoration.
    /// </para>
    /// <para>
    /// <b>The message that is not ours is left completely alone.</b> The <c>TaskbarCreated</c>
    /// broadcast S05 recovers on arrives at this same sink, and so does everything else a window
    /// receives; none of it is decoded, filtered or traced here.
    /// </para>
    /// <para>
    /// It never throws for input this library produces: the decoder cannot throw, an unmapped or
    /// foreign payload is reported at Verbose level only, and the only exception that can leave this
    /// method is one thrown by a consumer's own click or balloon-click handler - which propagates by
    /// contract, exactly as it does for <see cref="TrayErrorEvent"/>. It needs no marshalling: the
    /// sink runs on the thread that created the host, which is this instance's owning dispatcher
    /// thread.
    /// </para>
    /// </remarks>
    private void OnHostMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        TrayMessageWindow? host = _host;

        if (host is not null && host.IsTaskbarCreatedMessage(message))
        {
            // The shell rebuilt the notification area, which discards the registration that lived
            // in the previous Explorer process. Tested before the callback-id check below, because
            // the broadcast shares nothing with a notification callback and that early return
            // would drop it silently.
            RecoverAfterTaskbarCreated();
            return;
        }

        if (host is null || message != host.CallbackMessageId)
        {
            // Not this icon's notification callback. The window is a fresh, empty instance only for
            // the instant before the constructor stores it, and the shell's broadcast id is an
            // entirely different number, so both conditions mean "not a tray click".
            return;
        }

        // One classification drives the whole sink (D032): a click keeps the S02 path below
        // unchanged, a balloon callback takes the balloon branch, and everything else - a foreign
        // icon id or an unmapped event code - stays ordinary, Verbose-only traffic.
        TrayCallbackClassification classification =
            TrayEventDecoder.Classify(message, wParam, lParam, host.CallbackMessageId, _iconId);

        if (classification.Outcome == TrayCallbackOutcome.BalloonEvent)
        {
            // Unlike the click path below, this branch never reads wParam. The header promises the
            // anchor only for NIN_POPUPOPEN, NIN_SELECT, NIN_KEYSELECT and the mouse messages
            // between WM_MOUSEFIRST and WM_MOUSELAST; a balloon code lies outside that set, so
            // wParam holds whatever the shell happened to leave in the register and decoding it
            // would invent a screen point the shell never sent. The decoder already honoured the
            // rule - a balloon classification carries no anchor at all.
            RaiseBalloonCallback(classification.EventCode, classification.IconId);
            return;
        }

        TrayMouseEvent? decoded = classification.Click;

        if (decoded is null)
        {
            // Our callback, but either another icon's id or an event code this library does not map
            // to a click. Both are ordinary: pointer motion, keyboard selection and the popup codes
            // all arrive here. A Verbose line is the whole report - never an error line, which
            // stays reserved for failures (MEM026).
            TraceNoMappedClick(lParam);
            return;
        }

        TrayMouseEvent click = decoded.Value;
        (RoutedEvent Preview, RoutedEvent Main)? clickEvents = SelectClickEvents(click);

        if (clickEvents is null)
        {
            // Unreachable while the decoder and this mapping agree on the four click types. It is
            // still handled rather than thrown: this runs inside a window procedure, where an
            // exception would take the host application down over a click it could have ignored.
            TraceNoMappedClick(lParam);
            return;
        }

        // A fresh args instance per phase, never one instance raised twice: each event carries the
        // event it was raised for, and the two phases have different handler lists.
        var previewArgs = new TrayIconClickEventArgs(click.Button, click.ClickCount, click.ScreenAnchor, clickEvents.Value.Preview);

        RaiseEvent(previewArgs);

        if (previewArgs.Handled)
        {
            // Cancelled before the main phase - see the remarks for why this check lives here.
            return;
        }

        var mainArgs = new TrayIconClickEventArgs(click.Button, click.ClickCount, click.ScreenAnchor, clickEvents.Value.Main);

        RaiseEvent(mainArgs);

        if (!mainArgs.Handled)
        {
            OnTrayClick(mainArgs);
        }
    }

    /// <summary>
    /// Raises the public outcome of one balloon callback: the user-click code becomes the cancellable
    /// <see cref="PreviewBalloonTipClickedEvent"/> / <see cref="BalloonTipClickedEvent"/> pair, and
    /// each lifecycle code becomes a Verbose trace line with no public event (D033).
    /// </summary>
    /// <param name="eventCode">The balloon event code from <c>LOWORD(lParam)</c>.</param>
    /// <param name="iconId">The icon id from <c>HIWORD(lParam)</c>, already filtered to this icon.</param>
    /// <remarks>
    /// <para>
    /// <b>The suppression is implemented here, exactly as for a click.</b> WPF pairs a Preview event
    /// with its Bubble twin only for input it stages itself, so the check between the two raises is
    /// what makes <c>PreviewBalloonTipClicked</c> cancellable; deleting it as "redundant" would turn
    /// the Preview event into decoration.
    /// </para>
    /// <para>
    /// <b>It never throws for input this library produces.</b> It runs inside the window procedure,
    /// where an exception of the library's own making would become a crash; the only exception that
    /// can leave it is one thrown by a consumer's own balloon handler, which propagates by the same
    /// contract as a click handler's exception.
    /// </para>
    /// </remarks>
    private void RaiseBalloonCallback(uint eventCode, uint iconId)
    {
        if (eventCode != ShellNotifications.NIN_BALLOONUSERCLICK)
        {
            // SHOW, HIDE and TIMEOUT are lifecycle news, not user actions (D033): no public event,
            // one Verbose line - never an error line, because a balloon the system suppressed,
            // coalesced or timed out is not a library failure (D008, MEM026).
            TraceBalloonLifecycle(eventCode, iconId);
            return;
        }

        // A fresh args instance per phase, never one instance raised twice - the same rule the click
        // pair follows, for MEM025's reason: RoutedEventArgs(RoutedEvent) does not validate its
        // argument on net8.0-windows, so each phase constructs its own instance stamped with its own
        // event at this construction site.
        var previewArgs = new RoutedEventArgs(PreviewBalloonTipClickedEvent);

        RaiseEvent(previewArgs);

        if (previewArgs.Handled)
        {
            // Cancelled before the main phase - the same raiser-implemented suppression the click
            // pair relies on (see the remarks on OnHostMessage for why the raiser must check).
            return;
        }

        RaiseEvent(new RoutedEventArgs(BalloonTipClickedEvent));
    }

    /// <summary>
    /// Runs the default action for a click that still stands: a right click with a menu assigned and
    /// menu activation enabled opens that menu at the icon.
    /// </summary>
    /// <param name="e">
    /// The arguments of the main click event that was just raised - the button, the click count and
    /// the anchor point - plus the event it was raised for; never <see langword="null"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Deliberately narrow (D027).</b> The click events report what the user did,
    /// <see cref="MenuActivation"/> says which click should activate the menu, and
    /// <see cref="ContextMenu"/> says what to open; this method is the whole of the policy. It reads
    /// the property at click time, so a menu assigned or replaced between two clicks takes effect on
    /// the next one, and the state it keeps lives on this instance - never in a static field - so a
    /// recovery path can re-create the icon without disturbing a menu.
    /// </para>
    /// <para>
    /// <b>Called on the thread that owns the host window</b>, after the main event has been raised,
    /// and only while the click still stands: not when a Preview handler cancelled it, and not when a
    /// handler of the main event marked it <see cref="RoutedEventArgs.Handled"/> - WPF's own
    /// convention, where a handled input event means the default action is not wanted. That is why
    /// cancellation needs no code here: by the time this method runs, the click has already survived
    /// both suppression points. An override must call <c>base.OnTrayClick(e)</c> so behaviour added
    /// to this class keeps working.
    /// </para>
    /// </remarks>
    protected virtual void OnTrayClick(TrayIconClickEventArgs e)
    {
        // No base call: this *is* the base implementation of the hook - the element's only ancestor
        // is FrameworkElement, which has no such member. The contract for a subclass that overrides
        // this method is the opposite one: it must call base.OnTrayClick(e) so this behaviour keeps
        // running for the clicks it does not handle itself.
        if (e.Button != MouseButton.Right || e.ClickCount != 1)
        {
            // The default action is a right-click menu. A click count of 2 is the shell reporting a
            // double click, and acting on it would rebuild the anchor underneath the menu the first
            // click already opened; left and middle clicks have no library-provided action at all.
            return;
        }

        ContextMenu? menu = ContextMenu;

        if (menu is null)
        {
            // No menu assigned, which is the property's default: the click stays a pure event,
            // exactly as it was before this element had a menu property. A consumer that draws its
            // own UI depends on that.
            return;
        }

        if (MenuActivation != TrayMenuActivation.RightClick)
        {
            // Read here, on every click, rather than cached in a field: assigning the value between
            // two clicks must take effect on the next one. This check sits after the events were
            // raised, so MenuActivation = None still reports the click - it only declines the action.
            return;
        }

        if (_openMenu is not null)
        {
            // This instance already has a menu open. A second open would fight the live popup for
            // its placement target and build a second anchor window under the first one, and the
            // consumer's click means "it is already showing" rather than "show it twice".
            return;
        }

        OpenMenu(menu);
    }

    /// <summary>
    /// Opens <paramref name="menu"/> at the icon, owned by a freshly created anchor window, and
    /// records it as this instance's open menu.
    /// </summary>
    /// <param name="menu">
    /// The consumer's own menu, already known to be assigned, activated and not currently open by
    /// this instance.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Already on the owning dispatcher thread, on purpose.</b> <see cref="OnTrayClick"/> runs
    /// inside the host window's message sink, which is the thread that owns this instance's
    /// dispatcher, so nothing here goes through <c>ApplyOnDispatcher</c>: marshalling the open would
    /// only post it back to the queue the click is already running on, and a WPF popup cannot be
    /// opened from inside the pump that is opening it.
    /// </para>
    /// <para>
    /// <b>The placement chain, in this order.</b> The anchor rectangle comes from the shell
    /// (<see cref="IShellApi.ShellNotifyIconGetRect"/>), never from the click's own anchor point,
    /// which the shell documents as undefined for <c>WM_CONTEXTMENU</c> (D028); the monitor that owns
    /// that rectangle supplies the work area and the DPI (D026);
    /// <see cref="TrayIconPlacement"/> turns the three into the offset the placement engine consumes,
    /// applying the physical-to-DIP scale exactly once; and <see cref="TrayMenuAnchorWindow"/> is what
    /// gives the popup a real owner, which is what makes an outside click dismiss it.
    /// </para>
    /// <para>
    /// <b>It never throws, and it never leaves a half-open menu behind.</b> A click arrives as a
    /// window message, so an exception escaping this method would not be a report - it would be a
    /// crash in a window procedure. Any failure - no anchor at all, a refused foreground call, a WPF
    /// refusal to open - therefore tears down whatever was built and is reported through the same
    /// channels the runtime shell failures use: one trace line and the <see cref="TrayError"/> event.
    /// </para>
    /// </remarks>
    private void OpenMenu(ContextMenu menu)
    {
        if (_disposed)
        {
            // Disposal already tore the icon down; an open menu here would outlive its owner.
            return;
        }

        if (menu.IsOpen)
        {
            // The menu is open somewhere this instance does not own it: another TrayIcon was given
            // the same instance, or the consumer opened it itself. A WPF ContextMenu can only be
            // open in one place at a time, so adopting it - reassigning its placement target to this
            // icon's anchor - would move a live popup away from the owner that can still tear it
            // down, and leave that owner holding a window this instance is about to destroy.
            ReportMenuFailure(
                win32ErrorCode: 0,
                "The assigned ContextMenu is already open, so this TrayIcon cannot adopt it. "
                + "Use one ContextMenu instance per TrayIcon; the menu that is showing is left untouched.");
            return;
        }

        TrayMenuAnchorWindow? anchor = null;

        try
        {
            NativeRect iconRect = ResolveIconRectangle();
            MonitorInfoProvider monitorInfo = _monitorInfo ??= new MonitorInfoProvider();
            NativeRect workArea = ResolveWorkArea(monitorInfo, iconRect);
            uint dpi = ResolveDpi(monitorInfo, iconRect);

            double scale = TrayIconPlacement.ScaleFor(dpi);
            Point offset = TrayIconPlacement.Calculate(iconRect, workArea, dpi);

            // The anchor window sits at the same physical point the offset names. That point is the
            // icon rectangle's bottom-left already clamped into the work area, and the two are kept
            // consistent by construction: the offset is this point divided by the scale, so
            // multiplying it back - which is exactly what the placement engine does - lands on the
            // anchor. Rounding is safe here because the value is a screen coordinate in pixels and
            // the identity above is the definition, not an approximation of it (the offset itself is
            // deliberately left fractional; see TrayIconPlacement).
            int anchorX = (int)Math.Round(offset.X * scale, MidpointRounding.AwayFromZero);
            int anchorY = (int)Math.Round(offset.Y * scale, MidpointRounding.AwayFromZero);

            anchor = new TrayMenuAnchorWindow(anchorX, anchorY);

            // The foreground call is load-bearing rather than etiquette: measured, the identical
            // anchor with this call omitted produces an ownerless popup that an outside click does
            // not close. Its result is therefore surfaced in the trace line instead of assumed.
            bool foreground = anchor.MakeForeground();

            menu.PlacementTarget = anchor.RootVisual;
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = offset.X;
            menu.VerticalOffset = offset.Y;
            menu.Closed += OnContextMenuClosed;

            // The fields are assigned before the menu is opened, so a Closed event raised while
            // IsOpen is being set still finds the state it has to tear down.
            _menuAnchor = anchor;
            _openMenu = menu;

            menu.IsOpen = true;

            // Ownership moved to the fields; the handler must not also dispose it.
            anchor = null;

            NotifyIconTrace.Verbose(string.Create(
                CultureInfo.InvariantCulture,
                $"TrayIcon menu opened: iconRect=({iconRect.left},{iconRect.top},{iconRect.right},{iconRect.bottom}) source={(_lastMenuPlacementUsedCursorFallback ? "cursor" : "shell")} workArea=({workArea.left},{workArea.top},{workArea.right},{workArea.bottom}) dpi={dpi} scale={scale:0.###} offset=({offset.X:0.###},{offset.Y:0.###}) anchor=0x{MenuAnchorHandle.ToInt64():X} foreground={foreground}."));
        }
        catch (Exception ex)
        {
            // Everything, deliberately: this runs on the window-procedure path, where an escaping
            // exception is a process failure rather than a report. Whatever was built is torn down
            // first so the fields and the window list are left clean, and the failure is then
            // reported through the channels a windowless consumer can actually observe.
            TearDownMenu();
            anchor?.Dispose();

            // A TrayIconException already carries the operation, the code and the sentence that
            // explains the failure (the cursor path throws one when it has no anchor at all); anything
            // else is reported with a code of 0, because no Win32 call produced it.
            if (ex is TrayIconException trayException)
            {
                ReportMenuFailure(trayException);
            }
            else
            {
                ReportMenuFailure(
                    win32ErrorCode: 0,
                    $"The context menu could not be opened: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolves the physical rectangle the menu is anchored to.
    /// </summary>
    /// <returns>
    /// The shell's icon rectangle in physical screen pixels, or - when the shell cannot produce one -
    /// a 1x1 rectangle whose bottom-left corner is the cursor position.
    /// </returns>
    /// <exception cref="TrayIconException">
    /// Neither the shell nor the cursor could produce a rectangle, so there is no legal anchor. The
    /// caller reports it; it never reaches a consumer as an exception.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The cursor fallback is the designed answer, not a degradation to hide.</b> A failing
    /// <c>Shell_NotifyIconGetRect</c> is a legitimate, expected outcome - the icon may be in the
    /// notification-area overflow flyout or hidden - and the click's own anchor point is officially
    /// undefined for <c>WM_CONTEXTMENU</c> (D028), so it is never a substitute. A menu that opened at
    /// the cursor is better than a menu that did not open, and both the reason and the fact of the
    /// fallback are recorded (a Verbose line plus <see cref="LastIconRectHresult"/> and
    /// <see cref="LastMenuPlacementUsedCursorFallback"/>) rather than inferred.
    /// </para>
    /// <para>
    /// The fallback rectangle is the 1x1 rectangle whose <em>bottom-left</em> is the cursor, because
    /// that is the corner <see cref="TrayIconPlacement"/> anchors on: handing it the cursor and
    /// nothing else keeps this conversion in one place instead of teaching the calculator a second
    /// input shape.
    /// </para>
    /// </remarks>
    private NativeRect ResolveIconRectangle()
    {
        TrayMessageWindow? host = _host;

        if (_registered && host is not null)
        {
            // The identifier names the icon the way the shell locates it: the registration window
            // and the icon id, with guidItem left null (see NOTIFYICONIDENTIFIER.Create).
            var identifier = NOTIFYICONIDENTIFIER.Create(host.Handle, _iconId);
            int hresult = _shell.ShellNotifyIconGetRect(ref identifier, out NativeRect rectangle);

            _lastIconRectHresult = hresult;

            if (hresult == 0 && !rectangle.IsEmpty)
            {
                _lastMenuPlacementUsedCursorFallback = false;
                return rectangle;
            }

            NotifyIconTrace.Verbose(string.Create(
                CultureInfo.InvariantCulture,
                $"TrayIcon could not read the icon rectangle from the shell (HRESULT 0x{hresult:X8}, rectangle empty={rectangle.IsEmpty}); falling back to the cursor position, because the version-4 right-click anchor is undefined."));
        }
        else
        {
            // Not registered: Shell_NotifyIconGetRect has nothing to locate, so the call is not made
            // at all. Recorded as "not attempted" rather than as a code, because there is no HRESULT.
            _lastIconRectHresult = null;

            NotifyIconTrace.Verbose(
                "TrayIcon is not registered, so the shell has no icon rectangle to report; falling back to the cursor position.");
        }

        _lastMenuPlacementUsedCursorFallback = true;

        if (!_shell.GetCursorPosition(out int cursorX, out int cursorY))
        {
            // No shell rectangle and no cursor: there is no legal anchor left, and opening a menu at
            // the origin is the measured "no visible popup" failure rather than a fallback. Reported
            // by the caller through the trace channel and TrayError, never as an exception here.
            int error = _shell.GetLastError();

            throw new TrayIconException(
                TrayIconException.OperationOpenMenu,
                error,
                "Neither the shell's icon rectangle nor the cursor position could be read, so there is no anchor to place the menu at.");
        }

        return new NativeRect
        {
            left = cursorX,
            top = cursorY - 1,
            right = cursorX + 1,
            bottom = cursorY,
        };
    }

    /// <summary>
    /// Reads the work area of the monitor that owns <paramref name="iconRect"/>.
    /// </summary>
    /// <param name="monitorInfo">The monitor reader to ask.</param>
    /// <param name="iconRect">The icon rectangle whose monitor is wanted.</param>
    /// <returns>
    /// The monitor's work area, or the icon rectangle's own bounding area when the reading fails.
    /// </returns>
    /// <remarks>
    /// The fallback is the icon rectangle itself, which is the smallest area that is guaranteed to
    /// contain the anchor: the anchor is then clamped to the icon's own bottom-left, so a menu still
    /// opens at the icon with a legal offset instead of being placed against a guess about the screen
    /// it is on. Degrading is deliberate - this runs on the click path - and the Verbose line names
    /// the recorded error so a support log can tell a defaulted work area from a real one.
    /// </remarks>
    private static NativeRect ResolveWorkArea(MonitorInfoProvider monitorInfo, NativeRect iconRect)
    {
        if (monitorInfo.TryGetWorkArea(iconRect, out NativeRect workArea))
        {
            return workArea;
        }

        NotifyIconTrace.Verbose(string.Create(
            CultureInfo.InvariantCulture,
            $"TrayIcon could not read the monitor work area (status 0x{monitorInfo.LastError:X8}); using the icon rectangle ({iconRect.left},{iconRect.top},{iconRect.right},{iconRect.bottom}) as the placement boundary."));

        return iconRect;
    }

    /// <summary>
    /// Reads the effective DPI of the monitor that owns <paramref name="iconRect"/>.
    /// </summary>
    /// <param name="monitorInfo">The monitor reader to ask.</param>
    /// <param name="iconRect">The icon rectangle whose monitor is wanted.</param>
    /// <returns>The monitor's effective DPI, or <see cref="TrayIconPlacement.UserDefaultScreenDpi"/> (96) when the reading fails.</returns>
    /// <remarks>
    /// 96 is 100 % - the scale at which the offset equals the physical point - so a missing reading
    /// produces a menu placed as if the display had no scaling rather than no menu at all. The
    /// library never has a second source for this value: <c>GetDpiForWindow</c> would answer for the
    /// anchor window rather than for the monitor the icon is on (D026), and the process's own
    /// awareness is not the library's to set.
    /// </remarks>
    private static uint ResolveDpi(MonitorInfoProvider monitorInfo, NativeRect iconRect)
    {
        if (monitorInfo.TryGetDpi(iconRect, out uint dpi))
        {
            return dpi;
        }

        NotifyIconTrace.Verbose(string.Create(
            CultureInfo.InvariantCulture,
            $"TrayIcon could not read the monitor DPI (status 0x{monitorInfo.LastError:X8}); using USER_DEFAULT_SCREEN_DPI ({TrayIconPlacement.UserDefaultScreenDpi}) for the menu placement."));

        return TrayIconPlacement.UserDefaultScreenDpi;
    }

    /// <summary>
    /// The menu's <c>Closed</c> handler: the teardown that runs whenever the menu stops showing, by
    /// whatever route - an outside click, Escape, an item being chosen, another window taking
    /// activation, or this instance's own disposal.
    /// </summary>
    /// <param name="sender">The menu; unused, because the state lives on this instance.</param>
    /// <param name="e">The event payload; unused.</param>
    private void OnContextMenuClosed(object? sender, RoutedEventArgs e) => TearDownMenu();

    /// <summary>
    /// Releases the open menu's anchor window and clears the menu state. Idempotent, and safe to
    /// call when nothing is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The unsubscribe comes first.</b> Teardown clears the menu's placement target and destroys
    /// the anchor, and it must not be re-entered by a close that its own work happens to raise; the
    /// same reasoning is why the fields are cleared before the anchor is destroyed - a second call
    /// then finds nothing and does nothing, which is what makes <see cref="CloseMenu"/> safe in
    /// disposal and in the failure path of the open itself.
    /// </para>
    /// <para>
    /// <b>The placement target is detached before the anchor is destroyed.</b> The popup is owned by
    /// the anchor window, and a menu still pointing at a destroyed visual is exactly the half-torn
    /// state this method exists to prevent - the order mirrors the anchor's own "release what can be
    /// called, then destroy" discipline.
    /// </para>
    /// </remarks>
    private void TearDownMenu()
    {
        ContextMenu? menu = _openMenu;
        TrayMenuAnchorWindow? anchor = _menuAnchor;

        if (menu is null && anchor is null)
        {
            // Nothing is open: a close that arrived twice, or the first teardown of an instance that
            // never opened a menu. Not traced, so disposal of a menu-less icon stays silent.
            return;
        }

        if (menu is not null)
        {
            menu.Closed -= OnContextMenuClosed;
        }

        _openMenu = null;
        _menuAnchor = null;

        if (menu is not null)
        {
            menu.PlacementTarget = null;
        }

        // Read before disposal: a disposed anchor reports no handle, and the line has to name the
        // window that was destroyed rather than 0x0.
        IntPtr destroyedAnchor = anchor?.Handle ?? IntPtr.Zero;

        anchor?.Dispose();

        NotifyIconTrace.Verbose(string.Create(
            CultureInfo.InvariantCulture,
            $"TrayIcon menu closed; anchor 0x{destroyedAnchor.ToInt64():X} destroyed."));
    }

    /// <summary>
    /// Closes the open menu if it is showing, then tears the menu state down.
    /// </summary>
    /// <remarks>
    /// Closing raises <c>Closed</c>, which runs <see cref="TearDownMenu"/> - so the user-facing route
    /// and the disposal route are the same code. The explicit teardown afterwards is the idempotent
    /// remainder: a menu that was open but whose close raised nothing must still not leave its anchor
    /// window behind.
    /// </remarks>
    private void CloseMenu()
    {
        ContextMenu? menu = _openMenu;

        if (menu is not null && menu.IsOpen)
        {
            menu.IsOpen = false;
        }

        TearDownMenu();
    }

    /// <summary>
    /// Reports a failure of the menu path through the same channels a runtime shell failure uses, and
    /// never throws.
    /// </summary>
    /// <param name="win32ErrorCode">
    /// The Win32 error code behind the failure, or <c>0</c> when no Win32 call failed.
    /// </param>
    /// <param name="detail">The English sentence that says what could not be done.</param>
    /// <remarks>
    /// No retry: the runtime shell policy retries once because the shell occasionally refuses an
    /// update while Explorer is busy, and none of those reasons apply to a menu that could not be
    /// placed. The failure is still reported twice, exactly as the shell failures are - one trace
    /// line for the log and one routed event for the consumer - because a windowless host has no
    /// third channel. <c>Retried</c> is <see langword="false"/>: nothing was retried.
    /// </remarks>
    private void ReportMenuFailure(int win32ErrorCode, string detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(detail);

        ReportMenuFailure(new TrayIconException(TrayIconException.OperationOpenMenu, win32ErrorCode, detail));
    }

    /// <summary>
    /// Reports a menu failure whose exception already describes it, so the code the failing call
    /// reported reaches the consumer unaltered.
    /// </summary>
    /// <param name="exception">
    /// The failure, carrying the operation, the code and the detail sentence.
    /// </param>
    /// <remarks>
    /// The two overloads exist so a failure discovered by the menu path's own code is reported as the
    /// same <see cref="TrayIconException"/> the trace line and the event both name - rebuilding an
    /// equivalent exception instead would drop the error code the call actually produced, which is the
    /// one number a support log needs.
    /// </remarks>
    private void ReportMenuFailure(TrayIconException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        NotifyIconTrace.Error(exception.Operation, exception.Win32ErrorCode, exception, retried: false);
        RaiseEvent(new TrayErrorEventArgs(
            exception.Operation,
            exception.Win32ErrorCode,
            exception,
            retried: false,
            TrayErrorEvent));
    }

    /// <summary>
    /// Selects the Preview and main routed events for a decoded click.
    /// </summary>
    /// <param name="click">The decoded click.</param>
    /// <returns>
    /// The event pair, or <see langword="null"/> when the button and click count do not describe one
    /// of the four click types this class reports.
    /// </returns>
    /// <remarks>
    /// The mapping lives in one expression so the Preview and main halves cannot drift apart: a
    /// missing pair would mean a click that can never be cancelled, or a cancellation that cancels
    /// nothing.
    /// </remarks>
    private static (RoutedEvent Preview, RoutedEvent Main)? SelectClickEvents(TrayMouseEvent click) =>
        (click.Button, click.ClickCount) switch
        {
            (MouseButton.Left, 1) => (PreviewTrayLeftClickEvent, TrayLeftClickEvent),
            (MouseButton.Left, 2) => (PreviewTrayLeftDoubleClickEvent, TrayLeftDoubleClickEvent),
            (MouseButton.Right, 1) => (PreviewTrayRightClickEvent, TrayRightClickEvent),
            (MouseButton.Middle, 1) => (PreviewTrayMiddleClickEvent, TrayMiddleClickEvent),
            _ => null,
        };

    /// <summary>
    /// Writes the one Verbose line that records a callback payload this library raised nothing for.
    /// </summary>
    /// <param name="lParam">The callback payload, as the window procedure received it.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the payload is re-read here.</b> The decoder answers <see langword="null"/> for three
    /// different reasons - a foreign icon id, an unmapped event code and a non-callback message - and
    /// the caller has already ruled out the third. Naming the actual event code and icon id is what
    /// turns this line into the instrument that answers the open question about the real per-click
    /// message sequence, and it is worth the two masked reads: they mirror the layout the decoder
    /// documents (event code in <c>LOWORD</c>, icon id in <c>HIWORD</c>) and add no behaviour of their
    /// own. Do not move click routing logic here - the decoder remains the single authority on what a
    /// payload means.
    /// </para>
    /// <para>
    /// The line is written at Verbose level, so it is invisible until a listener raises the level and
    /// can never be mistaken for a failure report (MEM026).
    /// </para>
    /// </remarks>
    private static void TraceNoMappedClick(IntPtr lParam)
    {
        ulong payload = unchecked((ulong)lParam.ToInt64());

        NotifyIconTrace.Verbose(string.Create(
            CultureInfo.InvariantCulture,
            $"TrayIcon callback carried no mapped click: event code 0x{payload & 0xFFFF:X4}, icon id {(payload >> 16) & 0xFFFF}."));
    }

    /// <summary>
    /// Writes the one Verbose line that records a balloon lifecycle callback this library raised no
    /// public event for.
    /// </summary>
    /// <param name="eventCode">The balloon event code from <c>LOWORD(lParam)</c>.</param>
    /// <param name="iconId">The icon id from <c>HIWORD(lParam)</c>.</param>
    /// <remarks>
    /// <para>
    /// Same shape as <see cref="TraceNoMappedClick"/>: one line naming the event code, the icon id
    /// that sent it and what the phase means - "about to show", "being hidden" or "timed out" - so a
    /// raised-level log can answer why a balloon appeared or vanished without any event. The
    /// classification has already decoded the payload, so the values arrive as parameters instead of
    /// a second masked read of <c>lParam</c>.
    /// </para>
    /// <para>
    /// The line is written at Verbose level and never at Error: a balloon the system suppressed,
    /// coalesced or timed out is ordinary shell behaviour, not a library failure (D008, D033,
    /// MEM026).
    /// </para>
    /// </remarks>
    private static void TraceBalloonLifecycle(uint eventCode, uint iconId)
    {
        string meaning = eventCode switch
        {
            ShellNotifications.NIN_BALLOONSHOW => "about to show",
            ShellNotifications.NIN_BALLOONHIDE => "being hidden",
            ShellNotifications.NIN_BALLOONTIMEOUT => "timed out",
            _ => "unrecognised balloon phase",
        };

        NotifyIconTrace.Verbose(string.Create(
            CultureInfo.InvariantCulture,
            $"TrayIcon balloon callback raised no event: event code 0x{eventCode:X4} ({meaning}), icon id {iconId}."));
    }

    /// <summary>
    /// Creates an icon-data structure for this instance's icon on the given host.
    /// </summary>
    /// <param name="host">The host window the icon is anchored to.</param>
    /// <returns>A structure with the correct <c>cbSize</c>, the host handle and the icon id.</returns>
    /// <remarks>
    /// Every shell call goes through this factory, which is why the tests can assert that
    /// <c>cbSize</c> is the struct size on <em>every</em> recorded call without trusting the call
    /// sites to remember it.
    /// </remarks>
    private NOTIFYICONDATAW CreateIconData(TrayMessageWindow host) => NOTIFYICONDATAW.Create(host.Handle, _iconId);

    /// <summary>
    /// Truncates tooltip text to what <c>szTip</c> can hold.
    /// </summary>
    /// <param name="text">The text to truncate, possibly <see langword="null"/>.</param>
    /// <returns>A non-null string of at most <see cref="MaxToolTipLength"/> UTF-16 code units.</returns>
    /// <remarks>
    /// The cut never lands between a surrogate pair: half a pair would be an invalid string the
    /// shell would render as a replacement character, which is a worse outcome than one character
    /// less of tooltip.
    /// </remarks>
    private static string TruncateToolTipText(string? text)
    {
        return TruncateToFieldCapacity(text, MaxToolTipLength);
    }

    /// <summary>
    /// Truncates text to what a fixed-size shell string field can hold.
    /// </summary>
    /// <param name="text">The text to truncate, possibly <see langword="null"/>.</param>
    /// <param name="maxLength">
    /// The field's capacity in UTF-16 code units, excluding the terminator - 127 for
    /// <c>szTip</c> (128 <c>WCHAR</c>), 255 for <c>szInfo</c> (256) and 63 for <c>szInfoTitle</c>
    /// (64).
    /// </param>
    /// <returns>A non-null string of at most <paramref name="maxLength"/> UTF-16 code units.</returns>
    /// <remarks>
    /// <b>One implementation for every fixed-size shell string.</b> The shell's <c>ByValTStr</c>
    /// fields are copied into inline buffers, and a string longer than the buffer is cut by the
    /// marshaller at an arbitrary point rather than reported - so the cut is made here, where it is
    /// deliberate, and the same rule applies to the tooltip and to both balloon strings.
    /// <para>
    /// The cut never lands between a surrogate pair: half a pair would be an invalid string the
    /// shell would render as a replacement character, which is a worse outcome than one character
    /// less of text.
    /// </para>
    /// </remarks>
    private static string TruncateToFieldCapacity(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        int length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;

        return text[..length];
    }

    /// <summary>
    /// Allocates a process-unique icon id bounded to the 16 bits the shell reports it in.
    /// </summary>
    /// <returns>A non-zero value in <c>1..0xFFFF</c>.</returns>
    /// <remarks>
    /// The counter wraps, so a process that creates 65535 icons starts handing out ids from the
    /// beginning again - acceptable because the shell only ever needs the id to be unique among
    /// the icons that are currently registered, and 0 is skipped because it is indistinguishable
    /// from "no icon id".
    /// </remarks>
    private static uint AllocateIconId()
    {
        while (true)
        {
            uint id = (uint)(Interlocked.Increment(ref s_nextIconId) & 0xFFFF);

            if (id != 0)
            {
                return id;
            }
        }
    }
}
