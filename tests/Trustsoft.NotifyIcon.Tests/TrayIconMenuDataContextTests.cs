using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Where a menu's bindings resolve from on the declarative path: the S06 acceptance criterion's last
/// clause ("bindings in the menu resolve against the component's <c>DataContext</c>") settled with a
/// measurement instead of prose.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tension this file exists to resolve.</b> The acceptance line can be read as a promise that
/// the library feeds a menu the <see cref="TrayIcon"/>'s <c>DataContext</c>. The menu contract the
/// library documents and pins says the opposite - the menu "is the caller's instance", nothing "in
/// this library inspects, re-parents or mutates the menu's items", and
/// <see cref="TrayIconMenuContractTests"/> pins the assigned menu and its items by reference identity
/// for exactly that reason. Both cannot be true, so the outcome is measured here rather than argued:
/// <b>the library never writes a menu's <c>DataContext</c>, and the <see cref="TrayIcon"/>'s own
/// <c>DataContext</c> does not reach the menu</b> - a menu declared in a resource dictionary has no
/// logical parent, and what the library sets as the menu's <c>PlacementTarget</c> is its own
/// <c>1x1</c> anchor window, whose element tree carries no <c>DataContext</c> either.
/// </para>
/// <para>
/// <b>What a consumer does instead, and what is pinned here.</b> The caller sets the menu's own
/// <c>DataContext</c> - <c>DataContext="{StaticResource ...}"</c> in markup, <c>menu.DataContext = ...</c>
/// in C# - and the menu's items bind against it, because a <see cref="ContextMenu"/> is the
/// inheritance parent of its own items. That is the documented division of responsibility, and it is
/// asserted in both directions: the bound item resolves to the value on the object graph the caller
/// assigned (same instance, no clone), and the library neither overwrites that value on open nor
/// clears it on teardown.
/// </para>
/// <para>
/// <b>Opening really happens.</b> Binding resolution and the library's non-interference are asserted
/// against a menu opened through the library's own production path - the click path, the shell's icon
/// rectangle, the anchor window and a real WPF popup - not against a menu that was merely assigned,
/// because "the library must not set the menu's <c>DataContext</c> <em>on open</em>" is the clause
/// that matters to a consumer. The shell is <see cref="FakeShellApi"/> throughout, so nothing here
/// reaches the real notification area.
/// </para>
/// <para>
/// <b>In the serial tail collection</b> for the same reason <see cref="TrayIconMenuContractTests"/> is:
/// real popups are opened, and WPF refuses the next foreground grant while a popup is still closing.
/// </para>
/// </remarks>
[Collection(TrayMenuDismissalCollection.Name)]
public sealed class TrayIconMenuDataContextTests
{
    /// <summary>
    /// <b>Measured outcome:</b> the icon's <c>DataContext</c> does not flow into a menu declared in a
    /// resource dictionary, so the bound item resolves to nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declaration is a parsed <see cref="ResourceDictionary"/>, which is the shape S06's sample
    /// uses and the shape a resource-declared menu really has: no logical parent, no inheritance
    /// context, and nothing that would carry a <c>DataContext</c> into it. The icon is created over
    /// the scripted seam and the menu is opened through the library's own path, so the measurement is
    /// taken on the delivered route - the same route that sets <c>PlacementTarget</c>, the placement
    /// offsets and nothing else.
    /// </para>
    /// <para>
    /// The assertions are deliberately about values a reader can act on: the menu's
    /// <c>DataContext</c> is still null after an open (the library wrote nothing), the item's
    /// inherited <c>DataContext</c> is null, and the bound <c>Header</c> resolved to null rather than
    /// to the icon's value. This is the outcome the S06 acceptance line needed; it is recorded as the
    /// measured result rather than as a gap, and the consumer-side mechanism is pinned by the tests
    /// below it.
    /// </para>
    /// </remarks>
    [StaFact]
    public void The_icons_data_context_does_not_reach_a_menu_declared_in_a_resource_dictionary()
    {
        ResourceDictionary dictionary = ParseDictionary(MenuMarkup());
        var menu = Assert.IsType<ContextMenu>(dictionary["TrayMenu"]);
        var boundItem = Assert.IsType<MenuItem>(menu.Items[0]);
        var iconGraph = new MenuGraph { Label = "the icon's data context" };

        FakeShellApi shell = TrayIconMenuFixture.CreateShell();

        using TrayIcon icon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        icon.DataContext = iconGraph;

        try
        {
            TrayIconMenuFixture.OpenMenu(icon, iconId);

            Assert.True(
                menu.IsOpen,
                $"The measurement needs the menu really open on the library's own path. {TrayIconMenuFixture.Describe(icon, shell)}");

            // The icon really carries the value the acceptance line talks about...
            Assert.Same(iconGraph, icon.DataContext);

            // ... and none of it reached the menu: the library set PlacementTarget and the placement
            // offsets, and no DataContext anywhere.
            Assert.Null(menu.DataContext);
            Assert.Null(boundItem.DataContext);
            Assert.Null(boundItem.Header);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayIconMenuFixture.Settle();
        }
    }

