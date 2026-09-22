using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The close-notification proof of M001/S08: a disposal-driven close delivers
/// <see cref="ContextMenu.Closed"/> to the consumer's own handler exactly once <em>before</em>
/// <see cref="TrayIcon.Dispose"/> returns, and leaves neither the anchor window nor the popup window
/// behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes (D030, S03 finding F1).</b> Measured twice in the reference sample -
/// <c>menu opens=1, menu dismissals=0</c> for a menu closed only by the icon's disposal, once with a
/// 300 ms dispatcher pump after the disposal - because WPF raises the menu's <c>Closed</c> from the
/// popup destroy it <em>schedules</em> on the owning dispatcher, while the library used to destroy the
/// anchor window (the popup's owner) immediately after <c>IsOpen = false</c>. The owner died first, so
/// the framework's teardown never reached <c>OnClosed</c> and a consumer handler never ran. The repair
/// is the library's bounded wait for that delivery: it is the product's own close path doing the
/// waiting, so these tests exercise the shipped behaviour rather than a test-local reconstruction of it.
/// </para>
/// <para>
/// <b>Opened through the product's own route.</b> Every test injects the shell's version-4
/// <c>WM_CONTEXTMENU</c> callback into the window the icon really registered (the S02/T04 injection
/// precedent shared by the other menu tests), so the anchor window, the placement arithmetic, the
/// owner repair and the close sequence are the shipped ones. Only the shell seam is a fake.
/// </para>
/// <para>
/// <b>What the assertions are, and what they are not.</b> "Ran exactly once before Dispose returned"
/// is read as the handler count immediately after the call returns - the delivery has to have happened
/// <em>inside</em> the call, because the anchor the popup is owned by no longer exists afterwards and
/// no later pump can produce it. "No window left behind" is read with <c>IsWindow</c> on both handles
/// the library recorded, plus the same visible-popup enumeration the reference sample uses, and it is
/// read with no pump in between: that is the "synchronous and complete" half of the disposal contract,
/// which this task must not weaken.
/// </para>
/// <para>
/// <b>No skip, and no condition.</b> A refused foreground claim is a state the contract is written for
/// (D044 repairs the owner and re-claims the activation relationship), so the hostile variant below is
/// a second unconditional test rather than a branch inside the first one.
/// </para>
/// <para>
/// The class opens real popups on the desktop, so it joins <see cref="TrayMenuDismissalCollection"/> -
/// the serial tail the dismissal proof and the owner-determinism proof already run in - rather than
/// sharing the process with window-creating classes whose own measurements read the same window set.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayIconMenuCloseNotificationTests
{
    /// <summary>
    /// A menu closed by disposing the icon delivers <c>Closed</c> once, inside the disposal, and the
    /// disposal leaves no anchor and no popup window behind.
    /// </summary>
    /// <remarks>
    /// The handler is attached <em>after</em> the open, so it is the second subscriber the framework
    /// invokes - the library's own handler (which unsubscribes itself and tears the anchor down) runs
    /// first. Both are invoked by the same raise, which is why a count of one here also proves the
    /// library did not consume the event or tear anything down before the framework raised it.
    /// </remarks>
    [StaFact]
    public void Disposing_the_icon_delivers_the_open_menus_Closed_exactly_once_before_it_returns()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");
        TrayIcon trayIcon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        var deliveries = new List<object?>();

        menu.Closed += (sender, _) => deliveries.Add(sender);

        try
        {
            TrayIconMenuFixture.OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, $"The injected right click must open the menu. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            IntPtr anchor = trayIcon.MenuAnchorHandle;
            IntPtr popup = trayIcon.MenuPopupHandle;

            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.True(Win32.IsWindow(anchor), $"The anchor window must exist while the menu is open. {TrayIconMenuFixture.Describe(trayIcon, shell)}");
            Assert.NotEqual(IntPtr.Zero, popup);
            Assert.True(Win32.IsWindow(popup), $"The popup window must exist while the menu is open. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            // Nothing is delivered while the menu is still showing: the notification belongs to the
            // close, not to the open.
            Assert.Empty(deliveries);

            trayIcon.Dispose();

            // The delivery happened inside Dispose - the assertion is taken before anything is pumped
            // afterwards, and it is the consumer's own menu instance that raised it.
            Assert.Single(deliveries);
            Assert.Same(menu, deliveries[0]);

            // The existing disposal contract, unchanged: synchronous, complete, idempotent.
            Assert.False(menu.IsOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.False(trayIcon.IsMenuOpen);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor), $"Disposal must destroy the anchor window. {TrayIconMenuFixture.Describe(trayIcon, shell)}");
            Assert.Null(menu.PlacementTarget);

            // ... including the popup window, which is the half a "delivered but still on screen" close
            // would get wrong.
            Assert.False(Win32.IsWindow(popup), $"The popup window must be gone before Dispose returns. {TrayIconMenuFixture.Describe(trayIcon, shell)}");
            Assert.Empty(TrayMenuScenario.FindPopupWindows());

            // A second disposal delivers nothing: one open menu, one close, one notification.
            trayIcon.Dispose();

            Assert.Single(deliveries);

            // ... and nothing arrives late either, once the queue has had every chance to raise it
            // again: the late second delivery is what "exactly once" has to exclude.
            TrayMenuScenario.Pump(TrayIconMenuFixture.PopupSettleMilliseconds);

            Assert.Single(deliveries);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayMenuScenario.Pump(TrayIconMenuFixture.PopupSettleMilliseconds);
        }
    }

    /// <summary>
    /// The same disposal-driven close, in the hermetic hostile state where Windows refuses the
    /// foreground claim: the popup is built ownerless, and the consumer still gets its
    /// <c>Closed</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is scripted on the seam (<c>SetForegroundWindowResult = false</c>), which is the
    /// state the S06 UAT measured as five failing tests and the state every menu-repair contract in this
    /// slice is written for; here it is the state the close notification must survive as well.
    /// </para>
    /// <para>
    /// The ownerless construction is asserted rather than assumed - <see cref="TrayIcon.MenuOwnerBeforeRepair"/>
    /// must be <see cref="IntPtr.Zero"/> while the menu is open - so this test cannot pass by quietly
    /// taking the ordinary path when the session grants the foreground.
    /// </para>
    /// </remarks>
    [StaFact]
    public void With_the_foreground_claim_refused_the_disposal_driven_close_still_notifies_and_leaves_no_window()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();

        shell.SetForegroundWindowResult = false;

        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");
        TrayIcon trayIcon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        int deliveries = 0;

        menu.Closed += (_, _) => deliveries++;

        try
        {
            TrayIconMenuFixture.OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, $"The injected right click must open the menu. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            // The hostile state is the measured one: the claim was refused through the seam, so WPF
            // built the popup without an owner and the library's repair is what made the anchor its
            // owner (T02/D044). If this reading is not zero, the refusal was not real and the test
            // below proves nothing about the hostile state.
            Assert.True(
                shell.SetForegroundWindowResults.Count > 0 && !shell.SetForegroundWindowResults[0],
                $"The scripted refusal must be the state the open ran in. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            // ... and WPF really built the popup ownerless in that state, which is the reading the
            // library records before its repair runs.
            Assert.Equal(IntPtr.Zero, trayIcon.MenuOwnerBeforeRepair);

            IntPtr anchor = trayIcon.MenuAnchorHandle;
            IntPtr popup = trayIcon.MenuPopupHandle;

            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.NotEqual(IntPtr.Zero, popup);

            trayIcon.Dispose();

            Assert.Equal(1, deliveries);
            Assert.False(menu.IsOpen);
            Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(anchor), $"Disposal must destroy the anchor window. {TrayIconMenuFixture.Describe(trayIcon, shell)}");
            Assert.False(Win32.IsWindow(popup), $"The popup window must be gone before Dispose returns. {TrayIconMenuFixture.Describe(trayIcon, shell)}");
            Assert.Empty(TrayMenuScenario.FindPopupWindows());

            trayIcon.Dispose();

            Assert.Equal(1, deliveries);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayMenuScenario.Pump(TrayIconMenuFixture.PopupSettleMilliseconds);
        }
    }

    /// <summary>
    /// The close trace line records whether the consumer's notification was delivered, so a support
    /// log can tell a close that notified from one that did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The instrument is a listener on the library's own <see cref="NotifyIconTrace.Source"/>, raised
    /// to Verbose for the duration of the disposal. It has to be a test rather than the reference
    /// sample: the sample can only attach to a source it constructs by name, which is not the internal
    /// instance the library writes to (the S03 finding about the documented channel), so a sample line
    /// would measure a different object.
    /// </para>
    /// <para>
    /// The line is written from inside the disposal - the delivery runs the library's own handler,
    /// which is what tears the anchor down - so exactly one close line must appear for one disposal,
    /// and it must say the notification arrived.
    /// </para>
    /// </remarks>
    [StaFact]
    public void The_close_trace_line_records_that_the_consumer_notification_was_delivered()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");
        TrayIcon trayIcon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        int deliveries = 0;

        menu.Closed += (_, _) => deliveries++;

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var listener = new TextWriterTraceListener(writer);
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        string captured;

        try
        {
            TrayIconMenuFixture.OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, $"The injected right click must open the menu. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            source.Listeners.Add(listener);
            source.Switch.Level = SourceLevels.Verbose;

            trayIcon.Dispose();

            source.Flush();
            captured = writer.ToString();
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(listener);
            listener.Dispose();
            TrayIconMenuFixture.CloseMenu(menu);
            TrayMenuScenario.Pump(TrayIconMenuFixture.PopupSettleMilliseconds);
        }

        Assert.Equal(1, deliveries);
        Assert.Contains("TrayIcon menu closed; anchor 0x", captured, StringComparison.Ordinal);
        Assert.Contains("closeNotificationDelivered=True", captured, StringComparison.Ordinal);

        // One close, one line: the pump must not have torn the menu down twice.
        Assert.Equal(1, captured.Split("TrayIcon menu closed;").Length - 1);
    }

    /// <summary>
    /// Disposal of an icon whose menu is already closed delivers nothing and does not pump the
    /// dispatcher queue - the bounded wait exists for a pending close, not for every disposal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A negative control for the repair rather than a restatement of it: a queued work item posted
    /// immediately before <see cref="TrayIcon.Dispose"/> must still be waiting afterwards, which is
    /// only observable if no pump ran. The item is then run by an explicit pump and asserted to have
    /// executed, so the assertion above is about the absence of a pump and not about a post that never
    /// happened.
    /// </para>
    /// <para>
    /// The menu is closed the consumer's way (<c>menu.IsOpen = false</c> plus a pump), which delivers
    /// its own <c>Closed</c> - the same route an outside click takes - so the count of one afterwards
    /// is the consumer-driven close's notification while the disposal contributes none.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Disposing_an_icon_whose_menu_is_already_closed_delivers_nothing_and_does_not_pump_the_queue()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");
        TrayIcon trayIcon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        int deliveries = 0;
        bool postedWorkRan = false;

        menu.Closed += (_, _) => deliveries++;

        try
        {
            TrayIconMenuFixture.OpenMenu(trayIcon, iconId);

            Assert.True(menu.IsOpen, $"The injected right click must open the menu. {TrayIconMenuFixture.Describe(trayIcon, shell)}");

            // The consumer-driven close, with the queue pumped so WPF delivers the notification itself.
            TrayIconMenuFixture.CloseMenu(menu);

            Assert.False(menu.IsOpen);
            Assert.Null(trayIcon.OpenContextMenu);
            Assert.Equal(1, deliveries);

            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => postedWorkRan = true));

            trayIcon.Dispose();

            Assert.False(
                postedWorkRan,
                "Disposal of an icon with nothing open must not pump the dispatcher queue; a queued work item was executed by it.");

            Assert.Equal(1, deliveries);

            // The control: the item was really queued, so running the queue runs it.
            TrayMenuScenario.Pump(50);

            Assert.True(postedWorkRan, "The queued work item must run when the queue is pumped; the check above would otherwise be vacuous.");
            Assert.Equal(1, deliveries);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayMenuScenario.Pump(TrayIconMenuFixture.PopupSettleMilliseconds);
        }
    }
}
