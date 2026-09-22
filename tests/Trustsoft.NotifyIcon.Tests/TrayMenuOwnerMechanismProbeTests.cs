using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;
using Xunit.Abstractions;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The owner mechanism under measurement: how the library's own menu open should end up with a popup
/// that is owned by the anchor window and is dismissed by an outside click even when Windows refuses
/// this process the foreground.
/// </summary>
/// <remarks>
/// The four variants the S08 measurement named are reproduced here as the three the probe drives plus
/// the one it did not need. V4 - an in-library low-level mouse hook - is applied only if V2 and V3 both
/// fail to dismiss, and the measurement below shows V3 dismissing, so no test drives it; the
/// remediation document records that and why.
/// </remarks>
internal enum MenuOwnerMechanism
{
    /// <summary>
    /// V1, the control: the shipped construction with the foreground claim refused, and no repair at
    /// all. This is the state the S06 UAT measured and the state the five tests failed in.
    /// </summary>
    Shipped,

    /// <summary>
    /// V2: the shipped construction plus <c>GWLP_HWNDPARENT</c> set to the anchor handle after the
    /// popup exists.
    /// </summary>
    ExplicitOwner,

    /// <summary>
    /// V3, the chosen mechanism: V2 plus the attach-thread foreground sequence (attach to the
    /// foreground window's thread, <c>BringWindowToTop</c>, <c>SetForegroundWindow</c>, detach), with
    /// <c>SwitchToThisWindow</c> as the recorded last resort when there is no foreground window to
    /// attach to.
    /// </summary>
    AttachThreadForeground,
}

/// <summary>
/// The raw readings of one menu-open-and-outside-click measurement, as a value the tests assert on and
/// the remediation document records.
/// </summary>
/// <param name="Mechanism">The variant that was measured.</param>
/// <param name="HostHandle">The shell registration host (never the owner).</param>
/// <param name="AnchorHandle">The anchor window handle read after the click - zero when the dismissal tore it down.</param>
/// <param name="AnchorHandleBeforeClick">
/// The anchor handle read immediately before the injected click. Read here rather than after it
/// because a dismissed menu tears its anchor down, which is itself part of the measurement.
/// </param>
/// <param name="PopupHandle">The popup window resolved for this open, or zero when none opened.</param>
/// <param name="PopupResolution">How the popup was resolved (<c>presentationSource</c>, <c>heuristic</c> or <c>none</c>).</param>
/// <param name="HeuristicPopup">The single process window larger than 20x20 the size heuristic found.</param>
/// <param name="PresentationSourcePopup">The window <c>PresentationSource.FromVisual(menu)</c> resolved to.</param>
/// <param name="OwnerBeforeRepair">The popup's <c>GW_OWNER</c> before the mechanism was applied.</param>
/// <param name="OwnerAfterRepair">The popup's <c>GW_OWNER</c> after the mechanism was applied.</param>
/// <param name="ClaimGranted">The scripted <c>SetForegroundWindow</c> result the library received.</param>
/// <param name="ClaimCallObserved">Whether the library actually made the foreground claim through the seam.</param>
/// <param name="ForegroundBeforeOpen">The desktop foreground window just before the open.</param>
/// <param name="ForegroundAfterOpen">The desktop foreground window while the menu was open.</param>
/// <param name="ActiveWindowAfterOpen">This thread's active window while the menu was open.</param>
/// <param name="ForegroundAfterRepair">The desktop foreground window after the mechanism was applied.</param>
/// <param name="IsOpenBeforeClick">The menu's <c>IsOpen</c> immediately before the injected click.</param>
/// <param name="IsOpenAfterClick">The menu's <c>IsOpen</c> after the injected click was pumped.</param>
/// <param name="PopupWindowsAfterClick">This process's popup-sized windows after the click.</param>
/// <param name="ForegroundAfterClick">The desktop foreground window after the click.</param>
internal sealed record MenuOwnerProbeResult(
    MenuOwnerMechanism Mechanism,
    IntPtr HostHandle,
    IntPtr AnchorHandle,
    IntPtr AnchorHandleBeforeClick,
    IntPtr PopupHandle,
    string PopupResolution,
    IntPtr HeuristicPopup,
    IntPtr PresentationSourcePopup,
    IntPtr OwnerBeforeRepair,
    IntPtr OwnerAfterRepair,
    bool ClaimGranted,
    bool ClaimCallObserved,
    IntPtr ForegroundBeforeOpen,
    IntPtr ForegroundAfterOpen,
    IntPtr ActiveWindowAfterOpen,
    IntPtr ForegroundAfterRepair,
    bool IsOpenBeforeClick,
    bool IsOpenAfterClick,
    IReadOnlyList<IntPtr> PopupWindowsAfterClick,
    IntPtr ForegroundAfterClick)
{
    /// <summary>Renders the measurement as one line for the remediation document and failure messages.</summary>
    /// <returns>A single-line description of every measured field.</returns>
    internal string Describe() =>
        $"mechanism={Mechanism} host=0x{HostHandle.ToInt64():X} anchor=0x{AnchorHandle.ToInt64():X} " +
        $"anchorBeforeClick=0x{AnchorHandleBeforeClick.ToInt64():X} popup=0x{PopupHandle.ToInt64():X}({PopupResolution}) " +
        $"heuristic=0x{HeuristicPopup.ToInt64():X} presentationSource=0x{PresentationSourcePopup.ToInt64():X} " +
        $"setForegroundWindow={ClaimGranted} claimCallObserved={ClaimCallObserved} " +
        $"ownerBefore=0x{OwnerBeforeRepair.ToInt64():X} ownerAfter=0x{OwnerAfterRepair.ToInt64():X} " +
        $"foregroundBeforeOpen=0x{ForegroundBeforeOpen.ToInt64():X} foregroundAfterOpen=0x{ForegroundAfterOpen.ToInt64():X} " +
        $"activeWindowAfterOpen=0x{ActiveWindowAfterOpen.ToInt64():X} foregroundAfterRepair=0x{ForegroundAfterRepair.ToInt64():X} " +
        $"isOpenBefore={IsOpenBeforeClick} isOpenAfter={IsOpenAfterClick} " +
        $"windowsAfterClick=[{string.Join(", ", PopupWindowsAfterClick.Select(window => $"0x{window.ToInt64():X}"))}] " +
        $"foregroundAfterClick=0x{ForegroundAfterClick.ToInt64():X}";
}

