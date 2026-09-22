using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;
using Xunit.Abstractions;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The owner-determinism proof of M001/S08: the popup a menu open produces is owned by the library's
/// anchor window and dismissed by an outside click, in a session where Windows refuses this process
/// the foreground - and the ordinary click-driven path is unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class proves, and against what.</b> Every test opens its menu through the
/// <em>product's</em> own path - a registered <see cref="TrayIcon"/> with an assigned
/// <see cref="ContextMenu"/> receives the shell's version-4 <c>WM_CONTEXTMENU</c> callback by a
/// same-thread <c>SendMessage</c> (the S02 and T04 injection precedent) - so the anchor window, the
/// placement arithmetic, the foreground claim and the library's owner repair are the shipped ones.
/// The popup and the anchor are real windows, the outside click is real system input, and only the
/// shell seam answers are scripted. A hand-built popup is never used here: the raw WPF construction
/// is pinned as a negative in <c>TrayMenuDismissalTests</c>, where it belongs, because the library's
/// repair is not what that construction runs through.
/// </para>
/// <para>
/// <b>The hostile state is scripted, not hoped for.</b> <c>SetForegroundWindowResult = false</c> on
/// <see cref="FakeShellApi"/> makes the claim the library makes before the popup exists get refused
/// exactly the way Windows refuses it in an automation session, so the popup is built ownerless
/// (<c>owner=0x0</c>, the measured F5 shape) and the assertions no longer depend on whether this
/// machine grants the test process the foreground. D044's mechanism - the explicit owner write plus
/// the attach-thread foreground sequence - is what the library then does about it, and these tests
/// assert the delivered result rather than a test-local reconstruction of it.
/// </para>
/// <para>
/// <b>D043's bound is tightened to an equality here.</b> S03 asserted the popup's owner as "the
/// anchor window or absent" because WPF decided the value inside <c>Popup.BuildWindow</c> and the
/// value therefore flipped with the environment. The library now writes the owner itself, reads it
/// back, and records both readings (<see cref="TrayIcon.MenuOwnerBeforeRepair"/> and
/// <see cref="TrayIcon.MenuOwnerAfterRepair"/>), so these tests assert the anchor as an equality -
/// the one direction D043 named as permissible.
/// </para>
/// <para>
/// <b>No skip, anywhere.</b> A refused claim is the state the contract is written for, and a
/// conditional skip would be the masking this slice exists to remove: every clause below is
/// unconditional, including the dismissal.
/// </para>
/// <para>
/// The class joins <see cref="TrayMenuDismissalCollection"/> because it creates real top-level
/// windows, injects real input and reads the desktop-wide foreground state - the same reason the
/// probe and the dismissal proof are serialized with it.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayIconMenuOwnerDeterminismTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes the determinism proof and its raw-measurement output channel.</summary>
    /// <param name="output">The test output the raw measurement line is written to.</param>
    public TrayIconMenuOwnerDeterminismTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// With the foreground claim refused, a menu opened with no click is owned by the anchor window -
    /// as an equality, never as "the anchor or absent", and never by the shell registration host.
    /// </summary>
    /// <remarks>
    /// The two readings the assertion rests on are the library's own: the popup was built ownerless
    /// (WPF's construction, the hostile state reproduced) and the value that stands afterwards is the
    /// anchor's handle, read back from the OS rather than taken from the write's return value.
    /// </remarks>
    [StaFact]
    public void With_the_foreground_claim_refused_the_popup_is_owned_by_the_anchor_and_never_by_the_registration_host()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        // The hermetic refusal really happened: the library claimed the foreground through the seam
        // and the seam refused it, which is why the popup below had no owner of its own.
        Assert.True(result.ClaimObserved, $"The library must claim the foreground through the seam. {result.Describe()}");
        Assert.False(result.ClaimResults[0], $"The claim made before the popup existed must be refused. {result.Describe()}");

        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");

        IntPtr popup = Assert.Single(result.HeuristicPopupWindows);
        IntPtr anchor = result.AnchorHandleBeforeClick;

        Assert.NotEqual(IntPtr.Zero, anchor);

        // The equality, and the two windows it must not be confused with.
        Assert.Equal(anchor, result.OwnerMeasured);
        Assert.NotEqual(result.HostHandle, result.OwnerMeasured);
        Assert.NotEqual(IntPtr.Zero, result.OwnerMeasured);

        // The library's own readings: WPF built the popup ownerless and the library's write is what
        // made the anchor its owner.
        Assert.Equal(popup, result.PopupHandle);
        Assert.Equal(IntPtr.Zero, result.OwnerBeforeRepair);
        Assert.Equal(anchor, result.OwnerAfterRepair);
        Assert.True(result.OwnerRepaired, $"The owner write is what repaired this popup. {result.Describe()}");

        // ... and the activation half is in place too, so the popup is dismissable by the ordinary
        // route rather than only by the owner relationship.
        Assert.True(result.AnchorIsForeground, $"The repair must leave the anchor foreground. {result.Describe()}");
    }

    /// <summary>
    /// In the same refused state, an injected outside click leaves the menu closed and no visible
    /// popup window in the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the clause the consumer sees. It is asserted unconditionally, together with the two
    /// readings that explain it - the repaired owner and the anchor holding the desktop's foreground
    /// relationship - and with the teardown, because a dismissed menu must not leave its anchor
    /// window behind.
    /// </para>
    /// <para>
    /// <b>Both halves of D044's mechanism are in play here, and neither is credited alone.</b> The
    /// T01 measurement found the owner write by itself insufficient when it was applied to a popup
    /// that had already been shown (the V2 row of <c>docs/REMEDIATION-S08-MEASUREMENT.md</c>: the
    /// popup owned by the anchor, the outside click still leaving it open). In the delivered path the
    /// write happens inside the open, while the popup is being created, and the re-claim follows it -
    /// so this test proves the outcome the contract needs and the recorded measurement remains the
    /// evidence for why the mechanism carries both halves.
    /// </para>
    /// </remarks>
    [StaFact]
    public void With_the_foreground_claim_refused_an_outside_click_dismisses_the_menu_and_leaves_no_popup_window()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true);

        _output.WriteLine(result.Describe());

        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");

        // The refused claim was re-claimed and the anchor holds the foreground relationship the
        // dismissal is routed through; the owner half of the same repair is asserted by the test
        // above.
        Assert.True(result.AnchorIsForeground, $"The refused claim must be re-claimed. {result.Describe()}");
        Assert.Equal(result.AnchorHandleBeforeClick, result.ForegroundAfterRepair);

        // The clause that reaches the user.
        Assert.False(result.IsOpenAfterClick, $"An outside click must dismiss the repaired menu. {result.Describe()}");
        Assert.Empty(result.PopupWindowsAfterClick);

        // ... and the teardown destroyed the anchor rather than leaving it behind.
        Assert.Equal(IntPtr.Zero, result.AnchorHandle);
    }

    /// <summary>
    /// The click-driven path is unchanged: the claim is granted once and never re-made, WPF's own
    /// construction already owns the popup with the anchor, no repair is needed, and the placement is
    /// bit for bit the round trip the placement tests pin.
    /// </summary>
    /// <remarks>
    /// The "unchanged" clause is measured in three independent ways rather than asserted as an
    /// intention: the seam sees exactly one foreground claim (the pre-open one - a repair would add a
    /// second), the library reports that it wrote nothing, and the popup's owner was already the
    /// anchor before the repair ran. The placement half is the same arithmetic
    /// <c>TrayIconMenuActivationTests</c> pins: the offset the calculator produced, multiplied by the
    /// scale the engine applies, lands on the icon rectangle's bottom-left - where the anchor window
    /// sits.
    /// </remarks>
    [StaFact]
    public void The_click_driven_path_claims_once_needs_no_repair_and_is_placed_unchanged()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: false, grantLastInput: true);

        _output.WriteLine(result.Describe());

        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");

        // Exactly one claim, granted: the repair's second claim never happens on this path.
        Assert.Equal([true], result.ClaimResults);
        Assert.True(result.AnchorIsForeground, $"The anchor is foreground here. {result.Describe()}");
        Assert.False(result.OwnerRepaired, $"WPF already resolved the owner here. {result.Describe()}");

        // The owner is WPF's own decision - and it is the anchor, which is what the equality asserts.
        IntPtr anchor = result.AnchorHandleBeforeClick;

        Assert.NotEqual(IntPtr.Zero, anchor);
        Assert.Equal(anchor, result.OwnerBeforeRepair);
        Assert.Equal(anchor, result.OwnerMeasured);
        Assert.NotEqual(result.HostHandle, result.OwnerMeasured);

        // The placement round trip: the offset times the scale is the icon's bottom-left, the anchor
        // sits exactly there, and the menu is attached to the anchor's own visual.
        const double scale = TrayMenuProductScenario.ScriptedDpi / 96.0;

        Assert.Equal(MenuDeterminismResult.IconLeft / scale, result.HorizontalOffset, 6);
        Assert.Equal(MenuDeterminismResult.IconBottom / scale, result.VerticalOffset, 6);
        Assert.Equal(MenuDeterminismResult.IconLeft, result.AnchorRectangle.left);
        Assert.Equal(MenuDeterminismResult.IconBottom, result.AnchorRectangle.top);
        Assert.Equal(1, result.AnchorRectangle.right - result.AnchorRectangle.left);
        Assert.Equal(1, result.AnchorRectangle.bottom - result.AnchorRectangle.top);
        Assert.Equal(anchor, result.PlacementTargetWindow);

        // ... and the same popup rectangle: the popup is where the placement arithmetic put it.
        Assert.Equal(MenuDeterminismResult.IconLeft, result.PopupRectangle.left);
        Assert.Equal(MenuDeterminismResult.IconBottom, result.PopupRectangle.top);

        Assert.False(result.IsOpenAfterClick, $"An outside click must dismiss this menu. {result.Describe()}");
        Assert.Empty(result.PopupWindowsAfterClick);
    }

    /// <summary>
    /// With no window holding the foreground there is nothing to attach to, so the recorded last
    /// resort is taken: <c>SwitchToThisWindow</c> plus the claim, and the menu is still owned by the
    /// anchor and still dismissed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edge is not hypothetical: <c>AttachThreadInput</c> needs a second thread to join, and a
    /// desktop whose foreground window is <see cref="IntPtr.Zero"/> has none - which is why D044
    /// records <c>SwitchToThisWindow</c> as the last resort rather than as the primary route.
    /// </para>
    /// <para>
    /// The assertions separate the two branches rather than only checking the outcome: the switch is
    /// in the seam's log and no input queue was attached, so this cannot pass by taking the ordinary
    /// sequence.
    /// </para>
    /// </remarks>
    [StaFact]
    public void With_no_foreground_window_the_recorded_last_resort_repairs_the_owner_and_dismisses_the_menu()
    {
        MenuDeterminismResult result = TrayMenuProductScenario.Run(refuseClaim: true, scriptNoForegroundWindow: true);

        _output.WriteLine(result.Describe());

        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");

        // The last-resort branch, and not the attach-thread one: there was no thread to attach to.
        Assert.Contains(result.Calls, call => call.Operation == nameof(IShellApi.SwitchToThisWindow));
        Assert.DoesNotContain(result.Calls, call => call.Operation == nameof(IShellApi.AttachThreadInput));

        // The owner is still repaired and the menu is still dismissable.
        IntPtr anchor = result.AnchorHandleBeforeClick;

        Assert.NotEqual(IntPtr.Zero, anchor);
        Assert.True(result.OwnerRepaired, $"The owner must still be repaired here. {result.Describe()}");
        Assert.Equal(anchor, result.OwnerMeasured);
        Assert.True(result.AnchorIsForeground);
        Assert.False(result.IsOpenAfterClick, $"The repaired menu must still dismiss. {result.Describe()}");
        Assert.Empty(result.PopupWindowsAfterClick);
        Assert.Equal(IntPtr.Zero, result.AnchorHandle);
    }
}

