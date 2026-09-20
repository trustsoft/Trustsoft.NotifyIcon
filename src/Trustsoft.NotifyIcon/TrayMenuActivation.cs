namespace Trustsoft.NotifyIcon;

/// <summary>
/// Decides which notification-area click activates the icon's context menu.
/// </summary>
/// <remarks>
/// <para>
/// This is the decision surface the element reads when a right click arrives. It is a value, not a
/// behaviour: the click events are raised whatever this says, and the element consults it to decide
/// whether it should open the menu in response. That separation is what lets a consumer keep full
/// control - subscribing to the click event and setting the menu up itself - while still being able
/// to say "never open a menu automatically".
/// </para>
/// <para>
/// The member set is deliberately extensible: a future member such as a left-click activation slots
/// in without breaking any existing consumer, and no member's numeric value is part of the public
/// contract except that <see cref="RightClick"/> must stay <c>0</c> so that the default value of
/// the property that carries it never changes meaning.
/// </para>
/// </remarks>
public enum TrayMenuActivation
{
    /// <summary>
    /// A right click opens the icon's context menu. This is the default.
    /// </summary>
    /// <remarks>
    /// The behaviour is owned by the element that consumes this value: the click event still fires
    /// first, and a handler that sets <c>Handled</c> - or a consumer that opens the menu itself -
    /// takes precedence, because the menu is only opened when the click was not handled.
    /// </remarks>
    RightClick = 0,

    /// <summary>
    /// No click opens the context menu; the consumer opens it itself if it wants one.
    /// </summary>
    /// <remarks>
    /// The click events are unaffected - they are raised exactly as before, because they report
    /// what the shell delivered rather than what the library decided to do about it.
    /// </remarks>
    None = 1,
}
