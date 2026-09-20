using System.Windows;
using System.Windows.Input;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the NOTIFYICON_VERSION_4 callback decoding implemented by
/// <see cref="TrayEventDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The decoder is the one place in S02 where the payload interpretation can be wrong in a way that
/// no build error and no live click exposes clearly - the click simply becomes a different click,
/// or silently nothing. Being a pure function over two integers it is also fully provable here,
/// with no window, no dispatcher and no notification area, which is exactly why it was isolated
/// before any of the wiring exists.
/// </para>
/// <para>
/// The negative cases carry as much of the contract as the positive ones: the deliberately unmapped
/// codes (notably <c>WM_RBUTTONUP</c>) are asserted so the decision cannot be reversed by accident,
/// and the "never throws" property is asserted because the only caller runs inside a window
/// procedure where an exception would crash the process rather than fail a test.
/// </para>
/// </remarks>
public sealed class TrayEventDecoderTests
{
    /// <summary>The callback message id the decoder is told to match.</summary>
    private const uint CallbackMessage = ShellConstants.TrayCallbackMessage;

    /// <summary>The 16-bit icon id the decoder is told to accept.</summary>
    private const uint IconId = 7;

    /// <summary>
    /// Each mapped event code decodes to its button and click count, and reports the raw code it
    /// came from.
    /// </summary>
    /// <remarks>
    /// The four rows are the whole mapping table for M001: single left, double left, right (which
    /// arrives as <c>WM_CONTEXTMENU</c> under version 4) and middle.
    /// </remarks>
    [Theory]
    [InlineData(ShellConstants.WM_LBUTTONUP, MouseButton.Left, 1)]
    [InlineData(ShellConstants.WM_LBUTTONDBLCLK, MouseButton.Left, 2)]
    [InlineData(ShellConstants.WM_CONTEXTMENU, MouseButton.Right, 1)]
    [InlineData(ShellConstants.WM_MBUTTONUP, MouseButton.Middle, 1)]
    public void Mapped_event_codes_decode_to_their_click(uint eventCode, MouseButton expectedButton, int expectedClickCount)
    {
        TrayMouseEvent decoded = Assert.NotNull(
            TrayEventDecoder.Decode(CallbackMessage, Anchor(10, 20), Payload(eventCode, IconId), CallbackMessage, IconId));

        Assert.Equal(expectedButton, decoded.Button);
        Assert.Equal(expectedClickCount, decoded.ClickCount);
        Assert.Equal(eventCode, decoded.RawEvent);
        Assert.Equal(new Point(10, 20), decoded.ScreenAnchor);
    }

    /// <summary>
    /// <c>WM_RBUTTONUP</c> and the other deliberately unmapped codes decode to nothing.
    /// </summary>
    /// <remarks>
    /// <c>WM_RBUTTONUP</c> is the load-bearing row: under version 4 the right click is reported as
    /// <c>WM_CONTEXTMENU</c>, so mapping this one too would raise a second, spurious right click
    /// if the shell ever sent both. The keyboard-selection codes stay unmapped so a future
    /// accessibility slice can own the activation semantics rather than inheriting a click.
    /// </remarks>
    [Theory]
    [InlineData(ShellConstants.WM_RBUTTONUP)]
    [InlineData(ShellConstants.WM_MOUSEMOVE)]
    [InlineData(ShellNotifications.NIN_SELECT)]
    [InlineData(ShellNotifications.NIN_KEYSELECT)]
    [InlineData(0x0201u)] // WM_LBUTTONDOWN: only the release is a click under v4.
    [InlineData(0x00ABu)]
    public void Unmapped_event_codes_decode_to_nothing(uint eventCode)
    {
        Assert.Null(TrayEventDecoder.Decode(CallbackMessage, Anchor(10, 20), Payload(eventCode, IconId), CallbackMessage, IconId));
    }

    /// <summary>
    /// A message that is not the callback message decodes to nothing, even when the payload would
    /// otherwise be a perfect click.
    /// </summary>
    /// <remarks>
    /// This is the separation between the callback message id and the event codes: the
    /// <c>NIN_*</c> codes live inside <c>WM_USER..WM_USER+7</c> and so do host-window messages, so
    /// matching on the payload alone would route an unrelated message into the click events. The
    /// <c>TaskbarCreated</c> broadcast that S05 consumes is the practical case - it must arrive
    /// intact at the slice that owns it.
    /// </remarks>
    [Theory]
    [InlineData(ShellConstants.TrayCallbackMessage + 1)]
    [InlineData(ShellNotifications.NIN_SELECT)] // Shares WM_USER's value, i.e. the neighbouring id.
    [InlineData(ShellConstants.WM_DESTROY)]
    [InlineData(0u)]
    [InlineData(0x8000u)]
    [InlineData(0xFFFFu)]
    public void Non_callback_messages_decode_to_nothing(uint message)
    {
        Assert.Null(TrayEventDecoder.Decode(message, Anchor(10, 20), Payload(ShellConstants.WM_LBUTTONUP, IconId), CallbackMessage, IconId));
    }

