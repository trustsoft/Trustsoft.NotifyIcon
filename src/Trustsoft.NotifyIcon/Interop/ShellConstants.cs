namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// Shell_NotifyIcon message codes, notification-area flags and the host-window message ids
/// used by the tray icon implementation.
/// </summary>
/// <remarks>
/// <para>
/// Ground truth for every numeric value in this file is the Windows SDK header on the build
/// machine (<c>Windows Kits\10\Include\10.0.26100.0\um\shellapi.h</c> for the
/// <c>NIM_*</c>/<c>NIF_*</c>/<c>NIS_*</c>/<c>NIN_*</c> families and <c>um\winuser.h</c> for the
/// window messages), <b>not</b> the rendered documentation. The documentation is known to be
/// wrong for this API surface (it labels <c>szTip</c> as <c>CHAR[64]</c> while the header
/// declares <c>WCHAR szTip[128]</c>), so anything numeric is copied from the header.
/// </para>
/// <para>
/// Values are typed <see cref="uint"/> because that is how the shell API receives them:
/// <c>Shell_NotifyIcon(DWORD dwMessage, ...)</c> and the <c>uFlags</c>/<c>dwState</c> members
/// of <see cref="NOTIFYICONDATAW"/> are all <c>DWORD</c>/<c>UINT</c>.
/// </para>
/// </remarks>
internal static class ShellConstants
{
    // ---------------------------------------------------------------------------------------
    // Shell_NotifyIcon message codes (shellapi.h, NIM_*) - passed as dwMessage.
    // ---------------------------------------------------------------------------------------

    /// <summary>Adds an icon to the notification area. <c>NIM_ADD</c>.</summary>
    internal const uint NIM_ADD = 0x00000000;

    /// <summary>Modifies an icon already in the notification area. <c>NIM_MODIFY</c>.</summary>
    internal const uint NIM_MODIFY = 0x00000001;

    /// <summary>Removes an icon from the notification area. <c>NIM_DELETE</c>.</summary>
    internal const uint NIM_DELETE = 0x00000002;

    /// <summary>Sets the keyboard focus to the icon. <c>NIM_SETFOCUS</c>.</summary>
    internal const uint NIM_SETFOCUS = 0x00000003;

    /// <summary>
    /// Sets the notification-area protocol version from
    /// <see cref="NOTIFYICONDATAW.uTimeoutOrVersion"/>. <c>NIM_SETVERSION</c>.
    /// </summary>
    internal const uint NIM_SETVERSION = 0x00000004;

    // ---------------------------------------------------------------------------------------
    // Notification-area flags (shellapi.h, NIF_*) - written into NOTIFYICONDATAW.uFlags.
    // Which members of the struct the shell reads is decided by this bit set.
    // ---------------------------------------------------------------------------------------

    /// <summary>The <c>uCallbackMessage</c> member is valid. <c>NIF_MESSAGE</c>.</summary>
    internal const uint NIF_MESSAGE = 0x00000001;

    /// <summary>The <c>hIcon</c> member is valid. <c>NIF_ICON</c>.</summary>
    internal const uint NIF_ICON = 0x00000002;

    /// <summary>The <c>szTip</c> member is valid. <c>NIF_TIP</c>.</summary>
    internal const uint NIF_TIP = 0x00000004;

    /// <summary>The <c>dwState</c> and <c>dwStateMask</c> members are valid. <c>NIF_STATE</c>.</summary>
    internal const uint NIF_STATE = 0x00000008;

    /// <summary>The balloon members (<c>szInfo</c>, <c>szInfoTitle</c>, ...) are valid. <c>NIF_INFO</c>.</summary>
    internal const uint NIF_INFO = 0x00000010;

    /// <summary>The <c>guidItem</c> member identifies the icon. <c>NIF_GUID</c>.</summary>
    internal const uint NIF_GUID = 0x00000020;

    /// <summary>The balloon is shown immediately, ignoring quiet time. <c>NIF_REALTIME</c>.</summary>
    internal const uint NIF_REALTIME = 0x00000040;

    /// <summary>
    /// Shows the standard tooltip. <c>NIF_SHOWTIP</c>.
    /// </summary>
    /// <remarks>
    /// Required under <see cref="NOTIFYICON_VERSION_4"/>: that version suppresses the standard
    /// tooltip, so without this flag <c>ToolTipText</c> is silently non-functional - the icon
    /// appears and no error is reported. The minimal add flag set therefore includes it.
    /// </remarks>
    internal const uint NIF_SHOWTIP = 0x00000080;

