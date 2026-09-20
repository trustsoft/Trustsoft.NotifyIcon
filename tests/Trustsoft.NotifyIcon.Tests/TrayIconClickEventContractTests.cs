using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The S02 boundary contracts that S03 and S06 consume, asserted as contracts rather than as
/// incidental values, so a member that is missing or shaped differently is discovered here instead
/// of in the middle of the consumer's own work.
/// </summary>
/// <remarks>
/// <para>
/// S02 has two outbound edges, and each test names the edge it protects. The <b>S02 to S06</b> edge
/// is the markup surface: XAML event attributes and <c>AddHandler</c> calls resolve through the
/// registered routed event's name, routing strategy, handler type and owner type, plus the CLR
/// event wrapper for the attribute to attach to - a silently renamed or re-strategied event still
/// compiles and still works from C# while breaking declarative usage, which is exactly the failure
/// this file exists to catch. The <b>S02 to S03</b> edge is the right-click decision surface: the
/// <see cref="TrayIcon.MenuActivation"/> dependency property and the enum values it carries, which
/// S03 branches on when it decides whether a right click opens the menu.
/// </para>
/// <para>
/// This follows the precedent <c>SliceContractTests</c> set for S01, including its premise: the
/// audit runs against the delivered code, and a failure belongs to the slice that owes the
/// member rather than to whichever later slice would have tripped over it. Nothing here changes
/// product behaviour - it only reads the surface the way a consumer will.
/// </para>
/// </remarks>
public sealed class TrayIconClickEventContractTests
{
    /// <summary>
    /// The eight click events S02 ships, each with the literal markup name, the public name
    /// constant, the registered routed event, the documented routing strategy and the CLR event
    /// wrapper that markup and code subscribe through.
    /// </summary>
    /// <remarks>
    /// The name is spelled literally <em>and</em> compared against the constant, so the two cannot
    /// drift: a rename that touched the constant and the registration together but not the
    /// documented markup name would still be caught here. The subscribe/unsubscribe pairs are what
    /// let the wiring test prove each CLR wrapper is bound to its own routed event.
    /// </remarks>
    private static readonly ClickEventContract[] ClickEventContracts =
    [
        new(
            "TrayLeftClick",
            TrayIcon.TrayLeftClickEventName,
            TrayIcon.TrayLeftClickEvent,
            RoutingStrategy.Bubble,
            (icon, handler) => icon.TrayLeftClick += handler,
            (icon, handler) => icon.TrayLeftClick -= handler),
        new(
            "TrayLeftDoubleClick",
            TrayIcon.TrayLeftDoubleClickEventName,
            TrayIcon.TrayLeftDoubleClickEvent,
            RoutingStrategy.Bubble,
            (icon, handler) => icon.TrayLeftDoubleClick += handler,
            (icon, handler) => icon.TrayLeftDoubleClick -= handler),
        new(
            "TrayRightClick",
            TrayIcon.TrayRightClickEventName,
            TrayIcon.TrayRightClickEvent,
            RoutingStrategy.Bubble,
            (icon, handler) => icon.TrayRightClick += handler,
            (icon, handler) => icon.TrayRightClick -= handler),
        new(
            "TrayMiddleClick",
            TrayIcon.TrayMiddleClickEventName,
            TrayIcon.TrayMiddleClickEvent,
            RoutingStrategy.Bubble,
            (icon, handler) => icon.TrayMiddleClick += handler,
            (icon, handler) => icon.TrayMiddleClick -= handler),
        new(
            "PreviewTrayLeftClick",
            TrayIcon.PreviewTrayLeftClickEventName,
            TrayIcon.PreviewTrayLeftClickEvent,
            RoutingStrategy.Tunnel,
            (icon, handler) => icon.PreviewTrayLeftClick += handler,
            (icon, handler) => icon.PreviewTrayLeftClick -= handler),
        new(
            "PreviewTrayLeftDoubleClick",
            TrayIcon.PreviewTrayLeftDoubleClickEventName,
            TrayIcon.PreviewTrayLeftDoubleClickEvent,
            RoutingStrategy.Tunnel,
            (icon, handler) => icon.PreviewTrayLeftDoubleClick += handler,
            (icon, handler) => icon.PreviewTrayLeftDoubleClick -= handler),
        new(
            "PreviewTrayRightClick",
            TrayIcon.PreviewTrayRightClickEventName,
            TrayIcon.PreviewTrayRightClickEvent,
            RoutingStrategy.Tunnel,
            (icon, handler) => icon.PreviewTrayRightClick += handler,
            (icon, handler) => icon.PreviewTrayRightClick -= handler),
        new(
            "PreviewTrayMiddleClick",
            TrayIcon.PreviewTrayMiddleClickEventName,
            TrayIcon.PreviewTrayMiddleClickEvent,
            RoutingStrategy.Tunnel,
            (icon, handler) => icon.PreviewTrayMiddleClick += handler,
            (icon, handler) => icon.PreviewTrayMiddleClick -= handler),
    ];

