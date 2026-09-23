using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the public activation surface M002/S03 adds: the three typed events on
/// <see cref="ToastNotifier"/> and the four types that carry them
/// (<see cref="ToastActivatedEventArgs"/>, <see cref="ToastDismissedEventArgs"/>,
/// <see cref="ToastDismissalReason"/>, <see cref="ToastErrorEventArgs"/>), plus the failure split
/// (D055/D061) that sends a show-path failure through the event and the Error-level trace line
/// instead of a throw.
/// </summary>
/// <remarks>
/// <para>
/// Every test is fake-driven: <see cref="FakeToastApi.RaiseActivated"/>,
/// <see cref="FakeToastApi.RaiseDismissed"/> and <see cref="FakeToastApi.RaiseFailed"/> invoke the
/// handlers the show path subscribed through the seam, which is the only way to deliver an activation
/// in-process. No live banner, no click, no apartment.
/// </para>
/// <para>
/// What these tests can and cannot prove is worth stating, because the slice's demo sentence is about
/// clicks: they prove the payload plumbing end to end - that each delivered argument, reason and code
/// reaches the event with the documented shape - and they prove nothing about which element a user
/// touched, which the platform does not report (see the wording rule on
/// <see cref="ToastNotifier"/>).
/// </para>
/// <para>
/// The failure-split tests read the shared trace source, so this class joins
/// <c>TraceChannelCollection</c>: a test that asserts an exact line count cannot overlap with another
/// class' writer on the same process-wide source.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class ToastEventTests
{
    /// <summary>The override id every test registers with, chosen so it cannot be the derived default.</summary>
    private const string OverrideId = "Vendor.Custom.App";

    /// <summary>
    /// The coexistence sentence S03 and S05 share, asserted verbatim against the generated
    /// documentation so the two slices cannot drift into two different promises.
    /// </summary>
    private const string CoexistenceSentence =
        "Toasts and balloons are independent: showing a toast never suppresses, replaces or re-routes a balloon tip, and showing a balloon tip never replaces or re-routes a toast.";

    /// <summary>
    /// The wording rule that forbids reading an activation as a click report, asserted verbatim for
    /// the same reason.
    /// </summary>
    private const string ActivationWordingRule =
        "Activated reports that the toast's launch or button argument arrived; it is not a report that the user clicked the body.";

    /// <summary>
    /// <see cref="ToastNotifier.Activated"/> delivers the argument exactly as Windows sent it,
    /// including a <see langword="null"/> argument and an empty one, once per raise and from the
    /// notifier as the sender.
    /// </summary>
    [Fact]
    public void Activated_carries_the_delivered_argument_verbatim()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content(launch: "sample-toast-1"));

        List<object?> senders = [];
        List<string?> delivered = [];
        notifier.Activated += (sender, e) =>
        {
            senders.Add(sender);
            delivered.Add(e.Arguments);
        };

        const string ButtonArgument = "sample-button-1";
        string empty = string.Empty;

        Assert.True(fake.RaiseActivated(ButtonArgument));
        Assert.True(fake.RaiseActivated(empty));
        Assert.True(fake.RaiseActivated(null));

        List<string?> expected = [ButtonArgument, string.Empty, null];

        Assert.Equal(expected, delivered);
        Assert.All(senders, sender => Assert.Same(notifier, sender));

        // Verbatim means the same string instance the shell delivered: the library does not copy,
        // trim or normalise the argument on its way to the event.
        Assert.Same(ButtonArgument, delivered[0]);
    }

    /// <summary>
    /// A raise with no subscription behind it reaches nothing and reports that nothing was delivered,
    /// so a test can never mistake "no handler" for "handled".
    /// </summary>
    [Fact]
    public void No_activation_is_delivered_before_the_first_show_subscribes()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        int activations = 0;
        notifier.Activated += (_, _) => activations++;

        Assert.False(fake.RaiseActivated("sample-toast-1"));
        Assert.Equal(0, activations);
    }

    /// <summary>
    /// <see cref="ToastNotifier.Dismissed"/> reports every known reason by name and reports anything
    /// outside the known set - including the seam's unreadable-payload sentinel - as
    /// <see cref="ToastDismissalReason.Unknown"/> rather than guessing.
    /// </summary>
    [Fact]
    public void Dismissed_maps_every_known_reason_and_reports_anything_else_as_unknown()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        List<ToastDismissalReason> reported = [];
        notifier.Dismissed += (_, e) => reported.Add(e.Reason);

        Assert.True(fake.RaiseDismissed((int)ToastDismissalReason.UserCanceled));
        Assert.True(fake.RaiseDismissed((int)ToastDismissalReason.ApplicationHidden));
        Assert.True(fake.RaiseDismissed((int)ToastDismissalReason.TimedOut));

        // -1 is the seam's read-failure sentinel; 42 is a value the platform does not define. Both
        // must be reported as unknown, and neither may be folded into UserCanceled.
        Assert.True(fake.RaiseDismissed(-1));
        Assert.True(fake.RaiseDismissed(42));

        List<ToastDismissalReason> expected =
        [
            ToastDismissalReason.UserCanceled,
            ToastDismissalReason.ApplicationHidden,
            ToastDismissalReason.TimedOut,
            ToastDismissalReason.Unknown,
            ToastDismissalReason.Unknown,
        ];

        Assert.Equal(expected, reported);
    }

    /// <summary>
    /// The shell's asynchronous delivery failure reaches <see cref="ToastNotifier.ToastError"/> with
    /// the pinned operation name, the code it reported, and no exception - and it is the same
    /// vocabulary <see cref="ToastException"/> publishes.
    /// </summary>
    [Fact]
    public void The_shells_asynchronous_failure_reaches_ToastError_with_the_pinned_operation_and_code()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        Assert.True(fake.RaiseFailed(FakeToastApi.DefaultFailureHResult));

        ToastErrorEventArgs error = Assert.Single(errors);

        Assert.Equal(ToastException.OperationNotificationFailed, error.Operation);
        Assert.Equal(ToastShow.OperationNotificationFailed, error.Operation);
        Assert.Equal("NotificationFailed", error.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, error.ErrorCode);

        // The shell's callback reports a bare HRESULT: there is no exception behind it.
        Assert.Null(error.Exception);
    }

    /// <summary>
    /// Two shows subscribe two independent sets of callbacks, and each one delivers to the notifier -
    /// the proof that the wiring is per show rather than a single assignment that the second show
    /// overwrites.
    /// </summary>
    [Fact]
    public void Each_show_subscribes_callbacks_that_reach_the_notifier()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<string?> delivered = [];
        notifier.Activated += (_, e) => delivered.Add(e.Arguments);

        notifier.Show(Content("first", "sample-toast-1"));
        Assert.True(fake.RaiseActivated("sample-toast-1"));

        notifier.Show(Content("second", "sample-toast-2"));
        Assert.True(fake.RaiseActivated("sample-toast-2"));

        Assert.Equal(2, fake.CallCount(ToastOperation.SubscribeActivated));

        List<string?> expected = ["sample-toast-1", "sample-toast-2"];
        Assert.Equal(expected, delivered);

        // And each show's subscription is still the fake's live one, so teardown must remove two.
        notifier.Dispose();

        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeActivated));
    }

    /// <summary>
    /// A consumer handler that throws is caught and never crosses back into the shell's callback
    /// path, and the notifier stays usable for the events raised afterwards.
    /// </summary>
    [Fact]
    public void A_throwing_consumer_handler_does_not_escape_the_raise()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        notifier.Activated += (_, _) => throw new InvalidOperationException("consumer bug in Activated");
        notifier.ToastError += (_, _) => throw new InvalidOperationException("consumer bug in ToastError");

        int dismissals = 0;
        notifier.Dismissed += (_, _) => dismissals++;

        // Statement bodies on purpose: a bool-returning expression lambda would be convertible to
        // both the Action and the Func<object?> overloads of Record.Exception.
        Assert.Null(Record.Exception(() => { fake.RaiseActivated("sample-button-1"); }));
        Assert.Null(Record.Exception(() => { fake.RaiseFailed(FakeToastApi.DefaultFailureHResult); }));

        // Later callbacks still arrive: the failure is the handler's, not the notifier's.
        Assert.True(fake.RaiseDismissed((int)ToastDismissalReason.TimedOut));
        Assert.Equal(1, dismissals);
    }

    /// <summary>
    /// A failure the show path reports after a successful registration is non-fatal (D055/D061): it
    /// raises <see cref="ToastNotifier.ToastError"/> once with the failing seam member as the
    /// operation, writes exactly one Error-level line on the trace channel carrying the pinned event
    /// id, and <c>Show</c> returns normally instead of throwing.
    /// </summary>
    [Fact]
    public void A_show_path_failure_raises_ToastError_and_writes_one_Error_line_without_throwing()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.Show);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            // The runtime failure is reported, not fatal: no exception may leave Show (D055/D061).
            // Statement body on purpose - Record.Exception's overloads are ambiguous for an
            // expression lambda whose value could be ignored.
            Exception? thrown = Record.Exception(() => { notifier.Show(Content()); });

            Assert.Null(thrown);
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        ToastErrorEventArgs error = Assert.Single(errors);

        Assert.Equal(nameof(IToastApi.Show), error.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, error.ErrorCode);
        Assert.Null(error.Exception);

        // Exactly one line, at Error severity, with the toast-failure event id - a listener at the
        // source's default Warning level sees it without configuration, unlike the Verbose steps.
        (TraceEventType eventType, int eventId, string? message) = Assert.Single(recorder.Events);

        Assert.Equal(TraceEventType.Error, eventType);
        Assert.Equal(NotifyIconTrace.ToastErrorEventId, eventId);
        Assert.NotNull(message);
        Assert.Contains(
            $"Toast {nameof(IToastApi.Show)} failed (code {FakeToastApi.DefaultFailureHResult}, 0x{FakeToastApi.DefaultFailureHResult:X8}).",
            message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the split D061 fixes: a registration failure still throws
    /// <see cref="ToastException"/> from <c>Show</c> and raises no <see cref="ToastNotifier.ToastError"/>
    /// - a notifier that cannot establish its identity has no non-fatal way to say so.
    /// </summary>
    [Fact]
    public void A_registration_failure_still_throws_and_raises_no_ToastError()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.SaveShortcut);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        ToastException error = Assert.Throws<ToastException>(() => notifier.Show(Content()));

        Assert.Equal(nameof(IToastApi.SaveShortcut), error.Operation);
        Assert.Equal(fake.FailureHResult, error.ErrorCode);
        Assert.Empty(errors);
    }

    /// <summary>
    /// A consumer handler that throws is the consumer's bug, not a delivery failure: it is caught,
    /// written as exactly one Error-level line naming the event it arrived on, never published as
    /// <see cref="ToastNotifier.ToastError"/>, and never crosses back into the callback path.
    /// </summary>
    [Fact]
    public void A_throwing_consumer_handler_is_traced_at_Error_level_and_raises_no_ToastError()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);
        notifier.Activated += (_, _) => throw new InvalidOperationException("consumer bug in Activated");

        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            // No Assert.Throws: the assertion is that control returns here at all.
            Assert.Null(Record.Exception(() => { fake.RaiseActivated("sample-button-1"); }));
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        // The toast was delivered; it is the handler that failed, so the failure is not reported as
        // a ToastError - it is the consumer's own bug.
        Assert.Empty(errors);

        (TraceEventType eventType, int eventId, string? message) = Assert.Single(recorder.Events);

        Assert.Equal(TraceEventType.Error, eventType);
        Assert.Equal(NotifyIconTrace.ToastErrorEventId, eventId);
        Assert.NotNull(message);
        Assert.Contains(
            $"Toast {nameof(ToastNotifier.Activated)} failed (code 0, 0x00000000).",
            message,
            StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A healthy show emits nothing at Error level: the trace channel stays failure-only (MEM026),
    /// so a consumer who subscribes the documented source is never woken by ordinary traffic. The
    /// show path's own step lines are Verbose and are filtered by the source's default Warning level.
    /// </summary>
    [Fact]
    public void A_healthy_show_writes_no_Error_level_line()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        var recorder = new RecordingTraceListener();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            notifier.Show(Content());

            // Delivery traffic is not a failure: an activation and a dismissal must stay silent too.
            Assert.True(fake.RaiseActivated("sample-button-1"));
            Assert.True(fake.RaiseDismissed((int)ToastDismissalReason.UserCanceled));
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        Assert.Empty(recorder.Events);
    }

    /// <summary>
    /// The dismissal vocabulary carries the platform's own ordinals, and
    /// <see cref="ToastDismissalReason.Unknown"/> is the seam's sentinel at <c>-1</c> - never a
    /// platform value.
    /// </summary>
    [Fact]
    public void Dismissal_reason_ordinals_are_the_platform_values()
    {
        Assert.Equal(-1, (int)ToastDismissalReason.Unknown);
        Assert.Equal(0, (int)ToastDismissalReason.UserCanceled);
        Assert.Equal(1, (int)ToastDismissalReason.ApplicationHidden);
        Assert.Equal(2, (int)ToastDismissalReason.TimedOut);

        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ToastDismissalReason)));
    }

    /// <summary>
    /// The three payload types validate what they promise: the args are standard
    /// <see cref="EventArgs"/> a consumer can hand to an <see cref="EventHandler{T}"/>, the activated
    /// payload accepts a null argument, and the error payload rejects a missing operation while
    /// accepting a missing exception.
    /// </summary>
    [Fact]
    public void Argument_types_validate_and_round_trip_what_they_promise()
    {
        var activated = new ToastActivatedEventArgs("sample-button-2");

        Assert.Equal("sample-button-2", activated.Arguments);
        Assert.Null(new ToastActivatedEventArgs(null).Arguments);

        var dismissed = new ToastDismissedEventArgs(ToastDismissalReason.ApplicationHidden);

        Assert.Equal(ToastDismissalReason.ApplicationHidden, dismissed.Reason);

        var withoutException = new ToastErrorEventArgs(ToastException.OperationNotificationFailed, 5, exception: null);

        Assert.Equal("NotificationFailed", withoutException.Operation);
        Assert.Equal(5, withoutException.ErrorCode);
        Assert.Null(withoutException.Exception);

        var exception = new InvalidOperationException("boom");
        var withException = new ToastErrorEventArgs(nameof(IToastApi.Show), FakeToastApi.DefaultFailureHResult, exception);

        Assert.Same(exception, withException.Exception);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, withException.ErrorCode);

        // The operation name is machine-readable, so an absent one is rejected the same way
        // TrayErrorEventArgs rejects it - and a null exception is explicitly allowed, unlike there.
        Assert.Throws<ArgumentNullException>(() => new ToastErrorEventArgs(null!, 0, exception: null));
        Assert.Throws<ArgumentException>(() => new ToastErrorEventArgs(string.Empty, 0, exception: null));
        Assert.Throws<ArgumentException>(() => new ToastErrorEventArgs(string.Empty, 0, new InvalidOperationException()));

        // All three flow through the framework's own event shape.
        Assert.IsAssignableFrom<EventArgs>(activated);
        Assert.IsAssignableFrom<EventArgs>(dismissed);
        Assert.IsAssignableFrom<EventArgs>(withException);
    }

    /// <summary>
    /// The two boundary sentences the slice requires are present verbatim in the generated
    /// documentation of <see cref="ToastNotifier"/> - the file a consumer reads, not the source - so
    /// a reworded remark cannot quietly weaken the promise S05 depends on.
    /// </summary>
    /// <remarks>
    /// The documentation is generated from the same doc comments the library ships, so asserting
    /// against the generated file is asserting against the shipped documentation rather than against
    /// the convention that it exists.
    /// </remarks>
    [Fact]
    public void The_two_boundary_sentences_appear_verbatim_in_the_generated_documentation()
    {
        string documentationPath = Path.ChangeExtension(typeof(TrayIcon).Assembly.Location, ".xml");

        Assert.True(
            File.Exists(documentationPath),
            $"No generated documentation file was found at '{documentationPath}', so the boundary sentences cannot be checked. "
            + "Build the solution first: dotnet build Trustsoft.NotifyIcon.sln -c Release.");

        XElement? member = XDocument.Load(documentationPath)
            .Descendants("member")
            .FirstOrDefault(element => (string?)element.Attribute("name") == $"T:{typeof(ToastNotifier).FullName}");

        Assert.NotNull(member);

        string documented = member!.Value;

        Assert.Contains(CoexistenceSentence, documented, StringComparison.Ordinal);
        Assert.Contains(ActivationWordingRule, documented, StringComparison.Ordinal);
    }

    /// <summary>
    /// A representative content, with a launch argument the way the sample's runs use one.
    /// </summary>
    /// <param name="title">The title; the two-show test varies it.</param>
    /// <param name="launch">The launch argument the toast carries.</param>
    /// <returns>The content.</returns>
    private static ToastContent Content(string title = "Build finished", string launch = "sample-toast-1") => new()
    {
        Title = title,
        Body = "3 projects built in 12.4s",
        Severity = ToastSeverity.Reminder,
        Launch = launch,
    };

    /// <summary>
    /// Captures the structured trace events the shared source emits, so severity and event id can be
    /// asserted instead of inferred from formatted text. The same shape
    /// <see cref="NotifyIconTraceTests"/> uses for the tray writer.
    /// </summary>
    private sealed class RecordingTraceListener : TraceListener
    {
        /// <summary>Gets the sequence of trace events this listener has received.</summary>
        internal List<(TraceEventType EventType, int EventId, string? Message)> Events { get; } = [];

        /// <inheritdoc />
        public override void Write(string? message)
        {
        }

        /// <inheritdoc />
        public override void WriteLine(string? message)
        {
        }

        /// <inheritdoc />
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            Events.Add((eventType, id, message));
        }
    }
}