    // ---------------------------------------------------------------------------------------
    // Balloon icon flags (shellapi.h, NIIF_*) - written into NOTIFYICONDATAW.dwInfoFlags.
    // This family is not a bit set like NIF_* above: the severity is a small ordinal in the
    // low nibble, and the header states the icon flags "are mutually exclusive and take only the
    // lowest 2 bits". Only the sound, large-icon and quiet-time members are plain single bits.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Shows the balloon with no severity icon. <c>NIIF_NONE</c>. One of the four severity values
    /// selected by <see cref="NIIF_ICON_MASK"/>.
    /// </summary>
    internal const uint NIIF_NONE = 0x00000000;

    /// <summary>
    /// Shows the balloon with the information icon. <c>NIIF_INFO</c>. One of the four severity
    /// values selected by <see cref="NIIF_ICON_MASK"/>.
    /// </summary>
    internal const uint NIIF_INFO = 0x00000001;

    /// <summary>
    /// Shows the balloon with the warning icon. <c>NIIF_WARNING</c>. One of the four severity
    /// values selected by <see cref="NIIF_ICON_MASK"/>.
    /// </summary>
    internal const uint NIIF_WARNING = 0x00000002;

    /// <summary>
    /// Shows the balloon with the error icon. <c>NIIF_ERROR</c>. One of the four severity values
    /// selected by <see cref="NIIF_ICON_MASK"/>.
    /// </summary>
    internal const uint NIIF_ERROR = 0x00000003;

    /// <summary>
    /// Uses the caller's own icon from <see cref="NOTIFYICONDATAW.hBalloonIcon"/> instead of one of
    /// the four built-in severities. <c>NIIF_USER</c>.
    /// </summary>
    /// <remarks>
    /// <b>Not a severity.</b> Its value sits above <see cref="NIIF_ERROR"/> rather than inside the
    /// severity ordinal, and its low two bits are <see cref="NIIF_NONE"/> - so reading it as a
    /// severity would report "no icon" while asking for a custom one. It is declared here because
    /// the value is part of the family and a reader comparing <c>dwInfoFlags</c> against the header
    /// needs it; the library does not use it in v1 (a custom balloon icon is out of M001's scope,
    /// so <see cref="NOTIFYICONDATAW.hBalloonIcon"/> stays zero).
    /// </remarks>
    internal const uint NIIF_USER = 0x00000004;

    /// <summary>
    /// The mask that selects the balloon's severity out of
    /// <see cref="NOTIFYICONDATAW.dwInfoFlags"/>. <c>NIIF_ICON_MASK</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The severity is not a bit set.</b> Unlike every member of the <c>NIF_*</c> family above
    /// and the other <c>NIIF_*</c> members below, the four severity values
    /// (<see cref="NIIF_NONE"/>..<see cref="NIIF_ERROR"/>) are a small ordinal: <c>dwInfoFlags &amp;
    /// NIIF_ICON_MASK</c> <em>is</em> the severity, so the values are compared rather than tested
    /// for bits. The header adds that the icon flags "are mutually exclusive and take only the
    /// lowest 2 bits" - the four severities are exactly <c>0..3</c>, which is why
    /// <see cref="NIIF_USER"/> (whose low two bits are zero) is a separate selector and not a
    /// fifth severity.
    /// </para>
    /// <para>
    /// <b>Realtime is not a member of this family, and not this field.</b>
    /// <see cref="NIF_REALTIME"/> is a <c>NIF_*</c> bit: it belongs in
    /// <see cref="NOTIFYICONDATAW.uFlags"/>, never in <see cref="NOTIFYICONDATAW.dwInfoFlags"/>. The
    /// two fields do legitimately share bit numbers - <see cref="NIIF_NOSOUND"/> and
    /// <see cref="NIF_INFO"/> are both <c>0x10</c> in their respective fields - so a constant that
    /// is correct in one field is silently meaningless in the other, which is exactly how a
    /// misplaced realtime request becomes invisible. There is deliberately no <c>NIIF_*</c> member
    /// at <c>NIF_REALTIME</c>'s value, and the placement rule is asserted by
    /// <c>ShellConstantsTests</c>.
    /// </para>
    /// </remarks>
    internal const uint NIIF_ICON_MASK = 0x0000000F;

    /// <summary>
    /// Shows the balloon silently. <c>NIIF_NOSOUND</c>.
    /// </summary>
    /// <remarks>
    /// A plain bit, not part of the severity ordinal, so it is OR-ed alongside a severity rather
    /// than replacing one. Its numeric value equals <see cref="NIF_INFO"/>: the same bit number in
    /// two different fields, which is why this constant may only ever be written to
    /// <see cref="NOTIFYICONDATAW.dwInfoFlags"/>.
    /// </remarks>
    internal const uint NIIF_NOSOUND = 0x00000010;

