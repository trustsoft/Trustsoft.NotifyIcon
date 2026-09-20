using System.Windows;
using System.Windows.Input;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// Decodes a notification-area callback message into the click it represents, or into
/// <see langword="null"/> when the message does not carry one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Documented encoding.</b> Under <see cref="ShellConstants.NOTIFYICON_VERSION_4"/> the shell
/// posts every notification-area event for our icon to the host window as our callback message
/// with the payload packed into the two message parameters:
/// </para>
/// <list type="bullet">
/// <item><description><c>LOWORD(lParam)</c> is the event code: a <c>NIN_*</c> notification
/// (<see cref="ShellNotifications"/>) or a window message such as
/// <see cref="ShellConstants.WM_LBUTTONUP"/>.</description></item>
/// <item><description><c>HIWORD(lParam)</c> is the icon id, which the header restricts to 16 bits
/// - the same reason <c>TrayIcon</c> allocates a bounded id.</description></item>
/// <item><description><c>wParam</c> is the anchor point, read with
/// <c>GET_X_LPARAM</c>/<c>GET_Y_LPARAM</c> semantics: two <c>short</c>-sized halves. The
/// documentation promises it for <c>NIN_POPUPOPEN</c>, <c>NIN_SELECT</c>, <c>NIN_KEYSELECT</c> and
/// all mouse messages between <see cref="ShellConstants.WM_MOUSEFIRST"/> and
/// <see cref="ShellConstants.WM_MOUSELAST"/>; for every other event code it is officially
/// undefined.</description></item>
/// </list>
/// <para>
/// <b>Purity.</b> The decoder is a static pure function: it takes no window, no dispatcher and no
/// shell, allocates nothing, touches no shared state and throws for no input. That is what makes
/// the encoding rules above provable in unit tests without a live notification area, and it is
/// required, not merely convenient: the only caller runs inside a window procedure, where an
/// exception would become a crash. It also writes no trace - deciding what to log about an
/// unmapped event belongs to the caller, which is the only party that knows the ambient verbosity.
/// </para>
/// <para>
/// <b>Anchor caveat.</b> <see cref="ShellConstants.WM_CONTEXTMENU"/> is decoded, including its
/// anchor, because that is how the shell reports a right click under version 4. The anchor is
/// nevertheless <em>officially undefined</em> for that code (it lies outside
/// <c>WM_MOUSEFIRST..WM_MOUSELAST</c>), so a consumer must not treat it as authoritative - menu
/// placement in particular must not depend on it.
/// </para>
/// </remarks>
internal static class TrayEventDecoder
{
    /// <summary>
    /// Decodes a message the host window received into the click it represents.
    /// </summary>
    /// <param name="message">The message id the host window was sent.</param>
    /// <param name="wParam">The first message parameter: the anchor point.</param>
    /// <param name="lParam">The second message parameter: the event code and the icon id.</param>
    /// <param name="callbackMessageId">
    /// The callback message id this library registered with the shell, i.e.
    /// <see cref="ShellConstants.TrayCallbackMessage"/> in production.
    /// </param>
    /// <param name="expectedIconId">The 16-bit icon id of the icon the click must belong to.</param>
    /// <returns>
    /// The decoded click, or <see langword="null"/> when the message is not our callback, not our
    /// icon, or an event code this library does not map to a click.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The three ways to get <see langword="null"/> are deliberately indistinguishable to the
    /// caller: "not ours", "someone else's icon" and "ours but not a mapped event" all mean the
    /// same thing at this layer. Anything that is not the callback message - including the
    /// <c>TaskbarCreated</c> broadcast - is returned untouched so that other slices can consume it.
    /// </para>
    /// <para>
    /// The icon-id filter is cheap defence rather than a live need: today each <c>TrayIcon</c>
    /// owns its own host window, so the id always matches. It keeps a future shared-host refactor
    /// from silently cross-delivering one icon's clicks to another icon's handlers.
    /// </para>
    /// </remarks>
    internal static TrayMouseEvent? Decode(
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint callbackMessageId,
        uint expectedIconId)
    {
        if (message != callbackMessageId)
        {
            return null;
        }

        // The parameters are pointer-width, but the shell only fills the low 32 bits on both
        // architectures; the cast keeps a 64-bit IntPtr from confusing the shifts below.
        ulong secondParam = unchecked((ulong)lParam.ToInt64());
        uint eventCode = (uint)(secondParam & 0xFFFF);
        uint iconId = (uint)((secondParam >> 16) & 0xFFFF);

        // Icon ids are 16-bit by header contract, so a plain comparison is exact; an id outside
        // that range could never have been reported and therefore never matches.
        if (iconId != expectedIconId)
        {
            return null;
        }

        (int x, int y) = DecodeAnchor(wParam);

        return eventCode switch
        {
            ShellConstants.WM_LBUTTONUP => new TrayMouseEvent(MouseButton.Left, 1, new Point(x, y), eventCode),
            ShellConstants.WM_LBUTTONDBLCLK => new TrayMouseEvent(MouseButton.Left, 2, new Point(x, y), eventCode),
            ShellConstants.WM_CONTEXTMENU => new TrayMouseEvent(MouseButton.Right, 1, new Point(x, y), eventCode),
            ShellConstants.WM_MBUTTONUP => new TrayMouseEvent(MouseButton.Middle, 1, new Point(x, y), eventCode),
            _ => null,
        };
    }

    /// <summary>
    /// Reads the anchor point out of <paramref name="wParam"/> with
    /// <c>GET_X_LPARAM</c>/<c>GET_Y_LPARAM</c> semantics.
    /// </summary>
    /// <param name="wParam">The first message parameter of the callback message.</param>
    /// <returns>The anchor point as two signed coordinates, in screen device pixels.</returns>
    /// <remarks>
    /// Each half is sign-extended from 16 bits, which is the whole point: the shell packs screen
    /// coordinates as <c>short</c>s, so a notification area on a monitor to the left of the
    /// primary one reports a negative <c>x</c>. Reading the halves as unsigned would turn
    /// <c>-1</c> into <c>65535</c> and place the click on a monitor that does not exist.
    /// </remarks>
    private static (int X, int Y) DecodeAnchor(IntPtr wParam)
    {
        long firstParam = wParam.ToInt64();
        int x = (short)(firstParam & 0xFFFF);
        int y = (short)((firstParam >> 16) & 0xFFFF);
        return (x, y);
    }
}