/// <summary>
/// The S08 measurement probe: the same menu open measured three ways against real Windows with the
/// foreground claim refused hermetically through the shell seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class is for.</b> The five failing tests and findings F4 and F5 are one mechanism -
/// WPF decides the popup's owner from the foreground relationship at the instant the popup window is
/// built - and that mechanism had never been measured in a state a test can create on purpose. Here
/// the refusal is scripted through <see cref="IShellApi.SetForegroundWindow"/> (a
/// <see cref="FakeShellApi"/> that reports <see langword="false"/> while a foreign window is the
/// desktop's foreground), so the measurement no longer depends on whether the machine running the
/// suite happens to let the test process take the foreground. The state this creates is the recorded
/// hostile state: <c>setForegroundWindow=False</c>, <c>owner=0x0</c>.
/// </para>
/// <para>
/// <b>It opens the menu through the product's own path.</b> A registered <see cref="TrayIcon"/> with
/// an assigned <see cref="ContextMenu"/> receives the shell's <c>WM_CONTEXTMENU</c> callback by a
/// same-thread <c>SendMessage</c> (the S02/T04 injection precedent), so the anchor window, the
/// placement arithmetic and the foreground claim are the shipped ones. Only the shell answers are
/// scripted, and the popup is a real WPF popup window.
/// </para>
/// <para>
/// <b>The repairs are applied through the real seam.</b> V2 and V3 use a real
/// <see cref="ShellApi"/> over the real popup handle, so the measurement exercises
/// <see cref="IShellApi.SetWindowOwner"/> and <see cref="IShellApi.GetWindowOwner"/> against Windows
/// rather than a fake that would agree with anything.
/// </para>
/// <para>
/// <b>The V1 control is the point.</b> It reproduces the ownerless popup and the menu an outside click
/// leaves open. If it ever stops doing so, the hostile state is no longer reproduced and the failure
/// that motivated S08 is no longer pinned - which is a failure of this probe, not a pass.
/// </para>
/// <para>
/// <b>Every test writes its raw line to the test output channel, and every assertion failure carries
/// it too.</b> The numbers in <c>docs/REMEDIATION-S08-MEASUREMENT.md</c> come from a dump run of this
/// class whose exec id is cited there, so the document's values are re-derivable rather than
/// transcribed by hand.
/// </para>
/// <para>
/// The class joins <see cref="TrayMenuDismissalCollection"/>: it creates real top-level windows,
/// injects real mouse input and reads the desktop-wide foreground window, so it must not share the
/// process with another test class that does the same.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayMenuOwnerMechanismProbeTests
{
    /// <summary>The scripted icon rectangle's left edge, in physical pixels.</summary>
    private const int IconLeft = 100;

    /// <summary>The scripted icon rectangle's top edge, in physical pixels.</summary>
    private const int IconTop = 200;

    /// <summary>The scripted icon rectangle's exclusive right edge, in physical pixels.</summary>
    private const int IconRight = 116;

    /// <summary>The scripted icon rectangle's exclusive bottom edge - the placement anchor - in physical pixels.</summary>
    private const int IconBottom = 216;

    /// <summary>The scripted monitor work area's exclusive right edge, in physical pixels.</summary>
    private const int WorkAreaRight = 1920;

    /// <summary>The scripted monitor work area's exclusive bottom edge, in physical pixels.</summary>
    private const int WorkAreaBottom = 1080;

    /// <summary>The scripted monitor DPI: 144 is Display Scale 150 %, so the scale factor is 1.5.</summary>
    private const uint ScriptedDpi = 144;

    /// <summary>How long the dispatcher is pumped after the open, before the popup is measured.</summary>
    private const int PopupOpenMilliseconds = 600;

    /// <summary>How long the dispatcher is pumped after an injected click.</summary>
    private const int ClickMilliseconds = 900;

    /// <summary>How long the dispatcher is pumped after a menu close so the popup window is really gone.</summary>
    private const int PopupSettleMilliseconds = 400;

    /// <summary>
    /// The window the scripted seam reports as the desktop's foreground window: not this process, which
    /// is the shape the hostile session shows (<c>Shell_TrayWnd</c> or a terminal holds the foreground).
    /// </summary>
    private static readonly IntPtr ForeignForeground = new(unchecked((long)0x0000000000010001));

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes the probe and its raw-measurement output channel.</summary>
    /// <param name="output">The test output the raw measurement line is written to.</param>
    public TrayMenuOwnerMechanismProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// V1, the control: with the foreground claim refused and no repair, the popup is ownerless and a
    /// real outside click leaves the menu open, with its popup window still on screen.
    /// </summary>
    /// <remarks>
    /// This is the state that made five tests fail in one session and pass in another. Making it
    /// scriptable is what turns "green on a good day" into a contract: the assertion no longer depends
    /// on whether Windows happens to grant this process the foreground.
    /// </remarks>
    [StaFact]
    public void V1_the_shipped_construction_with_the_claim_refused_reproduces_the_ownerless_menu()
    {
        MenuOwnerProbeResult result = Probe(MenuOwnerMechanism.Shipped);

        _output.WriteLine(result.Describe());

        // The hermetic refusal really happened: the library made the claim through the seam and the
        // seam refused it.
        Assert.True(result.ClaimCallObserved, $"The library must make its foreground claim through the seam. {result.Describe()}");
        Assert.False(result.ClaimGranted, $"This probe scripts the refusal. {result.Describe()}");

        // The popup opened, and WPF built it without an owner - the measured failing shape.
        Assert.NotEqual(IntPtr.Zero, result.PopupHandle);
        Assert.Equal(IntPtr.Zero, result.OwnerBeforeRepair);

        // ... and the outside click did not dismiss it: the menu is open and its popup window remains.
        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");
        Assert.True(
            result.IsOpenAfterClick,
            $"The measured failure mode is that this menu stays open after an outside click. {result.Describe()}");
        Assert.Contains(result.PopupHandle, result.PopupWindowsAfterClick);
    }

    /// <summary>
    /// V2: the explicit owner alone repairs the owner value but <em>not</em> the dismissal - the
    /// measured reason the shipped mechanism also re-claims the foreground.
    /// </summary>
    /// <remarks>
    /// This is the fact that decides between V2 and V3, so it is asserted rather than described: the
    /// owner write works (the owner becomes the anchor, which is exactly the equality D043 named as the
    /// one direction it may be tightened), and the menu an outside click leaves open anyway.
    /// </remarks>
    [StaFact]
    public void V2_the_explicit_owner_alone_is_measurably_insufficient()
    {
        MenuOwnerProbeResult result = Probe(MenuOwnerMechanism.ExplicitOwner);

        _output.WriteLine(result.Describe());

        Assert.Equal(IntPtr.Zero, result.OwnerBeforeRepair);

        // The owner write took: the value the library set is the value the OS reports.
        Assert.NotEqual(IntPtr.Zero, result.OwnerAfterRepair);
        Assert.Equal(result.AnchorHandleBeforeClick, result.OwnerAfterRepair);
        Assert.NotEqual(result.HostHandle, result.OwnerAfterRepair);

        // ... and the outside click still left the menu open with its popup on screen.
        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");
        Assert.True(
            result.IsOpenAfterClick,
            $"Measured: the owner write alone does not make the menu dismissable. {result.Describe()}");
        Assert.Contains(result.PopupHandle, result.PopupWindowsAfterClick);
    }

    /// <summary>
    /// V3, the chosen mechanism: the explicit owner plus the attach-thread foreground sequence produces
    /// a popup owned by the anchor that a real outside click dismisses and leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The dismissal half is what the consumer sees, and it is asserted unconditionally: the menu is
    /// closed after the click, no popup-sized window of this process remains, and the teardown
    /// destroyed the anchor. The owner half is the equality, not the "anchor or absent" bound, because
    /// the value is now the library's own write rather than WPF's internal decision.
    /// </remarks>
    [StaFact]
    public void V3_the_chosen_mechanism_owns_the_popup_and_dismisses_it()
    {
        MenuOwnerProbeResult result = Probe(MenuOwnerMechanism.AttachThreadForeground);

        _output.WriteLine(result.Describe());

        Assert.Equal(IntPtr.Zero, result.OwnerBeforeRepair);

        // The owner is the anchor window, and the anchor is not the shell registration host.
        Assert.NotEqual(IntPtr.Zero, result.AnchorHandleBeforeClick);
        Assert.Equal(result.AnchorHandleBeforeClick, result.OwnerAfterRepair);
        Assert.NotEqual(result.HostHandle, result.OwnerAfterRepair);

        // The sequence really made the anchor the foreground window, which is the half V2 lacked.
        Assert.Equal(result.AnchorHandleBeforeClick, result.ForegroundAfterRepair);

        // The clause that reaches the user: the outside click dismissed the menu and left nothing.
        Assert.True(result.IsOpenBeforeClick, $"The menu should have opened. {result.Describe()}");
        Assert.False(
            result.IsOpenAfterClick,
            $"The chosen mechanism must be dismissed by an outside click. {result.Describe()}");
        Assert.Empty(result.PopupWindowsAfterClick);

        // A dismissed menu tore its anchor down, which is part of the delivered contract.
        Assert.Equal(IntPtr.Zero, result.AnchorHandle);
    }

    /// <summary>
    /// The popup window can be resolved from the menu's own presentation source, not only by a size
    /// heuristic over the process's windows - the route the shipped repair must use, because a consumer
    /// application has large windows of its own.
    /// </summary>
    /// <remarks>
    /// Measured alongside every variant: <c>PresentationSource.FromVisual</c> on the open menu resolves
    /// to the same window the size heuristic finds, so the repair can name the popup without guessing.
    /// </remarks>
    [StaFact]
    public void The_popup_is_resolvable_from_the_menu_presentation_source()
    {
        MenuOwnerProbeResult result = Probe(MenuOwnerMechanism.Shipped);

        _output.WriteLine(result.Describe());

        Assert.NotEqual(IntPtr.Zero, result.PresentationSourcePopup);
        Assert.Equal(result.HeuristicPopup, result.PresentationSourcePopup);
        Assert.Equal(result.PopupHandle, result.PresentationSourcePopup);
    }

    /// <summary>
    /// Runs one menu open through the product path with the foreground claim refused, applies the given
    /// mechanism to the real popup, injects a real outside click and returns every raw reading.
    /// </summary>
    /// <param name="mechanism">The variant to measure.</param>
    /// <returns>The measurement; see <see cref="MenuOwnerProbeResult"/> for each field.</returns>
    private static MenuOwnerProbeResult Probe(MenuOwnerMechanism mechanism)
    {
        var shell = new FakeShellApi
        {
            GetRectResult = 0,
            GetRectRectangle = new NativeRect
            {
                left = IconLeft,
                top = IconTop,
                right = IconRight,
                bottom = IconBottom,
            },
            CursorPositionX = 640,
            CursorPositionY = 480,

            // The hermetic hostile state: Windows refuses this process the foreground, so the popup is
            // built ownerless exactly as it is in an automation session.
            SetForegroundWindowResult = false,
            ForegroundWindowOverride = ForeignForeground,
        };

        var monitorInfo = new ScriptedMonitorInfoProvider
        {
            WorkArea = new NativeRect { left = 0, top = 0, right = WorkAreaRight, bottom = WorkAreaBottom },
            Dpi = ScriptedDpi,
        };

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Alpha" });

        using var trayIcon = new TrayIcon(shell, Dispatcher.CurrentDispatcher, monitorInfo)
        {
            ContextMenu = menu,
            Visible = true,
        };

        var realApi = new ShellApi();

        uint iconId = trayIcon.IconId;
        IntPtr host = trayIcon.HostHandle;

        try
        {
            IntPtr foregroundBeforeOpen = Win32.GetForegroundWindow();

            SendContextMenuCallback(trayIcon, iconId);
            TrayMenuScenario.Pump(PopupOpenMilliseconds);

            IReadOnlyList<IntPtr> popupWindows = TrayMenuScenario.FindPopupWindows();
            IntPtr heuristicPopup = popupWindows.Count == 1 ? popupWindows[0] : IntPtr.Zero;

            IntPtr presentationSourcePopup = PresentationSource.FromVisual(menu) is HwndSource source
                ? source.Handle
                : IntPtr.Zero;

            IntPtr popup = heuristicPopup != IntPtr.Zero ? heuristicPopup : presentationSourcePopup;

            (IntPtr ownerBefore, string resolution) = popup == IntPtr.Zero
                ? (IntPtr.Zero, "none")
                : (realApi.GetWindowOwner(popup), heuristicPopup != IntPtr.Zero ? "heuristic" : "presentationSource");

            IntPtr foregroundAfterOpen = realApi.GetForegroundWindow();
            IntPtr activeWindowAfterOpen = Win32TestInput.GetActiveWindow();

            ApplyMechanism(mechanism, realApi, popup, trayIcon.MenuAnchorHandle);

            IntPtr ownerAfter = popup == IntPtr.Zero ? IntPtr.Zero : realApi.GetWindowOwner(popup);
            IntPtr foregroundAfterRepair = realApi.GetForegroundWindow();

            bool isOpenBefore = menu.IsOpen;
            IntPtr anchorBeforeClick = trayIcon.MenuAnchorHandle;

            (int cursorX, int cursorY) = GetCursorPosition();

            try
            {
                (int clickX, int clickY) = popup == IntPtr.Zero
                    ? (IconLeft - 200, IconTop - 200)
                    : OutsideClickPoint(popup);

                Win32TestInput.SetCursorPosition(clickX, clickY);
                Win32TestInput.ClickRightThenLeft();
                TrayMenuScenario.Pump(ClickMilliseconds);
            }
            finally
            {
                Win32TestInput.SetCursorPosition(cursorX, cursorY);
            }

            return new MenuOwnerProbeResult(
                Mechanism: mechanism,
                HostHandle: host,
                AnchorHandle: trayIcon.MenuAnchorHandle,
                AnchorHandleBeforeClick: anchorBeforeClick,
                PopupHandle: popup,
                PopupResolution: resolution,
                HeuristicPopup: heuristicPopup,
                PresentationSourcePopup: presentationSourcePopup,
                OwnerBeforeRepair: ownerBefore,
                OwnerAfterRepair: ownerAfter,
                ClaimGranted: shell.LastSetForegroundWindowResult == true,
                ClaimCallObserved: shell.Calls.Any(call => call.Operation == nameof(IShellApi.SetForegroundWindow)),
                ForegroundBeforeOpen: foregroundBeforeOpen,
                ForegroundAfterOpen: foregroundAfterOpen,
                ActiveWindowAfterOpen: activeWindowAfterOpen,
                ForegroundAfterRepair: foregroundAfterRepair,
                IsOpenBeforeClick: isOpenBefore,
                IsOpenAfterClick: menu.IsOpen,
                PopupWindowsAfterClick: TrayMenuScenario.FindPopupWindows(),
                ForegroundAfterClick: realApi.GetForegroundWindow());
        }
        finally
        {
            if (menu.IsOpen)
            {
                menu.IsOpen = false;
            }

            TrayMenuScenario.Pump(PopupSettleMilliseconds);
        }
    }

    /// <summary>Applies one variant's repair to the live popup.</summary>
    /// <param name="mechanism">The variant to apply.</param>
    /// <param name="realApi">The real seam the owner write goes through.</param>
    /// <param name="popup">The popup window handle, or zero when none opened.</param>
    /// <param name="anchor">The anchor window handle.</param>
    private static void ApplyMechanism(MenuOwnerMechanism mechanism, ShellApi realApi, IntPtr popup, IntPtr anchor)
    {
        if (mechanism == MenuOwnerMechanism.Shipped || popup == IntPtr.Zero)
        {
            return;
        }

        // V2 and V3 both begin with the explicit owner.
        realApi.SetWindowOwner(popup, anchor);

        if (mechanism == MenuOwnerMechanism.AttachThreadForeground)
        {
            AttachThreadForeground(realApi, anchor);
        }
    }

    /// <summary>
    /// The attach-thread foreground sequence: attach this thread's input queue to the foreground
    /// window's thread, bring the anchor to the top, claim the foreground, then detach.
    /// </summary>
    /// <param name="realApi">The real seam used for the foreground claim.</param>
    /// <param name="anchor">The anchor window handle.</param>
    /// <remarks>
    /// <c>SwitchToThisWindow</c> is the recorded last resort when there is no foreground window to
    /// attach to, because <c>AttachThreadInput</c> needs a thread to attach to and a desktop with no
    /// foreground window has none.
    /// </remarks>
    private static void AttachThreadForeground(ShellApi realApi, IntPtr anchor)
    {
        IntPtr foreground = realApi.GetForegroundWindow();
        uint ourThread = GetCurrentThreadId();

        if (foreground == IntPtr.Zero)
        {
            // No foreground window to attach to: the documented last resort, recorded as such in the
            // remediation document rather than asserted here (this session has a foreground window).
            SwitchToThisWindow(anchor, false);
            realApi.SetForegroundWindow(anchor);
            return;
        }

        uint foregroundThread = realApi.GetWindowThreadProcessId(foreground, out _);

        bool attached = false;

        try
        {
            attached = AttachThreadInput(ourThread, foregroundThread, true);
            BringWindowToTop(anchor);
            realApi.SetForegroundWindow(anchor);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ourThread, foregroundThread, false);
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

    /// <summary>Chooses a point guaranteed to be outside the popup and away from the anchor.</summary>
    /// <param name="popup">The popup's screen rectangle.</param>
    /// <returns>A screen point in physical pixels.</returns>
    private static (int X, int Y) OutsideClickPoint(IntPtr popup)
    {
        if (!Win32.GetWindowRect(popup, out NativeRect rectangle))
        {
            return (IconLeft - 200, IconTop - 200);
        }

        const int margin = 200;

        return rectangle.left - margin > 0 && rectangle.top - margin > 0
            ? (rectangle.left - margin, rectangle.top - margin)
            : (rectangle.right + margin, rectangle.bottom + margin);
    }

    /// <summary>Reads the current cursor position so the harness can put it back.</summary>
    /// <returns>The cursor position; <c>(0,0)</c> when the reading fails.</returns>
    private static (int X, int Y) GetCursorPosition()
    {
        Win32TestInput.POINT point = default;

        return Win32TestInput.GetCursorPosition(ref point) ? (point.X, point.Y) : (0, 0);
    }

    [DllImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll", EntryPoint = "BringWindowToTop", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SwitchToThisWindow", SetLastError = true)]
    private static extern void SwitchToThisWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

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
