namespace Trustsoft.NotifyIcon;

/// <summary>
/// Carries the reason Windows reported for a dismissal, for <see cref="ToastNotifier.Dismissed"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every dismissal reports a reason, and the vocabulary is total.</b> The raw value the shell
/// delivers is mapped onto <see cref="ToastDismissalReason"/> such that a known value becomes its
/// named member and anything else - including the seam's unreadable-payload sentinel - becomes
/// <see cref="ToastDismissalReason.Unknown"/>. A consumer therefore never has to handle an
/// out-of-range enum value and never has an unknown reason silently presented as
/// <see cref="ToastDismissalReason.UserCanceled"/>.
/// </para>
/// <para>
/// <b>A dismissal is not a click and carries no argument.</b> The dismissal callback delivers only
/// the reason; which toast was dismissed is again identified by what was shown, not by anything in
/// this payload (the same limitation <see cref="ToastActivatedEventArgs"/> documents).
/// </para>
/// </remarks>
public sealed class ToastDismissedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance carrying the reported dismissal reason.
    /// </summary>
    /// <param name="reason">The reason the shell reported, already mapped to this library's vocabulary.</param>
    public ToastDismissedEventArgs(ToastDismissalReason reason) => Reason = reason;

    /// <summary>
    /// Gets the reason the shell reported for the dismissal.
    /// </summary>
    /// <value>
    /// One of the <see cref="ToastDismissalReason"/> members;
    /// <see cref="ToastDismissalReason.Unknown"/> when the reason could not be read or was not one
    /// of the three known values.
    /// </value>
    public ToastDismissalReason Reason { get; }
}