/// <summary>
/// The raw readings of one menu open - and one outside click - taken through the product's own path
/// with the shell seam scripted.
/// </summary>
/// <param name="HostHandle">The shell registration host (never the owner).</param>
/// <param name="AnchorHandle">The anchor window's handle read after the click - zero when the dismissal tore it down.</param>
/// <param name="AnchorHandleBeforeClick">The anchor's handle read while the menu was open, before the injected click.</param>
/// <param name="PopupHandle">
/// The popup window the library resolved from the menu's own presentation source, as the library
/// recorded it.
/// </param>
/// <param name="PresentationSourcePopup">The window <c>PresentationSource.FromVisual(menu)</c> resolves to, read by the harness.</param>
/// <param name="HeuristicPopupWindows">This process's popup-sized visible top-level windows while the menu was open.</param>
/// <param name="OwnerBeforeRepair">The popup's <c>GW_OWNER</c> as WPF left it, read by the library.</param>
/// <param name="OwnerAfterRepair">The popup's <c>GW_OWNER</c> as read back by the library after its repair.</param>
/// <param name="OwnerMeasured">The popup's <c>GW_OWNER</c> read by the harness with <c>GetWindow(GW_OWNER)</c>.</param>
/// <param name="OwnerRepaired">Whether the library wrote the owner because WPF had not resolved the anchor.</param>
/// <param name="AnchorIsForeground">Whether the anchor was left holding the desktop's foreground relationship.</param>
/// <param name="ForegroundAfterRepair">The desktop's foreground window while the menu was open, after the repair.</param>
/// <param name="ClaimResults">Every <c>SetForegroundWindow</c> result the seam reported, oldest first.</param>
/// <param name="ClaimObserved">Whether the library made any foreground claim through the seam at all.</param>
/// <param name="Calls">Every recorded seam call, in order, for the branch assertions.</param>
/// <param name="AnchorRectangle">The anchor window's screen rectangle while the menu was open.</param>
/// <param name="PopupRectangle">The popup's screen rectangle while the menu was open.</param>
/// <param name="HorizontalOffset">The horizontal offset the library placed the menu at, in DIPs.</param>
/// <param name="VerticalOffset">The vertical offset the library placed the menu at, in DIPs.</param>
/// <param name="PlacementTargetWindow">The window the menu's placement target visual belongs to.</param>
/// <param name="IsOpenBeforeClick">The menu's <see cref="ContextMenu.IsOpen"/> immediately before the injected click.</param>
/// <param name="IsOpenAfterClick">The menu's <see cref="ContextMenu.IsOpen"/> after the injected click was pumped.</param>
/// <param name="PopupWindowsAfterClick">This process's popup-sized visible top-level windows after the click.</param>
internal sealed record MenuDeterminismResult(
    IntPtr HostHandle,
    IntPtr AnchorHandle,
    IntPtr AnchorHandleBeforeClick,
    IntPtr PopupHandle,
    IntPtr PresentationSourcePopup,
    IReadOnlyList<IntPtr> HeuristicPopupWindows,
    IntPtr OwnerBeforeRepair,
    IntPtr OwnerAfterRepair,
    IntPtr OwnerMeasured,
    bool OwnerRepaired,
    bool AnchorIsForeground,
    IntPtr ForegroundAfterRepair,
    IReadOnlyList<bool> ClaimResults,
    bool ClaimObserved,
    IReadOnlyList<ShellCall> Calls,
    NativeRect AnchorRectangle,
    NativeRect PopupRectangle,
    double HorizontalOffset,
    double VerticalOffset,
    IntPtr PlacementTargetWindow,
    bool IsOpenBeforeClick,
    bool IsOpenAfterClick,
    IReadOnlyList<IntPtr> PopupWindowsAfterClick)
{
    /// <summary>The scripted icon rectangle's left edge, in physical pixels.</summary>
    internal const int IconLeft = 100;

    /// <summary>The scripted icon rectangle's top edge, in physical pixels.</summary>
    internal const int IconTop = 200;

    /// <summary>The scripted icon rectangle's exclusive right edge, in physical pixels.</summary>
    internal const int IconRight = 116;

    /// <summary>The scripted icon rectangle's exclusive bottom edge - the placement anchor, in physical pixels.</summary>
    internal const int IconBottom = 216;

    /// <summary>Renders every reading as one line, for the test output and for failure messages.</summary>
    /// <returns>A single-line description of the measurement.</returns>
    internal string Describe() =>
        $"host=0x{HostHandle.ToInt64():X} anchor=0x{AnchorHandle.ToInt64():X} anchorBeforeClick=0x{AnchorHandleBeforeClick.ToInt64():X} " +
        $"libraryPopup=0x{PopupHandle.ToInt64():X} presentationSource=0x{PresentationSourcePopup.ToInt64():X} " +
        $"windows=[{string.Join(", ", HeuristicPopupWindows.Select(window => $"0x{window.ToInt64():X}"))}] " +
        $"ownerBefore=0x{OwnerBeforeRepair.ToInt64():X} ownerAfter=0x{OwnerAfterRepair.ToInt64():X} ownerMeasured=0x{OwnerMeasured.ToInt64():X} " +
        $"repaired={OwnerRepaired} anchorForeground={AnchorIsForeground} foregroundAfterRepair=0x{ForegroundAfterRepair.ToInt64():X} " +
        $"claims=[{string.Join(", ", ClaimResults)}] claimObserved={ClaimObserved} " +
        $"anchorRect=({AnchorRectangle.left},{AnchorRectangle.top},{AnchorRectangle.right - AnchorRectangle.left}x{AnchorRectangle.bottom - AnchorRectangle.top}) " +
        $"popupRect=({PopupRectangle.left},{PopupRectangle.top},{PopupRectangle.right - PopupRectangle.left}x{PopupRectangle.bottom - PopupRectangle.top}) " +
        $"offset=({HorizontalOffset:0.###},{VerticalOffset:0.###}) placementTarget=0x{PlacementTargetWindow.ToInt64():X} " +
        $"isOpenBefore={IsOpenBeforeClick} isOpenAfter={IsOpenAfterClick} " +
        $"windowsAfterClick=[{string.Join(", ", PopupWindowsAfterClick.Select(window => $"0x{window.ToInt64():X}"))}]";
}

