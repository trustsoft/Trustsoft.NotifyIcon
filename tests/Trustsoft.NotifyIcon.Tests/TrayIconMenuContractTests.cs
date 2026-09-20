using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S03 boundary contracts that S05 and S06 consume, asserted as contracts rather than as
/// incidental values, so an edge S03 owes its neighbours is discovered here instead of in the middle
/// of their own work.
/// </summary>
/// <remarks>
/// <para>
/// <b>The S03 to S06 edge is the declarative menu surface.</b> S06 hands the icon a
/// <see cref="ContextMenu"/> out of a <c>StaticResource</c>, so the property has to be a real
/// dependency property with a resolvable descriptor, a null default and no eager application - the
/// same three clauses <c>SliceContractTests</c> asserts for the S01 properties, which is the
/// precedent this file follows. A property a resource-dictionary lookup cannot resolve, or one that
/// is applied eagerly at assignment time, still compiles from C# and silently breaks declarative
/// usage. The value must also be the caller's own instance, by reference: a clone would lose the
/// caller's <c>DataContext</c>, its item templates and every handler it wired up.
/// </para>
/// <para>
/// <b>The S03 to S05 edge is "the menu path holds no process-wide state".</b> S05 re-creates the
/// icon after an Explorer restart and the menu must then open exactly as it does now, so the menu
/// state has to live on the instance: one icon's open menu must be invisible to a second icon, and
/// no static field of <see cref="TrayIcon"/> may be able to hold a menu or an anchor window. This is
/// asserted by reading the type's statics with reflection and by opening a menu on one instance and
/// inspecting another, which is the same measurement S05's recovery will depend on.
/// </para>
/// <para>
/// <b>The S03 to S02 edge is re-asserted rather than assumed.</b> The menu path reads the routed
/// event it was raised for and opens nothing when a handler marked the click handled, so the
/// naming/routing contract of the four click pairs is load-bearing for the menu as well; a
/// re-strategied <c>Preview</c> event would silently turn menu cancellation into decoration.
/// </para>
/// <para>
/// <b>In the serial tail collection, on purpose.</b> Two tests here open real popups and inject real
/// input through <see cref="Win32TestInput"/>, so the class shares
/// <see cref="TrayMenuDismissalCollection"/> with the proofs that perform the same measurement (and,
/// like them, is deliberately not the GDI collection, since nothing here reads
/// <c>GdiHandles.Count()</c>). Every menu is settled in a <see langword="finally"/> for the reason
/// T04's file documents: a closing WPF popup keeps Windows from granting the next foreground call,
/// which would make a later test's measurement read as a product defect.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayIconMenuContractTests
{
    /// <summary>
    /// <see cref="TrayIcon.ContextMenu"/> is a markup-resolvable dependency property owned by
    /// <see cref="TrayIcon"/>, with <see langword="null"/> as its default, and it is assignable while
    /// no window exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S03 to S06 edge in its exact shape: a <c>StaticResource</c> resolves through a
    /// dependency property, which is why the property exists at all, and S06 populates it from a
    /// resource dictionary <em>before</em> the icon is shown. The "no window exists" clause is
    /// asserted on <see cref="TrayIcon.HostHandle"/> rather than asserted in a remark, because
    /// "assignable before the first registration" is the whole S06 shape: an element that required a
    /// live host window to accept a menu could not be populated declaratively at all.
    /// </para>
    /// <para>
    /// The shadowing is asserted too: the base <see cref="FrameworkElement.ContextMenuProperty"/>
    /// belongs to <c>ContextMenuService</c>'s input-driven automatic opening, which this element
    /// never receives, so a consumer's markup must resolve to the library's property and not to the
    /// base one. Asserting the two are different objects is what makes the property unambiguous
    /// rather than ambiguous-but-currently-working.
    /// </para>
    /// </remarks>
    [StaFact]
    public void ContextMenu_is_a_markup_resolvable_dependency_property_with_a_null_default()
    {
        DependencyProperty property = TrayIcon.ContextMenuProperty;

        Assert.Equal("ContextMenu", property.Name);
        Assert.Equal(typeof(TrayIcon), property.OwnerType);
        Assert.Equal(typeof(ContextMenu), property.PropertyType);
        Assert.False(property.ReadOnly);
        Assert.Null(property.DefaultMetadata.DefaultValue);

        // Registered on TrayIcon itself, not inherited or attached: markup resolves the property
        // through this metadata.
        Assert.NotNull(property.GetMetadata(typeof(TrayIcon)));

        // What a markup consumer actually looks through.
        DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(TrayIcon));

        Assert.NotNull(descriptor);
        Assert.Equal("ContextMenu", descriptor!.Name);
        Assert.Equal(property, descriptor.DependencyProperty);

        // The library's own property, deliberately not FrameworkElement's (D029).
        Assert.NotSame(FrameworkElement.ContextMenuProperty, property);

        PropertyInfo clrProperty = typeof(TrayIcon).GetProperty(nameof(TrayIcon.ContextMenu))!;

        Assert.Equal(typeof(TrayIcon), clrProperty.DeclaringType);
        Assert.Equal(typeof(ContextMenu), clrProperty.PropertyType);
        Assert.NotNull(clrProperty.GetMethod);
        Assert.NotNull(clrProperty.SetMethod);

        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu menu = TrayIconMenuFixture.CreateMenu("Alpha");

        using var trayIcon = new TrayIcon(shell);

        // The S06 shape: a resource dictionary populates the property before the icon is shown, so
        // nothing about the assignment may need a window, a dispatcher call or a shell call.
        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);
        Assert.False(trayIcon.Visible);
        Assert.Null(trayIcon.ContextMenu);

        trayIcon.ContextMenu = menu;

        Assert.Same(menu, trayIcon.ContextMenu);
        Assert.Same(menu, trayIcon.GetValue(TrayIcon.ContextMenuProperty));

        // The accessors are bound to the library's property and not to the base one: the value a
        // consumer assigns is the value the click path reads, and the framework's own
        // ContextMenuService property stays empty rather than holding a second, silently different
        // menu. This is the assertion that makes the shadowing unambiguous rather than ambiguous-but-
        // currently-working.
        Assert.Null(trayIcon.GetValue(FrameworkElement.ContextMenuProperty));

        // Assignment applied nothing: no host window, no registration, no seam call.
        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);
        Assert.Empty(shell.Calls);
        Assert.Equal(IntPtr.Zero, trayIcon.MenuAnchorHandle);

        // ... and the documented "no menu" spelling is the null assignment.
        trayIcon.ContextMenu = null;

        Assert.Null(trayIcon.ContextMenu);

        // MenuActivation travels with the same markup edge and keeps its documented default.
        Assert.Equal(TrayMenuActivation.RightClick, TrayIcon.MenuActivationProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(TrayMenuActivation.RightClick, trayIcon.MenuActivation);
        Assert.Equal(typeof(TrayMenuActivation), TrayIcon.MenuActivationProperty.PropertyType);
    }

    /// <summary>
    /// The assigned menu and its items survive the round trip <b>by reference identity</b>, so what
    /// the click path opens is the caller's own object graph.
    /// </summary>
    /// <remarks>
    /// The task's R009 clause in its measurable form: a clone would break the caller's
    /// <c>DataContext</c>, its item templates and its handlers, and no amount of "the menu opened"
    /// evidence would show it - the popup would exist and belong to the wrong object graph. Asserting
    /// <see cref="Assert.Same(object?, object?)"/> on both the menu and its item is the only check
    /// that distinguishes the two, and it is cheap enough to run on every commit.
    /// </remarks>
    [StaFact]
    public void The_assigned_menu_and_its_items_survive_the_round_trip_by_reference_identity()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        var menu = new ContextMenu();
        var firstItem = new MenuItem { Header = "Alpha" };
        var secondItem = new MenuItem { Header = "Beta" };

        menu.Items.Add(firstItem);
        menu.Items.Add(secondItem);

        using var trayIcon = new TrayIcon(shell);

        trayIcon.ContextMenu = menu;

        ContextMenu resolved = Assert.IsType<ContextMenu>(trayIcon.ContextMenu);

        Assert.Same(menu, resolved);
        Assert.Equal(2, resolved.Items.Count);
        Assert.Same(firstItem, resolved.Items[0]);
        Assert.Same(secondItem, resolved.Items[1]);

        // The instance the property holds is the instance the caller still owns - nothing was
        // re-parented into a library-owned container on assignment.
        Assert.Empty(firstItem.Name);

        var replacement = new ContextMenu();

        replacement.Items.Add(new MenuItem { Header = "Gamma" });
        trayIcon.ContextMenu = replacement;

        Assert.Same(replacement, trayIcon.ContextMenu);
        Assert.NotSame(menu, trayIcon.ContextMenu);
    }

    /// <summary>
    /// The menu state is per instance and cannot live in a static field: a second icon is unaffected
    /// by the first icon's open menu, the two anchors are different windows, and closing one leaves
    /// the other alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the S03 to S05 edge. S05 re-creates the icon after an Explorer restart and the menu
    /// must open exactly as it does now; that is only true while the menu path's state is instance
    /// state, so the property is measured rather than asserted in a comment. The reflection half of
    /// the test is the static-side statement of the same thing: <see cref="TrayIcon"/> has static
    /// fields (the routed events, the dependency properties and the icon-id counter are all static by
    /// design), and a future field of a menu- or window-bearing type would be the leak this test
    /// exists to prevent - so the assertion is over the <em>declared types</em> of the statics, not
    /// over a hand-picked list.
    /// </para>
    /// <para>
    /// <b>The simultaneous case is asserted as an invariant, not as an outcome.</b> Measured while
    /// writing this test: with one icon's menu showing, opening a second icon's menu left both menus
    /// closed - WPF keeps only one popup active, and the newly made-foreground anchor window is an
    /// interaction the previously open menu reacts to. The outcome belongs to the framework rather
    /// than to this library, so the test asserts what the library owes - neither instance ever holds
    /// the other instance's menu or the other instance's anchor window - and the measurement itself is
    /// recorded in the failure message and in the slice's UAT notes (finding F2 of
    /// <c>docs/UAT-S03.md</c>).
    /// </para>
    /// </remarks>
    [StaFact]
    public void The_menu_state_is_per_instance_and_no_static_field_can_hold_a_menu_or_an_anchor()
    {
        FieldInfo[] statics = typeof(TrayIcon).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(statics);

        foreach (FieldInfo field in statics)
        {
            string message = $"Static field '{field.Name}' is of type {field.FieldType}, which could hold menu-path state; the menu must live on the instance (S05's recovery depends on it).";

            Assert.False(typeof(ContextMenu).IsAssignableFrom(field.FieldType), message);
            Assert.False(typeof(FrameworkElement).IsAssignableFrom(field.FieldType), message);
            Assert.False(typeof(TrayMenuAnchorWindow).IsAssignableFrom(field.FieldType), message);
            Assert.False(typeof(TrayIcon).IsAssignableFrom(field.FieldType), message);
            Assert.NotEqual(typeof(object), field.FieldType);
        }

        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu firstMenu = TrayIconMenuFixture.CreateMenu("Alpha");
        ContextMenu secondMenu = TrayIconMenuFixture.CreateMenu("Beta");

        using TrayIcon first = TrayIconMenuFixture.CreateRegisteredIcon(shell, firstMenu, out uint firstIconId);
        using TrayIcon second = TrayIconMenuFixture.CreateRegisteredIcon(shell, secondMenu, out uint secondIconId);

        try
        {
            Assert.NotEqual(firstIconId, secondIconId);

            TrayIconMenuFixture.OpenMenu(first, firstIconId);

            Assert.True(first.IsMenuOpen, TrayIconMenuFixture.Describe(first, shell));
            Assert.Same(firstMenu, first.OpenContextMenu);

            IntPtr firstAnchor = first.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, firstAnchor);
            Assert.True(Win32.IsWindow(firstAnchor));

            // The second instance sees none of it: no menu, no anchor, and its own seam entry is
            // untouched.
            Assert.False(second.IsMenuOpen);
            Assert.Null(second.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, second.MenuAnchorHandle);
            Assert.Null(second.LastIconRectHresult);

            // Closing the first instance's menu tears down that instance's anchor only - and leaves
            // the second instance, which never opened anything, still untouched.
            TrayIconMenuFixture.CloseMenu(firstMenu);

            Assert.False(first.IsMenuOpen);
            Assert.Equal(IntPtr.Zero, first.MenuAnchorHandle);
            Assert.False(Win32.IsWindow(firstAnchor));
            Assert.False(second.IsMenuOpen);
            Assert.Equal(IntPtr.Zero, second.MenuAnchorHandle);
            Assert.Null(second.OpenContextMenu);

            // The second instance opens its own menu: its state is its own, and the first instance's
            // state stays cleared. The anchors are two different windows.
            TrayIconMenuFixture.OpenMenu(second, secondIconId);

            Assert.True(second.IsMenuOpen, TrayIconMenuFixture.Describe(second, shell));
            Assert.Same(secondMenu, second.OpenContextMenu);

            IntPtr secondAnchor = second.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, secondAnchor);
            Assert.True(Win32.IsWindow(secondAnchor));
            Assert.NotEqual(firstAnchor, secondAnchor);

            Assert.False(first.IsMenuOpen);
            Assert.Equal(IntPtr.Zero, first.MenuAnchorHandle);
            Assert.Null(first.OpenContextMenu);

            // Both instances in play at once. Measured, and deliberately not pinned to an outcome:
            // with the second icon's menu showing, opening the first icon's menu left both menus
            // closed - WPF keeps one popup active at a time and an anchor taking the foreground is an
            // interaction the previously open menu reacts to. That is framework behaviour rather than
            // a library property, so this phase asserts only what the library owes: neither instance
            // may end up holding the other instance's menu or the other instance's anchor window, and
            // no popup may be left ownerless-but-alive by the sequence. The measured outcome is
            // carried in the failure messages and recorded in docs/UAT-S03.md.
            TrayIconMenuFixture.OpenMenu(first, firstIconId);

            bool firstOpenAfterSimultaneousOpen = first.IsMenuOpen;
            bool secondOpenAfterSimultaneousOpen = second.IsMenuOpen;
            string outcome = $"measured after opening the first icon's menu while the second's was open: firstOpen={firstOpenAfterSimultaneousOpen}, secondOpen={secondOpenAfterSimultaneousOpen}";

            Assert.True(
                first.OpenContextMenu is null || ReferenceEquals(first.OpenContextMenu, firstMenu),
                $"The first instance must never hold the second instance's menu. {outcome}. {TrayIconMenuFixture.Describe(first, shell)}");
            Assert.True(
                first.MenuAnchorHandle == IntPtr.Zero || first.MenuAnchorHandle != secondAnchor,
                $"The first instance must never hold the second instance's anchor window. {outcome}. {TrayIconMenuFixture.Describe(first, shell)}");
            Assert.True(
                second.OpenContextMenu is null || ReferenceEquals(second.OpenContextMenu, secondMenu),
                $"The second instance must never hold the first instance's menu. {outcome}. {TrayIconMenuFixture.Describe(second, shell)}");
            Assert.True(
                second.MenuAnchorHandle == IntPtr.Zero || second.MenuAnchorHandle != firstAnchor,
                $"The second instance must never hold the first instance's anchor window. {outcome}. {TrayIconMenuFixture.Describe(second, shell)}");

            // The two anchors were, and remain, two different windows - whichever of them survived.
            Assert.NotEqual(firstAnchor, secondAnchor);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(firstMenu);
            TrayIconMenuFixture.CloseMenu(secondMenu);
            TrayIconMenuFixture.Settle();
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// One menu instance assigned to two icons fails visibly instead of silently moving the live
    /// popup: the second icon reports through <see cref="TrayIcon.TrayError"/>, opens nothing and
    /// does no placement work, and the first icon's popup keeps its owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The negative half of the two-icon rule the property's remarks document as unsupported. The
    /// behaviour was picked in T04 (fail visibly, never adopt) and is pinned here as an S03 boundary
    /// contract because "which of the two popups is misplaced" is exactly the class of bug S05's
    /// re-creation could reintroduce. <c>TrayIconMenuActivationTests</c> holds the fuller measurement
    /// including the popup's owner; this test asserts the decision at the boundary.
    /// </para>
    /// <para>
    /// The precondition is measured before the second click, not assumed: the popup that is showing
    /// must really be owned by the first instance's anchor window, because an ownerless popup (the
    /// failure T03 pinned) is dismissed by things the framework owns - among them the synthetic mouse
    /// move the foreground grant performs - and a test that skipped this check would then attribute a
    /// framework dismissal to the two-icon rule.
    /// </para>
    /// </remarks>
    [StaFact]
    public void A_menu_already_open_on_another_icon_is_reported_and_the_live_popup_is_not_moved()
    {
        FakeShellApi shell = TrayIconMenuFixture.CreateShell();
        ContextMenu sharedMenu = TrayIconMenuFixture.CreateMenu("Shared");

        using TrayIcon first = TrayIconMenuFixture.CreateRegisteredIcon(shell, sharedMenu, out uint firstIconId);
        using TrayIcon second = TrayIconMenuFixture.CreateRegisteredIcon(shell, sharedMenu, out uint secondIconId);

        try
        {
            var errors = new List<TrayErrorEventArgs>();

            second.TrayError += (_, e) => errors.Add(e);

            TrayIconMenuFixture.OpenMenu(first, firstIconId);

            Assert.True(sharedMenu.IsOpen, TrayIconMenuFixture.Describe(first, shell));

            IntPtr firstAnchor = first.MenuAnchorHandle;

            Assert.NotEqual(IntPtr.Zero, firstAnchor);

            // The precondition: the popup that is showing has a real owner, and it is the first
            // instance's anchor. Without this, an ownerless popup (the T03 failure) could be
            // dismissed by the foreground grant itself and the assertions below would read as the
            // two-icon rule working when the measurement was simply lost.
            IntPtr popup = Assert.Single(TrayMenuScenario.FindPopupWindows());
            IntPtr popupOwner = Win32.GetWindow(popup, Win32.GW_OWNER);

            Assert.True(
                popupOwner == firstAnchor,
                $"The popup must be owned by the first instance's anchor 0x{firstAnchor.ToInt64():X}, not 0x{popupOwner.ToInt64():X}. {TrayIconMenuFixture.Describe(first, shell)}");

            TrayIconMenuFixture.OpenMenu(second, secondIconId);

            TrayErrorEventArgs reported = Assert.Single(errors);

            Assert.Equal(TrayIconException.OperationOpenMenu, reported.Operation);
            Assert.Equal(0, reported.Win32ErrorCode);
            Assert.Contains("already open", reported.Exception.Message, StringComparison.Ordinal);

            // The second icon opened nothing, owns nothing and did no placement work: the shell was
            // asked for a rectangle exactly once, by the first icon.
            Assert.False(second.IsMenuOpen);
            Assert.Null(second.OpenContextMenu);
            Assert.Equal(IntPtr.Zero, second.MenuAnchorHandle);
            Assert.Null(second.LastIconRectHresult);
            Assert.Single(shell.ShellNotifyIconGetRectIdentifiers);

            // ... and whatever became of the menu that was showing, the second instance never adopted
            // it and never acquired its anchor window.
            string outcome = $"after the second icon's click: firstOpen={first.IsMenuOpen}, sharedMenu.IsOpen={sharedMenu.IsOpen}, firstAnchorLive={Win32.IsWindow(firstAnchor)}";

            Assert.True(
                first.OpenContextMenu is null || ReferenceEquals(first.OpenContextMenu, sharedMenu),
                $"The first instance's menu state must not be replaced by the second instance's. {outcome}. {TrayIconMenuFixture.Describe(first, shell)}");
            Assert.True(
                first.MenuAnchorHandle == IntPtr.Zero || first.MenuAnchorHandle == firstAnchor,
                $"The first instance's anchor must be its own. {outcome}. {TrayIconMenuFixture.Describe(first, shell)}");
            Assert.NotEqual(firstAnchor, second.MenuAnchorHandle);

            if (first.IsMenuOpen)
            {
                // The clause T03/T04 measured: the live popup is untouched and still owned by the first
                // instance's anchor window - asserted when the menu really is still showing.
                Assert.Same(sharedMenu, first.OpenContextMenu);
                Assert.Equal(firstAnchor, first.MenuAnchorHandle);
                IntPtr stillShowingPopup = Assert.Single(TrayMenuScenario.FindPopupWindows());

                Assert.True(
                    Win32.GetWindow(stillShowingPopup, Win32.GW_OWNER) == firstAnchor,
                    $"The live popup must still be owned by the first instance's anchor 0x{firstAnchor.ToInt64():X}. {outcome}");
            }
            else
            {
                // A dismissed popup must leave no state behind: not the menu, not the anchor window.
                Assert.Null(first.OpenContextMenu);
                Assert.Equal(IntPtr.Zero, first.MenuAnchorHandle);
                Assert.False(Win32.IsWindow(firstAnchor), outcome);
            }
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(sharedMenu);
            TrayIconMenuFixture.Settle();
        }

        Assert.Empty(TrayMenuScenario.FindPopupWindows());
    }

    /// <summary>
    /// The routed-event contract the menu's cancellation depends on is unchanged: the four click
    /// pairs keep their names, their Bubble/Tunnel strategies, their owner and their handler type.
    /// </summary>
    /// <remarks>
    /// The S03 to S02 edge. The menu respects cancellation by not opening when a handler marked the
    /// click handled, which only works because the Touch/Preview event really is raised first and
    /// really is a tunnel-routed event WPF resolves by name - and because a markup consumer
    /// subscribes through that name. <c>TrayIconClickEventContractTests</c> audits the CLR wrappers
    /// and the literal markup spellings for S02; what this test adds is that the <em>names the menu
    /// path's own code reads</em> and the strategies it relies on are still the ones S02 shipped.
    /// </remarks>
    [StaFact]
    public void The_click_event_routing_contract_the_menu_cancellation_relies_on_is_unchanged()
    {
        (string MainName, RoutedEvent Main, string PreviewName, RoutedEvent Preview)[] pairs =
        [
            (TrayIcon.TrayLeftClickEventName, TrayIcon.TrayLeftClickEvent, TrayIcon.PreviewTrayLeftClickEventName, TrayIcon.PreviewTrayLeftClickEvent),
            (TrayIcon.TrayLeftDoubleClickEventName, TrayIcon.TrayLeftDoubleClickEvent, TrayIcon.PreviewTrayLeftDoubleClickEventName, TrayIcon.PreviewTrayLeftDoubleClickEvent),
            (TrayIcon.TrayRightClickEventName, TrayIcon.TrayRightClickEvent, TrayIcon.PreviewTrayRightClickEventName, TrayIcon.PreviewTrayRightClickEvent),
            (TrayIcon.TrayMiddleClickEventName, TrayIcon.TrayMiddleClickEvent, TrayIcon.PreviewTrayMiddleClickEventName, TrayIcon.PreviewTrayMiddleClickEvent),
        ];

        Assert.Equal(4, pairs.Length);

        foreach ((string mainName, RoutedEvent main, string previewName, RoutedEvent preview) in pairs)
        {
            Assert.Equal(mainName, main.Name);
            Assert.Equal(previewName, preview.Name);
            Assert.Equal($"Preview{main.Name}", preview.Name);
            Assert.Equal(RoutingStrategy.Bubble, main.RoutingStrategy);
            Assert.Equal(RoutingStrategy.Tunnel, preview.RoutingStrategy);
            Assert.Equal(typeof(TrayIcon), main.OwnerType);
            Assert.Equal(typeof(TrayIcon), preview.OwnerType);
            Assert.Equal(typeof(EventHandler<TrayIconClickEventArgs>), main.HandlerType);
            Assert.Equal(typeof(EventHandler<TrayIconClickEventArgs>), preview.HandlerType);
        }

        // The right-click pair is the one the menu path consumes, so it is named explicitly rather
        // than only reached through the loop.
        Assert.Equal("TrayRightClick", TrayIcon.TrayRightClickEvent.Name);
        Assert.Equal("PreviewTrayRightClick", TrayIcon.PreviewTrayRightClickEvent.Name);
        Assert.Equal(RoutingStrategy.Tunnel, TrayIcon.PreviewTrayRightClickEvent.RoutingStrategy);
    }
}