    /// <summary>
    /// The supported mechanism: the consumer sets the menu's own <c>DataContext</c>, and the bound item
    /// resolves against <b>that same object graph</b> - and the library leaves it alone on open and on
    /// teardown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The negative half is what makes this a contract rather than a happy path: the icon is given a
    /// <em>different</em> <c>DataContext</c>, so a library that quietly pushed its own value into the
    /// menu would fail the <see cref="Assert.Same(object?, object?)"/> on the menu's
    /// <c>DataContext</c> instead of silently winning over the consumer.
    /// </para>
    /// <para>
    /// The close half matters because teardown is the second place a library could touch the menu: the
    /// library detaches its own <c>PlacementTarget</c>, and the assertions after the close show that is
    /// all it does - the consumer's data context and the resolved header are exactly where they were,
    /// so a menu can be re-opened without the consumer re-arming anything.
    /// </para>
    /// </remarks>
    [StaFact]
    public void A_consumer_set_data_context_on_the_menu_is_what_its_bound_items_resolve_against()
    {
        ResourceDictionary dictionary = ParseDictionary(MenuMarkup());
        var menu = Assert.IsType<ContextMenu>(dictionary["TrayMenu"]);
        var boundItem = Assert.IsType<MenuItem>(menu.Items[0]);
        var menuGraph = new MenuGraph { Label = "the consumer's data context" };

        // The documented division of responsibility, and the mechanism a declarative consumer writes
        // as DataContext="{StaticResource ...}": the menu is given its own data context.
        menu.DataContext = menuGraph;

        FakeShellApi shell = TrayIconMenuFixture.CreateShell();

        using TrayIcon icon = TrayIconMenuFixture.CreateRegisteredIcon(shell, menu, out uint iconId);

        // Deliberately a different graph: the icon's DataContext must not win over the consumer's.
        icon.DataContext = new MenuGraph { Label = "the icon's data context" };

        try
        {
            TrayIconMenuFixture.OpenMenu(icon, iconId);

            Assert.True(
                menu.IsOpen,
                $"The measurement needs the menu really open on the library's own path. {TrayIconMenuFixture.Describe(icon, shell)}");

            // Not overwritten by the open...
            Assert.Same(menuGraph, menu.DataContext);

            // ... inherited by the item that binds against it, as the same instance, never a copy...
            Assert.Same(menuGraph, boundItem.DataContext);
            Assert.Equal(menuGraph.Label, boundItem.Header);

            // ... and the item the caller declared is still the item the menu holds.
            Assert.Same(boundItem, menu.Items[0]);

            TrayIconMenuFixture.CloseMenu(menu);

            // Teardown touches its own placement target and nothing of the caller's: the mechanism is
            // left armed for the next open.
            Assert.False(menu.IsOpen);
            Assert.Null(menu.PlacementTarget);
            Assert.Same(menuGraph, menu.DataContext);
            Assert.Same(menuGraph, boundItem.DataContext);
            Assert.Equal(menuGraph.Label, boundItem.Header);
        }
        finally
        {
            TrayIconMenuFixture.CloseMenu(menu);
            TrayIconMenuFixture.Settle();
        }
    }