/// <summary>
/// The product-path menu harness: it registers a tray icon over a scripted seam, injects the shell's
/// version-4 <c>WM_CONTEXTMENU</c> callback, measures the open, injects a real outside click and
/// reports everything it saw.
/// </summary>
/// <remarks>
/// <para>
/// It deliberately asserts nothing: every judgement lives in the test that calls it, and the two
/// scripted axes - whether the foreground claim is refused, and whether any window holds the
/// foreground at all - are parameters rather than different harnesses.
/// </para>
/// <para>
/// <b>Why it is shared.</b> The S08 probe (<c>TrayMenuOwnerMechanismProbeTests</c>) and the
/// determinism proof open the menu the same way and read the same facts; keeping the route in one
/// place is what makes "the probe measured the delivered path" true of both. This follows the
/// precedent of <c>TrayMenuScenario</c>, which the dismissal proof and the S01-to-S03 boundary
/// contract share.
/// </para>
/// </remarks>
internal static class TrayMenuProductScenario
{
    /// <summary>The scripted cursor x coordinate, in physical pixels.</summary>
    internal const int CursorX = 640;

    /// <summary>The scripted cursor y coordinate, in physical pixels.</summary>
    internal const int CursorY = 480;

    /// <summary>The scripted monitor work area's exclusive right edge, in physical pixels.</summary>
    internal const int WorkAreaRight = 1920;

