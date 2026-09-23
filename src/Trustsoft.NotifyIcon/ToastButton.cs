namespace Trustsoft.NotifyIcon;

/// <summary>
/// One action button on a toast: the label the user sees and the argument Windows reports when
/// that button is activated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two strings, and both matter for different reasons.</b> <see cref="Text"/> is what the shell
/// renders on the button, so it is the consumer's wording. <see cref="Arguments"/> is not shown at
/// all: it is the deterministic identifier the shell hands back when the button is activated, the
/// same channel <see cref="ToastContent.Launch"/> uses for a body click. A toast with three
/// buttons therefore needs three distinct arguments for the consumer to be able to tell them
/// apart.
/// </para>
/// <para>
/// <b>A dumb data shape.</b> Like the rest of the content model this type carries no behaviour and
/// no validation; whether an empty label or a duplicated argument is an error is decided by the
/// notifier that shows the toast, not here.
/// </para>
/// <para>
/// <b>No activation type in v1.</b> The toast schema can route a button to a background task
/// (<c>activationType="background"</c>); D054 scopes v1 to activation in the running application,
/// which is the schema's foreground default, so the payload builder writes no activation type
/// attribute and this type exposes none. Adding one later is additive.
/// </para>
/// </remarks>
public sealed class ToastButton
{
    /// <summary>
    /// Gets or sets the button's visible label. Defaults to
    /// <see cref="string.Empty"/>; the shell renders an empty label as a blank button, which is a
    /// caller error the library does not silently repair.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the argument the shell reports when this button is activated. Defaults to
    /// <see cref="string.Empty"/>.
    /// </summary>
    /// <remarks>
    /// This is the button's identity on the activation callback, not a command line: the library
    /// passes it through unchanged, and a consumer who gives two buttons the same argument has
    /// given itself two indistinguishable buttons.
    /// </remarks>
    public string Arguments { get; set; } = string.Empty;
}