    /// <summary>
    /// A click belonging to a different icon decodes to nothing.
    /// </summary>
    /// <remarks>
    /// Today each <c>TrayIcon</c> owns its own host window so the id always matches; the filter
    /// exists so a future shared host cannot cross-deliver one icon's clicks to another's
    /// handlers. The assertion uses a payload that is otherwise a valid left click so a failure
    /// here means the <c>HIWORD(lParam)</c> check specifically was lost.
    /// </remarks>
    [Theory]
    [InlineData(IconId + 1)]
    [InlineData(0u)]
    [InlineData(0xFFFFu)]
    public void Mismatched_icon_id_decodes_to_nothing(uint payloadIconId)
    {
        Assert.Null(TrayEventDecoder.Decode(
            CallbackMessage,
            Anchor(10, 20),
            Payload(ShellConstants.WM_LBUTTONUP, payloadIconId),
            CallbackMessage,
            IconId));
    }

    /// <summary>
    /// The event code comes from <c>LOWORD(lParam)</c> and the icon id from <c>HIWORD(lParam)</c>,
    /// not the other way round.
    /// </summary>
    /// <remarks>
    /// Swapping the halves would still decode "a click for some icon" often enough to look like it
    /// worked in a smoke test, so the asymmetry is asserted directly with swapped values.
    /// </remarks>
    [Fact]
    public void Event_code_and_icon_id_are_read_from_the_documented_halves()
    {
        // Swapped: the event code sits in the high word where only the icon id belongs, so the
        // low word (0) is not a mapped event and nothing decodes.
        Assert.Null(TrayEventDecoder.Decode(
            CallbackMessage,
            Anchor(10, 20),
            Payload(0, ShellConstants.WM_LBUTTONUP),
            CallbackMessage,
            IconId));

        TrayMouseEvent decoded = Assert.NotNull(TrayEventDecoder.Decode(
            CallbackMessage,
            Anchor(10, 20),
            Payload(ShellConstants.WM_LBUTTONUP, IconId),
            CallbackMessage,
            IconId));

        Assert.Equal(ShellConstants.WM_LBUTTONUP, decoded.RawEvent);
    }

    /// <summary>
    /// Positive anchor coordinates decode as sent.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    [InlineData(32767, 32767)]
    public void Positive_anchor_coordinates_decode_as_sent(int x, int y)
    {
        TrayMouseEvent decoded = DecodeLeftClick(Anchor(x, y));

        Assert.Equal(new Point(x, y), decoded.ScreenAnchor);
    }