    /// <summary>
    /// Every click event carries the name its constant declares, the documented routing strategy
    /// (Bubble for the mains, Tunnel for the Previews), the typed click handler and
    /// <see cref="TrayIcon"/> as its owner.
    /// </summary>
    /// <remarks>
    /// This is the S02 to S06 edge. Markup event attributes and <c>AddHandler</c> resolve through
    /// exactly these four values: the name is what a <c>RoutedEvent</c> reference or a diagnostic
    /// filter spells, the strategy decides whether a handler sees the click before or after the
    /// main event, the handler type is what makes the typed args safe, and the owner type is the
    /// element a XAML attribute can be written on. A copy-paste that gave a click event the wrong
    /// strategy would leave every C# handler working while the Preview cancellation silently ran in
    /// the wrong order - which is why the strategy is asserted rather than assumed.
    /// </remarks>
    [StaFact]
    public void Click_routed_events_have_the_documented_name_strategy_handler_and_owner()
    {
        // The audit covers exactly the four main events and their four cancelling counterparts:
        // a fifth click event would need its own entry here (and its own name distinctness proof).
        Assert.Equal(8, ClickEventContracts.Length);

        foreach (ClickEventContract contract in ClickEventContracts)
        {
            Assert.Equal(contract.Name, contract.EventName);
            Assert.Equal(contract.Name, contract.Event.Name);
            Assert.Equal(contract.Strategy, contract.Event.RoutingStrategy);
            Assert.Equal(typeof(EventHandler<TrayIconClickEventArgs>), contract.Event.HandlerType);
            Assert.Equal(typeof(TrayIcon), contract.Event.OwnerType);
        }
    }

    /// <summary>
    /// No two click events share a name, no main event shares a name with its Preview counterpart,
    /// and none of them collides with the <c>TrayError</c> event or its constant.
    /// </summary>
    /// <remarks>
    /// A duplicate name is invisible from C#: both events route, both handlers run, and only markup
    /// or a name-based lookup sees the wrong one. The <c>TrayError</c> comparison is the other half
    /// of the same hazard - a click event registered as <c>TrayError</c> would silently take over
    /// the element's only failure channel.
    /// </remarks>
    [StaFact]
    public void Click_routed_event_names_are_distinct_and_never_collide_with_TrayError()
    {
        string[] names = [.. ClickEventContracts.Select(contract => contract.Name)];

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        (string Main, string Preview)[] pairs =
        [
            (TrayIcon.TrayLeftClickEventName, TrayIcon.PreviewTrayLeftClickEventName),
            (TrayIcon.TrayLeftDoubleClickEventName, TrayIcon.PreviewTrayLeftDoubleClickEventName),
            (TrayIcon.TrayRightClickEventName, TrayIcon.PreviewTrayRightClickEventName),
            (TrayIcon.TrayMiddleClickEventName, TrayIcon.PreviewTrayMiddleClickEventName),
        ];

        foreach ((string main, string preview) in pairs)
        {
            Assert.Contains(main, names);
            Assert.Contains(preview, names);
            Assert.NotEqual(main, preview);
        }

        Assert.DoesNotContain(TrayIcon.TrayErrorEventName, names);
        Assert.NotEqual(TrayIcon.TrayErrorEvent.Name, TrayIcon.TrayLeftClickEvent.Name);
    }