    /// <summary>
    /// Uses the large version of the balloon icon. <c>NIIF_LARGE_ICON</c>.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="NIIF_USER"/> and a custom <c>hBalloonIcon</c>, both of which are out of
    /// scope for M001; declared so the full family is present and a reader of <c>dwInfoFlags</c>
    /// does not have to consult the header.
    /// </remarks>
    internal const uint NIIF_LARGE_ICON = 0x00000020;

    /// <summary>
    /// Asks the shell to honour quiet time for this balloon. <c>NIIF_RESPECT_QUIET_TIME</c>.
    /// </summary>
    /// <remarks>
    /// Opt-in, not the default, and the tradeoff is the point: during quiet time (the first hour
    /// after a new account is created or the OS is upgraded) a notification carrying this flag is
    /// <em>dismissed unshown</em> rather than queued, while outside quiet time the flag has no
    /// effect. Honouring quiet time therefore means choosing to have some balloons suppressed
    /// silently - a choice the library leaves to the caller (R016) instead of making it for them.
    /// Only meaningful together with <see cref="NIF_INFO"/>.
    /// </remarks>
    internal const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;

    // ---------------------------------------------------------------------------------------
    // Version and state values.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The notification-area protocol version this library speaks, written to
    /// <see cref="NOTIFYICONDATAW.uTimeoutOrVersion"/> when
    /// <see cref="NIM_SETVERSION"/> is sent. <c>NOTIFYICON_VERSION_4</c>.
    /// </summary>
    /// <remarks>
    /// v4 is what modern Windows 10/11 shells expect, and it is the version whose callback
    /// semantics S02 relies on: <c>wParam</c> carries the anchor coordinates and
    /// <c>LOWORD(lParam)</c> carries the event code from <see cref="ShellNotifications"/>.
    /// </remarks>
    internal const uint NOTIFYICON_VERSION_4 = 4;

    /// <summary>
    /// Hides the icon rather than drawing it. <c>NIS_HIDDEN</c>, written to
    /// <see cref="NOTIFYICONDATAW.dwState"/> with <see cref="NOTIFYICONDATAW.dwStateMask"/>.
    /// </summary>
    internal const uint NIS_HIDDEN = 0x00000001;

    // ---------------------------------------------------------------------------------------
    // Host-window message ids (winuser.h).
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// First message id available to an application window. <c>WM_USER</c> (winuser.h).
    /// </summary>
    internal const uint WM_USER = 0x0400;

    /// <summary>Sent when the host window is being destroyed. <c>WM_DESTROY</c> (winuser.h).</summary>
    internal const uint WM_DESTROY = 0x00000002;

    /// <summary>
    /// The callback message the shell posts to the host window for every notification-area
    /// event on our icon.
    /// </summary>
    /// <remarks>
    /// This id is a library choice, not a header symbol: it must simply be unique to our host
    /// window, so it is derived from <see cref="WM_USER"/> instead of being spelled
    /// <c>0x0400</c> at the call site. The event code is <b>not</b> carried by this message id;
    /// under <see cref="NOTIFYICON_VERSION_4"/> the shell puts the event in
    /// <c>LOWORD(lParam)</c> (see <see cref="ShellNotifications"/>) and the icon id in
    /// <c>HIWORD(lParam)</c>. Keep the two concepts apart when reading S02.
    /// </remarks>
    internal const uint TrayCallbackMessage = WM_USER + 1;

    /// <summary>
    /// The user asked for the icon's context menu, which under
    /// <see cref="NOTIFYICON_VERSION_4"/> is how the shell reports a right click on the icon.
    /// <c>WM_CONTEXTMENU</c> (winuser.h).
    /// </summary>
    /// <remarks>
    /// The v4 encoding carries this code in <c>LOWORD(lParam)</c>, like the <c>NIN_*</c> codes and
    /// the mouse messages. The documented pre-v4 right-click pair (<c>WM_RBUTTONDOWN</c> /
    /// <c>WM_RBUTTONUP</c>) is not sent under v4, which is why
    /// <see cref="WM_RBUTTONUP"/> stays unmapped by <c>TrayEventDecoder</c>: mapping both would
    /// raise a second right click if the shell ever sent both.
    /// <para>
    /// <b>Anchor caveat:</b> <c>WM_CONTEXTMENU</c> lies <em>outside</em>
    /// <see cref="WM_MOUSEFIRST"/>..<see cref="WM_MOUSELAST"/>, and the header/docs only promise a
    /// valid <c>wParam</c> anchor for messages inside that range. The decoder still reads an
    /// anchor for this code (empirically the shell does set one), but the value is officially
    /// undefined and must not be the sole source of menu placement.
    /// </para>
    /// </remarks>
    internal const uint WM_CONTEXTMENU = 0x007B;

