using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the toast show path (<see cref="ToastShow"/>) and its payload
/// (<see cref="ToastPayload"/>): the measured acquisition order, the exact XML handed to the shell,
/// the show call, the subscribe/unsubscribe sequence, the failure-as-data unwinding and the
/// idempotent disposal - all asserted against the recording <see cref="FakeToastApi"/>, never
/// against a live shell.
/// </summary>
/// <remarks>
/// <para>
/// A real shell cannot be asked to fail <c>RoGetActivationFactory</c>, to leave a subscription
/// behind, or to report what XML it was given, so these are exactly the claims the fake exists to
/// make deterministic. The complementary live proof - that the shell really accepted a toast from
/// the library's own <see cref="ToastApi"/> - is <see cref="ToastApiLiveProbeTests"/>, which is
/// deliberately a separate class so this suite stays shell-free.
/// </para>
/// </remarks>
[Collection("Toast image temp folder")]
public sealed class ToastApiContractTests
{
    /// <summary>
    /// The measured toast show sequence, in order (docs/TOAST-MEASUREMENT.md Contract 2, and
    /// <c>FakeToastApiTests.ToastShowSequence</c>): eleven show operations, then the three
    /// unsubscribes of the teardown.
    /// </summary>
    private static readonly string[] ToastShowSequence =
    [
        nameof(IToastApi.GetToastNotificationManagerStatics),
        nameof(IToastApi.GetToastNotificationFactory),
        nameof(IToastApi.ActivateXmlDocument),
        nameof(IToastApi.CreateToastNotifier),
        nameof(IToastApi.GetNotifierSetting),
        nameof(IToastApi.LoadXml),
        nameof(IToastApi.CreateToastNotification),
        nameof(IToastApi.SubscribeActivated),
        nameof(IToastApi.SubscribeDismissed),
        nameof(IToastApi.SubscribeFailed),
        nameof(IToastApi.Show),
        nameof(IToastApi.UnsubscribeActivated),
        nameof(IToastApi.UnsubscribeDismissed),
        nameof(IToastApi.UnsubscribeFailed),
    ];

    /// <summary>The five seam members that hand a handle to the caller and must therefore be released.</summary>
    private const int HandleReturningOperations = 5;

    /// <summary>The length of the show half of <see cref="ToastShowSequence"/> (the teardown follows it).</summary>
    private const int ShowOperationCount = 11;

    private const string AppUserModelId = "Vendor.Toast.App";

    private static ToastPayload Payload => new(new ToastContent
    {
        Title = "T04 title",
        Body = "T04 body",
        Launch = "t04-launch",
    });

    /// <summary>
    /// Content whose tag, group and expiry must be applied to the notification object rather than
    /// rendered into the toast XML (the three non-XML fields of D056).
    /// </summary>
    private static ToastPayload TaggedPayload => new(new ToastContent
    {
        Title = "T03 title",
        Body = "T03 body",
        Tag = "t03-tag",
        Group = "t03-group",
        Expiry = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
    });

    /// <summary>The four notification-property steps, in the one order the show path must run them.</summary>
    private static readonly string[] NotificationPropertySequence =
    [
        nameof(IToastApi.SetNotificationTag),
        nameof(IToastApi.SetNotificationGroup),
        nameof(IToastApi.CreateDateTimePropertyValue),
        nameof(IToastApi.SetNotificationExpirationTime),
    ];

    /// <summary>
    /// A successful show produces exactly the measured sequence, binds the notifier to the
    /// registered identity, hands the exact payload XML over, and tearing it down appends the three
    /// unsubscribes and the five releases - nothing more.
    /// </summary>
    [Fact]
    public void Show_produces_the_measured_sequence_and_tears_down_without_leaving_a_subscription()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.True(result.Success);
        Assert.Equal(AppUserModelId, result.AppUserModelId);
        Assert.Equal(string.Empty, result.Operation);
        Assert.Equal(0, result.Code);
        Assert.True(show.IsShown);

        // The show half is the measured sequence, exactly, in order, with nothing extra.
        Assert.Equal(ToastShowSequence[..ShowOperationCount], fake.Operations.ToArray());

        show.Dispose();

        // The teardown adds the three unsubscribes (already part of the measured sequence) and the
        // release of every handle that was handed out - five of them, one per handle-returning call.
        string[] expected =
        [
            .. ToastShowSequence,
            .. Enumerable.Repeat(nameof(IToastApi.ReleaseHandle), HandleReturningOperations),
        ];