    /// <summary>The scripted monitor work area's exclusive bottom edge, in physical pixels.</summary>
    internal const int WorkAreaBottom = 1080;

    /// <summary>The scripted monitor DPI: 144 is Display Scale 150 %, so the scale factor is 1.5.</summary>
    internal const uint ScriptedDpi = 144;

    /// <summary>How long the dispatcher is pumped after the open, before the popup is measured.</summary>
    internal const int PopupOpenMilliseconds = 600;

    /// <summary>How long the dispatcher is pumped after an injected click.</summary>
    internal const int ClickMilliseconds = 900;

    /// <summary>How long the dispatcher is pumped after a menu close so the popup window is really gone.</summary>
    internal const int PopupSettleMilliseconds = 400;

    /// <summary>
    /// Runs one product-path menu open, measures it, injects a real outside click and returns every
    /// reading.
    /// </summary>
    /// <param name="refuseClaim">
    /// <see langword="true"/> to script the foreground claim refused - the hermetic hostile state in
    /// which WPF builds the popup ownerless; <see langword="false"/> to let the claim go to Windows,
    /// which is the ordinary path when the process may take the foreground.
    /// </param>
    /// <param name="grantLastInput">
    /// <see langword="true"/> to give this process the right to set the foreground window first, the
    /// precondition <c>TrayIconMenuActivationTests</c> documents for an injected message on a real
    /// desktop.
    /// </param>
    /// <param name="scriptNoForegroundWindow">
    /// <see langword="true"/> to script the desktop as having no foreground window at all, which is
    /// the state that forces the recorded last-resort branch.
    /// </param>
    /// <returns>The measurement; see <see cref="MenuDeterminismResult"/> for each field.</returns>
    internal static MenuDeterminismResult Run(
        bool refuseClaim,
        bool grantLastInput = false,
        bool scriptNoForegroundWindow = false)
    {
        var shell = new FakeShellApi
        {
            GetRectResult = 0,
            GetRectRectangle = new NativeRect
            {
                left = MenuDeterminismResult.IconLeft,
                top = MenuDeterminismResult.IconTop,
                right = MenuDeterminismResult.IconRight,
                bottom = MenuDeterminismResult.IconBottom,
            },
            CursorPositionX = CursorX,
            CursorPositionY = CursorY,
            SetForegroundWindowResult = refuseClaim ? false : null,
            ForegroundWindowOverride = scriptNoForegroundWindow ? IntPtr.Zero : null,
        };

        var monitorInfo = new ScriptedMonitorInfoProvider
        {
            WorkArea = new NativeRect { left = 0, top = 0, right = WorkAreaRight, bottom = WorkAreaBottom },
            Dpi = ScriptedDpi,
        };

        var menu = new ContextMenu();

        // A menu with no items is suppressed by WPF, so an empty menu would prove nothing about
        // placement or ownership.
        menu.Items.Add(new MenuItem { Header = "Alpha" });

        using var trayIcon = new TrayIcon(shell, Dispatcher.CurrentDispatcher, monitorInfo)
        {
            ContextMenu = menu,
            Visible = true,
        };

        uint iconId = trayIcon.IconId;
        IntPtr host = trayIcon.HostHandle;

        try
        {
            if (grantLastInput)
            {
                Win32TestInput.GrantLastInputToThisProcess();
            }

            SendContextMenuCallback(trayIcon, iconId);
            TrayMenuScenario.Pump(PopupOpenMilliseconds);

            IReadOnlyList<IntPtr> popupWindows = TrayMenuScenario.FindPopupWindows();
            IntPtr popup = popupWindows.Count == 1 ? popupWindows[0] : IntPtr.Zero;
            IntPtr presentationSourcePopup = PresentationSource.FromVisual(menu) is HwndSource source
                ? source.Handle
                : IntPtr.Zero;

            NativeRect popupRectangle = default;
            NativeRect anchorRectangle = default;

            if (popup != IntPtr.Zero)
            {
                Win32.GetWindowRect(popup, out popupRectangle);
            }

            Win32.GetWindowRect(trayIcon.MenuAnchorHandle, out anchorRectangle);

            IntPtr ownerMeasured = popup != IntPtr.Zero ? Win32.GetWindowOwner(popup) : IntPtr.Zero;

            // Read while the menu is still open and the anchor is still alive: the placement target
            // is detached and the anchor destroyed by the dismissal, and the foreground reading
            // belongs to the state this open produced rather than to whatever the click activated.
            bool isOpenBefore = menu.IsOpen;
            IntPtr anchorBeforeClick = trayIcon.MenuAnchorHandle;
            IntPtr foregroundAfterRepair = Win32.GetForegroundWindow();
            IntPtr placementTargetWindow = GetPlacementTargetWindow(menu);

            (int cursorX, int cursorY) = GetCursorPosition();

            try
            {
                (int clickX, int clickY) = OutsideClickPoint(popupRectangle);

                Win32TestInput.SetCursorPosition(clickX, clickY);
                Win32TestInput.ClickRightThenLeft();
                TrayMenuScenario.Pump(ClickMilliseconds);
            }
            finally
            {
                Win32TestInput.SetCursorPosition(cursorX, cursorY);
            }

            return new MenuDeterminismResult(
                HostHandle: host,
                AnchorHandle: trayIcon.MenuAnchorHandle,
                AnchorHandleBeforeClick: anchorBeforeClick,
                PopupHandle: trayIcon.MenuPopupHandle,
                PresentationSourcePopup: presentationSourcePopup,
                HeuristicPopupWindows: popupWindows,
                OwnerBeforeRepair: trayIcon.MenuOwnerBeforeRepair,
                OwnerAfterRepair: trayIcon.MenuOwnerAfterRepair,
                OwnerMeasured: ownerMeasured,
                OwnerRepaired: trayIcon.MenuOwnerRepaired,
                AnchorIsForeground: trayIcon.MenuAnchorIsForeground,
                ForegroundAfterRepair: foregroundAfterRepair,
                ClaimResults: shell.SetForegroundWindowResults,
                ClaimObserved: shell.Calls.Any(call => call.Operation == nameof(IShellApi.SetForegroundWindow)),
                Calls: shell.Calls,
                AnchorRectangle: anchorRectangle,
                PopupRectangle: popupRectangle,
                HorizontalOffset: menu.HorizontalOffset,
                VerticalOffset: menu.VerticalOffset,
                PlacementTargetWindow: placementTargetWindow,
                IsOpenBeforeClick: isOpenBefore,
                IsOpenAfterClick: menu.IsOpen,
                PopupWindowsAfterClick: TrayMenuScenario.FindPopupWindows());
        }
        finally
        {
            if (menu.IsOpen)
            {
                menu.IsOpen = false;
                TrayMenuScenario.Pump(PopupSettleMilliseconds);
            }
        }
    }

