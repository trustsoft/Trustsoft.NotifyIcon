namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The notification-area event codes the shell reports through the host window's
/// <see cref="ShellConstants.TrayCallbackMessage"/> message.
/// </summary>
/// <remarks>
/// <para>
/// Ground truth is <c>shellapi.h</c> (the <c>NIN_*</c> family), not the rendered documentation.
/// </para>
/// <para>
/// <b>These constants are defined here ahead of their consumers on purpose.</b> They are
/// required by S02 (click routed events) and S04 (balloon notifications), and defining the
/// entire <c>NIN_*</c> family in one place during S01 means those slices never have to reopen
/// the interop layer just to add a constant. Members that no code consumes yet are marked with
/// a <c>consumed by S0x</c> comment; the values themselves are all from the header.
/// </para>
/// <para>
/// Under <see cref="ShellConstants.NOTIFYICON_VERSION_4"/> the shell reports events with
/// <c>LOWORD(lParam)</c> set to one of these codes and <c>HIWORD(lParam)</c> set to the 16-bit
/// icon id; the anchor coordinates arrive in <c>wParam</c>.
/// </para>
/// </remarks>
internal static class ShellNotifications
{
    /// <summary>
    /// The icon was selected (left click or the notification-area equivalent).
    /// <c>NIN_SELECT</c> == <c>WM_USER + 0</c>. Consumed by S02.
    /// </summary>
    internal const uint NIN_SELECT = ShellConstants.WM_USER + 0;

    /// <summary>
    /// The bit that turns <see cref="NIN_SELECT"/> into <see cref="NIN_KEYSELECT"/>.
    /// <c>NINF_KEY</c>. Consumed by S02.
    /// </summary>
    internal const uint NINF_KEY = 0x00000001;

    /// <summary>
    /// The icon was selected with the keyboard. <c>NIN_KEYSELECT</c> ==
    /// <c>NIN_SELECT | NINF_KEY</c> (the header defines it by OR-ing, it is not a literal).
    /// Consumed by S02.
    /// </summary>
    internal const uint NIN_KEYSELECT = NIN_SELECT | NINF_KEY;

    /// <summary>A balloon is about to be shown. <c>NIN_BALLOONSHOW</c> == <c>WM_USER + 2</c>. Consumed by S04.</summary>
    internal const uint NIN_BALLOONSHOW = ShellConstants.WM_USER + 2;

    /// <summary>A balloon is being hidden. <c>NIN_BALLOONHIDE</c> == <c>WM_USER + 3</c>. Consumed by S04.</summary>
    internal const uint NIN_BALLOONHIDE = ShellConstants.WM_USER + 3;

    /// <summary>A balloon timed out. <c>NIN_BALLOONTIMEOUT</c> == <c>WM_USER + 4</c>. Consumed by S04.</summary>
    internal const uint NIN_BALLOONTIMEOUT = ShellConstants.WM_USER + 4;

    /// <summary>The user clicked a balloon. <c>NIN_BALLOONUSERCLICK</c> == <c>WM_USER + 5</c>. Consumed by S04.</summary>
    internal const uint NIN_BALLOONUSERCLICK = ShellConstants.WM_USER + 5;

    /// <summary>A popup is about to open. <c>NIN_POPUPOPEN</c> == <c>WM_USER + 6</c>. Consumed by S04.</summary>
    internal const uint NIN_POPUPOPEN = ShellConstants.WM_USER + 6;

    /// <summary>A popup is about to close. <c>NIN_POPUPCLOSE</c> == <c>WM_USER + 7</c>. Consumed by S04.</summary>
    internal const uint NIN_POPUPCLOSE = ShellConstants.WM_USER + 7;

    /// <summary>
    /// Packs an icon id into the v4 low-word event value that marks a keyboard selection.
    /// </summary>
    /// <param name="id">The 16-bit icon id from <c>HIWORD(lParam)</c>.</param>
    /// <returns>The event value the shell reports for a keyboard-driven selection.</returns>
    /// <remarks>
    /// <b>Not a header symbol.</b> <c>shellapi.h</c> defines <c>NIN_KEYSELECT</c> as
    /// <c>NIN_SELECT | NINF_KEY</c> and leaves the id packing to the consumer, so this helper
    /// exists only so the packing expression lives next to the constants it is built from
    /// instead of being re-derived in S02. Because <see cref="NIN_SELECT"/> is
    /// <c>0x0400</c>, only ids whose low word does not overlap <c>0x0400</c> can be represented
    /// this way - which is exactly why a library-chosen icon id must stay small.
    /// </remarks>
    internal static uint WithKeySelect(uint id) => NIN_KEYSELECT | id;
}
