using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// M001/S03/T04: a right-click callback injected through the real host window opens the caller's own
/// <see cref="ContextMenu"/> at the icon, owned by the library's anchor window, dismisses on an
/// outside click, and is torn down by its own close and by disposal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The end-to-end chain, exercised through the product's own entry point.</b> Every test here
/// injects the shell's <c>WM_CONTEXTMENU</c> callback with a same-thread <c>SendMessage</c> into the
/// window the icon really registered (<see cref="TrayIcon.HostHandle"/>) - the S02 injection
/// precedent - so the whole path runs for real: host message, <c>TrayEventDecoder</c>, routed Preview
/// and main events, <c>OnTrayClick</c>, <see cref="TrayIcon.MenuActivation"/>, the shell's icon
/// rectangle, <see cref="TrayIconPlacement"/>, the anchor window, a WPF popup with a real owner, and
/// an outside click routed by the OS. Only the shell seam and the monitor reader are doubles; the
/// windows, the popup and the input are real.
/// </para>
/// <para>
/// <b>Why the seamless cases can be scripted deterministically.</b> The shell rectangle, the cursor
/// position and the monitor's work area and DPI are all scripted through the two seams, so the
/// placement assertions are exact numbers rather than "somewhere near the tray": a menu anchored to
/// the scripted icon rectangle must land on <c>rect / scale</c> for the scripted DPI, and its anchor
/// window must sit at the physical point that offset names. The arithmetic itself is T02's fixture
/// suite; what these tests add is that the <em>product</em> feeds it the shell's rectangle, once.
/// </para>
/// <para>
/// <b>In the serial-tail collection, on purpose.</b> Three tests enumerate this process's visible
/// top-level windows and one injects real mouse input, both of which read process-global OS state, so
/// the class shares <see cref="TrayMenuDismissalCollection"/> with the proof that performs the same
/// measurement (and, like that class, is deliberately not the GDI collection, since nothing here
/// reads <c>GdiHandles.Count()</c>).
/// </para>
/// <para>
/// <b>Every menu is settled in a <see langword="finally"/> (measured).</b> WPF tears a closing
/// popup's window down through the dispatcher rather than inside the close call, and an active or
/// half-destroyed menu on the desktop makes Windows refuse the next <c>SetForegroundWindow</c> - "no
/// menus are active" is one of the documented conditions for that call - which costs the next open
/// its popup owner. Without the settle, a test that leaves a menu closing behind makes the following
/// owner assertion fail with owner <c>0x0</c> for a reason that has nothing to do with the product;
/// with it, every test here is order-independent, which is why each one closes and pumps even when its
/// own assertions already threw.
/// </para>
/// <para>
/// <b>Values observed inside a click handler are recorded and asserted after the send</b>, never
/// asserted inside the handler: the handler runs inside a window procedure, where a thrown assertion
/// would be an unhandled exception in the message path rather than a test failure.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayIconMenuActivationTests
{
    /// <summary>
    /// How long the dispatcher is pumped after a menu is closed, so the popup window is really gone
    /// before the next assertion or the next test - see the class remarks for the measurement behind
    /// this number.
    /// </summary>
    private const int PopupSettleMilliseconds = 400;

    /// <summary>
    /// How long the dispatcher is pumped after a menu is opened, before the popup is measured. It is
    /// the T03 probe's own settle, which is what makes these tests the same measurement as the
    /// recorded dismissal evidence.
    /// </summary>
    private const int PopupOpenMilliseconds = 600;

    /// <summary>The scripted icon rectangle's left edge, in physical pixels.</summary>
    private const int IconLeft = 100;

    /// <summary>The scripted icon rectangle's top edge, in physical pixels.</summary>
    private const int IconTop = 200;

    /// <summary>The scripted icon rectangle's exclusive right edge, in physical pixels.</summary>
    private const int IconRight = 116;

    /// <summary>
    /// The scripted icon rectangle's exclusive bottom edge, in physical pixels. It is the bottom-left
    /// corner - <c>(100, 216)</c> - that the placement law anchors on.
    /// </summary>
    private const int IconBottom = 216;

    /// <summary>The scripted monitor work area's exclusive right edge, in physical pixels.</summary>
    private const int WorkAreaRight = 1920;

    /// <summary>The scripted monitor work area's exclusive bottom edge, in physical pixels.</summary>
    private const int WorkAreaBottom = 1080;

    /// <summary>The scripted monitor DPI: 144 is Display Scale 150 %, so the scale factor is 1.5.</summary>
    private const uint ScriptedDpi = 144;

    /// <summary>The x coordinate the scripted <c>GetCursorPos</c> reports for the fallback tests.</summary>
    private const int CursorX = 640;

    /// <summary>The y coordinate the scripted <c>GetCursorPos</c> reports for the fallback tests.</summary>
    private const int CursorY = 480;

    /// <summary>
    /// The delivered shape: the assigned menu opens on a right-click callback, its popup is owned by
    /// the anchor window rather than by the host or by <see cref="IntPtr.Zero"/>, it is placed by the
    /// calculator, and an outside click closes it <em>and</em> destroys the anchor.
    /// </summary>
    /// <remarks>
    /// This is the task's headline proof and it is machine-measured throughout: the popup is found by
    /// enumerating this process's visible top-level windows (the T03 discrimination, never a class
    /// name), the owner is read with <c>GetWindow(GW_OWNER)</c>, the anchor's position is read with
    /// <c>GetWindowRect</c>, and the dismissal is the observed value of <see cref="ContextMenu.IsOpen"/>
    /// plus the anchor handle's <c>IsWindow</c> after the click. The arithmetic assertion is the
    /// round trip: the offset the menu was given, multiplied by the scale the engine multiplies it by,
    /// is the physical point the anchor sits at - i.e. the icon rectangle's bottom-left, which is the
    /// strongest available form of "the scale factor was applied exactly once".
    /// </remarks>
    [StaFact]
    public void A_right_click_opens_the_assigned_menu_owned_by_the_anchor_and_an_outside_click_closes_and_destroys_it()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Same(menu, trayIcon.OpenContextMenu);
            Assert.True(trayIcon.IsMenuOpen);

            IntPtr popup = Assert.Single(TrayMenuScenario.FindPopupWindows());
            IntPtr anchor = trayIcon.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.True(Win32.IsWindow(anchor));

            // The decisive clause: the popup has a real owner and it IS the anchor window - not the
            // shell registration host, whose handle the S01 contract test proves is a legal but
            // useless owner for a dismissable popup.
            IntPtr popupOwner = Win32.GetWindow(popup, Win32.GW_OWNER);

            Assert.True(
                popupOwner == anchor,
                $"The popup's owner must be the anchor window 0x{anchor.ToInt64():X}, not 0x{popupOwner.ToInt64():X}. {Describe(trayIcon, shell, menu)}");

            Assert.NotEqual(IntPtr.Zero, popupOwner);
            Assert.NotEqual(trayIcon.HostHandle, popupOwner);

            // The menu is the caller's own instance, attached to the anchor's laid-out 1x1 visual, and
            // placed as an absolute point - a never-laid-out target produces no popup at all (T03).
            Assert.NotNull(menu.PlacementTarget);
            Assert.Equal(anchor, GetPlacementTargetWindow(menu));
            Assert.Equal(PlacementMode.AbsolutePoint, menu.Placement);

            const double scale = ScriptedDpi / 96.0;

            Assert.Equal(IconLeft / scale, menu.HorizontalOffset, 6);
            Assert.Equal(IconBottom / scale, menu.VerticalOffset, 6);

            // ... and the anchor window sits at the physical point that offset names, so the engine's
            // own multiplication lands on the icon rather than on the icon scaled a second time.
            Assert.True(Win32.GetWindowRect(anchor, out NativeRect anchorRectangle), Describe(trayIcon, shell, menu));
            Assert.Equal(IconLeft, anchorRectangle.left);
            Assert.Equal(IconBottom, anchorRectangle.top);
            Assert.Equal(1, anchorRectangle.right - anchorRectangle.left);
            Assert.Equal(1, anchorRectangle.bottom - anchorRectangle.top);
            Assert.Equal(IconLeft, (int)Math.Round(menu.HorizontalOffset * scale, MidpointRounding.AwayFromZero));
            Assert.Equal(IconBottom, (int)Math.Round(menu.VerticalOffset * scale, MidpointRounding.AwayFromZero));

            // The shell was asked about this icon exactly once, the answer was used, and the
            // documented cursor fallback was not taken - both recorded rather than inferred.
            Assert.NotNull(trayIcon.LastIconRectHresult);
            Assert.Equal(0, trayIcon.LastIconRectHresult!.Value);
            Assert.False(trayIcon.LastMenuPlacementUsedCursorFallback);
            Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);
            Assert.DoesNotContain(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));

            // The identifier named this icon's own host window and icon id, so the rectangle really
            // came from the registration rather than from a guess.
            NOTIFYICONIDENTIFIER identifier = shell.ShellNotifyIconGetRectIdentifiers[0];
            Assert.Equal(trayIcon.HostHandle, identifier.hWnd);
            Assert.Equal(shell.ShellNotifyIconDataSnapshots[0].uID, identifier.uID);

            Dismiss(menu, popup);

            Assert.False(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.False(trayIcon.IsMenuOpen);

            // The teardown destroyed the anchor: a dismissed menu must not leave its 1x1 window
            // behind, because the next open would then have two.
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// <see cref="TrayIcon.MenuActivation"/> = <see cref="TrayMenuActivation.None"/> opens nothing
    /// while still delivering both right-click events.
    /// </summary>
    /// <remarks>
    /// The opt-out has to be complete in both directions: no menu, and no lost click. The menu's
    /// absence is asserted on the recorded placement state rather than only on
    /// <see cref="ContextMenu.IsOpen"/>, so the test can tell "the early return happened" from "an
    /// open was attempted and quietly did nothing": no shell rectangle was asked for and no cursor
    /// was read.
    /// </remarks>
    [StaFact]
    public void MenuActivation_None_opens_nothing_and_still_delivers_both_right_click_events()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            trayIcon.MenuActivation = TrayMenuActivation.None;

            var phases = new List<string>();
            trayIcon.PreviewTrayRightClick += (_, _) => phases.Add("preview");
            trayIcon.TrayRightClick += (_, _) => phases.Add("bubble");

            OpenMenu(trayIcon, iconId);

            Assert.Equal(["preview", "bubble"], phases);
            Assert.False(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);

            // Nothing on the open path ran: no shell query, no cursor read, no recorded reason.
            Assert.Null(trayIcon.LastIconRectHresult);
            Assert.Empty(shell.ShellNotifyIconGetRectIdentifiers);
            Assert.DoesNotContain(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// A Preview handler that marks the right click handled suppresses the main event <em>and</em> the
    /// menu.
    /// </summary>
    /// <remarks>
    /// The suppression itself is S02's contract, proven there; what this test adds is that the menu
    /// respects it without any code of its own - the placement work never starts, which is why the
    /// assertion is "the shell was never asked" rather than only "the menu is closed".
    /// </remarks>
    [StaFact]
    public void A_preview_handler_that_handles_the_right_click_suppresses_the_events_and_the_menu()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            bool mainRaised = false;
            trayIcon.PreviewTrayRightClick += (_, e) => e.Handled = true;
            trayIcon.TrayRightClick += (_, _) => mainRaised = true;

            OpenMenu(trayIcon, iconId);

            Assert.False(mainRaised, Describe(trayIcon, shell, menu));
            Assert.False(menu.IsOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.Null(trayIcon.LastIconRectHresult);
            Assert.Empty(shell.ShellNotifyIconGetRectIdentifiers);
            Assert.DoesNotContain(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }
    }

    /// <summary>
    /// A main-event handler that marks the right click handled suppresses the menu while still
    /// observing the event.
    /// </summary>
    /// <remarks>
    /// This is the second and last cancellation point, and it lives in the caller's own handler rather
    /// than in a Preview hook: WPF's convention is that a handled input event means the default action
    /// is not wanted, and <c>OnTrayClick</c> is only reached when the click survived both phases.
    /// </remarks>
    [StaFact]
    public void A_main_event_handler_that_handles_the_right_click_suppresses_the_menu()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            bool mainRaised = false;
            trayIcon.TrayRightClick += (_, e) =>
            {
                mainRaised = true;
                e.Handled = true;
            };

            OpenMenu(trayIcon, iconId);

            Assert.True(mainRaised, Describe(trayIcon, shell, menu));
            Assert.False(menu.IsOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.Null(trayIcon.LastIconRectHresult);
            Assert.Empty(shell.ShellNotifyIconGetRectIdentifiers);
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }
    }

    /// <summary>
    /// With no menu assigned - the property's default - the right click is a pure event.
    /// </summary>
    /// <remarks>
    /// This is the compatibility clause of the task: an element that never mentions a menu behaves
    /// exactly as it did before this property existed, so adding the menu changed nothing for an
    /// existing consumer.
    /// </remarks>
    [StaFact]
    public void An_unassigned_menu_leaves_the_right_click_a_pure_event()
    {
        FakeShellApi shell = CreateShell();
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu: null, out uint iconId);

        Assert.Null(trayIcon.ContextMenu);

        bool raised = false;
        trayIcon.TrayRightClick += (_, _) => raised = true;

        OpenMenu(trayIcon, iconId);

        Assert.True(raised, Describe(trayIcon, shell, menu: null));
        Assert.False(trayIcon.IsMenuOpen);
        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
        Assert.Null(trayIcon.LastIconRectHresult);
        Assert.Empty(shell.ShellNotifyIconGetRectIdentifiers);
        Assert.DoesNotContain(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));
        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// An unregistered icon falls back to the cursor and records that the shell was never asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The icon is registered and then unregistered, which is the state a consumer reaches by hiding
    /// the icon without disposing it - and the host window deliberately survives that, so the
    /// callback can still arrive. <c>Shell_NotifyIconGetRect</c> has nothing to locate in that state,
    /// so it must not even be called (an attempt would be a lie in the trace), and the fallback has to
    /// be the cursor: the click's own anchor is officially undefined for <c>WM_CONTEXTMENU</c> (D028).
    /// </para>
    /// <para>
    /// The anchor position is asserted from the scripted cursor, so "the fallback was taken" is a
    /// measurement rather than a claim about a code path.
    /// </para>
    /// </remarks>
    [StaFact]
    public void An_unregistered_icon_falls_back_to_the_cursor_and_records_that_the_shell_was_not_asked()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            IntPtr host = trayIcon.HostHandle;

            trayIcon.Visible = false;

            // The registration is gone but the host window is not: the callback still arrives, which
            // is what makes this state reachable in a live application.
            Assert.False(trayIcon.IsRegistered);
            Assert.Equal(host, trayIcon.HostHandle);

            OpenMenu(trayIcon, iconId);

            Assert.True(trayIcon.LastMenuPlacementUsedCursorFallback, Describe(trayIcon, shell, menu));
            Assert.Null(trayIcon.LastIconRectHresult);
            Assert.Empty(shell.ShellNotifyIconGetRectIdentifiers);
            Assert.Contains(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));
            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));

            IntPtr anchor = trayIcon.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.True(Win32.GetWindowRect(anchor, out NativeRect anchorRectangle), Describe(trayIcon, shell, menu));
            Assert.Equal(CursorX, anchorRectangle.left);
            Assert.Equal(CursorY, anchorRectangle.top);

            const double scale = ScriptedDpi / 96.0;

            Assert.Equal(CursorX / scale, menu.HorizontalOffset, 6);
            Assert.Equal(CursorY / scale, menu.VerticalOffset, 6);

            CloseMenuAndSettle(menu);

            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// A failing <c>Shell_NotifyIconGetRect</c> <c>HRESULT</c> takes the same documented cursor
    /// fallback, and the code is recorded so the reason is traceable after the fact.
    /// </summary>
    /// <remarks>
    /// This is the live case the seam's contract calls legitimate: the icon is in the overflow flyout
    /// or hidden and the shell cannot report a rectangle. The menu must still open - at the cursor -
    /// and the failure must be visible in the recorded state instead of only in a log line.
    /// </remarks>
    [StaFact]
    public void A_failing_shell_rectangle_takes_the_cursor_fallback_and_records_the_hresult()
    {
        const int EFail = unchecked((int)0x80004005);

        FakeShellApi shell = CreateShell();
        shell.GetRectResult = EFail;

        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(trayIcon.LastMenuPlacementUsedCursorFallback, Describe(trayIcon, shell, menu));
            Assert.NotNull(trayIcon.LastIconRectHresult);
            Assert.Equal(EFail, trayIcon.LastIconRectHresult!.Value);

            // The call was made - the shell was asked and refused - which is what distinguishes this
            // case from the unregistered one above.
            Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);
            Assert.Contains(shell.Calls, call => call.Operation == nameof(IShellApi.GetCursorPosition));
            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));

            Assert.True(
                Win32.GetWindowRect(trayIcon.MenuAnchorHandle, out NativeRect anchorRectangle),
                Describe(trayIcon, shell, menu));

            Assert.Equal(CursorX, anchorRectangle.left);
            Assert.Equal(CursorY, anchorRectangle.top);

            IntPtr anchor = trayIcon.MenuAnchorHandle;

            CloseMenuAndSettle(menu);

            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// An unreadable cursor position - the last resort after the shell failed - is reported and opens
    /// nothing, rather than placing a menu at a coordinate nobody measured.
    /// </summary>
    /// <remarks>
    /// There is no legal anchor left in this state, and the alternative is the measured failure mode
    /// where a defaulted absolute point produces no visible popup at all. The click still must not
    /// throw, so the failure travels on the channels a windowless consumer has: the
    /// <see cref="TrayIcon.TrayError"/> event (with the menu operation and the captured error code)
    /// and the trace line behind it.
    /// </remarks>
    [StaFact]
    public void An_unreadable_cursor_position_is_reported_and_opens_nothing()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            trayIcon.Visible = false;
            shell.FailNext(ShellOperation.GetCursorPosition);

            var errors = new List<TrayErrorEventArgs>();
            trayIcon.TrayError += (_, e) => errors.Add(e);

            OpenMenu(trayIcon, iconId);

            Assert.False(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.False(trayIcon.IsMenuOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.Empty(TrayMenuScenario.FindPopupWindows());

            TrayErrorEventArgs reported = Assert.Single(errors);

            Assert.Equal(TrayIconException.OperationOpenMenu, reported.Operation);
            Assert.Equal(shell.LastErrorToReport, reported.Win32ErrorCode);
            Assert.False(reported.Retried);
            Assert.Equal(TrayIconException.OperationOpenMenu, Assert.IsType<TrayIconException>(reported.Exception).Operation);
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }
    }

    /// <summary>
    /// A monitor whose work area and DPI cannot be read still places the menu: the icon rectangle
    /// becomes the placement boundary and 96 DPI (100 %) the scale, and the clamp keeps the anchor
    /// inside that boundary.
    /// </summary>
    /// <remarks>
    /// Measured numbers rather than a smoke test: with the work area defaulted to the scripted icon
    /// rectangle, the anchor is that rectangle's bottom-left clamped into it - 216 is clamped to the
    /// largest legal y, 215 - and with 96 DPI the offset equals that physical point exactly. A
    /// defaulted reading therefore cannot produce an off-screen offset, which is the point of
    /// degrading rather than giving up.
    /// </remarks>
    [StaFact]
    public void A_monitor_whose_work_area_and_dpi_cannot_be_read_still_places_the_menu_inside_the_icon_rectangle()
    {
        var monitor = new ScriptedMonitorInfoProvider
        {
            WorkAreaAvailable = false,
            DpiAvailable = false,
        };

        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, monitor, menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Equal(IconLeft, menu.HorizontalOffset, 6);
            Assert.Equal(IconBottom - 1, menu.VerticalOffset, 6);

            Assert.True(
                Win32.GetWindowRect(trayIcon.MenuAnchorHandle, out NativeRect anchorRectangle),
                Describe(trayIcon, shell, menu));

            Assert.Equal(IconLeft, anchorRectangle.left);
            Assert.Equal(IconBottom - 1, anchorRectangle.top);
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// A second right-click callback while the menu is already open opens no second menu and builds no
    /// second anchor.
    /// </summary>
    /// <remarks>
    /// The idempotency clause is asserted on identity, not on "one popup": the anchor handle must be
    /// the same window, the open menu must be the same instance, and the process must still contain
    /// exactly one popup. The shell call count is the sharpest half - it proves the second click never
    /// got as far as resolving a placement, so no work was thrown away.
    /// </remarks>
    [StaFact]
    public void A_second_right_click_while_the_menu_is_open_opens_nothing_new()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));

            IntPtr firstAnchor = trayIcon.MenuAnchorHandle;
            IntPtr firstPopup = Assert.Single(TrayMenuScenario.FindPopupWindows());

            OpenMenu(trayIcon, iconId);

            Assert.Equal(firstAnchor, trayIcon.MenuAnchorHandle);
            Assert.Equal(firstPopup, Assert.Single(TrayMenuScenario.FindPopupWindows()));
            Assert.Same(menu, trayIcon.OpenContextMenu);
            Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// An Explorer restart while the menu is open leaves the menu, its anchor and its popup exactly
    /// where they were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S03 to S05 edge. The menu's popup is owned by a process-local anchor window, not
    /// by anything the shell owns, so a rebuilt notification area is irrelevant to it - but only if
    /// recovery keeps its hands off. A recovery that re-anchored, dismissed or re-created the popup
    /// would close a menu the user is looking at, in response to something the user did not do.
    /// </para>
    /// <para>
    /// The recorded call sequence is the decisive clause: recovery's two calls are the whole story,
    /// and the identity assertions on the menu instance, the anchor handle and the popup window are
    /// what make "untouched" a measurement rather than a claim.
    /// </para>
    /// </remarks>
    [StaFact]
    public void An_explorer_restart_while_the_menu_is_open_leaves_the_menu_and_its_anchor_alone()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));

            IntPtr anchor = trayIcon.MenuAnchorHandle;
            IntPtr popup = Assert.Single(TrayMenuScenario.FindPopupWindows());
            int callsBefore = shell.ShellNotifyIconCalls.Count;

            // The real broadcast, sent the same way the tests send every shell message: a
            // same-thread SendMessage runs the window procedure - and therefore recovery -
            // synchronously before this call returns.
            Win32.SendMessage(trayIcon.HostHandle, FakeShellApi.DefaultRegisteredMessageId, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal(
                [ShellConstants.NIM_ADD, ShellConstants.NIM_SETVERSION],
                shell.ShellNotifyIconCalls.Skip(callsBefore).Select(call => call.Message).ToArray());

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Same(menu, trayIcon.OpenContextMenu);
            Assert.Equal(anchor, trayIcon.MenuAnchorHandle);
            Assert.True(Win32.IsWindow(anchor));
            Assert.Equal(popup, Assert.Single(TrayMenuScenario.FindPopupWindows()));
        }
        finally
        {
            CloseMenuAndSettle(menu);
        }

        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// Replacing the menu between two clicks takes effect on the next click.
    /// </summary>
    /// <remarks>
    /// The property is read at click time rather than captured when the icon was configured, so this is
    /// the test that keeps that true: the first menu is dismissed, a different instance is assigned,
    /// and the second click opens the new one - with the old one left closed and unowned.
    /// </remarks>
    [StaFact]
    public void Replacing_the_menu_between_two_clicks_opens_the_new_one()
    {
        FakeShellApi shell = CreateShell();
        var first = CreateMenu("First");
        ContextMenu? second = null;
        using TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), first, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(first.IsOpen, Describe(trayIcon, shell, first));
            Assert.Single(TrayMenuScenario.FindPopupWindows());

            CloseMenuAndSettle(first);

            Assert.Null(trayIcon.OpenContextMenu);

            second = CreateMenu("Second");

            trayIcon.ContextMenu = second;

            OpenMenu(trayIcon, iconId);

            Assert.True(second.IsOpen, Describe(trayIcon, shell, second));
            Assert.False(first.IsOpen);
            Assert.Same(second, trayIcon.OpenContextMenu);
            Assert.Single(TrayMenuScenario.FindPopupWindows());
        }
        finally
        {
            // Both instances may be open when an assertion throws: the whole point of the test is that
            // the menu is swapped, so the cleanup cannot assume which one is showing.
            if (second is not null)
            {
                CloseMenuAndSettle(second);
            }

            CloseMenuAndSettle(first);
        }

        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// Disposal closes the open menu, destroys its anchor, and is safe to call twice.
    /// </summary>
    /// <remarks>
    /// The menu is closed through its own <c>Closed</c> handler - the same route a user dismissal takes
    /// - because that is the ordering the class documents: the registration and the icon handle go
    /// first, then the menu and its anchor, then the host window. <c>IsWindow</c> is asserted only
    /// <em>after</em> the menu is closed, which is the precondition the task's failure-mode list
    /// names; the popup window itself needs a pump to disappear, exactly as in a dismissal.
    /// </remarks>
    [StaFact]
    public void Dispose_closes_the_menu_destroys_the_anchor_and_is_idempotent()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Alpha");
        TrayIcon trayIcon = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint iconId);

        try
        {
            OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, Describe(trayIcon, shell, menu));

            IntPtr anchor = trayIcon.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.True(Win32.IsWindow(anchor));

            trayIcon.Dispose();

            Assert.False(menu.IsOpen, Describe(trayIcon, shell, menu));
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.False(trayIcon.IsMenuOpen);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor));
            Assert.Null(menu.PlacementTarget);

            TrayMenuScenario.Pump(PopupSettleMilliseconds);

            Assert.Empty(TrayMenuScenario.FindPopupWindows());

            // Double disposal is a no-op, not an exception.
            trayIcon.Dispose();

            Assert.False(Win32.IsWindow(anchor));
        }
        finally
        {
            CloseMenuAndSettle(menu);
            trayIcon.Dispose();
        }
    }

    /// <summary>
    /// The documented unsupported case: one menu instance assigned to two icons. The second icon
    /// reports the failure and opens nothing, leaving the first icon's popup exactly where it is.
    /// </summary>
    /// <remarks>
    /// A WPF <see cref="ContextMenu"/> can only be open in one place at a time, so the second icon
    /// cannot adopt it without moving a live popup away from the anchor that can still tear it down.
    /// Failing visibly is the chosen behaviour: the consumer gets a <see cref="TrayIcon.TrayError"/>
    /// naming the menu operation, and the shell is not asked for a second rectangle - the second icon
    /// does no placement work at all.
    /// </remarks>
    [StaFact]
    public void A_menu_already_open_on_another_icon_is_reported_and_not_adopted()
    {
        FakeShellApi shell = CreateShell();
        var menu = CreateMenu("Shared");

        using TrayIcon first = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint firstIconId);
        using TrayIcon second = CreateRegisteredIcon(shell, CreateScriptedMonitor(), menu, out uint secondIconId);

        try
        {
            var errors = new List<TrayErrorEventArgs>();
            second.TrayError += (_, e) => errors.Add(e);

            OpenMenu(first, firstIconId);

            Assert.True(menu.IsOpen, Describe(first, shell, menu));

            IntPtr firstAnchor = first.MenuAnchorHandle;
            IntPtr popup = Assert.Single(TrayMenuScenario.FindPopupWindows());
            IntPtr popupOwner = Win32.GetWindow(popup, Win32.GW_OWNER);

            Assert.True(
                popupOwner == firstAnchor,
                $"The first icon's popup must be owned by its own anchor 0x{firstAnchor.ToInt64():X}, not 0x{popupOwner.ToInt64():X}. {Describe(first, shell, menu)}");

            OpenMenu(second, secondIconId);

            // The second icon opened nothing and owns nothing.
            Assert.False(second.IsMenuOpen, Describe(second, shell, menu));
            Assert.Null(second.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, second.MenuAnchorHandle);
            Assert.Null(second.LastIconRectHresult);

            // ... and the first icon's menu is untouched: still open, still owned by its own anchor.
            Assert.True(menu.IsOpen, $"The first icon's menu must still be open after the second click. {Describe(first, shell, menu)}");
            Assert.Same(menu, first.OpenContextMenu);
            Assert.Equal(firstAnchor, first.MenuAnchorHandle);

            IReadOnlyList<IntPtr> popupsAfterSecondClick = TrayMenuScenario.FindPopupWindows();

            Assert.True(
                popupsAfterSecondClick.Count == 1 && Win32.GetWindow(popupsAfterSecondClick[0], Win32.GW_OWNER) == firstAnchor,
                $"The first icon's popup must still be the only popup, owned by 0x{firstAnchor.ToInt64():X}. {Describe(first, shell, menu)}");

            Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);

            TrayErrorEventArgs reported = Assert.Single(errors);

            Assert.Equal(TrayIconException.OperationOpenMenu, reported.Operation);
            Assert.Equal(0, reported.Win32ErrorCode);
            Assert.Contains("already open", reported.Exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            // Both icons share this menu, so one close settles it for both.
            CloseMenuAndSettle(menu);
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// The <c>MONITORINFO</c> layout is the header's, and its size is the <c>cbSize</c> the call must
    /// be handed.
    /// </summary>
    /// <remarks>
    /// The second layout-sensitive structure in this library, pinned the way
    /// <c>NotifyIconIdentifierLayoutTests</c> and <c>IconInfoLayoutTests</c> pin theirs: the offsets
    /// are read back through marshalling, not restated from the header, so a field added or reordered
    /// fails here instead of turning into a work area read from the wrong bytes. 40 is also the value
    /// <c>GetMonitorInfoW</c> validates, which is why the size is asserted separately from the
    /// offsets.
    /// </remarks>
    [Fact]
    public void MonitorInfo_layout_matches_the_header()
    {
        Assert.Equal(40u, MonitorInfoProvider.MonitorInfoSizeOf);
        Assert.Equal(40, Marshal.SizeOf<MonitorInfo>());

        Assert.Equal(0, Marshal.OffsetOf<MonitorInfo>(nameof(MonitorInfo.cbSize)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<MonitorInfo>(nameof(MonitorInfo.rcMonitor)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<MonitorInfo>(nameof(MonitorInfo.rcWork)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<MonitorInfo>(nameof(MonitorInfo.dwFlags)).ToInt32());

        // Read back out of native memory at the offsets above, so a wrong field order cannot pass by
        // marshalling consistently with itself in both directions.
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<MonitorInfo>());

        try
        {
            var written = new MonitorInfo
            {
                cbSize = MonitorInfoProvider.MonitorInfoSizeOf,
                rcMonitor = new NativeRect { left = 11, top = 22, right = 33, bottom = 44 },
                rcWork = new NativeRect { left = 55, top = 66, right = 77, bottom = 88 },
                dwFlags = 1,
            };

            Marshal.StructureToPtr(written, memory, fDeleteOld: false);

            Assert.Equal(40u, (uint)Marshal.ReadInt32(memory, 0));
            Assert.Equal(11, Marshal.ReadInt32(memory, 4));
            Assert.Equal(22, Marshal.ReadInt32(memory, 8));
            Assert.Equal(33, Marshal.ReadInt32(memory, 12));
            Assert.Equal(44, Marshal.ReadInt32(memory, 16));
            Assert.Equal(55, Marshal.ReadInt32(memory, 20));
            Assert.Equal(66, Marshal.ReadInt32(memory, 24));
            Assert.Equal(77, Marshal.ReadInt32(memory, 28));
            Assert.Equal(88, Marshal.ReadInt32(memory, 32));
            Assert.Equal(1u, (uint)Marshal.ReadInt32(memory, 36));
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>
    /// The real monitor reader answers on this machine, so a wrong entry point, a wrong library or a
    /// wrong <c>cbSize</c> cannot hide behind the scripted double used by the placement tests.
    /// </summary>
    /// <remarks>
    /// <c>GetDpiForMonitor</c> lives in <c>shcore.dll</c> rather than in <c>user32</c>, which is
    /// exactly the kind of detail that fails only in a consumer's process if nothing probes the real
    /// call. Both readings are ranges rather than values, because the machine's monitor count and
    /// Display Scale are not this test's to know.
    /// </remarks>
    [Fact]
    public void The_real_monitor_provider_reads_this_machines_work_area_and_dpi()
    {
        var provider = new MonitorInfoProvider();
        var probe = new NativeRect { left = 100, top = 100, right = 116, bottom = 116 };

        Assert.True(
            provider.TryGetWorkArea(probe, out NativeRect workArea),
            $"MonitorFromRect/GetMonitorInfoW failed with status 0x{provider.LastError:X8}.");

        Assert.True(workArea.right > workArea.left, $"The work area has no width: {workArea.right - workArea.left}.");
        Assert.True(workArea.bottom > workArea.top, $"The work area has no height: {workArea.bottom - workArea.top}.");
        Assert.Equal(0, provider.LastError);

        Assert.True(
            provider.TryGetDpi(probe, out uint dpi),
            $"GetDpiForMonitor failed with status 0x{provider.LastError:X8}.");

        Assert.InRange(dpi, 96u, 768u);
        Assert.Equal(0, provider.LastError);
    }

    /// <summary>
    /// Builds a shell seam whose icon rectangle is the scripted one, so every placement assertion in
    /// this class is an exact number.
    /// </summary>
    /// <returns>The scripted fake.</returns>
    private static FakeShellApi CreateShell() =>
        new()
        {
            GetRectResult = 0,
            GetRectRectangle = new NativeRect
            {
                left = IconLeft,
                top = IconTop,
                right = IconRight,
                bottom = IconBottom,
            },
            CursorPositionX = CursorX,
            CursorPositionY = CursorY,
        };

    /// <summary>
    /// Builds a monitor reader whose answers are the scripted work area and DPI.
    /// </summary>
    /// <returns>The scripted reader.</returns>
    private static ScriptedMonitorInfoProvider CreateScriptedMonitor() =>
        new()
        {
            WorkArea = new NativeRect
            {
                left = 0,
                top = 0,
                right = WorkAreaRight,
                bottom = WorkAreaBottom,
            },
            Dpi = ScriptedDpi,
        };

    /// <summary>
    /// Builds a one-item menu. WPF suppresses a menu with no items, so an empty menu would prove
    /// nothing about placement or ownership.
    /// </summary>
    /// <param name="header">The item's header text.</param>
    /// <returns>The menu, owned by the caller.</returns>
    private static ContextMenu CreateMenu(string header)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = header });

        return menu;
    }

    /// <summary>
    /// Creates a <see cref="TrayIcon"/> registered over the given seams, optionally carrying a menu.
    /// </summary>
    /// <param name="shell">The scripted shell seam.</param>
    /// <param name="monitorInfo">The scripted monitor reader.</param>
    /// <param name="menu">The menu to assign, or <see langword="null"/> for "no menu".</param>
    /// <param name="iconId">Receives the icon id the registration carried.</param>
    /// <returns>The registered icon, owned by the caller.</returns>
    private static TrayIcon CreateRegisteredIcon(
        FakeShellApi shell,
        MonitorInfoProvider monitorInfo,
        ContextMenu? menu,
        out uint iconId)
    {
        var trayIcon = new TrayIcon(shell, Dispatcher.CurrentDispatcher, monitorInfo)
        {
            ContextMenu = menu,
            Visible = true,
        };

        // The id this instance allocated, not the first snapshot's: several icons can be registered over
        // one seam (the two-TrayIcon sharing test does exactly that), and the shell reports the icon id
        // in HIWORD(lParam), so an injection carrying another icon's id is correctly ignored as foreign.
        iconId = trayIcon.IconId;

        Assert.NotEqual(0u, iconId);
        Assert.NotEqual(IntPtr.Zero, trayIcon.HostHandle);
        Assert.Equal(iconId, shell.ShellNotifyIconDataSnapshots[^1].uID);

        return trayIcon;
    }

    /// <summary>
    /// Sends one <c>WM_CONTEXTMENU</c> callback - the right-click notification - and pumps long enough
    /// for the popup to exist.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the callback.</param>
    /// <param name="iconId">The icon id for <c>HIWORD(lParam)</c>.</param>
    private static void OpenMenu(TrayIcon trayIcon, uint iconId)
    {
        // The OS precondition for the foreground call the popup's ownership depends on: see
        // Win32TestInput.GrantLastInputToThisProcess for the measurement (with the taskbar as the
        // desktop's foreground window the plain call is refused, and one synthetic mouse move - no
        // button, cursor put back - makes it succeed). A real user moves the pointer to the tray icon
        // before right-clicking, so this is the production precondition rather than a test crutch.
        Win32TestInput.GrantLastInputToThisProcess();

        SendContextMenuCallback(trayIcon, iconId);
        TrayMenuScenario.Pump(PopupOpenMilliseconds);
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
            // WM_CONTEXTMENU (D028) and the library must not read it, so the injection sends the least
            // informative value there is. A menu placed correctly from a zero anchor cannot have been
            // placed from the anchor at all.
            IntPtr.Zero,
            Payload(ShellConstants.WM_CONTEXTMENU, iconId));

    /// <summary>
    /// Packs an event code and a 16-bit icon id into the <c>lParam</c> payload the way the shell does.
    /// </summary>
    /// <param name="eventCode">The event code, taken from the low 16 bits.</param>
    /// <param name="iconId">The icon id, taken from the low 16 bits.</param>
    /// <returns>The packed parameter with a zero upper half.</returns>
    private static IntPtr Payload(uint eventCode, uint iconId)
    {
        uint packed = (eventCode & 0xFFFF) | ((iconId & 0xFFFF) << 16);
        return new IntPtr((long)packed);
    }

    /// <summary>
    /// Reads the window that owns a menu's placement target element.
    /// </summary>
    /// <param name="menu">The menu whose placement target's window is wanted.</param>
    /// <returns>The owner window handle, or <see cref="IntPtr.Zero"/> when there is none.</returns>
    /// <remarks>
    /// The framework derives the popup's owner from the placement target's window, so this is the
    /// documented relationship seen from the WPF side; the measurement itself is taken from the popup
    /// with <c>GetWindow(GW_OWNER)</c> in the test bodies.
    /// </remarks>
    private static IntPtr GetPlacementTargetWindow(ContextMenu menu) =>
        PresentationSource.FromVisual(menu.PlacementTarget!) is HwndSource source
            ? source.Handle
            : IntPtr.Zero;

    /// <summary>
    /// Injects a real outside click - the probe's <c>SetCursorPos</c> plus <c>mouse_event</c> sequence
    /// - and pumps long enough for the OS to route it, then restores the cursor.
    /// </summary>
    /// <param name="menu">The menu that should end up closed.</param>
    /// <param name="popup">The popup's handle, used to choose a point outside it.</param>
    private static void Dismiss(ContextMenu menu, IntPtr popup)
    {
        Assert.True(Win32.GetWindowRect(popup, out NativeRect popupRectangle), "The popup's rectangle could not be read.");

        Win32TestInput.POINT previous = default;
        bool restored = Win32TestInput.GetCursorPosition(ref previous);

        try
        {
            (int x, int y) = OutsideClickPoint(popupRectangle);

            Assert.True(Win32TestInput.SetCursorPosition(x, y), $"The cursor could not be moved to ({x},{y}).");

            Win32TestInput.ClickRightThenLeft();
            TrayMenuScenario.Pump(900);
        }
        finally
        {
            if (restored)
            {
                Win32TestInput.SetCursorPosition(previous.X, previous.Y);
            }
        }
    }

    /// <summary>
    /// Chooses a point guaranteed to be outside the popup and away from the anchor.
    /// </summary>
    /// <param name="popup">The popup's screen rectangle.</param>
    /// <returns>A screen point in physical pixels.</returns>
    /// <remarks>
    /// Up and left of the popup by a fixed margin, which is where the desktop or an unrelated window
    /// lives; the mirrored point is used when that one would fall off the screen. The click does not
    /// need to land on any particular window - the OS routes the dismissal through the anchor's
    /// ownership relationship - only outside the popup.
    /// </remarks>
    private static (int X, int Y) OutsideClickPoint(NativeRect popup)
    {
        const int margin = 200;

        return popup.left - margin > 0 && popup.top - margin > 0
            ? (popup.left - margin, popup.top - margin)
            : (popup.right + margin, popup.bottom + margin);
    }

    /// <summary>
    /// Closes a menu if it is open and pumps until its popup window is really gone.
    /// </summary>
    /// <param name="menu">The menu to close.</param>
    /// <remarks>
    /// The pump is not cosmetic - see the class remarks: WPF destroys the popup's <c>HWND</c> through
    /// the dispatcher rather than inside the close call, and a menu still closing on the desktop makes
    /// Windows refuse the next <c>SetForegroundWindow</c>, which would cost the following test its
    /// popup owner. Calling this in a <see langword="finally"/> is what keeps a failed assertion from
    /// changing the next test's measurement.
    /// </remarks>
    private static void CloseMenuAndSettle(ContextMenu menu)
    {
        if (menu.IsOpen)
        {
            menu.IsOpen = false;
        }

        TrayMenuScenario.Pump(PopupSettleMilliseconds);
    }

    /// <summary>
    /// Renders the whole measurement as one line, so a failing assertion carries the evidence that
    /// makes it diagnosable instead of only the value that differed.
    /// </summary>
    /// <param name="trayIcon">The icon under test.</param>
    /// <param name="shell">The scripted seam.</param>
    /// <param name="menu">The menu under test, if any.</param>
    /// <returns>A single-line description of every measured field.</returns>
    private static string Describe(TrayIcon trayIcon, FakeShellApi shell, ContextMenu? menu)
    {
        string anchor = trayIcon.MenuAnchorHandle == IntPtr.Zero
            ? "none"
            : Win32.GetWindowRect(trayIcon.MenuAnchorHandle, out NativeRect rectangle)
                ? $"0x{trayIcon.MenuAnchorHandle.ToInt64():X}({rectangle.left},{rectangle.top})"
                : $"0x{trayIcon.MenuAnchorHandle.ToInt64():X}(?)";

        IReadOnlyList<IntPtr> popups = TrayMenuScenario.FindPopupWindows();
        IntPtr popup = popups.Count == 1 ? popups[0] : IntPtr.Zero;
        string owner = popup == IntPtr.Zero
            ? "n/a"
            : $"0x{Win32.GetWindow(popup, Win32.GW_OWNER).ToInt64():X}";
        IntPtr foreground = Win32.GetForegroundWindow();

        return $"menuAssigned={menu is not null} menuIsOpen={menu?.IsOpen ?? false} placement={menu?.Placement.ToString() ?? "n/a"} "
            + $"offset=({menu?.HorizontalOffset ?? 0:0.###},{menu?.VerticalOffset ?? 0:0.###}) "
            + $"openMenu={(trayIcon.OpenContextMenu is null ? "none" : "assigned")} anchor={anchor} host=0x{trayIcon.HostHandle.ToInt64():X} "
            + $"iconRectHresult={(trayIcon.LastIconRectHresult is int hr ? $"0x{hr:X8}" : "not attempted")} "
            + $"cursorFallback={trayIcon.LastMenuPlacementUsedCursorFallback} registered={trayIcon.IsRegistered} "
            + $"getRectCalls={shell.ShellNotifyIconGetRectIdentifiers.Count} "
            + $"popupWindows=[{string.Join(", ", popups.Select(window => $"0x{window.ToInt64():X}"))}] popupOwner={owner} "
            + $"foreground=0x{foreground.ToInt64():X}({Win32.GetClassName(foreground)}) "
            + $"anchorIsForeground={foreground == trayIcon.MenuAnchorHandle}";
    }

    /// <summary>
    /// A monitor reader whose answers are scripted, including its failures.
    /// </summary>
    /// <remarks>
    /// The real reader answers correctly on this machine, which is exactly why it cannot exercise the
    /// documented degradation rules: a test cannot make <c>GetMonitorInfoW</c> fail. Substituting the
    /// answers is the same move the shell seam makes, and it keeps the production type's default
    /// behaviour (96 DPI, the icon rectangle as the boundary) on the real code path.
    /// </remarks>
    private sealed class ScriptedMonitorInfoProvider : MonitorInfoProvider
    {
        /// <summary>Gets or sets the work area the scripted reading reports.</summary>
        internal NativeRect WorkArea { get; set; }

        /// <summary>Gets or sets the DPI the scripted reading reports.</summary>
        internal uint Dpi { get; set; } = TrayIconPlacement.UserDefaultScreenDpi;

        /// <summary>Gets or sets whether the work area reading succeeds.</summary>
        internal bool WorkAreaAvailable { get; set; } = true;

        /// <summary>Gets or sets whether the DPI reading succeeds.</summary>
        internal bool DpiAvailable { get; set; } = true;

        /// <summary>Gets or sets the status a scripted failure reports.</summary>
        internal int FailureStatus { get; set; } = unchecked((int)0x80070057);

        /// <inheritdoc />
        internal override bool TryGetWorkArea(NativeRect rectangle, out NativeRect workArea)
        {
            workArea = WorkAreaAvailable ? WorkArea : default;

            if (!WorkAreaAvailable)
            {
                LastError = FailureStatus;
            }

            return WorkAreaAvailable;
        }

        /// <inheritdoc />
        internal override bool TryGetDpi(NativeRect rectangle, out uint dpi)
        {
            dpi = DpiAvailable ? Dpi : 0;

            if (!DpiAvailable)
            {
                LastError = FailureStatus;
            }

            return DpiAvailable;
        }
    }
}
