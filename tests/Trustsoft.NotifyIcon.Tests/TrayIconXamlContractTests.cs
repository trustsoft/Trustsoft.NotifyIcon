using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
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
/// <b>Zero notification-area dependency.</b> Every parsed instance here keeps <c>Visible</c> false,
/// so it never creates its host window and never calls the shell: what is under test is markup
/// resolution, not registration. Nothing in this file needs a notification area. The one test that
/// counts registrations takes them over the scripted <c>FakeShellApi</c> seam instead, because that
/// is the only way to measure "the shell sees exactly one icon" without putting an icon into the
/// notification area of the machine running the suite.
/// </para>
/// <para>
/// <b>Requirements proven here:</b> R008 and R009 (the declarative surface: the same single class
/// is the XAML component, with the menu and the event wiring expressible in markup) as far as a
/// parse can prove them, with the delivery half left to the sample build and the live UAT - and, for
/// R009's merged-<c>ResourceDictionary</c> clause, the declaration itself plus the one-icon
/// registration count over the scripted seam.
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

    /// <summary>The tooltip the merged declaration carries.</summary>
    private const string MergedToolTipText = "merged declarative";

    /// <summary>
    /// The label the merged declaration's bound menu item resolves to: the string the consumer's own
    /// data context object carries, compared against the item's resolved header.
    /// </summary>
    private const string MergedMenuLabel = "merged declaration menu data context";

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
    /// R009's second childless declaration location: the same declaration, expressed inside a
    /// <see cref="ResourceDictionary"/> merged into <see cref="Application.Resources"/>, resolves the
    /// same surface and still yields exactly one icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A second location is a second question.</b> R009 names both places a windowless consumer can
    /// declare the icon - inline in <c>Application.Resources</c> and in a dictionary merged into it -
    /// and the merge graph is a different container from the application's own entry table. A merged
    /// declaration that stopped resolving, or that minted a second instance for a lookup, would leave
    /// every test above green, which is why the requirement's merged clause is closed here rather than
    /// as one more variation of them.
    /// </para>
    /// <para>
    /// <b>Two instruments, one test, because neither can measure the whole clause.</b> The parsed
    /// instance is built by the shipped parameterless constructor and therefore talks to the real
    /// shell, so a registration count taken on it would put an icon into the notification area of the
    /// machine running the suite; it stays inert (<c>Visible="False"</c>) and carries the declaration
    /// assertions. The count is taken on the second instrument - a seam-built instance that a merged
    /// dictionary owns and that is resolved repeatedly - where "the shell sees exactly one icon" is
    /// observable as exactly one <c>NIM_ADD</c> and never a second one on a later lookup. The event
    /// surface is asserted as resolvable metadata rather than by raising, both because a
    /// <c>ResourceDictionary</c>-rooted element carrying an event attribute cannot be parsed at all
    /// (<see cref="Markup_event_attributes_in_a_resource_dictionary_are_a_pinned_boundary"/>) and
    /// because a declaration in a real application is compiled to BAML, where handler names never
    /// reach this parser.
    /// </para>
    /// <para>
    /// <b>The application is created on demand, once per process.</b> WPF refuses a second
    /// <see cref="Application"/>, so this test creates the one <see cref="Application.Current"/> the
    /// declaration needs - and puts the resource graph back the way it found it: both dictionaries are
    /// removed in a <see langword="finally"/> and both icons are disposed, so a failing assertion here
    /// cannot leave a merged icon behind for the rest of the suite.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Merging_a_declaration_dictionary_into_the_application_resources_yields_one_icon()
    {
        Application application = EnsureApplication();

        Assert.True(
            application.CheckAccess(),
            "The merged declaration needs the application whose dispatcher this test runs on: Application.Current belongs to another thread.");

        // The declaration half: markup inside a merged dictionary, inert over the real shell.
        ResourceDictionary declaration = ParseDictionary(MergedDeclarationMarkup());
        using var declaredIcon = Assert.IsType<TrayIcon>(declaration["TrayIcon"]);

        application.Resources.MergedDictionaries.Add(declaration);

        try
        {
            AssertTheMergedDeclarationResolvesTheSameSurface(application, declaration, declaredIcon);
        }
        finally
        {
            application.Resources.MergedDictionaries.Remove(declaration);
        }

        // The counting half: the same merged placement, over the scripted seam.
        var shell = new FakeShellApi();
        using var mergedIcon = new TrayIcon(shell) { IconSource = IconImage() };
        var mergedDictionary = new ResourceDictionary { ["TrayIcon"] = mergedIcon };

        application.Resources.MergedDictionaries.Add(mergedDictionary);

        try
        {
            AssertTheShellRegistersExactlyOneIconForRepeatedLookups(application, mergedDictionary, mergedIcon, shell);
        }
        finally
        {
            application.Resources.MergedDictionaries.Remove(mergedDictionary);
        }
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

    /// <summary>
    /// The merged-dictionary declaration: the image, the consumer's menu data, the menu and the icon,
    /// in the order <c>StaticResource</c> can resolve.
    /// </summary>
    /// <returns>The markup.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately <em>no</em> event attributes: a <c>ResourceDictionary</c>-rooted element carrying
    /// one cannot be parsed, which the boundary test above pins. The event surface is asserted as
    /// resolvable metadata instead.
    /// </para>
    /// <para>
    /// <c>Visible="False"</c> is what keeps the instance inert. The registration count is taken on a
    /// seam-built instance in a second instrument instead, because a registered markup instance would
    /// put an icon into the notification area of the machine running the suite.
    /// </para>
    /// </remarks>
    private static string MergedDeclarationMarkup() =>
        $$"""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:tni="{{LibraryNamespace}}"
                            xmlns:local="{{TestNamespace}}">
            <DrawingImage x:Key="TrayImage">
                <DrawingImage.Drawing>
                    <GeometryDrawing Brush="#FF3060A0">
                        <GeometryDrawing.Geometry>
                            <EllipseGeometry Center="8,8" RadiusX="8" RadiusY="8" />
                        </GeometryDrawing.Geometry>
                    </GeometryDrawing>
                </DrawingImage.Drawing>
            </DrawingImage>
            <local:MenuGraph x:Key="TrayMenuData" Label="{{MergedMenuLabel}}" />
            <ContextMenu x:Key="TrayMenu" DataContext="{StaticResource TrayMenuData}">
                <MenuItem Header="{Binding Label}" />
            </ContextMenu>
            <tni:TrayIcon x:Key="TrayIcon"
                          ToolTipText="{{MergedToolTipText}}"
                          Visible="False"
                          MenuActivation="None"
                          IconSource="{StaticResource TrayImage}"
                          ContextMenu="{StaticResource TrayMenu}" />
        </ResourceDictionary>
        """;

    /// <summary>
    /// The application the merged declaration needs: the one instance WPF allows per process.
    /// </summary>
    /// <returns>The current application, or a new one when this test is the first to need it.</returns>
    /// <remarks>
    /// Created on demand rather than by a fixture, because WPF throws on a second
    /// <see cref="Application"/> and no other test in this suite needs a resource graph of its own:
    /// the XAML contract tests above parse and stop there. An existing instance is reused rather than
    /// replaced, so a future test that needs the same graph cannot make this one fail for a reason
    /// that has nothing to do with the declaration.
    /// </remarks>
    private static Application EnsureApplication() => Application.Current ?? new Application();

    /// <summary>
    /// The declaration half of the merged-dictionary contract: one reachable icon, reference identity
    /// across repeated lookups from both the application and the source dictionary, the five dependency
    /// properties as markup declared them, the menu by identity with the consumer's own data context,
    /// and the inert-until-Visible rule.
    /// </summary>
    /// <param name="application">The application the declaration is merged into.</param>
    /// <param name="declaration">The merged dictionary.</param>
    /// <param name="declaredIcon">The instance that dictionary holds.</param>
    private static void AssertTheMergedDeclarationResolvesTheSameSurface(
        Application application,
        ResourceDictionary declaration,
        TrayIcon declaredIcon)
    {
        // One key, one instance, one icon - counted across the whole merge graph rather than through
        // one lookup, so a second instance reachable under the same key cannot hide behind it.
        Assert.Same(declaredIcon, Assert.Single(CollectTrayIcons(application.Resources)));

        // Repeated lookups resolve to that instance, from the application and from the source
        // dictionary alike: a merge that minted a copy per lookup fails here.
        Assert.Same(declaredIcon, application.Resources["TrayIcon"]);
        Assert.Same(declaredIcon, application.Resources["TrayIcon"]);
        Assert.Same(declaredIcon, declaration["TrayIcon"]);

        // Inert until Visible: merging the declaration registers nothing.
        Assert.False(declaredIcon.Visible);
        Assert.False(declaredIcon.IsRegistered);
        Assert.Equal(IntPtr.Zero, declaredIcon.HostHandle);

        // The five dependency properties, resolved the way markup resolves them, carrying exactly what
        // the dictionary declared.
        AssertDeclaredProperty(declaredIcon, TrayIcon.IconSourceProperty, nameof(TrayIcon.IconSource), declaration["TrayImage"]);
        AssertDeclaredProperty(declaredIcon, TrayIcon.ToolTipTextProperty, nameof(TrayIcon.ToolTipText), MergedToolTipText);
        AssertDeclaredProperty(declaredIcon, TrayIcon.VisibleProperty, nameof(TrayIcon.Visible), false);
        AssertDeclaredProperty(declaredIcon, TrayIcon.MenuActivationProperty, nameof(TrayIcon.MenuActivation), TrayMenuActivation.None);
        AssertDeclaredProperty(declaredIcon, TrayIcon.ContextMenuProperty, nameof(TrayIcon.ContextMenu), declaration["TrayMenu"]);

        // The two reference-valued properties are the dictionary's own objects, never copies.
        Assert.Same(declaration["TrayImage"], declaredIcon.IconSource);
        Assert.Same(declaration["TrayMenu"], declaredIcon.ContextMenu);

        // The library-owned menu, not the inherited FrameworkElement one - carrying the consumer's own
        // data context object, with the binding declared against it resolved.
        var menu = Assert.IsType<ContextMenu>(declaration["TrayMenu"]);
        var boundItem = Assert.IsType<MenuItem>(menu.Items[0]);

        Assert.Null(((FrameworkElement)declaredIcon).ContextMenu);
        Assert.Same(declaration["TrayMenuData"], menu.DataContext);
        Assert.Same(declaration["TrayMenuData"], boundItem.DataContext);
        Assert.Equal(MergedMenuLabel, boundItem.Header);

        // The whole event surface a markup author names, resolvable as metadata.
        AssertRoutedEventSurfaceIsMarkupResolvable();
    }

    /// <summary>
    /// The counting half: a seam-built icon owned by a merged dictionary registers exactly once, and
    /// repeated lookups of that dictionary return that one instance instead of minting another icon.
    /// </summary>
    /// <param name="application">The application the dictionary is merged into.</param>
    /// <param name="dictionary">The merged dictionary that owns the icon.</param>
    /// <param name="icon">The seam-built instance.</param>
    /// <param name="shell">The scripted seam the instance talks to.</param>
    private static void AssertTheShellRegistersExactlyOneIconForRepeatedLookups(
        Application application,
        ResourceDictionary dictionary,
        TrayIcon icon,
        FakeShellApi shell)
    {
        // Resolving a declaration registers nothing: an inert lookup is not an icon.
        Assert.Same(icon, application.Resources["TrayIcon"]);
        Assert.Same(icon, dictionary["TrayIcon"]);
        Assert.Empty(ShellCallsFor(shell, ShellConstants.NIM_ADD));
        Assert.False(icon.IsRegistered);

        icon.Visible = true;

        // Exactly one icon: the shell was asked to add one, and only one.
        ShellCall add = Assert.Single(ShellCallsFor(shell, ShellConstants.NIM_ADD));

        Assert.Equal(ShellConstants.NIM_ADD, add.Message);
        Assert.True(icon.IsRegistered);
        Assert.NotEqual(IntPtr.Zero, icon.RegisteredIconHandle);

        // The registration is the documented pair: the add, then the version that makes the tooltip
        // and the callback message meaningful.
        Assert.Single(ShellCallsFor(shell, ShellConstants.NIM_SETVERSION));

        // Repeated resolution - the lookup a template or a consumer's own code performs - returns the
        // same single instance, and looking the declaration up again never adds a second icon.
        Assert.Same(icon, application.Resources["TrayIcon"]);
        Assert.Same(icon, dictionary["TrayIcon"]);
        Assert.Same(icon, application.Resources["TrayIcon"]);
        Assert.Same(icon, Assert.Single(CollectTrayIcons(application.Resources)));
        Assert.Single(ShellCallsFor(shell, ShellConstants.NIM_ADD));

        icon.Dispose();

        // Disposal takes that one icon away and adds no second one.
        Assert.Single(ShellCallsFor(shell, ShellConstants.NIM_DELETE));
        Assert.Single(ShellCallsFor(shell, ShellConstants.NIM_ADD));
        Assert.False(icon.IsRegistered);
    }

    /// <summary>
    /// Asserts that one dependency property is resolvable for <see cref="TrayIcon"/> the way markup
    /// resolves it, and that it holds the value the dictionary declared.
    /// </summary>
    /// <param name="element">The parsed instance to read.</param>
    /// <param name="property">The property under test.</param>
    /// <param name="name">The CLR property name markup spells.</param>
    /// <param name="expected">The value the declaration carries.</param>
    private static void AssertDeclaredProperty(DependencyObject element, DependencyProperty property, string name, object? expected)
    {
        Assert.Equal(name, property.Name);
        Assert.Equal(typeof(TrayIcon), property.OwnerType);
        Assert.False(property.ReadOnly);

        // What a markup author actually looks through: a descriptor resolvable for this property on
        // this type. A property markup could not resolve this way could not be declared at all.
        DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(TrayIcon));

        Assert.NotNull(descriptor);
        Assert.Equal(name, descriptor!.Name);
        Assert.Equal(property, descriptor.DependencyProperty);

        Assert.Equal(expected, descriptor.GetValue(element));
    }

    /// <summary>
    /// Asserts that every routed event <see cref="TrayIcon"/> declares is resolvable as the markup
    /// surface a consumer writes: the field-name convention, the registered name, the owner type, the
    /// routing strategy a <c>Preview</c> prefix implies, the CLR event wrapper with its handler type
    /// and accessors, and a routed payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A merged declaration cannot carry event attributes, so this is where "the event wiring is
    /// expressible in markup" is pinned for it - as the metadata a parser, a BAML compiler and a
    /// <c>RoutedEvent</c> reference all resolve through. A renamed event, a strategy that lost its
    /// tunnel, or a handler type that stopped being a routed delegate would leave every C# handler
    /// working.
    /// </para>
    /// <para>
    /// The last assertion is the one-to-one check: every public instance event declared on the type is
    /// accounted for by a routed event field, so a new event cannot ship without the markup surface
    /// this test describes.
    /// </para>
    /// </remarks>
    private static void AssertRoutedEventSurfaceIsMarkupResolvable()
    {
        FieldInfo[] fields =
        [
            .. typeof(TrayIcon)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(RoutedEvent)),
        ];

        Assert.NotEmpty(fields);

        foreach (FieldInfo field in fields)
        {
            Assert.EndsWith("Event", field.Name, StringComparison.Ordinal);

            string eventName = field.Name[..^"Event".Length];
            var routedEvent = Assert.IsType<RoutedEvent>(field.GetValue(null));

            // The name markup writes in an attribute, the element it can be written on, and the
            // strategy that separates a cancelling Preview half from its main event.
            Assert.Equal(eventName, routedEvent.Name);
            Assert.Equal(typeof(TrayIcon), routedEvent.OwnerType);
            Assert.Equal(
                eventName.StartsWith("Preview", StringComparison.Ordinal) ? RoutingStrategy.Tunnel : RoutingStrategy.Bubble,
                routedEvent.RoutingStrategy);

            // The CLR event wrapper an attribute attaches its handler through, with the routed handler
            // type the payload has to match.
            EventInfo? wrapper = typeof(TrayIcon).GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(wrapper);
            Assert.Equal(routedEvent.HandlerType, wrapper!.EventHandlerType);
            Assert.NotNull(wrapper.AddMethod);
            Assert.NotNull(wrapper.RemoveMethod);
            Assert.True(wrapper.AddMethod!.IsPublic);
            Assert.True(wrapper.RemoveMethod!.IsPublic);

            // The payload a handler receives travels with the event.
            MethodInfo? invoke = routedEvent.HandlerType.GetMethod("Invoke");

            Assert.NotNull(invoke);

            ParameterInfo[] parameters = invoke!.GetParameters();

            Assert.Equal(2, parameters.Length);
            Assert.True(typeof(RoutedEventArgs).IsAssignableFrom(parameters[1].ParameterType));
        }

        string[] routedNames = [.. fields.Select(field => field.Name[..^"Event".Length]).OrderBy(name => name, StringComparer.Ordinal)];
        string[] declaredNames =
        [
            .. typeof(TrayIcon)
                .GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(declared => declared.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(declaredNames, routedNames);
    }

    /// <summary>
    /// The <c>Shell_NotifyIcon</c> calls the seam received for one operation code, in call order.
    /// </summary>
    /// <param name="shell">The scripted seam.</param>
    /// <param name="message">The <c>NIM_*</c> operation code to select.</param>
    /// <returns>The matching calls.</returns>
    /// <remarks>
    /// Selected rather than counted, so the assertions read as "exactly one add"
    /// (<see cref="Assert.Single(System.Collections.IEnumerable)"/>) or "no add at all", and cannot be
    /// satisfied by a total that also contains the <c>NIM_SETVERSION</c> half of the same registration.
    /// </remarks>
    private static IEnumerable<ShellCall> ShellCallsFor(FakeShellApi shell, uint message) =>
        shell.ShellNotifyIconCalls.Where(call => call.Message == message);

    /// <summary>
    /// The distinct <see cref="TrayIcon"/> instances a dictionary exposes, following its merged
    /// dictionaries.
    /// </summary>
    /// <param name="dictionary">The dictionary to walk.</param>
    /// <returns>The distinct instances reachable from it.</returns>
    /// <remarks>
    /// A set rather than a count, because the walk visits every merged dictionary and a raw count
    /// would report a duplicate for an instance its own dictionary and the merge both expose. What the
    /// assertion needs is "how many icons exist", which is a question about identity.
    /// </remarks>
    private static HashSet<TrayIcon> CollectTrayIcons(ResourceDictionary dictionary)
    {
        HashSet<TrayIcon> icons = [];

        Walk(dictionary);

        return icons;

        void Walk(ResourceDictionary current)
        {
            foreach (object value in current.Values)
            {
                if (value is TrayIcon icon)
                {
                    icons.Add(icon);
                }
            }

            foreach (ResourceDictionary merged in current.MergedDictionaries)
            {
                Walk(merged);
            }
        }
    }

    /// <summary>
    /// A 16x16 opaque <see cref="PixelFormats.Bgra32"/> source for the seam-built instance.
    /// </summary>
    /// <returns>The image.</returns>
    /// <remarks>
    /// A bitmap rather than a drawing, deliberately: a drawing is rasterized through the compositor and
    /// needs a running dispatcher (<c>HiconFactoryTests</c> records that boundary), while a bitmap is
    /// read directly. What this test counts is registrations, so the lightest source the conversion
    /// accepts is the right one - and a real source is what makes the count a count of icons rather
    /// than of calls with a null handle.
    /// </remarks>
    private static BitmapSource IconImage()
    {
        const int size = 16;
        byte[] pixels = new byte[size * size * 4];

        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0xA0;
            pixels[index + 1] = 0x60;
            pixels[index + 2] = 0x20;
            pixels[index + 3] = 0xFF;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }
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
