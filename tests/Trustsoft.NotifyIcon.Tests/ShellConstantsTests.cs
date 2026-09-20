using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the numeric contract between this library and the Windows shell.
/// </summary>
/// <remarks>
/// <para>
/// These constants are passed to the shell as raw numbers, and every one of them was copied by
/// hand out of <c>shellapi.h</c> / <c>winuser.h</c>. A transcription slip is otherwise
/// invisible: a wrong <c>NIF_*</c> bit makes the shell read a member the caller never set (the
/// classic silent failure being a tooltip that simply never appears), and a wrong <c>NIM_*</c>
/// code performs a different operation than intended. Pinning them turns that class of mistake
/// into a failing test.
/// </para>
/// <para>
/// Grouped one assertion set per constant family rather than one test per constant, so the
/// signal stays readable.
/// </para>
/// </remarks>
public sealed class ShellConstantsTests
{
    /// <summary><c>NIM_*</c> message codes from <c>shellapi.h</c>.</summary>
    [Fact]
    public void NIM_family_matches_shellapi_h()
    {
        Assert.Equal(0x00000000u, ShellConstants.NIM_ADD);
        Assert.Equal(0x00000001u, ShellConstants.NIM_MODIFY);
        Assert.Equal(0x00000002u, ShellConstants.NIM_DELETE);
        Assert.Equal(0x00000003u, ShellConstants.NIM_SETFOCUS);
        Assert.Equal(0x00000004u, ShellConstants.NIM_SETVERSION);
    }

    /// <summary>
    /// <c>NIF_*</c> field flags from <c>shellapi.h</c>. These are a bit set, so each value is
    /// asserted as well as the fact that they are distinct single bits.
    /// </summary>
    [Fact]
    public void NIF_family_matches_shellapi_h()
    {
        Assert.Equal(0x00000001u, ShellConstants.NIF_MESSAGE);
        Assert.Equal(0x00000002u, ShellConstants.NIF_ICON);
        Assert.Equal(0x00000004u, ShellConstants.NIF_TIP);
        Assert.Equal(0x00000008u, ShellConstants.NIF_STATE);
        Assert.Equal(0x00000010u, ShellConstants.NIF_INFO);
        Assert.Equal(0x00000020u, ShellConstants.NIF_GUID);
        Assert.Equal(0x00000040u, ShellConstants.NIF_REALTIME);
        Assert.Equal(0x00000080u, ShellConstants.NIF_SHOWTIP);

        uint[] flags =
        [
            ShellConstants.NIF_MESSAGE,
            ShellConstants.NIF_ICON,
            ShellConstants.NIF_TIP,
            ShellConstants.NIF_STATE,
            ShellConstants.NIF_INFO,
            ShellConstants.NIF_GUID,
            ShellConstants.NIF_REALTIME,
            ShellConstants.NIF_SHOWTIP,
        ];

        // Every flag must be a single distinct bit: an overlapping pair would make
        // "set which members are valid" ambiguous in a way the shell resolves differently
        // than the caller intends.
        Assert.Equal(flags.Length, flags.Distinct().Count());
        Assert.All(flags, flag => Assert.Equal(1, System.Numerics.BitOperations.PopCount(flag)));
    }

    /// <summary>
    /// <c>NIIF_*</c> balloon icon flags from <c>shellapi.h</c>.
    /// </summary>
    /// <remarks>
    /// This family is not a bit set in the way <c>NIF_*</c> is - the severities are an ordinal in the
    /// low nibble and only the sound, large-icon and quiet-time members are plain bits - so only the
    /// latter three are asserted to be single bits. Every value is asserted against its header
    /// literal because <c>dwInfoFlags</c> reaches the shell as a raw number: a transcription slip
    /// produces a balloon with the wrong icon and no error, which is the same silent-failure class
    /// the <c>NIF_*</c> set above exists to catch.
    /// </remarks>
    [Fact]
    public void NIIF_family_matches_shellapi_h()
    {
        Assert.Equal(0x00000000u, ShellConstants.NIIF_NONE);
        Assert.Equal(0x00000001u, ShellConstants.NIIF_INFO);
        Assert.Equal(0x00000002u, ShellConstants.NIIF_WARNING);
        Assert.Equal(0x00000003u, ShellConstants.NIIF_ERROR);
        Assert.Equal(0x00000004u, ShellConstants.NIIF_USER);
        Assert.Equal(0x0000000Fu, ShellConstants.NIIF_ICON_MASK);
        Assert.Equal(0x00000010u, ShellConstants.NIIF_NOSOUND);
        Assert.Equal(0x00000020u, ShellConstants.NIIF_LARGE_ICON);
        Assert.Equal(0x00000080u, ShellConstants.NIIF_RESPECT_QUIET_TIME);

        Assert.Equal(1, System.Numerics.BitOperations.PopCount(ShellConstants.NIIF_NOSOUND));
        Assert.Equal(1, System.Numerics.BitOperations.PopCount(ShellConstants.NIIF_LARGE_ICON));
        Assert.Equal(1, System.Numerics.BitOperations.PopCount(ShellConstants.NIIF_RESPECT_QUIET_TIME));
    }

