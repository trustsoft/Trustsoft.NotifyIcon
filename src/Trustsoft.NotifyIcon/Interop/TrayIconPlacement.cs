using System.Windows;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// Turns the shell's icon rectangle into the point a WPF <c>ContextMenu</c> must be given as its
/// <c>HorizontalOffset</c>/<c>VerticalOffset</c>, applying the physical-to-DIP scale factor
/// <b>exactly once</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measured law this type implements.</b> WPF interprets a <c>PlacementMode.AbsolutePoint</c>
/// offset in the placement target's DPI space:
/// <c>physical = offset * (GetDpiForWindow(target) / 96)</c>. Measured on this machine at Display
/// Scale 150 % (144 DPI), four requested points on a 144-DPI host: <c>(0,0) -&gt; (0,0)</c>,
/// <c>(100,100) -&gt; (150,150)</c>, <c>(600,300) -&gt; (900,450)</c>, <c>(900,40) -&gt; (1350,60)</c>
/// (raw output in <c>.gsd/s03-evidence/contextmenu-placement-probe.txt</c>). The engine therefore
/// performs the physical conversion for us, which is exactly why a library that converts the icon
/// rectangle to DIPs and then hands the result over lands the menu at <c>rect * scale</c> plus the
/// engine's own scaling - at 150 % that is 50 % too far right and down. The wrong and the right
/// version look equally plausible in review, so the arithmetic lives in one pure function and is
/// driven by fixtures rather than by inspection (R014).
/// </para>
/// <para>
/// <b>The two steps, in this order.</b>
/// </para>
/// <list type="number">
/// <item><description>Take the icon rectangle's <b>bottom-left</b> in physical pixels - the tray
/// convention, because the notification area sits at the bottom of the screen and the menu is meant
/// to appear above the icon - and clamp that point into the monitor work area. The work area is
/// consumed as a <c>RECT</c>: its <c>right</c>/<c>bottom</c> edges are exclusive, so the largest
/// legal coordinate is <c>right - 1</c>/<c>bottom - 1</c>.</description></item>
/// <item><description>Divide the clamped physical point by the scale factor to get the DIP offset
/// the engine consumes.</description></item>
/// </list>
/// <para>
/// Doing those in the other order is precisely the double-scaling bug, and the clamp is what keeps
/// the anchor legal: a negative or off-screen absolute point produces <b>no visible popup at all</b>
/// (MEM062, measured), not merely a misplaced one. WPF does clamp the popup itself once the offset
/// is legal - a request that would have landed at physical <c>(2250,1650)</c> was clamped to
/// <c>(1727,1118)</c> - so this type deliberately does not re-implement that. In particular there is
/// no menu-size overload: keeping the whole popup inside the work area needs the engine's own corner
/// and flip rules, which are unmeasured, and the only hard requirement on the anchor is that it be a
/// legal point.
/// </para>
/// <para>
/// The clamp respects a monitor with negative virtual-screen coordinates - a screen to the left of
/// or above the primary one has negative <c>x</c> or <c>y</c>, and negative output is correct there -
/// and it treats the <b>work area</b>, never the monitor rectangle, as the boundary, because a
/// taskbar occupying an edge makes the two differ and the menu must not be placed under the taskbar.
/// </para>
/// <para>
/// <b>Why a separate, pure type.</b> This is the most intricate arithmetic in M001/S03 - the part a
/// display shape the developer does not own can silently falsify - so it must be provable without a
/// notification area, a monitor or a message pump. Any window, DPI or monitor access in this file
/// would force its tests onto an STA thread with real hardware (<c>[StaFact]</c>), where a headless
/// CI runner cannot run them at all. Being pure is what makes these rules testable with
/// <c>[Fact]</c>/<c>[Theory]</c> instead (the S02 <see cref="TrayEventDecoder"/> precedent).
/// </para>
/// <para>
/// <b>What it deliberately does NOT do.</b> It does not choose a monitor, does not read the DPI,
/// does not create a window, does not own or open the menu, and does not fall back to the cursor.
/// Those belong to the menu path in <c>TrayIcon</c>, which resolves the icon rectangle through
/// <see cref="IShellApi.ShellNotifyIconGetRect"/> and the DPI through the monitor provider; keeping
/// them out is what lets this file stay pure. The single input the caller must supply with care is
/// <c>dpi</c>: it is the DPI of the coordinate space the placement engine will use, so the caller
/// must hand over the DPI that governs the placement target rather than an unrelated reading.
/// </para>
/// <para>
/// <b>Units and precision.</b> The icon rectangle and the work area are physical screen pixels;
/// the DPI is an unsigned value where
/// <see cref="UserDefaultScreenDpi"/> (96) means 100 %; the return value is in the DIP space the
/// offset consumes (<c>X</c> for <c>HorizontalOffset</c>, <c>Y</c> for <c>VerticalOffset</c>). The
/// returned offset is <b>fractional, not rounded</b>: at 150 % a physical <c>101</c> becomes
/// <c>67.333...</c>, and rounding to whole DIPs would move the menu by up to half a pixel per axis
/// and destroy the equality form of "the factor is applied exactly once". WPF's offsets are
/// <see langword="double"/>, so the exact quotient is the honest value.
/// </para>
/// </remarks>
internal static class TrayIconPlacement
{
    /// <summary>
    /// <c>USER_DEFAULT_SCREEN_DPI</c> from <c>windef.h</c>: the DPI that means "100 %", i.e. one
    /// device-independent unit equals one physical pixel.
    /// </summary>
    internal const uint UserDefaultScreenDpi = 96;

