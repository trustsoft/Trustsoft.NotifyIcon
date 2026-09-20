using System.Windows;
using System.Windows.Input;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// Carries the details of a notification-area click decoded from the shell's
/// <c>NOTIFYICON_VERSION_4</c> callback payload: which mouse button the shell reported, how many
/// clicks it reported, and the anchor point it sent.
/// </summary>
/// <remarks>
/// <para>
/// The type is a real <see cref="RoutedEventArgs"/> rather than a small payload record because a
/// click is delivered as a routed event: the args travel with the event to the handlers that
/// subscribed from code or from markup, and those handlers are the API. Everything a handler needs
/// beyond "the click happened" is exactly these three values - the button, the click count and
/// where the click was - so a consumer never has to decode a raw window message to find out what
/// the user did.
/// </para>
/// <para>
/// The args are stamped with the routed event they belong to. The library raises the event with
/// the <see cref="TrayIconClickEventArgs(MouseButton, int, Point, RoutedEvent)"/> overload; the
/// overload without a <see cref="RoutedEvent"/> exists for callers that supply it themselves (WPF
/// requires <see cref="RoutedEventArgs.RoutedEvent"/> to be non-null before <c>RaiseEvent</c> can
/// route the args).
/// </para>
/// <para>
/// <see cref="MouseButtonEventArgs"/> was considered and rejected as a base type: its
/// <c>GetPosition</c> resolves through the live mouse device, which for a click reported by the
/// notification area is the wrong answer at exactly the moment a consumer asks for it. A tray
/// click is a shell notification, not WPF mouse input, so the payload is modelled as such.
/// </para>
/// </remarks>
public sealed class TrayIconClickEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance whose routed event is supplied by the raiser.
    /// </summary>
    /// <param name="button">The mouse button the shell reported.</param>
    /// <param name="clickCount">The number of clicks: <c>1</c>, or <c>2</c> for a double click.</param>
    /// <param name="screenAnchor">The anchor point the shell reported, in screen device pixels.</param>
    public TrayIconClickEventArgs(MouseButton button, int clickCount, Point screenAnchor)
        : base()
    {
        // No routed event here: RoutedEventArgs' parameterless constructor leaves the event null,
        // and the raiser (or the bound overload below) supplies it. WPF requires a non-null
        // RoutedEvent before RaiseEvent, so an instance built this way must be stamped first.
        Button = button;
        ClickCount = clickCount;
        ScreenAnchor = screenAnchor;
    }

    /// <summary>
    /// Initializes a new instance bound to the routed event being raised. This is the overload the
    /// library uses, because <c>RaiseEvent</c> requires the args to know their event.
    /// </summary>
    /// <param name="button">The mouse button the shell reported.</param>
    /// <param name="clickCount">The number of clicks: <c>1</c>, or <c>2</c> for a double click.</param>
    /// <param name="screenAnchor">The anchor point the shell reported, in screen device pixels.</param>
    /// <param name="routedEvent">The routed event these arguments are being raised for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="routedEvent"/> is null.</exception>
    public TrayIconClickEventArgs(MouseButton button, int clickCount, Point screenAnchor, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        // RoutedEventArgs' own constructor accepts a null event (verified empirically on
        // net8.0-windows for TrayErrorEventArgs: base(null) does not throw), which would produce
        // args that RaiseEvent cannot route. Rejecting it here turns a confusing failure at raise
        // time into a clear one at construction.
        ArgumentNullException.ThrowIfNull(routedEvent);

        Button = button;
        ClickCount = clickCount;
        ScreenAnchor = screenAnchor;
    }

    /// <summary>
    /// Gets the mouse button the shell reported for the click.
    /// </summary>
    /// <value>
    /// <see cref="MouseButton.Left"/>, <see cref="MouseButton.Right"/> or
    /// <see cref="MouseButton.Middle"/> - the three the notification area reports as clicks. The
    /// type is WPF's own button enum on purpose: it is the right vocabulary for a button and
    /// carrying it adds no dependency the library does not already have.
    /// </value>
    public MouseButton Button { get; }

    /// <summary>
    /// Gets the number of clicks the shell reported.
    /// </summary>
    /// <value>
    /// <c>1</c> for a single click; <c>2</c> for a double click. The count is carried rather than
    /// implied by which routed event fired, so a consumer that handles both left-click events from
    /// one handler can still tell a double click from a single one.
    /// </value>
    public int ClickCount { get; }

    /// <summary>
    /// Gets the screen point the shell reported for the click, in screen device pixels.
    /// </summary>
    /// <value>
    /// The anchor point exactly as the shell sent it, with <see cref="Point.X"/> negative on a
    /// monitor left of the primary one. It is <b>informational</b>.
    /// </value>
    /// <remarks>
    /// <para>
    /// The value is the <c>NOTIFYICON_VERSION_4</c> anchor point (<c>GET_X_LPARAM(wParam)</c> /
    /// <c>GET_Y_LPARAM(wParam)</c>) decoded from the callback payload. It stays in device pixels
    /// because that is what the shell reports; converting to WPF device-independent units is a
    /// presentation decision that depends on the DPI of the monitor involved, which belongs to the
    /// consumer rather than to this payload.
    /// </para>
    /// <para>
    /// <b>Do not depend on it for right clicks.</b> Microsoft documents the anchor as valid only
    /// for <c>NIN_POPUPOPEN</c>, <c>NIN_SELECT</c>, <c>NIN_KEYSELECT</c> and the mouse messages
    /// between <c>WM_MOUSEFIRST</c> and <c>WM_MOUSELAST</c>, and the right-click notification
    /// (<c>WM_CONTEXTMENU</c>) is <em>outside</em> that set - so for a right click the official
    /// status of this value is undefined. It is carried because the shell does send it and because
    /// it is the only location information the payload itself contains, but menu placement must
    /// start from <c>Shell_NotifyIconGetRect</c>, never from this property.
    /// </para>
    /// </remarks>
    public Point ScreenAnchor { get; }

    /// <summary>
    /// Dispatches the handler with the strongly typed argument, instead of the reflection-based
    /// fallback WPF uses for event args it does not know.
    /// </summary>
    /// <param name="genericHandler">The handler registered for the routed event.</param>
    /// <param name="genericTarget">The element the event is being raised on.</param>
    /// <remarks>
    /// Every routed event that carries typed args overrides this method; the cast is safe because
    /// the click routed events are registered with
    /// <c>EventHandler&lt;TrayIconClickEventArgs&gt;</c>. Skipping the override would still work -
    /// the base implementation reaches the handler through reflection - but it would pay that cost
    /// on every click and would hide the handler-type contract from the compiler.
    /// </remarks>
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ((EventHandler<TrayIconClickEventArgs>)genericHandler)(genericTarget, this);
    }
}
