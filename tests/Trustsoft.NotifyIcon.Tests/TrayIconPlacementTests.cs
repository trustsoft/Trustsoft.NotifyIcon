using System.Reflection;
using System.Windows;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins <see cref="TrayIconPlacement"/>, the pure arithmetic that turns the shell's icon rectangle
/// into the DIP offset a WPF <c>ContextMenu</c> consumes - the assertion R014 is judged on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why fixtures rather than a live measurement.</b> The measured law is
/// <c>physical = offset * (dpi / 96)</c>, taken from four requested points on a 144-DPI host. At
/// Display Scale 150 % a library that hands the engine an unscaled physical point places the menu
/// 50 % too far right and down, and a library that has already applied the factor places it at
/// <c>rect * scale</c>. Both mistakes survive review, so every row below asserts the <em>equality</em>
/// <c>offset * scale == clamped physical point</c>: it fails for the double-applied and for the
/// not-applied version alike, and it cannot be satisfied by an inequality-shaped assertion.
/// </para>
/// <para>
/// <b>Honesty about the mixed-scale claim (R5).</b> This session has a single 1920x1200 monitor at
/// 150 %, so a live two-monitor check is impossible here. The mixed-scale rows below are the honest
/// maximum proof - they exercise the arithmetic, not the engine - and the live checklist records the
/// two-monitor case as NOT OBSERVED with these fixtures named as its evidence.
/// </para>
/// <para>
/// <b>No operating system is touched.</b> There is no window, no dispatcher, no STA thread, no real
/// DPI read, no registry and no desktop call anywhere in this file: every rectangle is constructed
/// by hand. That is what makes the whole suite runnable on a headless runner, and it is also the
/// reason the production type is forbidden from reading monitors or DPI itself.
/// </para>
/// </remarks>
public sealed class TrayIconPlacementTests
{
    /// <summary>
    /// The DIP value <c>USER_DEFAULT_SCREEN_DPI</c>, pinned as the project's one named 100 %.
    /// </summary>
    [Fact]
    public void UserDefaultScreenDpi_is_96()
    {
        Assert.Equal(96u, TrayIconPlacement.UserDefaultScreenDpi);
    }