    /// <summary>
    /// Each CLR event wrapper subscribes to, and unsubscribes from, its own registered routed event:
    /// subscribing through the CLR event runs the handler when that event is raised, and
    /// unsubscribing stops it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of the S02 to S06 edge that the metadata assertions cannot see. A wrapper
    /// whose accessor named the wrong <c>AddHandler</c> target would satisfy every name and strategy
    /// assertion above while delivering a click handler nothing at all - the failure mode that reads
    /// as "my handler is never called". The raise goes through
    /// <see cref="UIElement.RaiseEvent"/> with the registered event, which is the same path the
    /// library's own raiser uses, so what is proven is the connection between the CLR event and the
    /// routed event, not a test-local stand-in.
    /// </para>
    /// <para>
    /// The unsubscribe half is asserted as well, because an accessor pair that ignored its
    /// <c>RemoveHandler</c> call would leak handlers for the lifetime of a tray icon - and a tray
    /// icon lives for the lifetime of the process.
    /// </para>
    /// </remarks>
    [StaFact]
    public void CLR_click_event_wrappers_are_wired_to_their_registered_routed_events()
    {
        using var trayIcon = new TrayIcon(new FakeShellApi());

        foreach (ClickEventContract contract in ClickEventContracts)
        {
            int calls = 0;

            void Handler(object? sender, TrayIconClickEventArgs e)
            {
                Assert.Same(trayIcon, sender);
                Assert.Same(contract.Event, e.RoutedEvent);
                calls++;
            }

            EventHandler<TrayIconClickEventArgs> handler = Handler;

            contract.Subscribe(trayIcon, handler);

            trayIcon.RaiseEvent(new TrayIconClickEventArgs(MouseButton.Left, 1, new Point(10, 20), contract.Event));

            Assert.Equal(1, calls);

            contract.Unsubscribe(trayIcon, handler);

            trayIcon.RaiseEvent(new TrayIconClickEventArgs(MouseButton.Left, 1, new Point(10, 20), contract.Event));

            Assert.Equal(1, calls);
        }
    }

    /// <summary>
    /// <see cref="TrayIcon.MenuActivation"/> is the S03 decision surface: a public, writable,
    /// markup-resolvable dependency property owned by <see cref="TrayIcon"/>, defaulting to
    /// <see cref="TrayMenuActivation.RightClick"/>, which round-trips a value through the CLR
    /// property.
    /// </summary>
    /// <remarks>
    /// This is the S02 to S03 edge. S03 reads this value at right-click time to decide whether the
    /// element opens the menu itself, and markup sets it through the descriptor asserted here -
    /// the same markup-resolvability check <c>SliceContractTests</c> applies to the three S01
    /// properties. The round-trip is asserted through the CLR property rather than
    /// <c>GetValue</c> alone, because the CLR property is what a consumer writes and a wrapper that
    /// was not bound to the dependency property would still pass a metadata-only assertion.
    /// </remarks>
    [StaFact]
    public void MenuActivation_is_the_markup_resolvable_decision_surface_S03_reads()
    {
        DependencyProperty property = TrayIcon.MenuActivationProperty;

        Assert.Equal(nameof(TrayIcon.MenuActivation), property.Name);
        Assert.Equal(typeof(TrayIcon), property.OwnerType);
        Assert.Equal(typeof(TrayMenuActivation), property.PropertyType);
        Assert.False(property.ReadOnly);
        Assert.Equal(TrayMenuActivation.RightClick, property.DefaultMetadata.DefaultValue);

        // Metadata registered on TrayIcon itself, not inherited or attached.
        Assert.NotNull(property.GetMetadata(typeof(TrayIcon)));

        DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(TrayIcon));

        Assert.NotNull(descriptor);
        Assert.Equal(nameof(TrayIcon.MenuActivation), descriptor!.Name);
        Assert.Same(property, descriptor.DependencyProperty);

        using var trayIcon = new TrayIcon(new FakeShellApi());

        Assert.Equal(TrayMenuActivation.RightClick, trayIcon.MenuActivation);

        trayIcon.MenuActivation = TrayMenuActivation.None;