/// <summary>
/// The menu-path scaffolding shared by the S03 boundary contracts and the owner-lifetime proof: a
/// scripted shell seam, a one-item menu, a registered windowless icon and the two injected-input
/// operations (a synchronous right-click callback into the icon's own host window, and a close that
/// lets the popup settle).
/// </summary>
/// <remarks>
/// <para>
/// It deliberately asserts nothing beyond what the caller must be able to rely on to build a
/// measurement (the icon really registered, the id really is the registered one), exactly like
/// <see cref="TrayMenuScenario"/>. Everything here goes through the product's own entry point - the
/// real host window, the real decoder, the real anchor window and a real WPF popup - so the tests
/// that use it measure the delivered path and not a rehearsal of it. Only the shell seam and the
/// monitor reader are doubles.
/// </para>
/// <para>
/// <b>The shell rectangle is scripted to a fixed on-screen rectangle</b> so placement is
/// deterministic; the monitor reader is the real one, because the tests that use this fixture assert
/// ownership, identity and lifetime rather than coordinates, and the real reader keeps the
/// production path in the measurement.
/// </para>
/// </remarks>
internal static class TrayIconMenuFixture
{
    /// <summary>The scripted icon rectangle's left edge, in physical pixels.</summary>
    internal const int IconLeft = 100;

