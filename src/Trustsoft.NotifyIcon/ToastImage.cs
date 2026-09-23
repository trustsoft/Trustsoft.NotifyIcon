namespace Trustsoft.NotifyIcon;

/// <summary>
/// The image a toast shows: an already-formed image reference plus the placement and shape hints
/// the shell renders it with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference is finished, not a source.</b> <see cref="Reference"/> is a string the payload
/// builder can put into the image element as-is. It is <c>file:///</c> for a local file - a scheme
/// the toast image schema supports explicitly for desktop applications, which is exactly this
/// library's audience - or an <c>https</c> URL for a remote one. The builder XML-escapes the value
/// so the document stays well-formed, but it never rewrites, normalises or URL-encodes it: a
/// reference is the consumer's statement of where the image is.
/// </para>
/// <para>
/// <b>An <c>ImageSource</c> is not this type's job.</b> Turning a WPF <c>ImageSource</c> into a
/// reference means deciding where the bytes live and what URI reaches them, which is a separate
/// contract (D059) delivered by a later slice. S02 consumes the resolved reference only, and there
/// is deliberately no <c>ImageSource</c> member here.
/// </para>
/// <para>
/// <b>A dumb data shape.</b> No behaviour and no validation: an empty <see cref="Reference"/> or a
/// remote URL the machine cannot reach is not diagnosed here. The notifier decides what to refuse
/// and the shell decides what it can fetch.
/// </para>
/// </remarks>
public sealed class ToastImage
{
    /// <summary>
    /// Gets or sets the image reference exactly as it should reach the shell: a <c>file:///</c>
    /// URI for a local file or an <c>https</c> URL. Defaults to <see cref="string.Empty"/>.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets where the image is placed in the toast. Defaults to
    /// <see cref="ToastImagePlacement.AppLogoOverride"/>.
    /// </summary>
    public ToastImagePlacement Placement { get; set; } = ToastImagePlacement.AppLogoOverride;

    /// <summary>
    /// Gets or sets a value indicating whether the image is cropped to a circle. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A rendering hint only (<c>hint-crop="circle"</c>): the shell may ignore it, and it changes
    /// nothing about where the image comes from or how large it is drawn.
    /// </remarks>
    public bool CircleCrop { get; set; }
}
