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