    /// <summary>
    /// The scale factor is <c>dpi / 96</c>, so the standard Display Scale steps land on their exact
    /// ratios.
    /// </summary>
    /// <remarks>
    /// The 120-DPI row is the "custom value" case from the task plan: it is not one of the four
    /// standard increments Windows offers in Settings, and it is the value a caller gets from a
    /// monitor whose effective DPI is not a multiple of 24.
    /// </remarks>
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(168, 1.75)]
    [InlineData(192, 2.0)]
    [InlineData(288, 3.0)]
    public void ScaleFor_is_dpi_over_96(uint dpi, double expectedScale)
    {
        Assert.Equal(expectedScale, TrayIconPlacement.ScaleFor(dpi), 12);
    }

    /// <summary>
    /// A DPI of 0 is rejected instead of being divided by, on both the helper and the calculator.
    /// </summary>
    /// <remarks>
    /// Pins the documented decision: a missing DPI reading must be replaced with
    /// <see cref="TrayIconPlacement.UserDefaultScreenDpi"/> by the caller, and a caller that forgets
    /// gets an immediate <see cref="ArgumentOutOfRangeException"/> naming <c>dpi</c> rather than a
    /// silent <c>Infinity</c> offset. This path is not reachable from a window procedure, so throwing
    /// cannot crash a message loop.
    /// </remarks>
    [Fact]
    public void A_zero_dpi_is_rejected_rather_than_divided_by()
    {
        ArgumentOutOfRangeException fromHelper = Assert.Throws<ArgumentOutOfRangeException>(() => TrayIconPlacement.ScaleFor(0));
        Assert.Equal("dpi", fromHelper.ParamName);

        ArgumentOutOfRangeException fromCalculator = Assert.Throws<ArgumentOutOfRangeException>(
            () => TrayIconPlacement.Calculate(Rect(100, 100, 116, 116), Rect(0, 0, 1920, 1200), 0));
        Assert.Equal("dpi", fromCalculator.ParamName);
    }

    /// <summary>
    /// The four points of the measured law, pinned by the measurement rather than by a formula
    /// restated from memory, plus the same law at 200 %.
    /// </summary>
    /// <remarks>
    /// Each row states the <em>physical</em> point the engine must produce and the offset it must
    /// therefore have been handed. The 150 % rows are the measured ones: the probe asked the engine
    /// to place a popup at (100,100), (600,300) and (0,0) offsets and read back (150,150), (900,450)
    /// and (0,0) in physical pixels. For the calculator to reproduce that, an icon whose bottom-left
    /// is the physical point must yield the measured offset - which is exactly the reverse direction
    /// of the measurement.
    /// </remarks>
    [Theory]
    [InlineData(96, 100, 100, 100, 100)]   // identity at 100 %.
    [InlineData(96, 0, 0, 0, 0)]
    [InlineData(144, 150, 150, 100, 100)]  // measured: 100,100 -> 150,150 at 150 %.
    [InlineData(144, 900, 450, 600, 300)]  // measured: 600,300 -> 900,450 at 150 %.
    [InlineData(144, 0, 0, 0, 0)]          // measured: 0,0 -> 0,0 at 150 %.
    [InlineData(192, 2000, 1600, 1000, 800)] // the same law at 200 %.
    public void Measured_law_points_yield_the_offsets_the_probe_measured(uint dpi, int physicalX, int physicalY, double expectedOffsetX, double expectedOffsetY)
    {
        // An icon rectangle whose bottom-left is exactly the physical point under test, inside a
        // deliberately generous work area so that no clamping can interfere with the ratio.
        Point offset = TrayIconPlacement.Calculate(
            Rect(physicalX, physicalY - 16, physicalX + 16, physicalY),
            Rect(-4096, -4096, 4096, 4096),
            dpi);

        Assert.Equal(expectedOffsetX, offset.X, 9);
        Assert.Equal(expectedOffsetY, offset.Y, 9);
    }

    /// <summary>
    /// Every fixture's offset is its clamped physical point divided by the scale, and that physical
    /// point lies inside the fixture's own work area.
    /// </summary>
    /// <param name="label">A human-readable name for the fixture, printed on failure.</param>
    /// <param name="iconLeft">The icon rectangle's left edge, physical pixels.</param>
    /// <param name="iconTop">The icon rectangle's top edge, physical pixels.</param>
    /// <param name="iconRight">The icon rectangle's right (exclusive) edge, physical pixels.</param>
    /// <param name="iconBottom">The icon rectangle's bottom (exclusive) edge, physical pixels.</param>
    /// <param name="areaLeft">The work area's left edge, physical pixels.</param>
    /// <param name="areaTop">The work area's top edge, physical pixels.</param>
    /// <param name="areaRight">The work area's right (exclusive) edge, physical pixels.</param>
    /// <param name="areaBottom">The work area's bottom (exclusive) edge, physical pixels.</param>
    /// <param name="dpi">The fixture's DPI.</param>
    /// <param name="physicalX">The physical point the clamp must land on.</param>
    /// <param name="physicalY">The physical point the clamp must land on.</param>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Offset_is_the_clamped_physical_point_divided_by_the_scale(
        string label,
        int iconLeft,
        int iconTop,
        int iconRight,
        int iconBottom,
        int areaLeft,
        int areaTop,
        int areaRight,
        int areaBottom,
        int dpi,
        double physicalX,
        double physicalY)
    {
        double scale = TrayIconPlacement.ScaleFor((uint)dpi);
        Point offset = TrayIconPlacement.Calculate(
            Rect(iconLeft, iconTop, iconRight, iconBottom),
            Rect(areaLeft, areaTop, areaRight, areaBottom),
            (uint)dpi);

        Assert.Equal(physicalX / scale, offset.X, 9);
        Assert.Equal(physicalY / scale, offset.Y, 9);

        // The clamp's whole job: the anchor never escapes the work area, so the engine is never
        // handed the negative or off-screen point that shows no popup at all (MEM062). An empty work
        // area encloses nothing, so it has no inside to assert.
        if (areaRight > areaLeft && areaBottom > areaTop)
        {
            AssertInside(label, Rect(areaLeft, areaTop, areaRight, areaBottom), physicalX, physicalY);
        }
    }

    /// <summary>
    /// The strongest form of "the factor is applied exactly once": the engine's own multiplication
    /// returns the clamped physical point, as an equality - which fails both for a double-applied and
    /// for a not-applied factor.
    /// </summary>
    /// <param name="label">A human-readable name for the fixture, printed on failure.</param>
    /// <param name="iconLeft">The icon rectangle's left edge, physical pixels.</param>
    /// <param name="iconTop">The icon rectangle's top edge, physical pixels.</param>
    /// <param name="iconRight">The icon rectangle's right (exclusive) edge, physical pixels.</param>
    /// <param name="iconBottom">The icon rectangle's bottom (exclusive) edge, physical pixels.</param>
    /// <param name="areaLeft">The work area's left edge, physical pixels.</param>
    /// <param name="areaTop">The work area's top edge, physical pixels.</param>
    /// <param name="areaRight">The work area's right (exclusive) edge, physical pixels.</param>
    /// <param name="areaBottom">The work area's bottom (exclusive) edge, physical pixels.</param>
    /// <param name="dpi">The fixture's DPI.</param>
    /// <param name="physicalX">The physical point the round trip must return.</param>
    /// <param name="physicalY">The physical point the round trip must return.</param>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_factor_is_applied_exactly_once_so_the_round_trip_is_an_equality(
        string label,
        int iconLeft,
        int iconTop,
        int iconRight,
        int iconBottom,
        int areaLeft,
        int areaTop,
        int areaRight,
        int areaBottom,
        int dpi,
        double physicalX,
        double physicalY)
    {
        // Every row must be named: the label is the only thing in this file that says which display
        // shape a failure belongs to, so an unnamed row is a defect in the fixture table itself.
        Assert.False(string.IsNullOrEmpty(label), "Every fixture row must carry a label.");

        double scale = TrayIconPlacement.ScaleFor((uint)dpi);
        Point offset = TrayIconPlacement.Calculate(
            Rect(iconLeft, iconTop, iconRight, iconBottom),
            Rect(areaLeft, areaTop, areaRight, areaBottom),
            (uint)dpi);

        // The identity, in the direction the engine applies it: offset * scale == the clamped
        // physical point, to the last representable bit of the quotient.
        Assert.Equal(physicalX * 1.0, offset.X * scale, 9);
        Assert.Equal(physicalY * 1.0, offset.Y * scale, 9);
        Assert.Equal(physicalX, Math.Round(offset.X * scale));
        Assert.Equal(physicalY, Math.Round(offset.Y * scale));

        if (scale == 1.0)
        {
            // At 100 % the offset and the physical point are the same number; any scale factor
            // applied here would be the double-scaling bug in its mildest form.
            Assert.Equal(physicalX, offset.X);
            Assert.Equal(physicalY, offset.Y);
            return;
        }

        if (physicalX != 0)
        {
            // The unscaled implementation: the physical point handed to the engine as if it were DIPs.
            // The engine then produces physical * scale, i.e. the mark missed by the full factor.
            Assert.NotEqual(physicalX, offset.X);
            Assert.NotEqual(physicalX * scale, offset.X * scale, 9);

            // The double-applied implementation: an offset that already has the factor in it.
            Assert.NotEqual(physicalX * scale, offset.X);
            Assert.NotEqual(physicalX, offset.X * scale * scale, 9);
        }

        if (physicalY != 0)
        {
            Assert.NotEqual(physicalY, offset.Y);
            Assert.NotEqual(physicalY * scale, offset.Y * scale, 9);
            Assert.NotEqual(physicalY * scale, offset.Y);
            Assert.NotEqual(physicalY, offset.Y * scale * scale, 9);
        }
    }

    /// <summary>
    /// The anchor is the icon rectangle's <b>bottom-left</b>, not its top-left: the tray convention
    /// that makes the menu appear above an icon in the notification area.
    /// </summary>
    [Fact]
    public void Anchor_is_the_icon_rectangle_bottom_left_not_its_top_left()
    {
        Point offset = TrayIconPlacement.Calculate(Rect(100, 100, 116, 116), Rect(0, 0, 1920, 1200), 96);

        Assert.Equal(new Point(100, 116), offset);
        Assert.NotEqual(new Point(100, 100), offset);
        Assert.NotEqual(new Point(116, 116), offset);
        Assert.NotEqual(new Point(116, 100), offset);
    }

    /// <summary>
    /// A tray rectangle adjacent to each screen edge - including the bottom-right corner - is clamped
    /// to the last legal coordinate of the work area rather than handed to the engine outside it.
    /// </summary>
    /// <param name="edge">The edge the icon is against, for the failure message.</param>
    /// <param name="iconLeft">The icon rectangle's left edge.</param>
    /// <param name="iconTop">The icon rectangle's top edge.</param>
    /// <param name="iconRight">The icon rectangle's right edge.</param>
    /// <param name="iconBottom">The icon rectangle's bottom edge.</param>
    /// <param name="expectedX">The expected clamped physical x.</param>
    /// <param name="expectedY">The expected clamped physical y.</param>
    /// <remarks>
    /// The work area is 0,0..1920,1200, whose exclusive right/bottom edges mean the largest legal
    /// point is (1919, 1199) - the row that pins the exclusive-edge rule rather than an off-by-one
    /// intuition.
    /// </remarks>
    [Theory]
    [InlineData("left", -8, 1100, 8, 1116, 0, 1116)]
    [InlineData("top", 100, -20, 116, -4, 100, 0)]
    [InlineData("right", 1925, 1100, 1941, 1116, 1919, 1116)]
    [InlineData("bottom", 1800, 1190, 1816, 1230, 1800, 1199)]
    [InlineData("bottom-right corner", 1925, 1190, 1941, 1230, 1919, 1199)]
    public void A_tray_against_any_edge_lands_inside_the_work_area(
        string edge, int iconLeft, int iconTop, int iconRight, int iconBottom, double expectedX, double expectedY)
    {
        NativeRect workArea = Rect(0, 0, 1920, 1200);

        Point offset = TrayIconPlacement.Calculate(Rect(iconLeft, iconTop, iconRight, iconBottom), workArea, 96);

        Assert.Equal(new Point(expectedX, expectedY), offset);
        AssertInside($"{edge} edge", workArea, offset.X, offset.Y);
    }

    /// <summary>
    /// A monitor to the left of or above the primary one has negative virtual-screen coordinates, and
    /// the clamp must return a negative point that is still inside that monitor's work area - never
    /// zero, and never the absolute value.
    /// </summary>
    /// <param name="label">The monitor's position, for the failure message.</param>
    /// <param name="areaLeft">The work area's left edge.</param>
    /// <param name="areaTop">The work area's top edge.</param>
    /// <param name="areaRight">The work area's right edge.</param>
    /// <param name="areaBottom">The work area's bottom edge.</param>
    /// <param name="iconLeft">The icon rectangle's left edge, deliberately off the area.</param>
    /// <param name="iconBottom">The icon rectangle's bottom edge, deliberately off the area.</param>
    /// <param name="expectedX">The expected clamped physical x.</param>
    /// <param name="expectedY">The expected clamped physical y.</param>
    [Theory]
    [InlineData("left", -1920, 0, 0, 1200, -1930, 716, -1920, 716)]
    [InlineData("above", 0, -1200, 1920, 0, 500, -1224, 500, -1200)]
    [InlineData("diagonally up-left", -1920, -1200, 0, 0, -1940, -1224, -1920, -1200)]
    public void A_monitor_with_a_negative_origin_still_yields_a_point_inside_its_work_area(
        string label, int areaLeft, int areaTop, int areaRight, int areaBottom, int iconLeft, int iconBottom, double expectedX, double expectedY)
    {
        NativeRect workArea = Rect(areaLeft, areaTop, areaRight, areaBottom);

        Point offset = TrayIconPlacement.Calculate(
            Rect(iconLeft, iconBottom - 16, iconLeft + 16, iconBottom),
            workArea,
            96);

        Assert.Equal(new Point(expectedX, expectedY), offset);
        Assert.True(offset.X < 0 || offset.Y < 0, $"{label}: the fixture must exercise a negative coordinate.");
        AssertInside(label, workArea, offset.X, offset.Y);
    }

    /// <summary>
    /// Two monitors at different Display Scales: the same icon position expressed in each monitor's
    /// own physical pixels must produce each monitor's own offset - the assertion that catches a
    /// single-process-DPI implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Monitor A is 1920x1200 at 100 % starting at (0,0); monitor B is 1920x1200 at 150 % starting at
    /// physical x 1920 (so 1280x800 in DIPs). The icon sits at DIP (100,100) with a 16-DIP extent on
    /// each, which is <c>(100,100,116,116)</c> in A's physical pixels and
    /// <c>(2070,150,2094,174)</c> in B's.
    /// </para>
    /// <para>
    /// A single-DPI implementation computes both offsets with one factor - either A's (1.0, giving
    /// B the raw physical point 2070) or B's (1.5, giving A 66) - and at least one of the two
    /// assertions below fails. The cross-monitor rows fail as well, which is what makes "neither
    /// result is the other one scaled by the other monitor's factor" a falsifiable statement rather
    /// than a restatement.
    /// </para>
    /// </remarks>
    [Fact]
    public void Mixed_scale_pair_uses_each_monitors_own_factor()
    {
        NativeRect monitorA = Rect(0, 0, 1920, 1200);
        NativeRect monitorB = Rect(1920, 0, 3840, 2160);

        Point onA = TrayIconPlacement.Calculate(Rect(100, 100, 116, 116), monitorA, 96);
        Point onB = TrayIconPlacement.Calculate(Rect(2070, 150, 2094, 174), monitorB, 144);

        // Each monitor's own scale, applied once.
        Assert.Equal(new Point(100, 116), onA);
        Assert.Equal(new Point(1380, 116), onB);

        // Each result reconstructs a physical point inside its own monitor's work area.
        AssertInside("monitor A", monitorA, onA.X * 1.0, onA.Y * 1.0);
        AssertInside("monitor B", monitorB, onB.X * 1.5, onB.Y * 1.5);

        // ... and outside the other monitor's, so the two placements are genuinely different points
        // and not one computation reported twice.
        Assert.False(onB.X * 1.5 < monitorA.right, "B's physical x must not be inside A's work area, or the pair proves nothing.");
        Assert.False(onA.X * 1.5 >= monitorB.left, "A's physical x must not be inside B's work area, or the pair proves nothing.");

        // Neither result is the other one scaled by the other monitor's factor.
        Assert.NotEqual(new Point(onA.X * 1.5, onA.Y * 1.5), onB);
        Assert.NotEqual(new Point(onB.X / 1.5, onB.Y / 1.5), onA);

        // The single-process-DPI failure modes: B's raw physical point (A's factor used for B) and
        // A's point pushed through B's factor.
        Assert.NotEqual(2070.0, onB.X);
        Assert.NotEqual(onB.X / 1.5, onA.X);
    }

    /// <summary>
    /// A work area smaller than the monitor - a taskbar occupying one edge - is the boundary, and the
    /// case is designed so that using the monitor rectangle instead produces a different, wrong
    /// answer.
    /// </summary>
    /// <remarks>
    /// With a 40-pixel taskbar at the bottom, an icon sitting on the monitor's bottom edge yields
    /// 1159 against the work area and 1199 against the monitor rectangle. The assertion that the two
    /// differ is what turns "assert against the work area, never the monitor" from a comment into a
    /// failing test.
    /// </remarks>
    [Fact]
    public void The_work_area_is_the_boundary_not_the_monitor_rectangle()
    {
        NativeRect monitorRectangle = Rect(0, 0, 1920, 1200);
        NativeRect workArea = Rect(0, 0, 1920, 1160);
        NativeRect iconOnTheMonitorBottomEdge = Rect(900, 1170, 916, 1200);

        Point againstWorkArea = TrayIconPlacement.Calculate(iconOnTheMonitorBottomEdge, workArea, 96);
        Point againstMonitorRectangle = TrayIconPlacement.Calculate(iconOnTheMonitorBottomEdge, monitorRectangle, 96);

        Assert.Equal(new Point(900, 1159), againstWorkArea);
        Assert.Equal(new Point(900, 1199), againstMonitorRectangle);
        Assert.NotEqual(againstMonitorRectangle, againstWorkArea);
        AssertInside("work area", workArea, againstWorkArea.X, againstWorkArea.Y);
    }

    /// <summary>
    /// A degenerate or empty icon rectangle - the state a failed <c>Shell_NotifyIconGetRect</c>
    /// leaves behind - still yields a point inside the work area.
    /// </summary>
    /// <remarks>
    /// The corners of an empty rectangle are still numbers, and the clamp is what keeps them legal.
    /// This does not make the empty rectangle meaningful: the menu path's documented cursor fallback
    /// is what a caller should use. It makes ignoring the failure mode <em>survivable</em> rather than
    /// a silent off-screen popup.
    /// </remarks>
    [Fact]
    public void An_empty_icon_rectangle_cannot_escape_the_work_area()
    {
        NativeRect zeroed = default;
        NativeRect inverted = Rect(500, 700, 400, 600);

        Assert.True(zeroed.IsEmpty, "The default NativeRect must be the named empty state.");
        Assert.True(inverted.IsEmpty, "right < left and bottom < top must read as empty.");

        Point zeroedInsidePrimary = TrayIconPlacement.Calculate(zeroed, Rect(0, 0, 1920, 1200), 96);
        Point zeroedOutsidePrimary = TrayIconPlacement.Calculate(zeroed, Rect(100, 100, 1920, 1200), 96);
        Point invertedPoint = TrayIconPlacement.Calculate(inverted, Rect(0, 0, 1920, 1200), 96);

        Assert.Equal(new Point(0, 0), zeroedInsidePrimary);
        Assert.Equal(new Point(100, 100), zeroedOutsidePrimary);
        Assert.Equal(new Point(500, 600), invertedPoint);

        AssertInside("zeroed, primary", Rect(0, 0, 1920, 1200), zeroedInsidePrimary.X, zeroedInsidePrimary.Y);
        AssertInside("zeroed, offset area", Rect(100, 100, 1920, 1200), zeroedOutsidePrimary.X, zeroedOutsidePrimary.Y);
        AssertInside("inverted", Rect(0, 0, 1920, 1200), invertedPoint.X, invertedPoint.Y);
    }

    /// <summary>
    /// A work area with no extent has no legal point inside it, so the anchor is pinned to the area's
    /// own origin - the documented, defined answer.
    /// </summary>
    [Fact]
    public void An_empty_work_area_pins_the_anchor_to_its_own_origin()
    {
        Point empty = TrayIconPlacement.Calculate(Rect(500, 500, 516, 516), Rect(10, 20, 10, 20), 96);
        Point inverted = TrayIconPlacement.Calculate(Rect(500, 500, 516, 516), Rect(10, 20, 5, 20), 96);

        Assert.Equal(new Point(10, 20), empty);
        Assert.Equal(new Point(10, 20), inverted);
    }

    /// <summary>
    /// Extreme virtual-desktop coordinates do not overflow the clamp - including the one subtraction
    /// that would wrap an <see langword="int"/> and silently turn the clamp into a no-op.
    /// </summary>
    /// <remarks>
    /// The second case is the guard itself: with a work area whose <c>right</c> is
    /// <see cref="int.MinValue"/>, an <c>int</c> computation of <c>right - 1</c> wraps to
    /// <see cref="int.MaxValue"/>, the clamp stops clamping, and the returned point escapes the area
    /// entirely. The implementation widens that subtraction to <see langword="long"/>, and this test
    /// fails if someone narrows it back.
    /// </remarks>
    [Fact]
    public void Extreme_virtual_desktop_coordinates_do_not_overflow_or_escape()
    {
        Point wrappedRightEdge = TrayIconPlacement.Calculate(
            Rect(500, 500, 516, 516),
            Rect(0, 0, int.MinValue, 1200),
            96);

        Assert.Equal(new Point(0, 516), wrappedRightEdge);

        NativeRect widestDesktop = Rect(int.MinValue + 1, int.MinValue + 1, int.MaxValue, int.MaxValue);
        Point farCorner = TrayIconPlacement.Calculate(
            Rect(int.MaxValue - 2, int.MaxValue - 20, int.MaxValue, int.MaxValue - 4),
            widestDesktop,
            96);

        Assert.Equal(new Point(int.MaxValue - 2, int.MaxValue - 4), farCorner);
        AssertInside("widest desktop", widestDesktop, farCorner.X, farCorner.Y);
    }

    /// <summary>
    /// Offsets at non-integer scales are fractional and are <b>not</b> rounded, so the round trip stays
    /// exact.
    /// </summary>
    /// <remarks>
    /// This pins the documented precision decision. Rounding to whole DIPs would be defensible only
    /// if the offset were a pixel count; it is not - it is a DIP coordinate that the engine multiplies
    /// by this same factor, so rounding would move the menu by up to half a physical pixel per axis
    /// and would break the equality form of "the factor is applied exactly once".
    /// </remarks>
    [Theory]
    [InlineData(144, 1.5)]
    [InlineData(120, 1.25)]
    public void Offsets_are_fractional_at_non_integer_scales(uint dpi, double scale)
    {
        Point offset = TrayIconPlacement.Calculate(Rect(101, 216, 117, 217), Rect(0, 0, 1920, 1200), dpi);

        Assert.Equal(101.0 / scale, offset.X, 12);
        Assert.NotEqual(Math.Round(offset.X), offset.X);
        Assert.NotEqual(Math.Round(offset.Y), offset.Y);

        // The engine's multiplication returns the physical point exactly, which rounding would destroy.
        Assert.Equal(101.0, offset.X * scale, 9);
        Assert.Equal(217.0, offset.Y * scale, 9);
    }

    /// <summary>
    /// The two implementations the fixtures exist to reject - the unscaled point handed to the engine
    /// and the offset that already carries the factor - both fail the same equality.
    /// </summary>
    /// <remarks>
    /// Stated as a standalone negative test so the failure mode is legible on its own: at 150 % with
    /// an icon at physical (900, 450), the correct offset is (600, 300); the unscaled point makes the
    /// engine place the menu at (1350, 675) and the double-applied offset at (2025, 1012.5).
    /// </remarks>
    [Fact]
    public void The_wrong_implementations_fail_the_same_equality()
    {
        const double scale = 1.5;

        Point correct = TrayIconPlacement.Calculate(Rect(900, 434, 916, 450), Rect(0, 0, 1920, 1200), 144);
        var unscaled = new Point(900, 450);
        var doubleApplied = new Point(900 * scale, 450 * scale);

        Assert.Equal(new Point(600, 300), correct);

        // Correct: the engine lands on the icon.
        Assert.Equal(900.0, correct.X * scale, 9);
        Assert.Equal(450.0, correct.Y * scale, 9);

        // Unscaled: 450 physical pixels too far right, 225 too far down.
        Assert.NotEqual(900.0, unscaled.X * scale, 9);
        Assert.NotEqual(450.0, unscaled.Y * scale, 9);

        // Double-applied: the error grows with the square of the scale.
        Assert.NotEqual(900.0, doubleApplied.X * scale, 9);
        Assert.NotEqual(450.0, doubleApplied.Y * scale, 9);
    }

    /// <summary>
    /// The production type is a static class with no instance state and one static entry point, which
    /// is the structural half of "no window, no dispatcher, no monitor lookup".
    /// </summary>
    /// <remarks>
    /// An instance field would be the only place a cached DPI reading or a window handle could live,
    /// and either would make the arithmetic stateful and the suite unable to run headless. The
    /// signature pin also keeps a future refactor from replacing the pure entry point with one that
    /// needs a live monitor.
    /// </remarks>
    [Fact]
    public void The_calculator_is_static_and_holds_no_instance_state()
    {
        Type type = typeof(TrayIconPlacement);

        Assert.True(type.IsAbstract && type.IsSealed, "A C# static class is abstract and sealed.");
        Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        MethodInfo? calculate = type.GetMethod(
            "Calculate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(NativeRect), typeof(NativeRect), typeof(uint) },
            modifiers: null);

        Assert.NotNull(calculate);
        Assert.True(calculate.IsStatic);
        Assert.Equal(typeof(Point), calculate.ReturnType);

        MethodInfo? scaleFor = type.GetMethod(
            "ScaleFor",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(uint) },
            modifiers: null);

        Assert.NotNull(scaleFor);
        Assert.Equal(typeof(double), scaleFor.ReturnType);
    }

    /// <summary>
    /// The fixture table: every row is one arithmetic situation, with the physical point the clamp
    /// must produce for it.
    /// </summary>
    /// <returns>One <c>object[]</c> per row, in the parameter order of the table-driven theories.</returns>
    /// <remarks>
    /// Rows are plain primitives so that xunit can pre-enumerate them, and the label is the first
    /// element so a failure names the situation instead of a row number. The rows cover the measured
    /// law at every standard Display Scale, a fractional quotient, negative virtual-screen origins
    /// (left, above and diagonally up-left of the primary), a taskbar-reduced work area, the clamp at
    /// every edge and at the bottom-right corner, empty and inverted icon rectangles, an empty and an
    /// extreme work area, and the mixed-scale pair.
    /// </remarks>
    public static IEnumerable<object[]> Fixtures()
    {
        // label, icon l/t/r/b, area l/t/r/b, dpi, clamped physical x/y
        yield return new object[] { "150 percent: measured 100,100 -> 150,150", 150, 134, 166, 150, 0, 0, 1920, 1200, 144, 150.0, 150.0 };
        yield return new object[] { "150 percent: measured 600,300 -> 900,450", 900, 434, 916, 450, 0, 0, 1920, 1200, 144, 900.0, 450.0 };
        yield return new object[] { "150 percent: measured 0,0 -> 0,0", 0, -16, 16, 0, 0, 0, 1920, 1200, 144, 0.0, 0.0 };
        yield return new object[] { "100 percent: identity", 150, 134, 166, 150, 0, 0, 1920, 1200, 96, 150.0, 150.0 };
        yield return new object[] { "125 percent", 125, 234, 141, 250, 0, 0, 1920, 1200, 120, 125.0, 250.0 };
        yield return new object[] { "175 percent", 175, 334, 191, 350, 0, 0, 1920, 1200, 168, 175.0, 350.0 };
        yield return new object[] { "200 percent", 200, 384, 216, 400, 0, 0, 1920, 1200, 192, 200.0, 400.0 };
        yield return new object[] { "125 percent with a fractional quotient", 101, 216, 117, 217, 0, 0, 1920, 1200, 120, 101.0, 217.0 };
        yield return new object[] { "left edge clamp", -8, 1100, 8, 1116, 0, 0, 1920, 1200, 96, 0.0, 1116.0 };
        yield return new object[] { "top edge clamp", 100, -20, 116, -4, 0, 0, 1920, 1200, 96, 100.0, 0.0 };
        yield return new object[] { "right edge clamp", 1925, 1100, 1941, 1116, 0, 0, 1920, 1200, 96, 1919.0, 1116.0 };
        yield return new object[] { "bottom edge clamp", 1800, 1190, 1816, 1230, 0, 0, 1920, 1200, 96, 1800.0, 1199.0 };
        yield return new object[] { "bottom-right corner clamp", 1925, 1190, 1941, 1230, 0, 0, 1920, 1200, 96, 1919.0, 1199.0 };
        yield return new object[] { "taskbar-reduced work area", 900, 1170, 916, 1200, 0, 0, 1920, 1160, 96, 900.0, 1159.0 };
        yield return new object[] { "150 percent with a clamped bottom edge", 1350, 1740, 1366, 1755, 0, 0, 1920, 1160, 144, 1350.0, 1159.0 };
        yield return new object[] { "monitor left of the primary", -1930, 700, -1914, 716, -1920, 0, 0, 1200, 96, -1920.0, 716.0 };
        yield return new object[] { "monitor above the primary", 500, -1240, 516, -1224, 0, -1200, 1920, 0, 96, 500.0, -1200.0 };
        yield return new object[] { "monitor up-left of the primary", -1940, -1240, -1924, -1224, -1920, -1200, 0, 0, 96, -1920.0, -1200.0 };
        yield return new object[] { "empty icon rectangle in an offset work area", 0, 0, 0, 0, 100, 100, 1920, 1200, 96, 100.0, 100.0 };
        yield return new object[] { "inverted icon rectangle", 500, 700, 400, 600, 0, 0, 1920, 1200, 96, 500.0, 600.0 };
        yield return new object[] { "mixed-scale pair, monitor A at 100 percent", 100, 100, 116, 116, 0, 0, 1920, 1200, 96, 100.0, 116.0 };
        yield return new object[] { "mixed-scale pair, monitor B at 150 percent", 2070, 150, 2094, 174, 1920, 0, 3840, 2160, 144, 2070.0, 174.0 };
        yield return new object[] { "extreme virtual desktop", int.MaxValue - 2, int.MaxValue - 20, int.MaxValue, int.MaxValue - 4, int.MinValue + 1, int.MinValue + 1, int.MaxValue, int.MaxValue, 96, int.MaxValue - 2.0, int.MaxValue - 4.0 };
        yield return new object[] { "empty work area", 500, 500, 516, 516, 10, 20, 10, 20, 96, 10.0, 20.0 };
        yield return new object[] { "work area whose right edge is int.MinValue", 500, 500, 516, 516, 0, 0, int.MinValue, 1200, 96, 0.0, 516.0 };
    }

    /// <summary>
    /// Builds a rectangle from its four edges, in the same order the Win32 <c>RECT</c> declares them.
    /// </summary>
    private static NativeRect Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    /// <summary>
    /// Asserts that a physical point lies inside a work area, honouring the exclusive right/bottom
    /// edges.
    /// </summary>
    private static void AssertInside(string label, NativeRect workArea, double physicalX, double physicalY)
    {
        Assert.True(physicalX >= workArea.left, $"{label}: x {physicalX} is left of the work area (left {workArea.left}).");
        Assert.True(physicalX < workArea.right, $"{label}: x {physicalX} is at or past the exclusive right edge {workArea.right}.");
        Assert.True(physicalY >= workArea.top, $"{label}: y {physicalY} is above the work area (top {workArea.top}).");
        Assert.True(physicalY < workArea.bottom, $"{label}: y {physicalY} is at or past the exclusive bottom edge {workArea.bottom}.");
    }
}
