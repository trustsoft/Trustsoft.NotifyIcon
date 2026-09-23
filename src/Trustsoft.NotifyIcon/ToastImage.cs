using System.Windows.Media;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The image a toast shows: either a WPF <see cref="Source"/> the library persists for the shell,
/// or an already-formed <see cref="Reference"/>, plus the placement and shape hints the shell
/// renders the image with.
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
/// <b>An <c>ImageSource</c> is now the other half of the same contract (D059).</b> The shell needs
/// a file or URI rather than an in-memory image, so a source set here is persisted as a PNG in the
/// library's own temp folder and handed to the shell as an absolute <c>file:///</c> reference;
/// <see cref="Source"/> and <see cref="Reference"/> are therefore two spellings of the same image,
/// and <see cref="Reference"/> is still never rewritten by the payload builder.
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
    /// <remarks>
    /// Ignored while <see cref="Source"/> is set: a typed source is more specific than a
    /// pre-formed string, so a consumer can keep a fallback reference in place while switching to
    /// the typed member. The notifier traces the ignored reference at Verbose level when both are
    /// set, so the choice is visible in a capture rather than silent.
    /// </remarks>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the WPF image to show, which the library persists as a PNG in its own temp
    /// folder and hands to the shell as an absolute <c>file:///</c> reference. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>When <see cref="Source"/> is set it wins and <see cref="Reference"/> is ignored.</b> A
    /// typed source is more specific than a pre-formed string, and a consumer must be able to keep
    /// a fallback reference without clearing this member; the notifier traces the ignored reference
    /// at Verbose level so the precedence is observable.
    /// </para>
    /// <para>
    /// <b>Nothing here is mutated and no reference is written back.</b> Resolving the source writes
    /// a fresh PNG per show and leaves <see cref="Reference"/> untouched, so the same content
    /// object can be shown more than once and constructing or filling one touches nothing outside
    /// the process.
    /// </para>
    /// <para>
    /// <b>A conversion failure is named, not raw.</b> A source that cannot be read, encoded or
    /// written fails the show with <see cref="ToastException"/> carrying
    /// <see cref="ToastException.OperationImageResolution"/> - never a <see cref="TrayIconException"/>
    /// and never a raw WPF exception - and leaves no file behind.
    /// </para>
    /// </remarks>
    public ImageSource? Source { get; set; }

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