    /// <summary>
    /// Negative anchor coordinates are sign-extended, which is what makes a notification area on a
    /// monitor left of the primary one decode correctly.
    /// </summary>
    /// <remarks>
    /// Reading either half as unsigned would report 65535 instead of -1: a coordinate on a monitor
    /// that does not exist, silently. The <c>GET_X_LPARAM</c>/<c>GET_Y_LPARAM</c> rule is the whole
    /// reason this test exists.
    /// </remarks>
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1920, 300)]
    [InlineData(500, -1200)]
    [InlineData(-32768, -32768)]
    [InlineData(-32768, 32767)]
    public void Negative_anchor_coordinates_are_sign_extended(int x, int y)
    {
        TrayMouseEvent decoded = DecodeLeftClick(Anchor(x, y));

        Assert.Equal(new Point(x, y), decoded.ScreenAnchor);
    }

    /// <summary>
    /// The right-click anchor is decoded even though the documentation leaves it undefined for
    /// <c>WM_CONTEXTMENU</c>.
    /// </summary>
    /// <remarks>
    /// The decoder deliberately reports the value the shell sent, because S03 needs a point and the
    /// shell does supply one in practice; the caveat is that this value is informational - menu
    /// placement must use <c>Shell_NotifyIconGetRect</c>, not this anchor. Asserting it here keeps
    /// the decision explicit rather than accidental.
    /// </remarks>
    [Fact]
    public void Right_click_anchor_is_decoded_despite_the_documented_gap()
    {
        TrayMouseEvent decoded = Assert.NotNull(TrayEventDecoder.Decode(
            CallbackMessage,
            Anchor(-800, 640),
            Payload(ShellConstants.WM_CONTEXTMENU, IconId),
            CallbackMessage,
            IconId));

        Assert.Equal(MouseButton.Right, decoded.Button);
        Assert.Equal(new Point(-800, 640), decoded.ScreenAnchor);
    }

    /// <summary>
    /// Unset high bits of the pointer-width parameters do not change the decoded click.
    /// </summary>
    /// <remarks>
    /// The shell fills the low 32 bits of a pointer-width parameter and leaves the rest undefined.
    /// Only the documented halves may be read, so a fully set upper half must decode exactly like a
    /// zero upper half.
    /// </remarks>
    [Fact]
    public void Upper_32_bits_of_the_parameters_are_ignored()
    {
        IntPtr lParam = new(unchecked((long)0xFFFFFFFF00000000UL | (uint)Payload(ShellConstants.WM_LBUTTONUP, IconId).ToInt64()));
        IntPtr wParam = new(unchecked((long)0xFFFFFFFF00000000UL | (uint)Anchor(120, 340).ToInt64()));

        TrayMouseEvent decoded = DecodeLeftClick(wParam, lParam);

        Assert.Equal(new Point(120, 340), decoded.ScreenAnchor);
        Assert.Equal(ShellConstants.WM_LBUTTONUP, decoded.RawEvent);
    }

    /// <summary>
    /// Decoding never throws for any 16-bit event code, and exactly the four documented codes map.
    /// </summary>
    /// <remarks>
    /// The decoder runs inside a window procedure: a throw is a crash of the host application, so
    /// "handles every input" is a correctness property and not defensive style. The count assertion
    /// turns an accidental addition (or removal) of a mapping row into a failing test, which is the
    /// cheapest way to notice that the click surface changed without the sample, docs and contract
    /// tests following.
    /// </remarks>
    [Fact]
    public void No_event_code_throws_and_exactly_four_map()
    {
        int mapped = 0;

        for (uint eventCode = 0; eventCode <= 0xFFFF; eventCode++)
        {
            TrayMouseEvent? decoded = TrayEventDecoder.Decode(
                CallbackMessage,
                Anchor(-1, -1),
                Payload(eventCode, IconId),
                CallbackMessage,
                IconId);

            if (decoded is not null)
            {
                mapped++;
            }
        }

        Assert.Equal(4, mapped);
    }

    /// <summary>
    /// Decodes a left click with the anchor under test.
    /// </summary>
    /// <param name="wParam">The anchor parameter.</param>
    /// <param name="lParam">The payload parameter; a left click for <see cref="IconId"/> by default.</param>
    /// <returns>The decoded click, which must not be null.</returns>
    private static TrayMouseEvent DecodeLeftClick(IntPtr wParam, IntPtr? lParam = null) =>
        Assert.NotNull(TrayEventDecoder.Decode(
            CallbackMessage,
            wParam,
            lParam ?? Payload(ShellConstants.WM_LBUTTONUP, IconId),
            CallbackMessage,
            IconId));

    /// <summary>
    /// Packs two screen coordinates into the <c>wParam</c> anchor the way the shell does: two
    /// <c>short</c>-sized halves, low word first.
    /// </summary>
    /// <param name="x">The x coordinate, possibly negative.</param>
    /// <param name="y">The y coordinate, possibly negative.</param>
    /// <returns>The packed parameter with a zero upper half.</returns>
    private static IntPtr Anchor(int x, int y)
    {
        uint packed = (uint)(ushort)x | ((uint)(ushort)y << 16);
        return new IntPtr((long)packed);
    }

    /// <summary>
    /// Packs an event code and a 16-bit icon id into the <c>lParam</c> payload the way the shell
    /// does: event in the low word, id in the high word.
    /// </summary>
    /// <param name="eventCode">The event code, taken from the low 16 bits.</param>
    /// <param name="iconId">The icon id, taken from the low 16 bits.</param>
    /// <returns>The packed parameter with a zero upper half.</returns>
    private static IntPtr Payload(uint eventCode, uint iconId)
    {
        uint packed = (eventCode & 0xFFFF) | ((iconId & 0xFFFF) << 16);
        return new IntPtr((long)packed);
    }
}
