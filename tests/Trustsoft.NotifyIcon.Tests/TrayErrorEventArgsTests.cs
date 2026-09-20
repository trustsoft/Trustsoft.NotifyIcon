using System.Windows;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Pins the payload a windowless app receives when a notification-area update fails at runtime
/// (R013 / D008), so the app can decide between "ignore, the retry worked" and "tell the user".
/// </summary>
/// <remarks>
/// <para>
/// The tests deliberately do not use <c>TrayIcon.TrayErrorEvent</c>: the lifecycle task creates it
/// later. What is being pinned here is the args shape, and a locally registered
/// <see cref="RoutedEvent"/> is enough to prove the args can carry their event - which WPF
/// requires before <c>RaiseEvent</c> will accept them.
/// </para>
/// </remarks>
public sealed class TrayErrorEventArgsTests
{
    /// <summary>
    /// A local routed event used as the fixture. It is not the real <c>TrayError</c> event - it
    /// exists only so the args can be constructed and raised in a test that runs before the
    /// lifecycle type exists.
    /// </summary>
    private static readonly RoutedEvent FixtureRoutedEvent = EventManager.RegisterRoutedEvent(
        "TrayErrorEventArgsFixture",
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayErrorEventArgs>),
        typeof(TrayErrorEventArgsTests));

    /// <summary>
    /// All four data properties are what a handler sees; each must round-trip unchanged.
    /// </summary>
    [StaFact]
    public void Constructor_round_trips_the_four_data_properties()
    {
        var inner = new InvalidOperationException("boom");

        var args = new TrayErrorEventArgs("Modify", 87, inner, retried: true);

        Assert.Equal("Modify", args.Operation);
        Assert.Equal(87, args.Win32ErrorCode);
        Assert.Same(inner, args.Exception);
        Assert.True(args.Retried);
    }

    /// <summary>
    /// The clean case: a first-attempt failure is reported with <c>Retried == false</c>, which is
    /// what distinguishes "the retry was spent" from "the failure is the retryable one".
    /// </summary>
    [StaFact]
    public void Retried_flag_is_false_for_a_first_attempt_failure()
    {
        var args = new TrayErrorEventArgs("Modify", 5, new InvalidOperationException("boom"), retried: false);

        Assert.False(args.Retried);
    }

    /// <summary>
    /// WPF's event system only accepts args whose <see cref="RoutedEventArgs.RoutedEvent"/> is set,
    /// so the args the library raises must be constructible with its event already attached.
    /// </summary>
    [StaFact]
    public void Routed_event_supplied_at_construction_is_carried()
    {
        var args = new TrayErrorEventArgs(
            "Modify",
            87,
            new InvalidOperationException("boom"),
            retried: true,
            FixtureRoutedEvent);

        Assert.Same(FixtureRoutedEvent, args.RoutedEvent);
    }

    /// <summary>
    /// The raiser may also stamp the event onto args it constructed without one - the shape
    /// T05 writes its failure path against, so it is proven here rather than assumed.
    /// </summary>
    [StaFact]
    public void Routed_event_can_be_assigned_by_the_raiser()
    {
        var args = new TrayErrorEventArgs("Modify", 87, new InvalidOperationException("boom"), retried: true);

        Assert.Null(args.RoutedEvent);

        args.RoutedEvent = FixtureRoutedEvent;

        Assert.Same(FixtureRoutedEvent, args.RoutedEvent);
    }

    /// <summary>
    /// Event args are observed by the consumer through the base type, so the inheritance
    /// relationship is part of the contract; sealing matches the exception type's rationale.
    /// </summary>
    [StaFact]
    public void Type_is_public_sealed_and_derives_from_RoutedEventArgs()
    {
        Type type = typeof(TrayErrorEventArgs);

        Assert.True(type.IsPublic, $"{type.FullName} must be public: a consumer subscribes to the event.");
        Assert.True(type.IsSealed, $"{type.FullName} must be sealed - it is a payload, not an extension point.");
        Assert.True(
            typeof(RoutedEventArgs).IsAssignableFrom(type),
            $"{type.FullName} must derive from System.Windows.RoutedEventArgs.");
    }

    /// <summary>
    /// Missing data would leave the consumer with nothing to log, so both required references are
    /// rejected at construction rather than delivered as null.
    /// </summary>
    [StaFact]
    public void Missing_operation_or_exception_is_rejected()
    {
        var boom = new InvalidOperationException("boom");

        Assert.Throws<ArgumentNullException>(() => new TrayErrorEventArgs(null!, 87, boom, retried: false));
        Assert.Throws<ArgumentException>(() => new TrayErrorEventArgs(string.Empty, 87, boom, retried: false));
        Assert.Throws<ArgumentNullException>(() => new TrayErrorEventArgs("Modify", 87, null!, retried: false));

        // The bound overload validates the same terms and also rejects a null routed event,
        // which would produce args that RaiseEvent cannot route.
        Assert.Throws<ArgumentNullException>(
            () => new TrayErrorEventArgs(null!, 87, boom, retried: false, FixtureRoutedEvent));
        Assert.Throws<ArgumentNullException>(
            () => new TrayErrorEventArgs("Modify", 87, null!, retried: false, FixtureRoutedEvent));
        Assert.Throws<ArgumentNullException>(
            () => new TrayErrorEventArgs("Modify", 87, boom, retried: false, routedEvent: null!));
    }
}