        Assert.Equal(TrayMenuActivation.None, trayIcon.MenuActivation);
        Assert.Equal(TrayMenuActivation.None, trayIcon.GetValue(TrayIcon.MenuActivationProperty));
    }

    /// <summary>
    /// The numeric values of <see cref="TrayMenuActivation"/> are stable: <c>RightClick</c> stays
    /// zero, because it is the default of a property consumers already have.
    /// </summary>
    /// <remarks>
    /// This is the other half of the S02 to S03 edge: S03 branches on the member, and anyone who
    /// persisted or compared the numeric value - or who simply relied on the property default -
    /// would change behaviour silently if the member were renumbered. <c>None</c> is pinned too, so
    /// a reordering cannot move it onto zero either.
    /// </remarks>
    [StaFact]
    public void TrayMenuActivation_values_are_stable_for_existing_consumers()
    {
        Assert.True(typeof(TrayMenuActivation).IsEnum);
        Assert.True(typeof(TrayMenuActivation).IsPublic);

        Assert.Equal(0, (int)TrayMenuActivation.RightClick);
        Assert.Equal(1, (int)TrayMenuActivation.None);

        Assert.True(Enum.IsDefined(typeof(TrayMenuActivation), TrayMenuActivation.RightClick));
        Assert.True(Enum.IsDefined(typeof(TrayMenuActivation), TrayMenuActivation.None));

        // The default of the property S03 reads is the member with the stable zero value, so the
        // two facts cannot drift apart.
        Assert.Equal(TrayMenuActivation.RightClick, TrayIcon.MenuActivationProperty.DefaultMetadata.DefaultValue);
    }

    /// <summary>
    /// <see cref="TrayIconClickEventArgs"/> is the sealed, public routed payload every click handler
    /// receives, and its <c>InvokeEventHandler</c> override dispatches the typed delegate directly
    /// instead of falling back to WPF's reflection path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the args contract consumers and S03 depend on: sealed (a consumer cannot pass a
    /// subclass the library would never raise), derived from <see cref="RoutedEventArgs"/> (so it can
    /// travel with a routed event at all), and exposing the button, click count and screen anchor as
    /// read-only values. The property types are asserted by reflection because a change to any of
    /// them is a source-breaking change for every handler.
    /// </para>
    /// <para>
    /// The dispatch half is asserted by invoking the override directly, for the reason
    /// <c>TrayIconClickEventArgsTests</c> records: the reflection fallback reaches the same handler
    /// with the same payload, so a behavioural raise cannot tell the two apart. Two things can. The
    /// override is declared on this type (not inherited), and a delegate whose type does not match
    /// <c>EventHandler&lt;TrayIconClickEventArgs&gt;</c> fails with the cast's
    /// <see cref="InvalidCastException"/> - a reflection-based dispatch would instead fail while
    /// trying to reconcile parameter types. That negative is the only observable difference between
    /// the override and the fallback, and it is deliberately asserted as the cast failure rather
    /// than as a message, so it survives any rework that keeps the typed dispatch.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Click_args_are_a_sealed_typed_payload_dispatched_by_the_typed_cast()
    {
        Type argsType = typeof(TrayIconClickEventArgs);

        Assert.True(argsType.IsPublic);
        Assert.True(argsType.IsSealed);
        Assert.Equal(typeof(RoutedEventArgs), argsType.BaseType);

        AssertProperty(argsType, nameof(TrayIconClickEventArgs.Button), typeof(MouseButton));
        AssertProperty(argsType, nameof(TrayIconClickEventArgs.ClickCount), typeof(int));
        AssertProperty(argsType, nameof(TrayIconClickEventArgs.ScreenAnchor), typeof(Point));

        MethodInfo? invoke = argsType.GetMethod(
            "InvokeEventHandler",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotNull(invoke);
        Assert.Equal(argsType, invoke!.DeclaringType);
        Assert.True(invoke.IsVirtual);

        var args = new TrayIconClickEventArgs(MouseButton.Middle, clickCount: 1, new Point(5, 6));
        object target = new();
        object? observedTarget = null;
        TrayIconClickEventArgs? observedArgs = null;
        int calls = 0;

        EventHandler<TrayIconClickEventArgs> handler = (sender, e) =>
        {
            calls++;
            observedTarget = sender;
            observedArgs = e;
        };

        invoke.Invoke(args, [handler, target]);

        Assert.Equal(1, calls);
        Assert.Same(target, observedTarget);
        Assert.Same(args, observedArgs);

        // A conforming-signature delegate of the wrong generic type is not silently reflected onto
        // the handler: the cast fails.
        EventHandler<RoutedEventArgs> nonMatchingHandler = (_, _) => { };

        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
            () => invoke.Invoke(args, [nonMatchingHandler, target]));

        Assert.IsType<InvalidCastException>(failure.InnerException);

        void AssertProperty(Type type, string name, Type expectedType)
        {
            PropertyInfo? property = type.GetProperty(name);

            Assert.NotNull(property);
            Assert.Equal(expectedType, property!.PropertyType);
            Assert.True(property.GetMethod!.IsPublic);
            Assert.Null(property.SetMethod);
        }
    }

    /// <summary>
    /// One row of the click-event contract table: the literal markup name, the public name
    /// constant, the registered routed event, the routing strategy it must use, and the CLR event
    /// wrapper as subscribe/unsubscribe pairs.
    /// </summary>
    /// <param name="Name">The literal name markup, diagnostics and tests spell.</param>
    /// <param name="EventName">The public constant that must equal the literal name.</param>
    /// <param name="Event">The registered routed event.</param>
    /// <param name="Strategy">The documented routing strategy.</param>
    /// <param name="Subscribe">Subscribes a handler through the CLR event wrapper.</param>
    /// <param name="Unsubscribe">Unsubscribes a handler through the CLR event wrapper.</param>
    private sealed record ClickEventContract(
        string Name,
        string EventName,
        RoutedEvent Event,
        RoutingStrategy Strategy,
        Action<TrayIcon, EventHandler<TrayIconClickEventArgs>> Subscribe,
        Action<TrayIcon, EventHandler<TrayIconClickEventArgs>> Unsubscribe);
}
