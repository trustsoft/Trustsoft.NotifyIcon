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
/// <para>
/// <b>Two entry points over one implementation.</b> <see cref="Decode"/> answers the click question
/// only; <see cref="Classify"/> answers the wider "what was this message" question, because a
/// balloon callback is not a click and the sink that handles both must tell them apart (D032). Both
/// are views of the same decode, so the encoding rules above exist once and the two entry points
/// cannot drift apart.
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
    /// This is the click-only view of <see cref="Classify"/> and delegates to it, so the payload
    /// decoding and the icon-id filter live in one place and the two entry points cannot diverge.
    /// </para>
    /// </remarks>
    internal static TrayMouseEvent? Decode(
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint callbackMessageId,
        uint expectedIconId) =>
        Classify(message, wParam, lParam, callbackMessageId, expectedIconId).Click;

    /// <summary>
    /// Classifies a message the host window received against the notification-area callback
    /// contract, reporting both what the message is and - for a click - the click it carries.
    /// </summary>
    /// <param name="message">The message id the host window was sent.</param>
    /// <param name="wParam">
    /// The first message parameter: the anchor point. Read only when the event code turns out to be
    /// a click.
    /// </param>
    /// <param name="lParam">The second message parameter: the event code and the icon id.</param>
    /// <param name="callbackMessageId">
    /// The callback message id this library registered with the shell, i.e.
    /// <see cref="ShellConstants.TrayCallbackMessage"/> in production.
    /// </param>
    /// <param name="expectedIconId">The 16-bit icon id of the icon the event must belong to.</param>
    /// <returns>
    /// The classification, whose <see cref="TrayCallbackClassification.Outcome"/> is one of the five
    /// cases <see cref="TrayCallbackOutcome"/> names.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why a balloon callback is classified here rather than by a second decoder.</b> A balloon
    /// event and a click arrive through the same callback message with the same
    /// <c>LOWORD(lParam)</c>/<c>HIWORD(lParam)</c> layout, so the icon-id filter and the half
    /// selection have to be the same code for both; a separate balloon decoder would duplicate the
    /// filter and could disagree with the click path about which icon a payload belongs to (D032).
    /// <see cref="Decode"/> is therefore a narrowing of this method, not a parallel implementation.
    /// </para>
    /// <para>
    /// <b>Purity, unchanged from <see cref="Decode"/>.</b> This is a pure static function that never
    /// throws and allocates nothing: no window, no dispatcher, no shell, no shared state, and no
    /// heap-typed value on any path. That is a requirement rather than a tidiness preference - the
    /// only caller runs inside a window procedure, where an exception becomes a crash of the host
    /// application. It also writes no trace, for the same reason the decoder never did: deciding what
    /// to log belongs to the caller, which is the only party that knows the ambient verbosity.
    /// </para>
    /// <para>
    /// <b><c>wParam</c> is read for a click and for no other outcome.</b> The documentation promises
    /// the anchor only for <c>NIN_POPUPOPEN</c>, <c>NIN_SELECT</c>, <c>NIN_KEYSELECT</c> and the mouse
    /// messages between <see cref="ShellConstants.WM_MOUSEFIRST"/> and
    /// <see cref="ShellConstants.WM_MOUSELAST"/>; for every other event code it is officially
    /// undefined. A balloon code lies outside that set, so a balloon classification must not decode
    /// one - on those events the parameter holds whatever the shell happened to leave there, and
    /// turning that into a coordinate would invent a screen point the shell never sent. The rule is
    /// observable in the result: <see cref="TrayCallbackClassification.Anchor"/> is populated
    /// exactly for <see cref="TrayCallbackOutcome.Click"/>, so "no anchor was computed" is a property
    /// of the classification rather than an absence a reader has to infer from the code.
    /// </para>
    /// </remarks>
    internal static TrayCallbackClassification Classify(
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint callbackMessageId,
        uint expectedIconId)
    {
        // The message id is checked before either parameter is read: the parameters of a message
        // that is not our callback belong to whatever sent it, not to the notification-area
        // encoding, so nothing about them is decoded or reported.
        if (message != callbackMessageId)
        {
            return new TrayCallbackClassification(TrayCallbackOutcome.NotOurCallback, 0u, 0u, null);
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
            return new TrayCallbackClassification(TrayCallbackOutcome.ForeignIcon, eventCode, iconId, null);
        }

        return eventCode switch
        {
            // Clicks are the only outcome that reads wParam; ClassifyClick performs the decode so
            // the anchor is never computed on a path that must not have one.
            ShellConstants.WM_LBUTTONUP => ClassifyClick(eventCode, iconId, wParam, MouseButton.Left, 1),
            ShellConstants.WM_LBUTTONDBLCLK => ClassifyClick(eventCode, iconId, wParam, MouseButton.Left, 2),
            ShellConstants.WM_CONTEXTMENU => ClassifyClick(eventCode, iconId, wParam, MouseButton.Right, 1),
            ShellConstants.WM_MBUTTONUP => ClassifyClick(eventCode, iconId, wParam, MouseButton.Middle, 1),

            // The four balloon codes are classified, never anchored (see the remark on wParam).
            // The match is by member, not by range, so the popup codes that share this region land
            // in the default arm and stay unclassified (D033).
            ShellNotifications.NIN_BALLOONSHOW or
            ShellNotifications.NIN_BALLOONHIDE or
            ShellNotifications.NIN_BALLOONTIMEOUT or
            ShellNotifications.NIN_BALLOONUSERCLICK =>
                new TrayCallbackClassification(TrayCallbackOutcome.BalloonEvent, eventCode, iconId, null),

            _ => new TrayCallbackClassification(TrayCallbackOutcome.UnmappedEventCode, eventCode, iconId, null),
        };
    }

    /// <summary>
    /// Classifies a mapped click event code and decodes its anchor.
    /// </summary>
    /// <param name="eventCode">The mapped event code from <c>LOWORD(lParam)</c>.</param>
    /// <param name="iconId">The icon id from <c>HIWORD(lParam)</c>.</param>
    /// <param name="wParam">The first message parameter carrying the anchor.</param>
    /// <param name="button">The button the event code represents.</param>
    /// <param name="clickCount">The click count the event code represents.</param>
    /// <returns>A click classification for the decoded event.</returns>
    /// <remarks>
    /// A plain static method rather than a local function or a lambda on purpose: a lambda that
    /// captured <paramref name="wParam"/> would allocate a closure on every click, and the decoder's
    /// no-allocation property is what lets it run safely inside a window procedure.
    /// </remarks>
    private static TrayCallbackClassification ClassifyClick(
        uint eventCode,
        uint iconId,
        IntPtr wParam,
        MouseButton button,
        int clickCount)
    {
        (int x, int y) = DecodeAnchor(wParam);
        var click = new TrayMouseEvent(button, clickCount, new Point(x, y), eventCode);
        return new TrayCallbackClassification(TrayCallbackOutcome.Click, eventCode, iconId, click);
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

/// <summary>
/// What a host-window message turned out to be when classified against the notification-area
/// callback contract.
/// </summary>
/// <remarks>
/// <para>
/// The three ways <see cref="TrayEventDecoder.Decode"/> returned <see langword="null"/> mean the
/// same thing to a click raiser and different things to the message sink: "not our callback",
/// "someone else's icon" and "ours but an unmapped code" are traced differently, and a sink that
/// also handles balloons must recognise a balloon callback rather than lump it in with the unmapped
/// codes. This type is that distinction. It stays internal because the public vocabulary is the
/// routed events raised by <c>TrayIcon</c>.
/// </para>
/// <para>
/// The members are ordered from "not ours at all" to "ours and actionable", and a sink may use that
/// order as the precedence of the checks - not as a severity. No member implies a failure: an
/// unmapped code and a foreign icon are both ordinary traffic, not errors.
/// </para>
/// </remarks>
internal enum TrayCallbackOutcome
{
    /// <summary>
    /// The message id is not this library's callback message. Neither parameter was decoded, so the
    /// classification reports no event code and no icon id.
    /// </summary>
    NotOurCallback,

    /// <summary>
    /// Our callback message, but the 16-bit icon id in <c>HIWORD(lParam)</c> belongs to another
    /// icon. The event code and icon id are reported so the mismatch can be traced.
    /// </summary>
    ForeignIcon,

    /// <summary>
    /// Our callback and our icon, but the event code is not one this library acts on. The event
    /// code is reported for tracing, and no public event follows from it.
    /// </summary>
    UnmappedEventCode,

    /// <summary>
    /// Our callback and our icon, and the event code is a mouse click:
    /// <see cref="TrayCallbackClassification.Click"/> carries the decoded click and its anchor.
    /// </summary>
    Click,

    /// <summary>
    /// Our callback and our icon, and the event code is one of the four <c>NIN_BALLOON*</c> codes.
    /// Which of the four is in <see cref="TrayCallbackClassification.EventCode"/>; whether it is the
    /// user click or a lifecycle phase is the sink's decision, not this type's.
    /// </summary>
    BalloonEvent,
}

/// <summary>
/// The result of classifying a host-window message with
/// <see cref="TrayEventDecoder.Classify"/>.
/// </summary>
/// <remarks>
/// <para>
/// An interop-level value, not public API: it carries the raw shell values the sink needs to branch,
/// trace and raise, and it exists so all of that can be tested without a live notification area.
/// It is a readonly struct with no heap-typed member, so producing one allocates nothing - the
/// decoder runs inside a window procedure, where an allocation per callback would be avoidable work
/// on the UI thread.
/// </para>
/// <para>
/// The invariant worth stating once: <see cref="Click"/> and <see cref="Anchor"/> are populated
/// exactly when <see cref="Outcome"/> is <see cref="TrayCallbackOutcome.Click"/>. Every other
/// outcome - including a balloon event - has neither, which is how "the anchor is not read for
/// balloon codes" is expressed as a shape rather than as a comment.
/// </para>
/// </remarks>
internal readonly struct TrayCallbackClassification
{
    /// <summary>
    /// Creates a classification.
    /// </summary>
    /// <param name="outcome">What the message turned out to be.</param>
    /// <param name="eventCode">
    /// The event code from <c>LOWORD(lParam)</c>, or <c>0</c> when the message was not our callback
    /// and the parameter was therefore left unread.
    /// </param>
    /// <param name="iconId">
    /// The icon id from <c>HIWORD(lParam)</c>, or <c>0</c> when the message was not our callback.
    /// </param>
    /// <param name="click">
    /// The decoded click, supplied only for <see cref="TrayCallbackOutcome.Click"/>.
    /// </param>
    internal TrayCallbackClassification(
        TrayCallbackOutcome outcome,
        uint eventCode,
        uint iconId,
        TrayMouseEvent? click)
    {
        Outcome = outcome;
        EventCode = eventCode;
        IconId = iconId;
        Click = click;
    }

    /// <summary>What the message turned out to be.</summary>
    internal TrayCallbackOutcome Outcome { get; }

    /// <summary>
    /// The raw event code from <c>LOWORD(lParam)</c>, so a caller can trace the exact shell event
    /// without re-deriving it. Zero when <see cref="Outcome"/> is
    /// <see cref="TrayCallbackOutcome.NotOurCallback"/>, because a foreign message's parameters are
    /// not ours to interpret.
    /// </summary>
    internal uint EventCode { get; }

    /// <summary>
    /// The icon id from <c>HIWORD(lParam)</c>. Zero when <see cref="Outcome"/> is
    /// <see cref="TrayCallbackOutcome.NotOurCallback"/>, for the same reason as
    /// <see cref="EventCode"/>; otherwise it is the id the payload claimed, which for
    /// <see cref="TrayCallbackOutcome.ForeignIcon"/> is by definition not the expected one.
    /// </summary>
    internal uint IconId { get; }

    /// <summary>
    /// The decoded click: non-<see langword="null"/> exactly when <see cref="Outcome"/> is
    /// <see cref="TrayCallbackOutcome.Click"/>.
    /// </summary>
    internal TrayMouseEvent? Click { get; }

    /// <summary>
    /// The anchor point the shell reported: non-<see langword="null"/> exactly when
    /// <see cref="Outcome"/> is <see cref="TrayCallbackOutcome.Click"/>.
    /// </summary>
    /// <remarks>
    /// This is the click's own anchor, surfaced under its own name because the anchor is the value a
    /// balloon classification is required not to have. On a balloon event the header leaves
    /// <c>wParam</c> undefined, so the property is <see langword="null"/> there - not because the
    /// point came out empty, but because it was never computed.
    /// </remarks>
    internal Point? Anchor => Click?.ScreenAnchor;
}
