namespace Trustsoft.NotifyIcon;

/// <summary>
/// Carries the launch or button argument Windows delivered when a toast was activated, for
/// <see cref="ToastNotifier.Activated"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The argument is delivered verbatim and is the only identifier.</b> <see cref="Arguments"/> is
/// the string the shell handed back for the activation - the toast's
/// <see cref="ToastContent.Launch"/> value for a click on the toast body, or the
/// <see cref="ToastButton.Arguments"/> of the button that was pressed - exactly as it was sent,
/// with no trimming, unescaping or normalisation by this library. Because one notifier can have
/// several toasts live at once and the platform delivers no per-show event object, the argument
/// string is the only thing that tells an activation apart: a consumer that needs to know which of
/// its toasts was activated must make its launch and button arguments unique.
/// </para>
/// <para>
/// <b>A null argument means the toast carried none.</b> Windows permits a toast with no launch
/// argument, and a body click on such a toast delivers <see langword="null"/> here. It is a
/// legitimate delivery, not a failure: "no argument" and "the argument was the empty string" are
/// different facts and are reported as different values.
/// </para>
/// <para>
/// <b>An activation is not a click report.</b> Per the wording rule this slice records, <c>Activated</c>
/// reports that the toast's launch or button argument arrived; it is not a report that the user
/// clicked the body. The measured limitations behind that rule - an activation can arrive with no
/// instrumented click at all, and a specific activation cannot be credited to a specific click -
/// are recorded in <c>docs/TOAST-MEASUREMENT.md</c>.
/// </para>
/// </remarks>
public sealed class ToastActivatedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance carrying the delivered activation argument.
    /// </summary>
    /// <param name="arguments">
    /// The launch or button argument Windows delivered, or <see langword="null"/> when the toast
    /// carried none.
    /// </param>
    public ToastActivatedEventArgs(string? arguments) => Arguments = arguments;

    /// <summary>
    /// Gets the launch or button argument Windows delivered, verbatim.
    /// </summary>
    /// <value>
    /// The exact string the activation carried - the toast's launch argument for a body click, or
    /// the pressed button's argument - or <see langword="null"/> when the toast carried none.
    /// </value>
    public string? Arguments { get; }
}
