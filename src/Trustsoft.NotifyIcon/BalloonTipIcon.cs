namespace Trustsoft.NotifyIcon;

/// <summary>
/// The severity icon a balloon notification is shown with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The values are the shell's own values.</b> They are exactly the <c>NIIF_NONE</c>,
/// <c>NIIF_INFO</c>, <c>NIIF_WARNING</c> and <c>NIIF_ERROR</c> constants from <c>shellapi.h</c>
/// (<c>0</c> through <c>3</c>), so writing the severity into the shell's <c>dwInfoFlags</c> member
/// is a cast and not a translation table: there is no mapping layer here that could drift out of
/// step with the header.
/// </para>
/// <para>
/// <b>The severity lives in the low nibble.</b> The header masks the severity out of
/// <c>dwInfoFlags</c> with <c>NIIF_ICON_MASK</c> (<c>0x000F</c>), which selects exactly these four
/// values - the icon flags are "mutually exclusive and take only the lowest 2 bits". The other
/// <c>NIIF_*</c> members (silent, large icon, quiet time) are single bits outside that nibble and
/// are therefore combined with a severity rather than replacing one; see
/// <see cref="BalloonTipOptions"/>.
/// </para>
/// <para>
/// <b>An empty balloon title makes the shell omit the severity icon.</b> That is a shell
/// behaviour, not a library choice: the shell draws the severity icon only alongside a non-empty
/// title, so <see cref="TrayIcon.ShowBalloonTip"/> with a <see langword="null"/> or empty title
/// produces a title-less balloon with no icon regardless of the value passed here. The library does
/// not invent a title to work around it and does not refuse the call.
/// </para>
/// </remarks>
public enum BalloonTipIcon
{
    /// <summary>
    /// No severity icon: the balloon is shown as plain text. <c>NIIF_NONE</c>.
    /// </summary>
    None = 0,

    /// <summary>
    /// The information icon. <c>NIIF_INFO</c>. The default of
    /// <see cref="TrayIcon.ShowBalloonTip"/>.
    /// </summary>
    Info = 1,

    /// <summary>
    /// The warning icon. <c>NIIF_WARNING</c>.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// The error icon. <c>NIIF_ERROR</c>.
    /// </summary>
    Error = 3,
}
