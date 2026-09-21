using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S06 vertical slice, first half: a <see cref="TrayIcon"/> declared in markup is a configured
/// instance, wired through the same dependency properties and routed events a code-first consumer
/// uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a headless parse can and cannot prove.</b> <c>XamlReader.Parse</c> resolves
/// property assignments, type converters and <c>StaticResource</c> references, so it is the right
/// instrument for the property surface, for reference identity and for the library-owned shadowed
/// <c>ContextMenu</c> property. It is the wrong instrument for markup <em>event</em> attributes
/// inside a resource: parse binds handler names against the root object only, so a
/// dictionary-rooted element with an event attribute fails - and that failure is pinned below as a
/// boundary rather than hidden, because it is exactly why resource-scope event wiring is proven by
/// the sample project's compiled markup and its live run instead.
/// </para>
/// <para>
/// <b>Zero shell dependency.</b> Every test here keeps <c>Visible</c> false, so the element never
/// creates its host window and never calls the shell seam: what is under test is markup
/// resolution, not registration. Nothing in this file needs a notification area.
/// </para>
/// <para>
/// <b>Requirements proven here:</b> R008 and R009 (the declarative surface: the same single class
/// is the XAML component, with the menu and the event wiring expressible in markup) as far as a
/// parse can prove them, with the delivery half left to the sample build and the live UAT.
/// </para>
/// </remarks>
public sealed class TrayIconXamlContractTests
{
    /// <summary>
    /// The library's CLR namespace with its assembly named explicitly. Without the
    /// <c>;assembly=</c> part the parser has no "local assembly" context to resolve against and the
    /// parse fails outright, so this constant is load-bearing rather than decorative.
    /// </summary>
    private const string LibraryNamespace = "clr-namespace:Trustsoft.NotifyIcon;assembly=Trustsoft.NotifyIcon";

    /// <summary>The test assembly, used to parse a root type declared in this file.</summary>
    private const string TestNamespace = "clr-namespace:Trustsoft.NotifyIcon.Tests;assembly=Trustsoft.NotifyIcon.Tests";

    /// <summary>
    /// The consumer namespace URI the library declares for markup, so the sample, a consumer and this
    /// test cannot disagree about it.
    /// </summary>
    private const string ConsumerNamespace = "http://schemas.trustsoft.com/notifyicon";

    /// <summary>
    /// The property surface of a declarative declaration resolves from markup, and the declared
    /// <c>Visible="False"</c> keeps the instance inert.
    /// </summary>
    /// <remarks>
    /// This is the slice's premise: the same class is the XAML component (R008), so a consumer can
    /// express the icon entirely as markup. The image is a markup <see cref="DrawingImage"/> so the
    /// declaration needs no binary asset, and the icon is declared last because
    /// <c>StaticResource</c> resolves in document order.
    /// </remarks>
    [StaFact]
    public void Declarative_property_markup_creates_a_configured_instance_without_registering()
    {
        ResourceDictionary dictionary = ParseDictionary(PropertyMarkup());

        var icon = Assert.IsType<TrayIcon>(dictionary["TrayIcon"]);

        Assert.Equal("declarative", icon.ToolTipText);
        Assert.Equal(TrayMenuActivation.None, icon.MenuActivation);
        Assert.IsAssignableFrom<ImageSource>(icon.IconSource);

        // Inert: nothing is created until the first Visible = true assignment (R001), which is what
        // lets an application declare the icon in Application.Resources without owning an icon.
        Assert.False(icon.Visible);
        Assert.False(icon.IsRegistered);
        Assert.Equal(IntPtr.Zero, icon.HostHandle);

        icon.Dispose();
    }