    /// <summary>The scripted icon rectangle's top edge, in physical pixels.</summary>
    internal const int IconTop = 200;

    /// <summary>The scripted icon rectangle's exclusive right edge, in physical pixels.</summary>
    internal const int IconRight = 116;

    /// <summary>The scripted icon rectangle's exclusive bottom edge, in physical pixels.</summary>
    internal const int IconBottom = 216;

    /// <summary>How long the dispatcher is pumped after the click so the popup exists.</summary>
    internal const int PopupOpenMilliseconds = 600;

    /// <summary>How long the dispatcher is pumped after a close so the popup is really gone.</summary>
    internal const int PopupSettleMilliseconds = 400;

    /// <summary>
    /// Builds a shell seam whose <c>Shell_NotifyIconGetRect</c> answers with the scripted icon
    /// rectangle, so the menu path takes the shell route rather than the cursor fallback.
    /// </summary>
    /// <returns>The scripted seam.</returns>
    internal static FakeShellApi CreateShell() =>
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
            CursorPositionX = 640,
            CursorPositionY = 480,
        };

    /// <summary>
    /// Builds a one-item menu. WPF suppresses a menu with no items, so an empty menu would prove
    /// nothing about placement, ownership or lifetime.
    /// </summary>
    /// <param name="header">The item's header text.</param>
    /// <returns>The menu, owned by the caller.</returns>
    internal static ContextMenu CreateMenu(string header)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = header });

        return menu;
    }

    /// <summary>
    /// Creates a registered <see cref="TrayIcon"/> over the given seam, optionally carrying a menu.
    /// </summary>
    /// <param name="shell">The scripted shell seam.</param>
    /// <param name="menu">The menu to assign, or <see langword="null"/> for "no menu".</param>
    /// <param name="iconId">Receives the icon id the registration carried.</param>
    /// <returns>The registered icon, owned by the caller.</returns>
    /// <remarks>
    /// The id comes from the instance, not from a snapshot index: several icons can be registered
    /// over one seam, and the shell reports the icon id in <c>HIWORD(lParam)</c>, so an injection
    /// carrying another icon's id is correctly ignored as foreign.
    /// </remarks>
    internal static TrayIcon CreateRegisteredIcon(FakeShellApi shell, ContextMenu? menu, out uint iconId)
    {
        var trayIcon = new TrayIcon(shell, Dispatcher.CurrentDispatcher)
        {
            ContextMenu = menu,
            Visible = true,
        };

        iconId = trayIcon.IconId;

        Assert.NotEqual(0u, iconId);
        Assert.NotEqual(IntPtr.Zero, trayIcon.HostHandle);
        Assert.Equal(iconId, shell.ShellNotifyIconDataSnapshots[^1].uID);

        return trayIcon;
    }

    /// <summary>
    /// Sends one <c>WM_CONTEXTMENU</c> callback - the right-click notification the shell delivers -
    /// into the icon's own host window, synchronously, and pumps long enough for the popup to exist.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the callback.</param>
    /// <param name="iconId">The icon id for <c>HIWORD(lParam)</c>.</param>
    /// <param name="settleMilliseconds">How long to pump after the send.</param>
    internal static void OpenMenu(TrayIcon trayIcon, uint iconId, int settleMilliseconds = PopupOpenMilliseconds)
    {
        // The OS precondition for the foreground call the popup's ownership depends on: see
        // Win32TestInput.GrantLastInputToThisProcess. A real user moves the pointer to the tray icon
        // before right-clicking, so this is the production precondition rather than a test crutch.
        Win32TestInput.GrantLastInputToThisProcess();

        SendContextMenuCallback(trayIcon, iconId);
        TrayMenuScenario.Pump(settleMilliseconds);
    }

    /// <summary>
    /// Sends one version-4 <c>WM_CONTEXTMENU</c> callback to the icon's own host window,
    /// synchronously.
    /// </summary>
    /// <param name="trayIcon">The icon whose host window receives the callback.</param>
    /// <param name="iconId">The icon id for <c>HIWORD(lParam)</c>.</param>
    /// <remarks>
    /// A zero anchor on purpose: the click's own anchor point is documented as undefined for
    /// <c>WM_CONTEXTMENU</c> and the library must not read it, so the injection sends the least
    /// informative value there is.
    /// </remarks>
    internal static void SendContextMenuCallback(TrayIcon trayIcon, uint iconId) =>
        Win32.SendMessage(
            trayIcon.HostHandle,
            ShellConstants.TrayCallbackMessage,
            IntPtr.Zero,
            Payload(ShellConstants.WM_CONTEXTMENU, iconId));

    /// <summary>
    /// Closes a menu if it is open and pumps until its popup is really gone.
    /// </summary>
    /// <param name="menu">The menu to close; <see langword="null"/> is ignored.</param>
    /// <param name="settleMilliseconds">How long to pump after the close.</param>
    /// <remarks>
    /// The pump is not cosmetic: WPF tears the popup's window down through the dispatcher rather than
    /// inside the close call, and an active or half-destroyed menu makes Windows refuse the next
    /// <c>SetForegroundWindow</c>, which would cost a later test its popup owner.
    /// </remarks>
    internal static void CloseMenu(ContextMenu? menu, int settleMilliseconds = PopupSettleMilliseconds)
    {
        if (menu is null)
        {
            return;
        }

        if (menu.IsOpen)
        {
            menu.IsOpen = false;
        }

        TrayMenuScenario.Pump(settleMilliseconds);
    }

    /// <summary>Pumps the dispatcher queue for the popup settle duration, without touching a menu.</summary>
    internal static void Settle() => TrayMenuScenario.Pump(PopupSettleMilliseconds);

    /// <summary>
    /// Renders the measured menu state as one line, so a failing assertion carries the evidence that
    /// makes it diagnosable instead of only the value that differed.
    /// </summary>
    /// <param name="trayIcon">The icon under test.</param>
    /// <param name="shell">The scripted seam.</param>
    /// <returns>A single-line description of the fields that describe the menu path.</returns>
    internal static string Describe(TrayIcon trayIcon, FakeShellApi shell)
    {
        IntPtr anchor = trayIcon.MenuAnchorHandle;
        string anchorPosition = anchor != IntPtr.Zero && Win32.GetWindowRect(anchor, out NativeRect rectangle)
            ? $"0x{anchor.ToInt64():X}({rectangle.left},{rectangle.top})"
            : anchor == IntPtr.Zero ? "none" : $"0x{anchor.ToInt64():X}(?)";

        return $"menuOpen={trayIcon.IsMenuOpen} anchor={anchorPosition} host=0x{trayIcon.HostHandle.ToInt64():X} "
            + $"registered={trayIcon.IsRegistered} iconRectHresult={(trayIcon.LastIconRectHresult is int hr ? $"0x{hr:X8}" : "not attempted")} "
            + $"cursorFallback={trayIcon.LastMenuPlacementUsedCursorFallback} getRectCalls={shell.ShellNotifyIconGetRectIdentifiers.Count} "
            + $"popupWindows=[{string.Join(", ", TrayMenuScenario.FindPopupWindows().Select(window => $"0x{window.ToInt64():X}"))}]";
    }

    /// <summary>
    /// Packs an event code and a 16-bit icon id into the <c>lParam</c> payload the way the shell
    /// does.
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