        Assert.Equal(expected, fake.Operations.ToArray());
        Assert.False(show.IsShown);
    }

    /// <summary>
    /// The ordering traps are asserted positionally: the factories and the document are acquired
    /// first, the handlers are subscribed <em>before</em> <c>Show</c>, and every unsubscribe runs
    /// <em>before</em> the first handle is released.
    /// </summary>
    [Fact]
    public void Show_acquires_first_subscribes_before_showing_and_unsubscribes_before_releasing()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);

        int statics = IndexOf(fake, nameof(IToastApi.GetToastNotificationManagerStatics));
        int factory = IndexOf(fake, nameof(IToastApi.GetToastNotificationFactory));
        int document = IndexOf(fake, nameof(IToastApi.ActivateXmlDocument));
        int notifier = IndexOf(fake, nameof(IToastApi.CreateToastNotifier));
        int subscribeFailed = IndexOf(fake, nameof(IToastApi.SubscribeFailed));
        int showIndex = IndexOf(fake, nameof(IToastApi.Show));

        Assert.True(statics < factory, "the manager statics must be acquired before the notification factory");
        Assert.True(factory < document, "the factory must be acquired before the XML document is activated");
        Assert.True(document < notifier, "the document must be activated before the notifier is created");
        Assert.True(subscribeFailed < showIndex, "all three handlers must be subscribed before Show");
        Assert.Equal(1, fake.CallCount(ToastOperation.Show));

        show.Dispose();

        int firstRelease = IndexOf(fake, nameof(IToastApi.ReleaseHandle));

        Assert.True(IndexOf(fake, nameof(IToastApi.UnsubscribeActivated)) < firstRelease);
        Assert.True(IndexOf(fake, nameof(IToastApi.UnsubscribeDismissed)) < firstRelease);
        Assert.True(IndexOf(fake, nameof(IToastApi.UnsubscribeFailed)) < firstRelease);

        // Exactly the tokens that were handed out are handed back, in the same order.
        Assert.Equal(fake.SubscribedTokens.ToArray(), fake.UnsubscribedTokens.ToArray());
        Assert.Equal(3, fake.SubscribedTokens.Count);
        Assert.DoesNotContain(0L, fake.SubscribedTokens);
    }

    /// <summary>
    /// The notifier is bound to the caller's identity through the explicit-identity overload (an
    /// unpackaged process has no process default AppUserModelID), and the XML handed to the shell
    /// is the payload's own rendering.
    /// </summary>
    [Fact]
    public void Show_binds_the_notifier_to_the_identity_and_hands_the_payload_xml_to_the_shell()
    {
        var fake = new FakeToastApi();
        ToastPayload payload = Payload;
        var show = new ToastShow(fake, payload);

        Assert.True(show.Show(AppUserModelId).Success);

        ToastCall notifier = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.CreateToastNotifier)));
        Assert.Contains(AppUserModelId, notifier.Detail, StringComparison.Ordinal);

        ToastCall loadXml = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.LoadXml)));
        Assert.Contains(payload.ToXml(), loadXml.Detail, StringComparison.Ordinal);

        // Exactly one LoadXml: the payload is handed over once, not re-loaded on a retry.
        Assert.Equal(1, fake.CallCount(ToastOperation.LoadXml));
    }

    /// <summary>
    /// Disposal is idempotent: the first call unsubscribes the three handlers and releases the five
    /// handles, every later call invokes no seam member again, and no handler is left subscribed.
    /// </summary>
    [Fact]
    public void Dispose_is_idempotent_and_unsubscribes_and_releases_exactly_once()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);

        int afterShow = fake.Operations.Count;

        show.Dispose();

        int afterFirstDispose = fake.Operations.Count;

        Assert.Equal(afterShow + 3 + HandleReturningOperations, afterFirstDispose);

        show.Dispose();
        show.Dispose();

        Assert.Equal(afterFirstDispose, fake.Operations.Count);
        Assert.Equal(HandleReturningOperations, fake.CallCount(ToastOperation.ReleaseHandle));
        Assert.Equal(3, fake.UnsubscribedTokens.Count);

        // Nothing is left subscribed: a late activation is not delivered to a disposed show.
        Assert.False(fake.RaiseActivated("after-dispose"));
        Assert.False(fake.RaiseDismissed(2));
        Assert.False(fake.RaiseFailed(unchecked((int)0x80004005)));
    }

    /// <summary>
    /// Every handle handed out is released exactly once, and none of them is the null handle: the
    /// disposal contract asserted on the release accounting rather than on the release count alone.
    /// </summary>
    [Fact]
    public void Dispose_releases_every_handle_that_was_handed_out_exactly_once()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);
        show.Dispose();

        Assert.Equal(HandleReturningOperations, fake.ReleasedHandles.Count);
        Assert.Equal(HandleReturningOperations, fake.ReleasedHandles.Distinct().Count());
        Assert.DoesNotContain(IntPtr.Zero, fake.ReleasedHandles);
    }

    /// <summary>
    /// Disposing a show that never ran touches nothing: there is no handle to release and no
    /// subscription to remove, so the seam is not invoked at all.
    /// </summary>
    [Fact]
    public void Dispose_without_showing_invokes_no_seam_member()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        show.Dispose();
        show.Dispose();

        Assert.Empty(fake.Operations);
        Assert.False(show.IsShown);
    }

    /// <summary>
    /// The callback path is proven in process without a click on a real banner: the fake raises the
    /// events the show path subscribed, and what the caller wired receives it.
    /// </summary>
    [Fact]
    public void Subscribed_handlers_receive_activation_dismissal_and_failure()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        string? activatedArguments = null;
        int dismissedReason = -1;
        int failedErrorCode = -1;

        show.Activated = arguments => activatedArguments = arguments;
        show.Dismissed = reason => dismissedReason = reason;
        show.Failed = errorCode => failedErrorCode = errorCode;

        Assert.True(show.Show(AppUserModelId).Success);

        Assert.True(fake.RaiseActivated("t04-launch"));
        Assert.True(fake.RaiseDismissed(2));
        Assert.True(fake.RaiseFailed(unchecked((int)0x80004005)));

        Assert.Equal("t04-launch", activatedArguments);
        Assert.Equal(2, dismissedReason);
        Assert.Equal(unchecked((int)0x80004005), failedErrorCode);

        show.Dispose();
    }

    /// <summary>
    /// Every failing step is reported as data naming the seam member plus its <c>HRESULT</c>, and
    /// the failure unwinds everything acquired so far: the handles are released, the handlers that
    /// were already registered are unsubscribed, and the failure is terminal (a later dispose
    /// invokes nothing).
    /// </summary>
    /// <param name="operationName">The name of the seam member scripted to fail (equal to its <see cref="ToastOperation"/> name).</param>
    /// <param name="expectedReleases">The number of handles acquired before the failing step.</param>
    /// <param name="expectedUnsubscribes">The number of handlers registered before the failing step.</param>
    [Theory]
    [InlineData(nameof(IToastApi.GetToastNotificationManagerStatics), 0, 0)]
    [InlineData(nameof(IToastApi.GetToastNotificationFactory), 1, 0)]
    [InlineData(nameof(IToastApi.ActivateXmlDocument), 2, 0)]
    [InlineData(nameof(IToastApi.CreateToastNotifier), 3, 0)]
    [InlineData(nameof(IToastApi.GetNotifierSetting), 4, 0)]
    [InlineData(nameof(IToastApi.LoadXml), 4, 0)]
    [InlineData(nameof(IToastApi.CreateToastNotification), 4, 0)]
    [InlineData(nameof(IToastApi.SubscribeActivated), 5, 0)]
    [InlineData(nameof(IToastApi.SubscribeDismissed), 5, 1)]
    [InlineData(nameof(IToastApi.SubscribeFailed), 5, 2)]
    [InlineData(nameof(IToastApi.Show), 5, 3)]
    public void A_failing_step_is_surfaced_as_the_operation_plus_hresult_and_unwinds(
        string operationName,
        int expectedReleases,
        int expectedUnsubscribes)
    {
        var operation = Enum.Parse<ToastOperation>(operationName);
        var fake = new FakeToastApi();
        fake.FailNext(operation);

        var show = new ToastShow(fake, Payload);

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.False(result.Success);
        Assert.Equal(operationName, result.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, result.Code);
        Assert.False(show.IsShown);

        // Everything acquired before the failure was released, exactly once each.
        Assert.Equal(expectedReleases, fake.CallCount(ToastOperation.ReleaseHandle));
        Assert.Equal(expectedReleases, fake.ReleasedHandles.Distinct().Count());

        // Everything registered before the failure was unsubscribed.
        Assert.Equal(expectedUnsubscribes, fake.UnsubscribedTokens.Count);

        // Nothing is left subscribed, so a late activation is not delivered.
        Assert.False(fake.RaiseActivated("late"));

        // The failure is terminal: disposing afterwards invokes nothing further.
        int afterFailure = fake.Operations.Count;
        show.Dispose();
        Assert.Equal(afterFailure, fake.Operations.Count);
    }

    /// <summary>
    /// Tag, group and expiry are ToastNotification properties, not toast XML: the show path applies
    /// them, in one fixed order, after the notification exists and before the event subscribes. The
    /// boxed expiry property value is tracked and released exactly once, with the other handles.
    /// </summary>
    [Fact]
    public void Show_applies_tag_group_and_expiry_as_notification_properties_in_order()
    {
        var fake = new FakeToastApi();
        ToastPayload payload = TaggedPayload;
        var show = new ToastShow(fake, payload);

        Assert.True(show.Show(AppUserModelId).Success);

        // The four steps run after CreateToastNotification and before SubscribeActivated.
        int created = IndexOf(fake, nameof(IToastApi.CreateToastNotification));
        int tag = IndexOf(fake, nameof(IToastApi.SetNotificationTag));
        int group = IndexOf(fake, nameof(IToastApi.SetNotificationGroup));
        int propertyValue = IndexOf(fake, nameof(IToastApi.CreateDateTimePropertyValue));
        int expiry = IndexOf(fake, nameof(IToastApi.SetNotificationExpirationTime));
        int subscribe = IndexOf(fake, nameof(IToastApi.SubscribeActivated));

        Assert.True(created < tag, "the notification must exist before its tag is set");
        Assert.True(tag < group, "put_Tag must precede put_Group");
        Assert.True(group < propertyValue, "the group must be set before the expiry is boxed");
        Assert.True(propertyValue < expiry, "the boxed property value must exist before put_ExpirationTime");
        Assert.True(expiry < subscribe, "the notification properties must be applied before the event subscribes");

        // The recorded arguments are the content's own values, converted as documented.
        Assert.Contains("t03-tag", DetailOf(fake, nameof(IToastApi.SetNotificationTag)), StringComparison.Ordinal);
        Assert.Contains("t03-group", DetailOf(fake, nameof(IToastApi.SetNotificationGroup)), StringComparison.Ordinal);

        long expectedUniversalTime = ToastShow.ToWinRtUniversalTime(payload.Content.Expiry!.Value);
        Assert.Contains(expectedUniversalTime.ToString(), DetailOf(fake, nameof(IToastApi.CreateDateTimePropertyValue)), StringComparison.Ordinal);

        show.Dispose();

        // Five handles for the show sequence plus the one boxed property value; each released once.
        Assert.Equal(6, fake.ReleasedHandles.Count);
        Assert.Equal(6, fake.ReleasedHandles.Distinct().Count());
        Assert.DoesNotContain(IntPtr.Zero, fake.ReleasedHandles);
    }

    /// <summary>
    /// Content with no tag, group or expiry records none of the four notification-property steps:
    /// the XML-only path is unchanged, which is what keeps the measured plain-toast sequence valid.
    /// </summary>
    [Fact]
    public void Show_without_tag_group_or_expiry_skips_the_notification_property_steps()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);

        foreach (string operation in NotificationPropertySequence)
        {
            Assert.DoesNotContain(operation, fake.Operations);
        }

        show.Dispose();

        Assert.Equal(HandleReturningOperations, fake.ReleasedHandles.Count);
    }

    /// <summary>
    /// A failure at any notification-property step is surfaced as that seam member's name plus the
    /// injected <c>HRESULT</c> and unwinds every handle acquired so far - including the boxed
    /// property value when the failure is at <c>put_ExpirationTime</c> - leaving no subscription.
    /// </summary>
    /// <param name="operationName">The notification-property seam member scripted to fail.</param>
    /// <param name="expectedReleases">The number of handles acquired before the failing step.</param>
    [Theory]
    [InlineData(nameof(IToastApi.SetNotificationTag), 5)]
    [InlineData(nameof(IToastApi.SetNotificationGroup), 5)]
    [InlineData(nameof(IToastApi.CreateDateTimePropertyValue), 5)]
    [InlineData(nameof(IToastApi.SetNotificationExpirationTime), 6)]
    public void A_failing_notification_property_step_is_surfaced_and_unwinds(string operationName, int expectedReleases)
    {
        var fake = new FakeToastApi();
        fake.FailNext(Enum.Parse<ToastOperation>(operationName));

        var show = new ToastShow(fake, TaggedPayload);

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.False(result.Success);
        Assert.Equal(operationName, result.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, result.Code);
        Assert.False(show.IsShown);

        // Every handle acquired before the failure was released, exactly once each, and no handler
        // was ever subscribed (the failure precedes the subscriptions).
        Assert.Equal(expectedReleases, fake.CallCount(ToastOperation.ReleaseHandle));
        Assert.Equal(expectedReleases, fake.ReleasedHandles.Distinct().Count());
        Assert.Empty(fake.UnsubscribedTokens);
        Assert.False(fake.RaiseActivated("late"));

        // The failure is terminal: disposing afterwards invokes nothing further.
        int afterFailure = fake.Operations.Count;
        show.Dispose();
        Assert.Equal(afterFailure, fake.Operations.Count);
    }

    /// <summary>
    /// The expiry conversion maps the 1601-01-01 WinRT epoch and preserves a single 100-ns tick:
    /// 1970-01-01T00:00:00Z is 116,444,736,000,000,000.
    /// </summary>
    [Fact]
    public void ToWinRtUniversalTime_maps_the_1601_epoch_and_round_trips_a_tick()
    {
        Assert.Equal(116444736000000000L, ToastShow.ToWinRtUniversalTime(DateTimeOffset.UnixEpoch));

        // A non-UTC offset must not shift the instant: the conversion goes through UtcDateTime.
        var instant = new DateTimeOffset(2026, 9, 23, 12, 34, 56, 789, TimeSpan.FromHours(2));
        Assert.Equal(instant.UtcDateTime.Ticks - 504_911_232_000_000_000L, ToastShow.ToWinRtUniversalTime(instant));

        Assert.Equal(1L, ToastShow.ToWinRtUniversalTime(instant) - ToastShow.ToWinRtUniversalTime(instant.AddTicks(-1)));
    }

    /// <summary>
    /// <c>GetSetting</c> returning <c>E_NOT_FOUND</c> on first use is measured as benign: the show
    /// must continue (the identity has no notification-setting entry yet).
    /// </summary>
    [Fact]
    public void The_first_use_not_found_setting_result_is_benign()
    {
        var fake = new FakeToastApi { FailureHResult = ToastShow.ErrorSettingNotFound };
        fake.FailNext(ToastOperation.GetNotifierSetting);

        var show = new ToastShow(fake, Payload);

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.True(result.Success);
        Assert.Equal(0, result.NotificationSetting);
        Assert.Equal(1, fake.CallCount(ToastOperation.Show));

        show.Dispose();
    }

    /// <summary>
    /// A notifier whose setting says notifications are disabled for the app still reports the
    /// setting and still shows: interpreting the setting (and refusing) is S04's policy, not the
    /// show path's.
    /// </summary>
    [Fact]
    public void A_disabled_setting_is_reported_without_failing_the_show()
    {
        var fake = new FakeToastApi { NotifierSettingToReport = 2 };
        var show = new ToastShow(fake, Payload);

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.True(result.Success);
        Assert.Equal(2, result.NotificationSetting);
        Assert.Equal(2, show.NotificationSetting);

        show.Dispose();
    }

    /// <summary>
    /// A missing or empty identity is refused as data before anything is acquired - no factory is
    /// touched and no shortcut is written.
    /// </summary>
    /// <param name="appUserModelId">The identity to hand the show path.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Show_refuses_a_missing_identity_as_data(string? appUserModelId)
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        ToastShowResult result = show.Show(appUserModelId);

        Assert.False(result.Success);
        Assert.Equal(ToastShow.OperationInvalidArgument, result.Operation);
        Assert.Equal(ToastShow.ErrorInvalidArgument, result.Code);
        Assert.Empty(fake.Operations);
    }

    /// <summary>
    /// One show per instance: a second call is refused as data instead of reusing a notification
    /// that is already subscribed and shown.
    /// </summary>
    [Fact]
    public void A_second_show_on_the_same_instance_is_refused_as_data()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);

        int afterFirstShow = fake.Operations.Count;
        ToastShowResult second = show.Show(AppUserModelId);

        Assert.False(second.Success);
        Assert.Equal(ToastShow.OperationAlreadyShown, second.Operation);
        Assert.Equal(ToastShow.ErrorAlreadyShown, second.Code);
        Assert.Equal(afterFirstShow, fake.Operations.Count);

        show.Dispose();
    }

    /// <summary>Showing after disposal is refused as data, and the disposed instance's seam is untouched.</summary>
    [Fact]
    public void Show_after_dispose_is_refused_as_data()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, Payload);

        Assert.True(show.Show(AppUserModelId).Success);
        show.Dispose();

        int afterDispose = fake.Operations.Count;
        ToastShowResult result = show.Show(AppUserModelId);

        Assert.False(result.Success);
        Assert.Equal(ToastShow.OperationAlreadyShown, result.Operation);
        Assert.Equal(afterDispose, fake.Operations.Count);
    }

    /// <summary>The show path refuses a null seam or payload at construction rather than at show time.</summary>
    [Fact]
    public void Constructor_refuses_a_null_seam_or_payload()
    {
        Assert.Throws<ArgumentNullException>(() => new ToastShow(null!, Payload));
        Assert.Throws<ArgumentNullException>(() => new ToastShow(new FakeToastApi(), null!));
    }

    /// <summary>
    /// A payload that carries a resolved temp file makes the show delete that file on its single
    /// unwind path - after the three unsubscribes and after the last handle release - and the
    /// deletion invokes no seam member, so the measured eleven-plus-three sequence and the five
    /// releases are exactly what they are for a payload without a file.
    /// </summary>
    [Fact]
    public void A_payload_carrying_a_temp_file_deletes_it_on_the_unwind_path_after_the_last_release()
    {
        string path = CreateTempImageFile();

        try
        {
            var fake = new FakeToastApi();
            var show = new ToastShow(fake, PayloadWithImageFile(path));

            Assert.True(show.Show(AppUserModelId).Success);
            Assert.True(File.Exists(path), "the file must still exist while the show is live");

            show.Dispose();

            Assert.False(File.Exists(path), "the unwind must delete the temp file the payload carried");

            string[] expected =
            [
                .. ToastShowSequence,
                .. Enumerable.Repeat(nameof(IToastApi.ReleaseHandle), HandleReturningOperations),
            ];

            Assert.Equal(expected, fake.Operations.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A failed show deletes the temp file it carried in the same unwind path a disposal uses, and a
    /// later disposal of the already-unwound show attempts no second delete: the file is cleared from
    /// the show's ownership before the delete runs, so a file that reappears at the same path
    /// afterwards survives.
    /// </summary>
    [Fact]
    public void A_failed_show_deletes_the_temp_file_once_in_the_same_unwind_path()
    {
        string path = CreateTempImageFile();

        try
        {
            var fake = new FakeToastApi();
            fake.FailNext(ToastOperation.LoadXml);

            var show = new ToastShow(fake, PayloadWithImageFile(path));

            ToastShowResult result = show.Show(AppUserModelId);

            Assert.False(result.Success);
            Assert.Equal(nameof(IToastApi.LoadXml), result.Operation);
            Assert.False(File.Exists(path), "a failed show must delete the temp file it carried");

            // The show's ownership of the file ended with the unwind. A file that reappears at the
            // same path therefore survives the disposal that follows the failure - which is what
            // proves the deletion is attempted exactly once rather than once per unwind.
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

            show.Dispose();

            Assert.True(File.Exists(path), "disposing an already-unwound show must not delete a second time");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Creates a throwaway file in the library's fixed temp folder, with the library's own name
    /// shape, so the unwind path under test deletes a real file it could have written.
    /// </summary>
    /// <returns>The absolute path of the created file.</returns>
    private static string CreateTempImageFile()
    {
        Directory.CreateDirectory(ToastImageFile.DefaultFolder);

        string path = Path.Combine(ToastImageFile.DefaultFolder, "toast-" + Guid.NewGuid().ToString("N") + ".png");

        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

        return path;
    }

    /// <summary>
    /// Builds the payload of <see cref="Payload"/> with a resolved temp file attached - the shape the
    /// notifier hands the show when the content carried a <see cref="ToastImage.Source"/>, except that
    /// the owner is adopted from the path rather than handed in: either way the payload carries a
    /// <see cref="ToastImageFile"/>, and the show deletes through it.
    /// </summary>
    /// <param name="path">The file the payload's resolved reference names.</param>
    /// <returns>The payload.</returns>
    private static ToastPayload PayloadWithImageFile(string path) =>
        new(Payload.Content, new Uri(path, UriKind.Absolute).AbsoluteUri, path);

    // The payload's own exact-XML contract moved out of this class with the content model: it now
    // lives in ToastPayloadContractTests, one exact-string test per content shape, because this
    // suite's subject is the show sequence over the seam rather than the document the builder emits.

    private static int IndexOf(FakeToastApi fake, string operation) =>
        fake.Operations.ToList().IndexOf(operation);

    /// <summary>Returns the single recorded call's diagnostic detail for one seam operation.</summary>
    /// <param name="fake">The fake that recorded it.</param>
    /// <param name="operation">The seam member name.</param>
    /// <returns>The recorded detail string.</returns>
    private static string DetailOf(FakeToastApi fake, string operation) =>
        Assert.Single(fake.Calls.Where(call => call.Operation == operation)).Detail;
}

/// <summary>
/// The live re-measurement for T04: it runs the <b>library's own</b> <see cref="ToastApi"/> and
/// <see cref="ToastShow"/> against the real machine, for an identity registered through the
/// library's own <see cref="ToastIdentity"/>, and captures what the shell did with the HRESULT of
/// every step. Its raw evidence is appended to <c>docs/TOAST-MEASUREMENT.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot be a fake.</b> A mocked <c>S_OK</c> proves the sequence, not that Windows
/// accepted the toast: the fake's <c>Show</c> would return success no matter what the shell thinks
/// of the payload or the identity. Only a real run proves the activation factories, the vtables,
/// the HSTRING marshalling and the WinRT event delegates work against the actual runtime.
/// </para>
/// <para>
/// <b>Delivery is observed out of process.</b> This test can prove the shell accepted the toast; it
/// cannot prove the banner existed, because that is a fact about the Action Center, not about this
/// process. The companion observation is the probe's inventory mode
/// (<c>scripts/probe-toast --history &lt;aumid&gt;</c>), run as a separate process against the same
/// identity, and the raw pair is recorded in <c>docs/TOAST-MEASUREMENT.md</c>.
/// </para>
/// <para>
/// <b>No click is injected, deliberately.</b> The M002/S01 measurement established that no
/// automated instrument reaches the toast banner on this machine (a UIAutomation scan found
/// nothing), so a body click remains the human demonstration of T05. This test therefore asserts
/// that the callback did <em>not</em> fire - the honest reading - rather than waiting for a click
/// that cannot arrive.
/// </para>
/// <para>
/// <b>Non-parallel, for the same reason the click tests are.</b> Every probe raises the
/// process-wide trace level to Verbose to capture its raw HRESULT lines, and restores it from the
/// value it read at entry - so two probes running at once could restore Verbose over each other
/// and leave the whole run's source level raised, which is exactly what
/// <c>NotifyIconTraceTests.Source_is_named_and_defaults_to_warning_level</c> asserts against.
/// Joining the trace collection also matches the convention <c>BalloonCallbackTests</c> records.
/// </para>
/// </remarks>
[Collection(TraceChannelCollection.Name)]
public sealed class ToastApiLiveProbeTests
{
    /// <summary>A throwaway identity for the probe; never a real application's id.</summary>
    private const string ProbeAppUserModelId = "Trustsoft.NotifyIcon.T04.LiveProbe";

    /// <summary>A second throwaway identity, used by the S02 tag/group/expiry probe.</summary>
    private const string PropertyProbeAppUserModelId = "Trustsoft.NotifyIcon.S02.LiveProbe";

    /// <summary>
    /// Registers a throwaway identity, hands a real toast to the shell through the library's own
    /// show path, captures every trace line the library emitted (factory HRESULTs and the teardown),
    /// then removes the identity.
    /// </summary>
    [StaFact]
    public void The_show_path_hands_a_real_toast_to_the_shell_for_a_registered_identity()
    {
        var capturedTrace = new List<string>();
        var listener = new CapturingTraceListener(capturedTrace);
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        var identity = new ToastIdentity(new ShortcutLink());
        string shortcutPath = ToastIdentity.DefaultShortcutPath(ProbeAppUserModelId);

        source.Listeners.Add(listener);
        source.Switch.Level = SourceLevels.Verbose;

        try
        {
            Console.WriteLine($"[live] t04: machine={Environment.MachineName}; os={Environment.OSVersion.VersionString}; shortcut={shortcutPath}");

            ToastIdentityResult register = identity.Register(ProbeAppUserModelId, shortcutPath);
            Console.WriteLine($"[live] t04: register aumid='{ProbeAppUserModelId}' success={register.Success} operation='{register.Operation}' code=0x{register.Code:X8}");
            Assert.True(register.Success, $"registration failed: {register.Operation} 0x{register.Code:X8}");

            var payload = new ToastPayload(new ToastContent
            {
                Title = "Trustsoft.NotifyIcon T04 live probe",
                Body = "The library's own ToastApi + ToastShow handed this to the shell.",
                Launch = "t04-live-activation",
            });

            var show = new ToastShow(new ToastApi(), payload);

            string? activatedArguments = null;
            int dismissedReason = -1;
            int failedErrorCode = -1;

            show.Activated = arguments => activatedArguments = arguments;
            show.Dismissed = reason => dismissedReason = reason;
            show.Failed = errorCode => failedErrorCode = errorCode;

            ToastShowResult result = show.Show(ProbeAppUserModelId);
            bool shown = show.IsShown;

            show.Dispose();

            Console.WriteLine($"[live] t04: show success={result.Success} operation='{result.Operation}' code=0x{result.Code:X8} setting={result.NotificationSetting} aumid='{result.AppUserModelId}'");
            Console.WriteLine($"[live] t04: after dispose shown={shown} (unsubscribed and released; no click was injected, so activated={activatedArguments is not null} dismissed={dismissedReason} failed=0x{failedErrorCode:X8})");

            foreach (string line in capturedTrace)
            {
                Console.WriteLine($"[live] t04 trace: {line}");
            }

            Assert.True(result.Success, $"the show path failed at {result.Operation} 0x{result.Code:X8}");
            Assert.Equal(ProbeAppUserModelId, result.AppUserModelId);
            Assert.Equal(0, result.NotificationSetting);
            Assert.True(shown, "a successful Show must leave the notification live until disposal");

            // No click reaches the banner from an automated run (measured), so no activation may
            // have been delivered - and the callback must not have been invoked by anything else.
            Assert.Null(activatedArguments);
            Assert.Equal(-1, dismissedReason);
            Assert.Equal(-1, failedErrorCode);
            Assert.False(show.IsShown);

            // The factory acquisitions must have been traced, so a later failure can be attributed
            // to the identity or to the payload rather than guessed at.
            Assert.Contains(capturedTrace, line => line.Contains("RoGetActivationFactory", StringComparison.Ordinal));
            Assert.Contains(capturedTrace, line => line.Contains("RoActivateInstance", StringComparison.Ordinal));
            Assert.Contains(capturedTrace, line => line.Contains("CreateToastNotifierWithId", StringComparison.Ordinal));
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(listener);
            listener.Dispose();

            ToastIdentityResult remove = identity.Remove(shortcutPath);
            Console.WriteLine($"[live] t04: remove success={remove.Success} operation='{remove.Operation}' code=0x{remove.Code:X8}");
        }
    }

    /// <summary>
    /// The live re-measurement of the three fields that are not toast XML: S02's tag, group and
    /// expiry are applied through the notification object's own properties rather than rendered
    /// into the document, and this run records the shell's own HRESULT for each of the four steps
    /// (<c>put_Tag</c>, <c>put_Group</c>, <c>PropertyValue.CreateDateTime</c>,
    /// <c>put_ExpirationTime</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A string-only payload passes every XML assertion while silently dropping all three fields,
    /// and a mocked <c>S_OK</c> proves only what the library intended to call - so the evidence
    /// that the shell accepts an HSTRING tag, an HSTRING group and a boxed
    /// <c>IReference&lt;DateTime&gt;</c> is this run and nothing else. S02's sample does not carry
    /// these fields (it builds title, body, launch and severity), which is exactly why the probe
    /// exists: the four steps are conditional, so they can only be measured over content that sets
    /// them. The raw lines are quoted in <c>docs/TOAST-MEASUREMENT.md</c> (S02 section).
    /// </para>
    /// </remarks>
    [StaFact]
    public void The_show_path_applies_tag_group_and_expiry_to_the_real_notification_object()
    {
        var capturedTrace = new List<string>();
        var listener = new CapturingTraceListener(capturedTrace);
        TraceSource source = NotifyIconTrace.Source;
        SourceLevels previousLevel = source.Switch.Level;

        var identity = new ToastIdentity(new ShortcutLink());
        string shortcutPath = ToastIdentity.DefaultShortcutPath(PropertyProbeAppUserModelId);
        var expiry = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.FromHours(-5));
        long expectedUniversalTime = ToastShow.ToWinRtUniversalTime(expiry);

        source.Listeners.Add(listener);
        source.Switch.Level = SourceLevels.Verbose;

        try
        {
            Console.WriteLine($"[live] s02: machine={Environment.MachineName}; os={Environment.OSVersion.VersionString}; shortcut={shortcutPath}");

            ToastIdentityResult register = identity.Register(PropertyProbeAppUserModelId, shortcutPath);
            Console.WriteLine($"[live] s02: register aumid='{PropertyProbeAppUserModelId}' success={register.Success} operation='{register.Operation}' code=0x{register.Code:X8}");
            Assert.True(register.Success, $"registration failed: {register.Operation} 0x{register.Code:X8}");

            var payload = new ToastPayload(new ToastContent
            {
                Title = "Trustsoft.NotifyIcon S02 live probe",
                Body = "Tag, group and expiry are notification-object properties, not toast XML.",
                Launch = "s02-live-activation",
                Tag = "s02-live-tag",
                Group = "s02-live-group",
                Expiry = expiry,
            });

            var show = new ToastShow(new ToastApi(), payload);

            ToastShowResult result = show.Show(PropertyProbeAppUserModelId);
            bool shown = show.IsShown;

            show.Dispose();

            Console.WriteLine($"[live] s02: show success={result.Success} operation='{result.Operation}' code=0x{result.Code:X8} setting={result.NotificationSetting} aumid='{result.AppUserModelId}'");
            Console.WriteLine($"[live] s02: expiry='{expiry:O}' expectedUniversalTime={expectedUniversalTime}");

            foreach (string line in capturedTrace)
            {
                Console.WriteLine($"[live] s02 trace: {line}");
            }

            Assert.True(result.Success, $"the show path failed at {result.Operation} 0x{result.Code:X8}");
            Assert.True(shown, "a successful Show must leave the notification live until disposal");

            // Each of the four non-XML steps really ran against the real notification object and
            // the shell accepted it: the step's own trace line, carrying its HRESULT as S_OK.
            Assert.Contains(capturedTrace, line =>
                line.Contains("put_Tag('s02-live-tag')", StringComparison.Ordinal)
                && line.Contains("hr=0x00000000", StringComparison.Ordinal));
            Assert.Contains(capturedTrace, line =>
                line.Contains("put_Group('s02-live-group')", StringComparison.Ordinal)
                && line.Contains("hr=0x00000000", StringComparison.Ordinal));
            Assert.Contains(capturedTrace, line =>
                line.Contains("CreateDateTime(universalTime=" + expectedUniversalTime + ")", StringComparison.Ordinal)
                && line.Contains("hr=0x00000000", StringComparison.Ordinal));
            Assert.Contains(capturedTrace, line =>
                line.Contains("SetNotificationExpirationTime(put_ExpirationTime)", StringComparison.Ordinal)
                && line.Contains("hr=0x00000000", StringComparison.Ordinal));
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(listener);
            listener.Dispose();

            ToastIdentityResult remove = identity.Remove(shortcutPath);
            Console.WriteLine($"[live] s02: remove success={remove.Success} operation='{remove.Operation}' code=0x{remove.Code:X8}");
        }
    }

    /// <summary>
    /// Collects the library's own trace lines so the live run's raw evidence (one line per factory
    /// acquisition and per show step) can be printed and appended to the measurement record.
    /// </summary>
    private sealed class CapturingTraceListener : TraceListener
    {
        private readonly List<string> _lines;

        internal CapturingTraceListener(List<string> lines) => _lines = lines;

        public override void Write(string? message)
        {
            // The library traces whole lines through WriteLine only; partial writes carry nothing.
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                _lines.Add(message);
            }
        }
    }
}
