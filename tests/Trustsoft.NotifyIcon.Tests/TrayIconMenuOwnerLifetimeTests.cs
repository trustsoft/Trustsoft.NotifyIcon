using System.Windows.Controls;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The owner-lifetime evidence S03 owes R007/R014: opening and dismissing the menu through the
/// injected right-click path many times over must leak no GDI object, leave no anchor window behind,
/// and leave no visible popup in the process - and the measurement that says so must itself be shown
/// to be non-vacuous.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is exercised, exactly.</b> Every cycle injects the shell's real <c>WM_CONTEXTMENU</c>
/// callback with a same-thread <c>SendMessage</c> into the window the icon really registered (the T04
/// injection path, which is also S02's), lets the dispatcher produce the real WPF popup, and dismisses
/// the menu through <see cref="ContextMenu.IsOpen"/>'s close route - the same <c>Closed</c> event and
/// the same <c>TearDownMenu</c> teardown an outside click reaches, without the ~900 ms of
/// desktop-scale mouse-injection latency a real outside click costs per cycle. The synthetic message
/// <em>does</em> drive the full open/dismiss cycle in this harness - the assertions below would fail
/// if it did not - so the weaker "anchor creation and disposal leaks nothing" fallback the task plan
/// allows for was not needed; the cycle that is measured is the delivered open path, not a rehearsal
/// of it.
/// </para>
/// <para>
/// <b>Condition-waiting pumps, and why.</b> Both conditions here genuinely must become true (the
/// popup exists once the menu is open; the popup and the library's menu state are both gone once it
/// is closed), so <see cref="DispatcherHarness.PumpUntil"/> is the right instrument - measured while
/// writing this test, a 60 ms fixed settle was sometimes too short for WPF's asynchronous popup
/// close, and the failure then read as a product defect instead of as a slow pump. The fixed-settle
/// pump exists for the conditions that must <em>not</em> become true (the ownerless popup that stays
/// open), which is a different question and lives in <c>TrayMenuDismissalTests</c>.
/// </para>
/// <para>
/// <b>One-sided deltas, on purpose.</b> The GDI assertion is "the count did not grow beyond a small
/// stated bound between the first and the last cycle", not "the count returned to its baseline": a
/// concurrent release during the loop is not a leak, and a bounded amount of first-use allocation (a
/// popup's brushes and fonts are created lazily by WPF) is a real property of the first cycle rather
/// than of the fiftieth. Comparing two post-warm-up measurements is what keeps the assertion about
/// the library.
/// </para>
/// <para>
/// <b>In the non-parallel GDI collection (KNOWLEDGE rule 1).</b> <c>GdiHandles.Count()</c> is a
/// process-wide counter, so it is only attributable while no other test class is running beside this
/// one; the class therefore shares <see cref="GdiCountCollection"/> with the other GDI measurements.
/// It opens real popups but asserts nothing about "the only visible window in this process", so it is
/// deliberately not the dismissal collection.
/// </para>
/// <para>
/// <b>The control that keeps this from being a claim.</b> "Anchor windows are gone after every
/// dismissal" would also be satisfied by a measurement that simply cannot see live windows, so
/// <see cref="Fifty_undisposed_anchors_are_fifty_live_windows"/> creates fifty of them and shows the
/// same <c>IsWindow</c> read reporting them as alive, then as gone.
/// </para>
/// </remarks>
[Collection(GdiCountCollection.Name)]
public sealed class TrayIconMenuOwnerLifetimeTests
{
    /// <summary>
    /// How many open-and-dismiss cycles the leak measurement runs. Fifty is the number the slice's
    /// acceptance criterion names, and it is large enough that a leak of one object per cycle is
    /// unmissable while staying cheap: a cycle is a message send, a real popup, and a close.
    /// </summary>
    private const int CycleCount = 50;

    /// <summary>
    /// How long a cycle waits for the condition it expects. It is generous relative to the measured
    /// cost of a cycle (tens of milliseconds), because <see cref="DispatcherHarness.PumpUntil"/>
    /// throws a <see cref="TimeoutException"/> naming the elapsed wait: a genuinely stuck cycle fails
    /// with that message rather than hanging the run.
    /// </summary>
    private static readonly TimeSpan CycleTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The largest GDI-object growth between the first and the last cycle that is still reported as
    /// "no leak". A stated bound rather than an exact zero because WPF's popup machinery may allocate
    /// a small number of process-wide objects lazily; one object <em>per cycle</em> would be about 49
    /// and is what this bound exists to catch.
    /// </summary>
    private const int GdiGrowthBound = 4;

