namespace Trustsoft.NotifyIcon;

/// <summary>
/// Why Windows dismissed a toast: the public vocabulary for the native
/// <c>Windows.UI.Notifications.ToastDismissalReason</c> value the shell delivers to the dismissal
/// callback of a shown notification.
/// </summary>
/// <remarks>
/// <para>
/// <b>The numbers are the platform's, not this library's.</b> Unlike <see cref="ToastSeverity"/>,
/// whose members are the library's own ordinals rendered as names into the toast document, the
/// three known members here are the exact values of the WinRT
/// <c>ToastDismissalReason</c> enum, because the value travels the other way: the shell sends it and
/// the notifier publishes it. Renumbering them would silently misreport a dismissal, so the
/// ordinals are part of the contract and are pinned by a test.
/// </para>
/// <para>
/// <b><see cref="Unknown"/> is the seam's sentinel, not a WinRT member.</b> <c>-1</c> is what the
/// internal read of the callback payload returns when it cannot read a reason at all (the WinRT
/// value is not readable), and the notifier maps every value outside the three known ones onto
/// <see cref="Unknown"/> as well. A consumer therefore always gets a total answer, and an
/// unrecognised value is reported as unrecognised rather than guessed at - it is never folded into
/// <see cref="UserCanceled"/>, because claiming the user dismissed something they did not touch is
/// worse than admitting the reason is unknown.
/// </para>
/// </remarks>
public enum ToastDismissalReason
{
    /// <summary>
    /// The reason could not be determined: the callback payload carried a reason the seam could not
    /// read, or a value outside the three known members.
    /// </summary>
    /// <remarks>
    /// This member has no WinRT counterpart - it is the seam's read-failure sentinel
    /// (<c>-1</c>), produced when the payload's reason cannot be read, and it is also the total
    /// mapping's fallback for any value that is not <see cref="UserCanceled"/>,
    /// <see cref="ApplicationHidden"/> or <see cref="TimedOut"/>. Treat it as "unknown", never as
    /// "the user dismissed it".
    /// </remarks>
    Unknown = -1,

    /// <summary>
    /// The user dismissed the toast: they activated its close button or dismissed the banner
    /// directly (<c>ToastDismissalReason.UserCanceled</c>, <c>0</c>).
    /// </summary>
    UserCanceled = 0,

    /// <summary>
    /// The application itself removed the toast from the screen, for example by clearing the
    /// notification from the action center (<c>ToastDismissalReason.ApplicationHidden</c>,
    /// <c>1</c>).
    /// </summary>
    ApplicationHidden = 1,

    /// <summary>
    /// The toast ran out of time and the shell took it down on its own
    /// (<c>ToastDismissalReason.TimedOut</c>, <c>2</c>).
    /// </summary>
    /// <remarks>
    /// This is the shell's own expiry, not the optional <see cref="ToastContent.Expiry"/> the caller
    /// may have set; an expiring toast reports this reason whether or not an explicit expiry was
    /// requested.
    /// </remarks>
    TimedOut = 2,
}
