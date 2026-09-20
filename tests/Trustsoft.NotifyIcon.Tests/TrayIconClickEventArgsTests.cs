using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the public surface S02 adds for clicks: the args a click handler receives
/// (<see cref="TrayIconClickEventArgs"/>) and the decision surface the element reads when a right
/// click arrives (<see cref="TrayMenuActivation"/>, carried by <see cref="TrayIcon.MenuActivation"/>).
/// </summary>
/// <remarks>
/// <para>
/// S03 consumes the right-click payload and the <c>MenuActivation</c> value, and the click routed
/// events T03 raises are stamped with this args type; both are asserted here so a missing or
/// differently shaped member fails at the boundary rather than in the middle of the consumer's work.
/// </para>
/// <para>
/// The tests deliberately do not use the real click routed events: those are created by the wiring
/// task. What is pinned here is the args shape and the dependency property, and a locally registered
/// <see cref="RoutedEvent"/> is enough to prove the args can carry their event - which WPF requires
/// before <c>RaiseEvent</c> will route them.
/// </para>
/// </remarks>
public sealed class TrayIconClickEventArgsTests
{
    /// <summary>
    /// A local routed event used as the fixture. It is not one of the real click events - it exists
    /// only so the args can be constructed and raised without depending on the wiring task's
    /// registrations.
    /// </summary>
    private static readonly RoutedEvent FixtureRoutedEvent = EventManager.RegisterRoutedEvent(
        "TrayIconClickEventArgsFixture",
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayIconClickEventArgs>),
        typeof(TrayIconClickEventArgsTests));

    /// <summary>
    /// All three data properties are what a handler sees; each must round-trip unchanged, including
    /// the double-click count and a negative anchor (a tray on a monitor left of the primary one).
    /// </summary>
    [StaFact]
    public void Constructor_round_trips_the_three_data_properties()
    {
        var anchor = new Point(-1920, 1060);

        var args = new TrayIconClickEventArgs(MouseButton.Left, clickCount: 2, anchor);

        Assert.Equal(MouseButton.Left, args.Button);
        Assert.Equal(2, args.ClickCount);
        Assert.Equal(anchor, args.ScreenAnchor);
    }

    /// <summary>
    /// WPF's event system only accepts args whose <see cref="RoutedEventArgs.RoutedEvent"/> is set,
    /// so the args the library raises must be constructible with their event already attached.
    /// </summary>
    [StaFact]
    public void Routed_event_supplied_at_construction_is_carried()
    {
        var args = new TrayIconClickEventArgs(MouseButton.Right, clickCount: 1, new Point(10, 20), FixtureRoutedEvent);

        Assert.Same(FixtureRoutedEvent, args.RoutedEvent);
    }

    /// <summary>
    /// The raiser may also stamp the event onto args it constructed without one - the shape the
    /// wiring task writes against, so it is proven here rather than assumed.
    /// </summary>
    [StaFact]
    public void Routed_event_can_be_assigned_by_the_raiser()
    {
        var args = new TrayIconClickEventArgs(MouseButton.Middle, clickCount: 1, new Point(10, 20));

        Assert.Null(args.RoutedEvent);

        args.RoutedEvent = FixtureRoutedEvent;

        Assert.Same(FixtureRoutedEvent, args.RoutedEvent);
    }

    /// <summary>
    /// Event args are observed by the consumer through the base type, so the inheritance
    /// relationship is part of the contract; sealing matches the error args type's rationale.
    /// </summary>
    [StaFact]
    public void Type_is_public_sealed_and_derives_from_RoutedEventArgs()
    {
        Type type = typeof(TrayIconClickEventArgs);

        Assert.True(type.IsPublic, $"{type.FullName} must be public: a consumer subscribes to the event.");
        Assert.True(type.IsSealed, $"{type.FullName} must be sealed - it is a payload, not an extension point.");
        Assert.True(
            typeof(RoutedEventArgs).IsAssignableFrom(type),
            $"{type.FullName} must derive from System.Windows.RoutedEventArgs.");
    }

    /// <summary>
    /// A raise delivers the payload to a handler registered for the args type: this is the whole
    /// point of the type, and it is asserted on a parentless element because that is the only kind
    /// of element this library has.
    /// </summary>
    [StaFact]
    public void RaiseEvent_dispatches_the_payload_to_a_typed_handler_intact()
    {
        var observed = new List<TrayIconClickEventArgs>();

        using var trayIcon = new TrayIcon(new FakeShellApi());

        trayIcon.AddHandler(FixtureRoutedEvent, new EventHandler<TrayIconClickEventArgs>((_, args) => observed.Add(args)));

        var raised = new TrayIconClickEventArgs(MouseButton.Right, clickCount: 1, new Point(1440, 12), FixtureRoutedEvent);

        trayIcon.RaiseEvent(raised);

        TrayIconClickEventArgs delivered = Assert.Single(observed);
        Assert.Same(raised, delivered);
        Assert.Equal(MouseButton.Right, delivered.Button);
        Assert.Equal(1, delivered.ClickCount);
        Assert.Equal(new Point(1440, 12), delivered.ScreenAnchor);
        Assert.Same(trayIcon, delivered.Source);
    }

    /// <summary>
    /// The args override <c>InvokeEventHandler</c>, so WPF dispatches the typed delegate directly
    /// instead of falling back to reflection for an args type it does not know.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection on purpose: the reflection fallback reaches the same handler with the
    /// same payload, so a behavioural test cannot tell the two apart. The declaring type of the
    /// override is the only observable difference, and it is a real one - the handler-type contract
    /// is what makes the cast in the override safe.
    /// </remarks>
    [StaFact]
    public void InvokeEventHandler_is_overridden_on_the_args_type()
    {
        MethodInfo? method = typeof(TrayIconClickEventArgs).GetMethod(
            "InvokeEventHandler",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotNull(method);
        Assert.Equal(typeof(TrayIconClickEventArgs), method!.DeclaringType);
        Assert.Equal(new[] { typeof(Delegate), typeof(object) }, method.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>
    /// Args that cannot be routed must fail loudly, because the alternative - WPF accepting them and
    /// silently raising nothing - is exactly the class of silent failure this project has had to fix
    /// before.
    /// </summary>
    /// <remarks>
    /// Two independent guards are asserted. Construction with a null event is rejected by the type
    /// itself (<see cref="ArgumentNullException"/>), which is the guard the library's own raise path
    /// relies on; and a caller that constructs the unbound args and tries to raise them anyway gets
    /// an exception from WPF rather than a no-op. The exact exception type from WPF's internal route
    /// factory is deliberately not asserted - it is framework detail, not contract - so the test
    /// records any failure while still failing if the raise silently succeeds.
    /// </remarks>
    [StaFact]
    public void Args_without_a_routed_event_are_rejected_and_cannot_be_raised()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TrayIconClickEventArgs(MouseButton.Left, clickCount: 1, new Point(1, 1), routedEvent: null!));

        using var trayIcon = new TrayIcon(new FakeShellApi());

        var observed = new List<TrayIconClickEventArgs>();
        trayIcon.AddHandler(FixtureRoutedEvent, new EventHandler<TrayIconClickEventArgs>((_, args) => observed.Add(args)));

        var unstamped = new TrayIconClickEventArgs(MouseButton.Left, clickCount: 1, new Point(1, 1));

        Assert.Null(unstamped.RoutedEvent);

        Exception? failure = Record.Exception(() => trayIcon.RaiseEvent(unstamped));

        Assert.NotNull(failure);
        Assert.Empty(observed);
    }

    /// <summary>
    /// The <c>MenuActivation</c> dependency property is owned by <see cref="TrayIcon"/>, is public
    /// and writable, and defaults to <see cref="TrayMenuActivation.RightClick"/> - the value S03
    /// branches on and the value markup sets.
    /// </summary>
    /// <remarks>
    /// <see cref="System.ComponentModel.DependencyPropertyDescriptor"/> resolvability is not
    /// re-asserted here; the slice-contract audit owns the markup-resolvability edge for the whole
    /// property set.
    /// </remarks>
    [StaFact]
    public void MenuActivation_dependency_property_has_the_documented_shape_and_default()
    {
        DependencyProperty property = TrayIcon.MenuActivationProperty;

        Assert.Equal(nameof(TrayIcon.MenuActivation), property.Name);
        Assert.Equal(typeof(TrayIcon), property.OwnerType);
        Assert.Equal(typeof(TrayMenuActivation), property.PropertyType);
        Assert.False(property.ReadOnly);
        Assert.Equal(TrayMenuActivation.RightClick, property.DefaultMetadata.DefaultValue);

        // The numeric values are a contract: RightClick is the default of a property consumers
        // already have, so renumbering it would silently change their behaviour.
        Assert.Equal(0, (int)TrayMenuActivation.RightClick);
        Assert.Equal(1, (int)TrayMenuActivation.None);
    }

    /// <summary>
    /// The CLR property round-trips a value through the dependency property, and a fresh instance
    /// reports the documented default rather than nothing at all.
    /// </summary>
    [StaFact]
    public void MenuActivation_round_trips_and_defaults_to_RightClick()
    {
        using var trayIcon = new TrayIcon(new FakeShellApi());

        Assert.Equal(TrayMenuActivation.RightClick, trayIcon.MenuActivation);

        trayIcon.MenuActivation = TrayMenuActivation.None;

        Assert.Equal(TrayMenuActivation.None, trayIcon.MenuActivation);
        Assert.Equal(TrayMenuActivation.None, trayIcon.GetValue(TrayIcon.MenuActivationProperty));

        trayIcon.MenuActivation = TrayMenuActivation.RightClick;

        Assert.Equal(TrayMenuActivation.RightClick, trayIcon.MenuActivation);
    }

    /// <summary>
    /// Assigning <c>MenuActivation</c> from a foreign thread is refused by WPF's own thread check
    /// instead of being marshalled, and the property keeps its previous value.
    /// </summary>
    /// <remarks>
    /// This is the deliberate difference from <see cref="TrayIcon.IconSource"/>,
    /// <see cref="TrayIcon.ToolTipText"/> and <see cref="TrayIcon.Visible"/> (R015): those assign
    /// through <c>SetPropertyOnDispatcher</c> because their assignment has to reach the shell on the
    /// owning thread, while this property applies nothing and so has nothing to marshal. The test
    /// exists so the difference is a decision with evidence rather than an omission: if someone
    /// later adds the marshalling wrapper for symmetry, this assertion is where the intent is
    /// recorded - and it stays valid either way, because a refused assignment is also not applied.
    /// </remarks>
    [StaFact]
    public void MenuActivation_assignment_from_a_foreign_thread_is_refused_not_marshalled()
    {
        using var trayIcon = new TrayIcon(new FakeShellApi());

        Exception? failure = null;
        int assigningThread = 0;

        var worker = new Thread(() =>
        {
            assigningThread = Environment.CurrentManagedThreadId;

            try
            {
                trayIcon.MenuActivation = TrayMenuActivation.None;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        worker.Start();
        worker.Join();

        Assert.NotEqual(Environment.CurrentManagedThreadId, assigningThread);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(TrayMenuActivation.RightClick, trayIcon.MenuActivation);
    }
}