    /// <summary>
    /// Markup writes the library-owned shadowed <see cref="TrayIcon.ContextMenuProperty"/>, the
    /// menu arrives as the same instance that was declared, and the inherited
    /// <see cref="FrameworkElement.ContextMenu"/> stays null.
    /// </summary>
    /// <remarks>
    /// The S03 to S06 edge in its measured form. If markup resolved the inherited property instead,
    /// a declarative consumer's menu would silently never open while every code-first test passed -
    /// which is why the assertion goes through <see cref="DependencyPropertyDescriptor"/> (what a
    /// markup author actually looks through) and why the negative half - the base property being
    /// null - is asserted rather than inferred.
    /// </remarks>
    [StaFact]
    public void Markup_resolves_the_library_owned_context_menu_property_and_the_menu_by_identity()
    {
        ResourceDictionary dictionary = ParseDictionary(PropertyMarkup());

        var icon = Assert.IsType<TrayIcon>(dictionary["TrayIcon"]);
        var declaredMenu = Assert.IsType<System.Windows.Controls.ContextMenu>(dictionary["TrayMenu"]);

        DependencyPropertyDescriptor? descriptor =
            DependencyPropertyDescriptor.FromProperty(TrayIcon.ContextMenuProperty, typeof(TrayIcon));

        Assert.NotNull(descriptor);
        Assert.Equal(nameof(TrayIcon.ContextMenu), descriptor!.Name);

        // The caller's own instance, never a clone: a clone would lose the menu's data context,
        // its item templates and every handler it was wired with (R009).
        Assert.Same(declaredMenu, icon.ContextMenu);

        // The inherited property is untouched, which is what proves the shadowed one is the one
        // markup found.
        Assert.Null(((FrameworkElement)icon).ContextMenu);

        icon.Dispose();
    }

    /// <summary>
    /// One resource key yields one instance: <c>x:Shared</c> keeps its default.
    /// </summary>
    /// <remarks>
    /// A second instance would be a second icon in the notification area for one declaration, and
    /// <c>x:Shared="False"</c> is the single most damaging thing a consumer could add to this
    /// pattern. Two lookups returning the same object is the observable form of "the default is
    /// shared".
    /// </remarks>
    [StaFact]
    public void One_resource_key_yields_one_instance()
    {
        ResourceDictionary dictionary = ParseDictionary(PropertyMarkup());

        object first = dictionary["TrayIcon"];
        object second = dictionary["TrayIcon"];

        Assert.Same(first, second);

        (first as TrayIcon)?.Dispose();
    }

    /// <summary>
    /// Markup event attributes bind through the library's CLR event wrappers and the handlers run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Proved with the one construction a headless parse can wire: the parsed root is a
    /// <see cref="TrayIcon"/> subclass defined in this assembly, so handler-name resolution has a
    /// root object to bind against. Both directions are covered - the Bubble event and its Tunnel
    /// <c>Preview</c> partner - because an event that only existed as a static field would compile
    /// and never fire.
    /// </para>
    /// <para>
    /// This proves the <em>binding</em>, not the shell: the event is raised by this test on a
    /// parentless instance, which is the same delivery path the shell's callback ends in (S02's
    /// tests drive the other half).
    /// </para>
    /// </remarks>
    [StaFact]
    public void Markup_event_attributes_bind_and_fire_for_bubble_and_preview()
    {
        string markup =
            $"""
            <local:XamlWiredTrayIcon xmlns:local="{TestNamespace}"
                                     ToolTipText="markup wiring"
                                     TrayLeftClick="OnTrayLeftClick"
                                     PreviewTrayLeftClick="OnPreviewTrayLeftClick" />
            """;

        var icon = Assert.IsType<XamlWiredTrayIcon>(XamlReader.Parse(markup));

        Assert.Equal("markup wiring", icon.ToolTipText);

        var previewArgs = new TrayIconClickEventArgs(MouseButton.Left, 1, new Point(10, 20), TrayIcon.PreviewTrayLeftClickEvent);
        icon.RaiseEvent(previewArgs);

        var bubbleArgs = new TrayIconClickEventArgs(MouseButton.Left, 2, new Point(30, 40), TrayIcon.TrayLeftClickEvent);
        icon.RaiseEvent(bubbleArgs);

        // The attributes named these handlers, and the payload is the library's own args type with
        // the values the raise carried - not a bare RoutedEventArgs that happened to arrive.
        (string Handler, MouseButton Button, int ClickCount) preview = Assert.Single(icon.PreviewCalls);
        (string Handler, MouseButton Button, int ClickCount) bubble = Assert.Single(icon.ClickCalls);

        Assert.Equal(nameof(XamlWiredTrayIcon.OnPreviewTrayLeftClick), preview.Handler);
        Assert.Equal(MouseButton.Left, preview.Button);
        Assert.Equal(1, preview.ClickCount);

        Assert.Equal(nameof(XamlWiredTrayIcon.OnTrayLeftClick), bubble.Handler);
        Assert.Equal(MouseButton.Left, bubble.Button);
        Assert.Equal(2, bubble.ClickCount);

        icon.Dispose();
    }

