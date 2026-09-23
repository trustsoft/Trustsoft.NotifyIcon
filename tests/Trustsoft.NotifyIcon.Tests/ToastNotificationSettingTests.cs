using System.Collections.Generic;
using System.Diagnostics;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the notification-setting outcome M002/S04/T04 adds: the ordinals of
/// <see cref="ToastNotificationSetting"/> and the setting <see cref="ToastNotifier.NotificationSetting"/>
/// reports after a show.
/// </summary>
/// <remarks>
/// <para>
/// Fake-driven, like the other notifier tests: <see cref="FakeToastApi.NotifierSettingToReport"/>
/// stands in for the platform's <c>GetSetting</c>, so all five values and the "nothing was read"
/// case can be exercised without touching the machine's real notification state. The measured live
/// nuance - the platform can need a second process before it reports a change - is why the value is
/// scripted here rather than asserted against a live read (the live run is the sample's).
/// </para>
/// <para>
/// The class joins <c>TraceChannelCollection</c> because the setting's trace test raises the
/// process-wide source level, which must not overlap another class' trace assertions.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class ToastNotificationSettingTests
{
    /// <summary>The override id these tests register with, chosen so it cannot be the derived default.</summary>
    private const string OverrideId = "Vendor.Setting.App";

    /// <summary>
    /// The members are the platform's own ordinals, so a value read from <c>GetSetting</c> needs no
    /// translation of ours - and renumbering one is a wire-format change, which must be a deliberate
    /// edit here.
    /// </summary>
    [Fact]
    public void The_members_are_the_platforms_own_ordinals()
    {
        Assert.Equal(0, (int)ToastNotificationSetting.Enabled);
        Assert.Equal(1, (int)ToastNotificationSetting.DisabledForApplication);
        Assert.Equal(2, (int)ToastNotificationSetting.DisabledForUser);
        Assert.Equal(3, (int)ToastNotificationSetting.DisabledByGroupPolicy);
        Assert.Equal(4, (int)ToastNotificationSetting.DisabledByManifest);

        // Exactly the five values the platform reports: a sixth member would name a value Windows
        // does not produce.
        Assert.Equal(5, System.Enum.GetValues<ToastNotificationSetting>().Length);
    }

    /// <summary>
    /// Null before the first show is the "nothing was read yet" state, not "enabled": the property's
    /// zero value must never double as the ordinary Enabled outcome.
    /// </summary>
    [Fact]
    public void The_setting_is_null_before_the_first_show()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        Assert.Null(notifier.NotificationSetting);

        notifier.Show(Content());

        // The first show read one, so the property now reports the outcome rather than null.
        Assert.Equal(ToastNotificationSetting.Enabled, notifier.NotificationSetting);
    }

    /// <summary>
    /// Every value the platform can report after a show reaches the consumer as the matching member -
    /// asserted for all five, so the mapping cannot silently drift from the ordinals.
    /// </summary>
    /// <param name="rawSetting">The raw value <c>GetSetting</c> reports.</param>
    /// <param name="expected">The member the property must report.</param>
    [Theory]
    [InlineData(0, ToastNotificationSetting.Enabled)]
    [InlineData(1, ToastNotificationSetting.DisabledForApplication)]
    [InlineData(2, ToastNotificationSetting.DisabledForUser)]
    [InlineData(3, ToastNotificationSetting.DisabledByGroupPolicy)]
    [InlineData(4, ToastNotificationSetting.DisabledByManifest)]
    public void A_show_reports_the_setting_the_platform_returned(int rawSetting, ToastNotificationSetting expected)
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId, NotifierSettingToReport = rawSetting };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        Assert.Equal(expected, notifier.NotificationSetting);
    }

    /// <summary>
    /// A show that failed before the setting step reports no setting at all: the property stays
    /// <see langword="null"/> rather than claiming the platform said <c>Enabled</c>.
    /// </summary>
    /// <remarks>
    /// The failure is non-fatal (D055/D061) and reported through <see cref="ToastNotifier.ToastError"/>,
    /// so this is the ordinary path a windowless host sees when the show path fails early.
    /// </remarks>
    [Fact]
    public void A_show_that_failed_before_the_setting_step_leaves_the_setting_null()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.CreateToastNotifier);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        Exception? thrown = Record.Exception(() => { notifier.Show(Content()); });

        Assert.Null(thrown);

        ToastErrorEventArgs error = Assert.Single(errors);

        Assert.Equal(nameof(IToastApi.CreateToastNotifier), error.Operation);
        Assert.Equal(fake.FailureHResult, error.ErrorCode);

        // The setting step was never reached, so nothing is claimed about the platform's setting.
        Assert.Null(notifier.NotificationSetting);
    }

    /// <summary>
    /// A failure at the setting step itself leaves the property <see langword="null"/> as well: the
    /// out parameter of a failed <c>GetSetting</c> is not a reading, so reporting its <c>0</c> would
    /// announce "Enabled" for a show that never read one.
    /// </summary>
    [Fact]
    public void A_failure_at_the_setting_step_leaves_the_setting_null()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.GetNotifierSetting);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        notifier.Show(Content());

        ToastErrorEventArgs error = Assert.Single(errors);

        Assert.Equal(nameof(IToastApi.GetNotifierSetting), error.Operation);
        Assert.Equal(fake.FailureHResult, error.ErrorCode);
        Assert.Null(notifier.NotificationSetting);
    }

    /// <summary>
    /// The routine-setting rule, pinned rather than asserted in prose: a notifications-disabled
    /// identity still shows (the shell returns <c>S_OK</c> and raises any delivery failure out of
    /// band), raises no <see cref="ToastNotifier.ToastError"/> and writes no Error-level line - the
    /// show simply reports the outcome.
    /// </summary>
    [Fact]
    public void A_disabled_setting_raises_no_ToastError_and_writes_no_Error_level_line()
    {
        var fake = new FakeToastApi
        {
            AppUserModelIdToReadBack = OverrideId,
            NotifierSettingToReport = (int)ToastNotificationSetting.DisabledForApplication,
        };

        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        var recorder = new TraceRecorder();
        TraceSource source = NotifyIconTrace.Source;

        try
        {
            source.Listeners.Add(recorder);

            notifier.Show(Content());
        }
        finally
        {
            source.Listeners.Remove(recorder);
        }

        // The toast really was handed to the shell; the disabled setting did not stop or fail it.
        Assert.Equal(1, fake.CallCount(ToastOperation.Show));
        Assert.Equal(ToastNotificationSetting.DisabledForApplication, notifier.NotificationSetting);

        // No failure channel fired: no event, and no line at all on the default-configured source
        // (the setting line is Verbose and the source defaults to Warning).
        Assert.Empty(errors);
        Assert.Empty(recorder.Events);

        notifier.Dispose();
    }

    /// <summary>
    /// One Verbose line names the setting, so a support capture that raised the trace level reads the
    /// outcome rather than having to infer it from silence.
    /// </summary>
    [Fact]
    public void The_setting_is_named_on_the_trace_channel()
    {
        var fake = new FakeToastApi
        {
            AppUserModelIdToReadBack = OverrideId,
            NotifierSettingToReport = (int)ToastNotificationSetting.DisabledForApplication,
        };

        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        var recorder = new TraceRecorder();
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        try
        {
            source.Listeners.Add(recorder);
            source.Switch.Level = SourceLevels.Verbose;

            notifier.Show(Content());
            source.Flush();
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(recorder);
        }

        Assert.Contains(
            recorder.Events,
            entry => entry.EventType == TraceEventType.Verbose
                && entry.EventId == NotifyIconTrace.VerboseEventId
                && entry.Message is not null
                && entry.Message.Contains(
                    "toast notifier: notification setting=DisabledForApplication (1)",
                    StringComparison.Ordinal));

        notifier.Dispose();
    }

    /// <summary>
    /// A representative content: the minimal shape the other notifier tests use.
    /// </summary>
    /// <returns>The content.</returns>
    private static ToastContent Content() => new()
    {
        Title = "Build finished",
        Body = "3 projects built in 12.4s",
        Severity = ToastSeverity.Reminder,
        Launch = "sample-toast-1",
    };

    /// <summary>
    /// Captures the structured trace events the shared source emits, so severity, event id and
    /// message can be asserted instead of inferred from formatted text - the same shape
    /// <c>ToastEventTests</c> uses, kept local because that one is private to its class.
    /// </summary>
    private sealed class TraceRecorder : TraceListener
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