    /// <summary>
    /// A menu assigned before the icon is shown creates nothing at all: no host window, no anchor, no
    /// popup and no GDI object.
    /// </summary>
    /// <remarks>
    /// This is the S06 shape measured on the lifetime axis (the sibling contract test asserts the
    /// property itself): a resource dictionary populating <see cref="TrayIcon.ContextMenu"/> must not
    /// produce an OS resource, because the icon is constructed during application startup and may
    /// never be shown. The GDI half is the same measurement the cyclic test uses, taken once, so a
    /// future eager "create the anchor when a menu arrives" shortcut fails here rather than in the
    /// field.
    /// </remarks>
    [StaFact]
    public void A_menu_assigned_but_never_opened_creates_no_window_no_anchor_and_no_gdi_object()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");

        int gdiBefore = GdiHandles.Count();

        var trayIcon = new TrayIcon(shell, Dispatcher.CurrentDispatcher)
        {
            ContextMenu = menu,
        };

        try
        {
            Assert.Same(menu, trayIcon.ContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(trayIcon.IsMenuOpen);
            Assert.False(menu.IsOpen);
            Assert.Empty(shell.Calls);
            Assert.Empty(TrayMenuScenario.FindPopupWindows());

            int gdiAfter = GdiHandles.Count();

            Assert.True(
                gdiAfter <= gdiBefore + GdiGrowthBound,
                $"Assigning a menu must not allocate GDI objects: before={gdiBefore}, after={gdiAfter}, bound={GdiGrowthBound}.");
        }
        finally
        {
            trayIcon.Dispose();
        }

        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);
        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
    }

    /// <summary>
    /// Fifty injected right-click open-and-dismiss cycles, each a complete popup lifecycle, leave no
    /// anchor window alive, no visible popup in the process, and no GDI growth beyond a stated bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The task's leak criterion, measured as a trend rather than as a single reading. The anchor
    /// handles of all fifty cycles are kept and asserted dead afterwards, which is the "no
    /// accumulation" clause in its sharpest form: a leak would show up as at least one handle still
    /// being a window, independently of any count. The popup enumeration is the second, independent
    /// instrument: it would catch a popup window that outlived its menu even if the anchor was
    /// destroyed.
    /// </para>
    /// <para>
    /// Every cycle asserts that the menu really opened (the library's own state, set synchronously by
    /// the callback), that the anchor window really exists, that a real popup window appeared, and
    /// that all three are gone once the menu is closed - so a cycle that silently did nothing cannot
    /// be counted as a clean cycle, and the observed-popup count at the end says how many cycles were
    /// complete popup lifecycles rather than merely anchor lifecycles.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Fifty_menu_cycles_through_the_injected_right_click_leak_no_gdi_object_and_leave_no_anchor_window()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");

        using TrayIcon trayIcon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        var anchors = new List<IntPtr>(CycleCount);
        int cyclesWithObservedPopup = 0;
        int gdiAfterFirstCycle = 0;
        int gdiAfterLastCycle = 0;

        try
        {
            for (int cycle = 0; cycle < CycleCount; cycle++)
            {
                // The OS precondition for the foreground call the popup's ownership depends on; a real
                // user moves the pointer to the tray icon before right-clicking.
                Win32TestInput.GrantLastInputToThisProcess();
                TrayIconMenuFixture.SendContextMenuCallback(trayIcon, iconId);

                // The host window procedure runs inside the same-thread SendMessage, so the open is
                // already complete by the time the call returns.
                Assert.True(
                    trayIcon.IsMenuOpen,
                    $"Cycle {cycle + 1}: the injected right click must open the assigned menu. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

                IntPtr anchor = trayIcon.MenuAnchorHandle;

                Assert.NotEqual(IntPtr.Zero, anchor);
                Assert.True(Win32.IsWindow(anchor), $"Cycle {cycle + 1}: the anchor window must exist while the menu is open.");

                anchors.Add(anchor);

                // The real popup: WPF creates its window through the dispatcher, so the cycle waits
                // for the window rather than assuming it.
                DispatcherHarness.PumpUntil(() => TrayMenuScenario.FindPopupWindows().Count >= 1, CycleTimeout);
                cyclesWithObservedPopup++;

                menu.IsOpen = false;

                DispatcherHarness.PumpUntil(
                    () => !trayIcon.IsMenuOpen && TrayMenuScenario.FindPopupWindows().Count == 0,
                    CycleTimeout);

                Assert.False(trayIcon.IsMenuOpen, $"Cycle {cycle + 1}: the menu must be closed again.");
                Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
                Assert.False(Win32.IsWindow(anchor), $"Cycle {cycle + 1}: the anchor window must be destroyed with the menu.");

                if (cycle == 0)
                {
                    gdiAfterFirstCycle = GdiHandles.Count();
                }
            }

            gdiAfterLastCycle = GdiHandles.Count();

            // Every cycle is a complete popup lifecycle, not an anchor-only rehearsal of one.
            Assert.Equal(CycleCount, cyclesWithObservedPopup);
            Assert.Equal(CycleCount, anchors.Count);

            // No accumulation: not one of the fifty anchors is a window any more.
            Assert.All(anchors, anchor => Assert.False(Win32.IsWindow(anchor)));

            // One-sided: growth is the leak. A concurrent release during the loop is not.
            int growth = gdiAfterLastCycle - gdiAfterFirstCycle;

            Assert.True(
                growth <= GdiGrowthBound,
                $"Opening and dismissing the menu {CycleCount} times must not leak GDI objects: after cycle 1={gdiAfterFirstCycle}, "
                + $"after cycle {CycleCount}={gdiAfterLastCycle}, growth={growth}, bound={GdiGrowthBound}.");

            // The second, independent instrument: no popup window of this process outlived its menu.
            Assert.Empty(TrayMenuScenario.FindPopupWindows());
            Assert.False(menu.IsOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);

            // ... and nothing of the menu path outlives the icon either.
            IntPtr host = trayIcon.HostHandle;

            trayIcon.Dispose();

            Assert.False(Win32.IsWindow(host));
            Assert.False(menu.IsOpen);
            Assert.Null(menu.PlacementTarget);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayIconMenuFixture.Settle();
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// The control for the assertion above: fifty anchor windows that are deliberately <em>not</em>
    /// disposed are fifty live windows, read with the same <c>IsWindow</c> in the same process.
    /// </summary>
    /// <remarks>
    /// Without this, "every anchor handle is not a window after dismissal" could be satisfied by a
    /// measurement that never sees live windows at all (a wrong handle comparison, a stale list, a
    /// window destroyed before it was read). Creating the windows and reading them back as alive
    /// makes the negative assertion a comparison between two measured outcomes rather than a claim -
    /// the same discipline the T03 dismissal proof applies to its failing constructions.
    /// </remarks>
    [StaFact]
    public void Fifty_undisposed_anchors_are_fifty_live_windows()
    {
        var anchors = new List<TrayMenuAnchorWindow>(CycleCount);

        try
        {
            for (int i = 0; i < CycleCount; i++)
            {
                anchors.Add(new TrayMenuAnchorWindow(300 + i, 300));
            }

            Assert.Equal(CycleCount, anchors.Count);

            foreach (TrayMenuAnchorWindow anchor in anchors)
            {
                Assert.NotEqual(IntPtr.Zero, anchor.Handle);
                Assert.True(Win32.IsWindow(anchor.Handle), $"Anchor 0x{anchor.Handle.ToInt64():X} should still be a live window while it is undisposed.");
            }

            // A distinct window per anchor: the measurement is not reading one handle fifty times.
            Assert.Equal(CycleCount, anchors.Select(anchor => anchor.Handle).Distinct().Count());
            Assert.Equal(CycleCount, anchors.Select(anchor => anchor.RootVisual).Distinct().Count());

            foreach (TrayMenuAnchorWindow anchor in anchors)
            {
                anchor.Dispose();
            }

            Assert.All(anchors, anchor => Assert.False(Win32.IsWindow(anchor.Handle)));
            Assert.All(anchors, anchor => Assert.Equal(IntPtr.Zero, anchor.Handle));
        }
        finally
        {
            foreach (TrayMenuAnchorWindow anchor in anchors)
            {
                anchor.Dispose();
            }
        }
    }
}