    /// <summary>
    /// The severity is an ordinal selected by <c>NIIF_ICON_MASK</c> rather than a bit, and the
    /// realtime bit has no member in this family at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two silent traps are pinned here. First, the severity values are exactly <c>0..3</c> - the
    /// header states the icon flags "are mutually exclusive and take only the lowest 2 bits" - so a
    /// "fifth severity" is not representable; <see cref="ShellConstants.NIIF_USER"/> uses the value
    /// above <c>NIIF_ERROR</c> to select the caller's own icon from <c>hBalloonIcon</c> instead, and
    /// its low two bits are <c>NIIF_NONE</c>.
    /// </para>
    /// <para>
    /// Second, <c>uFlags</c> and <c>dwInfoFlags</c> are different fields whose bit numbers overlap:
    /// <c>NIF_INFO</c> and <c>NIIF_NOSOUND</c> are both <c>0x10</c>. Writing a flag into the wrong
    /// field is therefore a no-op rather than a build error, and the overlap is asserted rather than
    /// "cleaned up", because renumbering a constant the header defines would be the real bug. The
    /// realtime bit is asserted to have no member in this family, which is what makes "it belongs in
    /// <c>uFlags</c>" checkable instead of merely documented.
    /// </para>
    /// </remarks>
    [Fact]
    public void NIIF_severity_is_the_low_nibble_and_realtime_is_not_a_balloon_flag()
    {
        uint[] severities =
        [
            ShellConstants.NIIF_NONE,
            ShellConstants.NIIF_INFO,
            ShellConstants.NIIF_WARNING,
            ShellConstants.NIIF_ERROR,
        ];

        uint[] expectedSeverities = [0u, 1u, 2u, 3u];
        Assert.Equal(expectedSeverities, severities);
        Assert.All(severities, severity => Assert.Equal(severity, severity & ShellConstants.NIIF_ICON_MASK));

        // NIIF_USER is outside the severity range, and its low two bits are NIIF_NONE: reading it as
        // a severity would report "no icon" while asking the shell for a custom one.
        Assert.True(ShellConstants.NIIF_USER > ShellConstants.NIIF_ERROR);
        Assert.Equal(ShellConstants.NIIF_NONE, ShellConstants.NIIF_USER & 0x3u);

        // The non-severity members live outside the nibble, so a severity can be OR-ed with them
        // without disturbing which icon is shown.
        Assert.Equal(0u, ShellConstants.NIIF_ICON_MASK & ShellConstants.NIIF_NOSOUND);
        Assert.Equal(0u, ShellConstants.NIIF_ICON_MASK & ShellConstants.NIIF_LARGE_ICON);
        Assert.Equal(0u, ShellConstants.NIIF_ICON_MASK & ShellConstants.NIIF_RESPECT_QUIET_TIME);

        uint[] balloonFlags =
        [
            ShellConstants.NIIF_NONE,
            ShellConstants.NIIF_INFO,
            ShellConstants.NIIF_WARNING,
            ShellConstants.NIIF_ERROR,
            ShellConstants.NIIF_USER,
            ShellConstants.NIIF_ICON_MASK,
            ShellConstants.NIIF_NOSOUND,
            ShellConstants.NIIF_LARGE_ICON,
            ShellConstants.NIIF_RESPECT_QUIET_TIME,
        ];

        Assert.Equal(9, balloonFlags.Length);
        Assert.Equal(balloonFlags.Length, balloonFlags.Distinct().Count());

        // The documented cross-field overlap, asserted so nobody "fixes" it: the same bit number
        // means "the balloon members are valid" in uFlags and "no sound" in dwInfoFlags.
        Assert.Equal(ShellConstants.NIF_INFO, ShellConstants.NIIF_NOSOUND);

        // NIF_REALTIME, by contrast, has no member at its value in this family. If this ever fails,
        // a balloon flag was renumbered onto the realtime bit and misplacing the bit stopped being
        // detectable at all.
        Assert.DoesNotContain(ShellConstants.NIF_REALTIME, balloonFlags);
    }