    /// <summary>
    /// Sends one version-4 <c>WM_CONTEXTMENU</c> callback to the icon's own host window,
    /// synchronously.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the callback.</param>
    /// <param name="iconId">The icon id for <c>HIWORD(lParam)</c>.</param>
    private static void SendContextMenuCallback(TrayIcon trayIcon, uint iconId) =>
        Win32.SendMessage(
            trayIcon.HostHandle,
            ShellConstants.TrayCallbackMessage,

            // A zero anchor on purpose: the click's own anchor point is documented as undefined for
            // WM_CONTEXTMENU (D028), so the injection sends the least informative value there is.
            IntPtr.Zero,
            new IntPtr((ShellConstants.WM_CONTEXTMENU & 0xFFFF) | ((long)(iconId & 0xFFFF) << 16)));

    /// <summary>Resolves the window the menu's placement target visual belongs to.</summary>
    /// <param name="menu">The open menu.</param>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/> when it cannot be resolved.</returns>
    private static IntPtr GetPlacementTargetWindow(ContextMenu menu) =>
        menu.PlacementTarget is not null && PresentationSource.FromVisual(menu.PlacementTarget) is HwndSource source
            ? source.Handle
            : IntPtr.Zero;

    /// <summary>Chooses a point guaranteed to be outside the popup.</summary>
    /// <param name="popup">The popup's screen rectangle; may be empty when no popup opened.</param>
    /// <returns>A screen point in physical pixels.</returns>
    private static (int X, int Y) OutsideClickPoint(NativeRect popup)
    {
        const int margin = 200;

        return popup.left - margin > 0 && popup.top - margin > 0
            ? (popup.left - margin, popup.top - margin)
            : (popup.right + margin, popup.bottom + margin);
    }

    /// <summary>Reads the current cursor position, so the harness can put it back.</summary>
    /// <returns>The cursor position in screen pixels; <c>(0,0)</c> when the reading fails.</returns>
    private static (int X, int Y) GetCursorPosition()
    {
        Win32TestInput.POINT point = default;

        return Win32TestInput.GetCursorPosition(ref point) ? (point.X, point.Y) : (0, 0);
    }

    /// <summary>A monitor reader whose work area and DPI are scripted, so the geometry is fixed.</summary>
    private sealed class ScriptedMonitorInfoProvider : MonitorInfoProvider
    {
        /// <summary>Gets or sets the work area the scripted reading reports.</summary>
        internal NativeRect WorkArea { get; set; }

        /// <summary>Gets or sets the DPI the scripted reading reports.</summary>
        internal uint Dpi { get; set; } = TrayIconPlacement.UserDefaultScreenDpi;

        /// <inheritdoc />
        internal override bool TryGetWorkArea(NativeRect rectangle, out NativeRect workArea)
        {
            workArea = WorkArea;
            return true;
        }

        /// <inheritdoc />
        internal override bool TryGetDpi(NativeRect rectangle, out uint dpi)
        {
            dpi = Dpi;
            return true;
        }
    }
}