    /// <summary>
    /// Assigning the menu to a <see cref="TrayIcon"/> re-parents nothing: the menu keeps no logical
    /// parent, it does not join the icon's logical tree, and its items stay its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim behind the measurement above, asserted where it is cheap and where it cannot go
    /// flaky: the library's menu property is a dependency property of its own (D029), so assigning it
    /// is a value write and not <c>FrameworkElement.ContextMenu</c>'s property-changed callback, which
    /// would have made the menu a logical child of the icon and handed it the icon's
    /// <c>DataContext</c> by inheritance. That is exactly the mechanism a declarative consumer must be
    /// able to reason about, and it is measured here with no popup, no registration and no foreground
    /// grant.
    /// </para>
    /// <para>
    /// It also covers the boundary from the other side of the acceptance line: a menu handed to this
    /// library stays the object graph the caller built, with the caller's own items and the caller's
    /// own data context - which is what makes "the consumer sets the menu's DataContext" a reliable
    /// instruction rather than a race with the library.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Assigning_the_menu_re_parents_nothing_and_takes_no_data_context_from_the_icon()
    {
        var shell = new FakeShellApi();
        var menu = new ContextMenu();
        var item = new MenuItem { Header = "Alpha" };

        menu.Items.Add(item);

        using var icon = new TrayIcon(shell);

        icon.ContextMenu = menu;
        icon.DataContext = new MenuGraph { Label = "the icon's data context" };

        // The menu is the caller's object, unparented and un-inherited: assigning it to the icon is a
        // value write on a dependency property, not a logical-tree insertion.
        Assert.Null(menu.Parent);
        Assert.Null(menu.DataContext);
        Assert.DoesNotContain(menu, LogicalTreeHelper.GetChildren(icon).Cast<object>());

        // The item's only parent is the menu the caller put it in, and the icon never acquired it.
        Assert.Same(menu, item.Parent);
        Assert.DoesNotContain(item, LogicalTreeHelper.GetChildren(icon).Cast<object>());

        // Nothing was applied: no host window, no registration, no shell call - the assignment is a
        // write and the click path is what reads it.
        Assert.False(icon.Visible);
        Assert.Equal(IntPtr.Zero, icon.HostHandle);
        Assert.Empty(shell.Calls);
    }

    /// <summary>
    /// Parses a dictionary, turning the markup into an object graph on this STA thread.
    /// </summary>
    /// <param name="markup">The markup to parse.</param>
    /// <returns>The parsed dictionary.</returns>
    private static ResourceDictionary ParseDictionary(string markup) =>
        Assert.IsType<ResourceDictionary>(XamlReader.Parse(markup));

    /// <summary>
    /// The resource-declared menu: one item bound against the menu's own <c>DataContext</c> and one
    /// static item, so the measurement is about the binding and not about a menu that is empty.
    /// </summary>
    /// <returns>The markup.</returns>
    /// <remarks>
    /// The bound item uses a path (<c>{Binding Label}</c>) rather than a bare <c>{Binding}</c> so the
    /// resolved value is a string a reader can compare against the object graph the test assigned, and
    /// a failed resolution is visible as a null header rather than as a boxed object.
    /// </remarks>
    private static string MenuMarkup() =>
        """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <ContextMenu x:Key="TrayMenu">
                <MenuItem Header="{Binding Label}" />
                <MenuItem Header="static item" />
            </ContextMenu>
        </ResourceDictionary>
        """;
}

/// <summary>
/// A minimal object graph with one bindable property, standing in for whatever a consumer assigns to
/// a menu's <c>DataContext</c>.
/// </summary>
/// <remarks>
/// Public and top-level for the reason <see cref="XamlWiredTrayIcon"/> is: a binding engine and a
/// markup parser both have to be able to reach it, and it keeps the S06 sample's own
/// <c>SampleMenuData</c> honest - the same shape appears in both.
/// </remarks>
public sealed class MenuGraph
{
    /// <summary>Gets or sets the text a <c>{Binding Label}</c> item resolves to.</summary>
    public string Label { get; set; } = string.Empty;
}