    /// <summary>
    /// The scale factor between physical pixels and the DIP space the placement engine consumes:
    /// <c>dpi / 96.0</c>, so 96 is 1.0, 120 is 1.25, 144 is 1.5 and 192 is 2.0.
    /// </summary>
    /// <param name="dpi">The DPI of the placement target's coordinate space.</param>
    /// <returns>The factor to divide a physical length by, or to multiply a DIP length by.</returns>
    /// <remarks>
    /// <para>
    /// The division exists here and nowhere else so the offset and any other DIP arithmetic derived
    /// from the same reading cannot drift apart - deriving the offset from one factor and, say, a
    /// window size from another is the mixed-DPI bug in miniature.
    /// </para>
    /// <para>
    /// <b>Zero is rejected rather than divided by.</b> <paramref name="dpi"/> is a DPI value, never a
    /// scale percentage, so 0 is not a legal reading (the shell's own <c>GetDpiForWindow</c> /
    /// <c>GetDpiForMonitor</c> never produce it, and this library substitutes
    /// <see cref="UserDefaultScreenDpi"/> when a reading fails). Returning 0.0 would silently make
    /// every offset infinite, so the mistake is surfaced as an
    /// <see cref="ArgumentOutOfRangeException"/> at the boundary instead - this is not a window
    /// procedure, so throwing cannot crash a message loop.
    /// </para>
    /// </remarks>
    internal static double ScaleFor(uint dpi)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpi),
                dpi,
                "A DPI of 0 has no scale factor; a missing DPI reading must be replaced with USER_DEFAULT_SCREEN_DPI (96) before it reaches the placement arithmetic.");
        }

        return dpi / (double)UserDefaultScreenDpi;
    }

    /// <summary>
    /// Computes the point to assign to the menu's <c>HorizontalOffset</c> and
    /// <c>VerticalOffset</c> for an icon rectangle on a monitor whose work area and DPI are known.
    /// </summary>
    /// <param name="iconRect">
    /// The icon's rectangle from <c>Shell_NotifyIconGetRect</c>, in physical screen pixels.
    /// </param>
    /// <param name="workArea">
    /// The icon monitor's work area (the monitor rectangle minus the taskbar and any other appbars),
    /// in physical screen pixels, with exclusive <c>right</c>/<c>bottom</c> edges.
    /// </param>
    /// <param name="dpi">
    /// The DPI of the placement target's coordinate space, where 96 means 100 %; see
    /// <see cref="ScaleFor"/> for why 0 is rejected.
    /// </param>
    /// <returns>
    /// The anchor in the DIP space the placement engine consumes: <c>X</c> for
    /// <c>HorizontalOffset</c>, <c>Y</c> for <c>VerticalOffset</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The anchor is the icon rectangle's bottom-left, clamped into the work area and only then
    /// divided by the scale - see the type remarks for why that order is the whole point of this
    /// type. An empty or degenerate icon rectangle (see <see cref="NativeRect.IsEmpty"/>) is not
    /// rejected: it still names a bottom-left corner, and the clamp keeps that corner legal, so a
    /// caller that ignored a failed <c>Shell_NotifyIconGetRect</c> still cannot produce an off-screen
    /// anchor. A caller that knows the rectangle is meaningless should use its documented cursor
    /// fallback instead of this method.
    /// </para>
    /// <para>
    /// An empty or inverted work area has no legal point inside it, so the anchor is pinned to the
    /// area's own origin (<c>left</c>, <c>top</c>) - a defined answer rather than a throw, because
    /// this runs on the click path where a placement must still be produced.
    /// </para>
    /// </remarks>
    internal static Point Calculate(NativeRect iconRect, NativeRect workArea, uint dpi)
    {
        // Step 1: the physical point, clamped into the work area. The exclusive right/bottom edges
        // are handled in the clamp helper, with the arithmetic widened to long there because
        // (long) right - 1 is exactly the value that overflows an int on an extreme virtual desktop.
        int physicalX = ClampIntoArea(iconRect.left, workArea.left, workArea.right);
        int physicalY = ClampIntoArea(iconRect.bottom, workArea.top, workArea.bottom);

        // Step 2: the same point expressed in the DIP space the offset consumes. Dividing here, once,
        // is what makes the engine's own multiplication by this same factor land on the physical
        // point instead of on rect * scale.
        double scale = ScaleFor(dpi);
        return new Point(physicalX / scale, physicalY / scale);
    }

    /// <summary>
    /// Clamps a physical coordinate into <c>[low, highExclusive - 1]</c>, the legal range of a
    /// <c>RECT</c>-shaped work area.
    /// </summary>
    /// <param name="value">The physical coordinate to clamp.</param>
    /// <param name="low">The area's inclusive low edge (<c>left</c> or <c>top</c>).</param>
    /// <param name="highExclusive">The area's exclusive high edge (<c>right</c> or <c>bottom</c>).</param>
    /// <returns>The clamped coordinate, always inside the area when the area has any extent.</returns>
    /// <remarks>
    /// <para>
    /// <c>highExclusive - 1</c> is computed in <see langword="long"/>: with a work area whose
    /// <c>right</c> is <see cref="int.MinValue"/> the int subtraction would wrap to
    /// <see cref="int.MaxValue"/> and turn the clamp into a no-op that lets the point escape. The
    /// widened value is only ever cast back after being clamped between two <see langword="int"/>
    /// bounds, so the cast cannot overflow either.
    /// </para>
    /// <para>
    /// If the width is not positive the area encloses nothing and no point is legal; the area's
    /// origin is returned so the caller still receives a defined, finite anchor.
    /// </para>
    /// </remarks>
    private static int ClampIntoArea(int value, int low, int highExclusive)
    {
        long highest = (long)highExclusive - 1;
        if (highest < low)
        {
            return low;
        }

        return (int)Math.Clamp((long)value, low, highest);
    }
}