    /// <summary>
    /// The first of the mouse message ids. <c>WM_MOUSEFIRST</c> (winuser.h), which is the same
    /// value as <see cref="WM_MOUSEMOVE"/>.
    /// </summary>
    /// <remarks>
    /// Together with <see cref="WM_MOUSELAST"/> this bounds the documented set of messages whose
    /// <c>wParam</c> anchor is valid under <see cref="NOTIFYICON_VERSION_4"/>: "GET_X_LPARAM(wParam)
    /// returns the X anchor coordinate for notification events NIN_POPUPOPEN, NIN_SELECT,
    /// NIN_KEYSELECT, and all mouse messages between WM_MOUSEFIRST and WM_MOUSELAST". Any event
    /// code outside that range - notably <see cref="WM_CONTEXTMENU"/> and the <c>NIN_*</c> codes
    /// outside the three named ones - has an officially undefined <c>wParam</c>.
    /// </remarks>
    internal const uint WM_MOUSEFIRST = 0x0200;

    /// <summary>The mouse moved over the icon. <c>WM_MOUSEMOVE</c> (winuser.h).</summary>
    /// <remarks>
    /// Decoded by <c>TrayEventDecoder</c> as an unmapped event: motion is not one of M001's four
    /// click types, so it decodes to nothing rather than to an event.
    /// </remarks>
    internal const uint WM_MOUSEMOVE = 0x0200;

    /// <summary>
    /// The left mouse button was released over the icon: under
    /// <see cref="NOTIFYICON_VERSION_4"/> this is the single left click. <c>WM_LBUTTONUP</c>
    /// (winuser.h).
    /// </summary>
    /// <remarks>
    /// Mapped to a left click with a click count of 1. Whether the shell also sends this after a
    /// double click is not documented; the mapping is deliberately independent of
    /// <see cref="WM_LBUTTONDBLCLK"/> so it does not depend on an undocumented suppression.
    /// </remarks>
    internal const uint WM_LBUTTONUP = 0x0202;

    /// <summary>
    /// The left mouse button was double-clicked over the icon. <c>WM_LBUTTONDBLCLK</c>
    /// (winuser.h).
    /// </summary>
    /// <remarks>
    /// Mapped to a left click with a click count of 2, so a consumer can distinguish the double
    /// click from the single one by the args rather than by which of the two events arrived.
    /// </remarks>
    internal const uint WM_LBUTTONDBLCLK = 0x0203;

    /// <summary>
    /// The right mouse button was released over the icon. <c>WM_RBUTTONUP</c> (winuser.h).
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not mapped</b> to a click event. Under <see cref="NOTIFYICON_VERSION_4"/> a
    /// right click on the icon arrives as <see cref="WM_CONTEXTMENU"/>; this code is declared here
    /// so the decision is visible (and pinned by a test) instead of being an omission. If live
    /// evidence ever shows the shell sending it, promoting it is a one-line decoder change.
    /// </remarks>
    internal const uint WM_RBUTTONUP = 0x0205;

    /// <summary>
    /// The middle mouse button was released over the icon. <c>WM_MBUTTONUP</c> (winuser.h).
    /// </summary>
    /// <remarks>
    /// Mapped to a middle click with a click count of 1. The code carries no modifier state; a
    /// consumer that needs it reads the keyboard at handling time, if at all.
    /// </remarks>
    internal const uint WM_MBUTTONUP = 0x0208;

    /// <summary>
    /// The last of the mouse message ids. <c>WM_MOUSELAST</c> (winuser.h).
    /// </summary>
    /// <remarks>
    /// The upper bound of the documented "the <c>wParam</c> anchor is valid" set; see
    /// <see cref="WM_MOUSEFIRST"/>. The header guards several smaller values behind older
    /// <c>_WIN32_WINNT</c> levels; the unguarded modern value is the one pinned here.
    /// </remarks>
    internal const uint WM_MOUSELAST = 0x020E;

    // ---------------------------------------------------------------------------------------
    // Process resource counters (winuser.h) - the uiFlags argument of GetGuiResources.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GetGuiResources</c> flag selecting the process' GDI object count.
    /// <c>GR_GDIOBJECTS</c> (winuser.h).
    /// </summary>
    /// <remarks>
    /// Consumed by the GDI handle-leak evidence for R007 (replacing the icon repeatedly must not
    /// leak handles). Because a fresh process legitimately owns zero GDI objects, that evidence
    /// is always a delta against a measured baseline, never a positivity check - see
    /// <c>GdiHandles</c> in the test project.
    /// </remarks>
    internal const uint GR_GDIOBJECTS = 0;
}
