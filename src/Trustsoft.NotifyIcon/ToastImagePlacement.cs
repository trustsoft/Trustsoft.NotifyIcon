namespace Trustsoft.NotifyIcon;

/// <summary>
/// Where an image sits in a toast: the native <c>placement</c> attribute of the toast image
/// element.
/// </summary>
/// <remarks>
/// <para>
/// The schema defines exactly these two placements for a <c>ToastGeneric</c> image -
/// <c>appLogoOverride</c> and <c>hero</c> - and the payload builder always writes the attribute,
/// so a placement is never left for the shell to guess. The names here are the schema's own
/// spellings in camel case; the builder is what turns a member into the attribute text, so the
/// identifier a consumer types and the string the shell reads stay recognisably the same thing.
/// </para>
/// <para>
/// <b>Placement is chosen by the consumer, not inferred from the image.</b> The same picture can
/// legitimately be a small corner logo or a full-width hero, and nothing about the file says
/// which; the default is <see cref="AppLogoOverride"/> because replacing the application logo is
/// the common case.
/// </para>
/// </remarks>
public enum ToastImagePlacement
{
    /// <summary>
    /// The image replaces the application logo at the toast's top-left
    /// (<c>placement="appLogoOverride"</c>). This is the default value of
    /// <see cref="ToastImage.Placement"/>.
    /// </summary>
    AppLogoOverride = 0,

    /// <summary>
    /// The image is displayed as a full-width hero image across the top of the toast
    /// (<c>placement="hero"</c>).
    /// </summary>
    Hero = 1,
}