    /// <summary>
    /// <c>NIN_*</c> notification event codes from <c>shellapi.h</c>, plus the
    /// <c>NIN_KEYSELECT</c> relation the header defines by OR-ing rather than by a literal.
    /// </summary>
    [Fact]
    public void NIN_family_matches_shellapi_h()
    {
        Assert.Equal(0x0400u, ShellNotifications.NIN_SELECT);
        Assert.Equal(0x00000001u, ShellNotifications.NINF_KEY);
        Assert.Equal(0x0401u, ShellNotifications.NIN_KEYSELECT);
        Assert.Equal(0x0402u, ShellNotifications.NIN_BALLOONSHOW);
        Assert.Equal(0x0403u, ShellNotifications.NIN_BALLOONHIDE);
        Assert.Equal(0x0404u, ShellNotifications.NIN_BALLOONTIMEOUT);
        Assert.Equal(0x0405u, ShellNotifications.NIN_BALLOONUSERCLICK);
        Assert.Equal(0x0406u, ShellNotifications.NIN_POPUPOPEN);
        Assert.Equal(0x0407u, ShellNotifications.NIN_POPUPCLOSE);

        // shellapi.h: #define NIN_KEYSELECT (NIN_SELECT | NINF_KEY)
        Assert.Equal(ShellNotifications.NIN_SELECT | ShellNotifications.NINF_KEY, ShellNotifications.NIN_KEYSELECT);

        // The v4 keyboard-selection value the shell reports is the key-select code with the
        // icon id in the low word; a small id is representable, an id overlapping 0x0400 is not.
        Assert.Equal(ShellNotifications.NIN_KEYSELECT | 7u, ShellNotifications.WithKeySelect(7));
    }

    /// <summary>
    /// The protocol version, the hidden state bit and the host-window message ids from
    /// <c>shellapi.h</c> / <c>winuser.h</c>.
    /// </summary>
    [Fact]
    public void Version_state_and_window_message_ids_are_pinned()
    {
        Assert.Equal(4u, ShellConstants.NOTIFYICON_VERSION_4);
        Assert.Equal(0x00000001u, ShellConstants.NIS_HIDDEN);
        Assert.Equal(0x0400u, ShellConstants.WM_USER);
        Assert.Equal(0x00000002u, ShellConstants.WM_DESTROY);

        // The callback message id is a library choice derived from WM_USER, not a header symbol:
        // what matters is that it is derived rather than spelled out at the call site.
        Assert.Equal(ShellConstants.WM_USER + 1, ShellConstants.TrayCallbackMessage);

        // ...and that it cannot collide with the v4 notification codes, which live at
        // WM_USER + 0 .. WM_USER + 7.
        Assert.NotEqual(ShellConstants.TrayCallbackMessage, ShellNotifications.NIN_SELECT);
    }

    /// <summary>
    /// The mouse message ids from <c>winuser.h</c> that the v4 callback decoder reads out of
    /// <c>LOWORD(lParam)</c>.
    /// </summary>
    /// <remarks>
    /// A wrong value here is invisible in exactly the same way a wrong <c>NIF_*</c> bit is: the
    /// decoder would simply never match a real click and the tray icon would look inert, with no
    /// exception and nothing in the trace to point at. <c>WM_MOUSEFIRST</c> is additionally pinned
    /// to <c>WM_MOUSEMOVE</c>, which the header spells as the same number - the pair is the
    /// documented boundary of "the anchor in <c>wParam</c> is valid", so it is a relation worth
    /// asserting rather than two literals that could drift apart.
    /// </remarks>
    [Fact]
    public void Mouse_message_ids_match_winuser_h()
    {
        Assert.Equal(0x007Bu, ShellConstants.WM_CONTEXTMENU);
        Assert.Equal(0x0200u, ShellConstants.WM_MOUSEFIRST);
        Assert.Equal(0x0200u, ShellConstants.WM_MOUSEMOVE);
        Assert.Equal(0x0202u, ShellConstants.WM_LBUTTONUP);
        Assert.Equal(0x0203u, ShellConstants.WM_LBUTTONDBLCLK);
        Assert.Equal(0x0205u, ShellConstants.WM_RBUTTONUP);
        Assert.Equal(0x0208u, ShellConstants.WM_MBUTTONUP);
        Assert.Equal(0x020Eu, ShellConstants.WM_MOUSELAST);

        // The documented valid-anchor range is inclusive and does not contain the right-click
        // carrier, which is why the decoder marks the WM_CONTEXTMENU anchor as always-undefined.
        Assert.Equal(ShellConstants.WM_MOUSEMOVE, ShellConstants.WM_MOUSEFIRST);
        Assert.True(ShellConstants.WM_MOUSEFIRST < ShellConstants.WM_MOUSELAST);
        Assert.True(
            ShellConstants.WM_CONTEXTMENU < ShellConstants.WM_MOUSEFIRST || ShellConstants.WM_CONTEXTMENU > ShellConstants.WM_MOUSELAST);
    }

    /// <summary>
    /// <c>GR_GDIOBJECTS</c> is 0, which is the selector every piece of R007's handle-count
    /// evidence passes to <c>GetGuiResources</c>.
    /// </summary>
    [Fact]
    public void Gdi_objects_flag_is_zero()
    {
        Assert.Equal(0u, ShellConstants.GR_GDIOBJECTS);
    }
}