    /// <summary>
    /// A <c>ResourceDictionary</c>-rooted element carrying an event attribute cannot be parsed, and
    /// that boundary is the reason the sample's compiled markup is the event-wiring proof.
    /// </summary>
    /// <remarks>
    /// Pinned so the gap reads as a known boundary rather than as missing coverage. <c>XamlReader</c>
    /// binds handler names against the root object only, so resource-scope event attributes are
    /// resolved by the markup compiler into BAML instead - validated by building the sample and
    /// proven by running it, both recorded in <c>docs/UAT-S06.md</c>.
    /// </remarks>
    [StaFact]
    public void Markup_event_attributes_in_a_resource_dictionary_are_a_pinned_boundary()
    {
        string markup =
            $"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:tni="{LibraryNamespace}">
                <tni:TrayIcon x:Key="TrayIcon" TrayLeftClick="OnTrayLeftClick" />
            </ResourceDictionary>
            """;

        Assert.Throws<XamlParseException>(() => XamlReader.Parse(markup));
    }

    /// <summary>
    /// The consumer namespace is declared on the assembly, and markup really resolves through it.
    /// </summary>
    /// <remarks>
    /// The attribute is the difference between markup that needs
    /// <c>clr-namespace:...;assembly=...</c> and markup that needs one short URI - and the assembly
    /// part is not optional, because the parser has no local-assembly context to fall back on. Both
    /// halves are asserted: the declaration itself, so a documented namespace cannot silently
    /// disappear, and a real parse through the URI, because an attribute that nothing resolves is
    /// just a string.
    /// </remarks>
    [StaFact]
    public void The_consumer_namespace_is_declared_and_markup_resolves_through_it()
    {
        System.Reflection.Assembly library = typeof(TrayIcon).Assembly;

        System.Windows.Markup.XmlnsDefinitionAttribute definition = library
            .GetCustomAttributes<System.Windows.Markup.XmlnsDefinitionAttribute>()
            .Single(attribute => attribute.ClrNamespace == "Trustsoft.NotifyIcon");

        Assert.Equal(ConsumerNamespace, definition.XmlNamespace);

        System.Windows.Markup.XmlnsPrefixAttribute prefix = library
            .GetCustomAttributes<System.Windows.Markup.XmlnsPrefixAttribute>()
            .Single(attribute => attribute.XmlNamespace == ConsumerNamespace);

        Assert.Equal("tni", prefix.Prefix);

        // The attribute is only worth declaring if the parser honours it.
        object parsed = XamlReader.Parse(
            $"""
            <tni:TrayIcon xmlns:tni="{ConsumerNamespace}" ToolTipText="mapped namespace" Visible="False" />
            """);

        var icon = Assert.IsType<TrayIcon>(parsed);

        Assert.Equal("mapped namespace", icon.ToolTipText);
        Assert.False(icon.IsRegistered);

        icon.Dispose();
    }

    /// <summary>
    /// A parse-created parentless instance disposes cleanly, and disposal is idempotent.
    /// </summary>
    /// <remarks>
    /// An instance can be created by markup without ever entering a visual tree (R001, R008), so
    /// disposal has to work for exactly that shape - a parsed resource that a consumer drops
    /// without ever showing it.
    /// </remarks>
    [StaFact]
    public void A_parsed_parentless_instance_disposes_cleanly()
    {
        ResourceDictionary dictionary = ParseDictionary(PropertyMarkup());

        var icon = Assert.IsType<TrayIcon>(dictionary["TrayIcon"]);

        icon.Dispose();
        icon.Dispose();

        Assert.False(icon.Visible);
        Assert.False(icon.IsRegistered);
        Assert.Equal(IntPtr.Zero, icon.HostHandle);
    }

    /// <summary>
    /// Parses a dictionary, turning the markup into an object graph on this STA thread.
    /// </summary>
    /// <param name="markup">The markup to parse.</param>
    /// <returns>The parsed dictionary.</returns>
    private static ResourceDictionary ParseDictionary(string markup) =>
        Assert.IsType<ResourceDictionary>(XamlReader.Parse(markup));

    /// <summary>
    /// The property-only declaration: an image, a menu, and the icon that refers to both.
    /// </summary>
    /// <returns>The markup.</returns>
    /// <remarks>
    /// Document order matters - the icon's <c>StaticResource</c> references resolve against what has
    /// already been declared - and the geometry is inline so the declaration needs no asset on disk.
    /// The markup uses a doubled-dollar raw literal, so a single brace is literal XAML and only the
    /// doubled one interpolates the namespace.
    /// </remarks>
    private static string PropertyMarkup() =>
        $$"""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:tni="{{LibraryNamespace}}">
            <DrawingImage x:Key="TrayImage">
                <DrawingImage.Drawing>
                    <GeometryDrawing Brush="#FF3060A0">
                        <GeometryDrawing.Geometry>
                            <EllipseGeometry Center="8,8" RadiusX="8" RadiusY="8" />
                        </GeometryDrawing.Geometry>
                    </GeometryDrawing>
                </DrawingImage.Drawing>
            </DrawingImage>
            <ContextMenu x:Key="TrayMenu">
                <MenuItem Header="Alpha" />
            </ContextMenu>
            <tni:TrayIcon x:Key="TrayIcon"
                          ToolTipText="declarative"
                          Visible="False"
                          MenuActivation="None"
                          IconSource="{StaticResource TrayImage}"
                          ContextMenu="{StaticResource TrayMenu}" />
        </ResourceDictionary>
        """;
}

/// <summary>
/// A <see cref="TrayIcon"/> subclass that gives markup handler attributes something to bind to.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a top-level public type because XAML cannot reference anything else.</b> The parser has
/// to resolve the element name to a type, so a nested or internal subclass reports "cannot create
/// unknown type" - which is the same constraint that forces the sample's own icon subclass out of
/// its enclosing class in S06's second task. Handler methods must be public for the same reason.
/// </para>
/// <para>
/// The recorded calls are asserted after the raise, never inside a handler: a failed assertion
/// inside an event dispatch escapes as an exception through the raise, which would report the
/// failure as a parse or delivery problem rather than as the mismatch it is.
/// </para>
/// </remarks>
public sealed class XamlWiredTrayIcon : TrayIcon
{
    /// <summary>Gets the calls the markup-named Bubble handler received.</summary>
    public List<(string Handler, MouseButton Button, int ClickCount)> ClickCalls { get; } = [];

    /// <summary>Gets the calls the markup-named Preview handler received.</summary>
    public List<(string Handler, MouseButton Button, int ClickCount)> PreviewCalls { get; } = [];

    /// <summary>Receives the <c>TrayLeftClick</c> attribute's event.</summary>
    /// <param name="sender">The raising element.</param>
    /// <param name="e">The click payload.</param>
    public void OnTrayLeftClick(object sender, TrayIconClickEventArgs e) =>
        ClickCalls.Add((nameof(OnTrayLeftClick), e.Button, e.ClickCount));

    /// <summary>Receives the <c>PreviewTrayLeftClick</c> attribute's event.</summary>
    /// <param name="sender">The raising element.</param>
    /// <param name="e">The click payload.</param>
    public void OnPreviewTrayLeftClick(object sender, TrayIconClickEventArgs e) =>
        PreviewCalls.Add((nameof(OnPreviewTrayLeftClick), e.Button, e.ClickCount));
}
