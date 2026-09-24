using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the three toast content fields that are <em>not</em> toast XML -
/// <see cref="ToastContent.Tag"/>, <see cref="ToastContent.Group"/> and
/// <see cref="ToastContent.Expiry"/> - driven entirely by <see cref="FakeToastApi"/> and
/// <see cref="ToastShow"/>, with no live shell, no notification area and no WinRT runtime in the
/// file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these claims need a recording seam.</b> Tag, group and expiry are written onto the
/// notification object rather than into the document, so a live run shows only the two ends: a
/// banner appeared, or it did not. The order of the four property steps, the identity of the
/// argument each one carried, and - the part that only shows up under stress - which handles were
/// handed back when one of them failed are all invisible from outside the process. The fake
/// supplies exactly those three observables: the recorded call log, the recorded arguments, and the
/// handle-release accounting.
/// </para>
/// <para>
/// <b>The four steps are conditional.</b> Each step runs only when its content field is present, so
/// a plain title/body toast records the measured S01 sequence byte-for-byte and the measured
/// contracts about that sequence stay valid (asserted in
/// <see cref="Show_without_tag_group_or_expiry_records_the_xml_only_sequence_unchanged"/> and, with
/// the same expectation, in <c>ToastApiContractTests</c>).
/// </para>
/// <para>
/// <b>Failure is data and failure unwinds.</b> Each of the four steps can fail at a different point
/// in the sequence, and the boxed <c>IReference&lt;DateTime&gt;</c> joined the handle set when the
/// expiry was added - so the unwinding has to release one more handle at <c>put_ExpirationTime</c>
/// than at the three steps before it. That asymmetry is asserted per step in
/// <see cref="A_failed_property_step_is_surfaced_as_that_step_and_unwinds"/>, which is the only
/// place it is observable.
/// </para>
/// <para>
/// <b>Scope.</b> The live acceptance of these same steps - a real shell agreeing to the payload for
/// a registered identity - is recorded separately in the slice's measurement run
/// (<c>docs/TOAST-MEASUREMENT.md</c>) and must never be a precondition of this suite.
/// </para>
/// </remarks>
public sealed class ToastNotificationPropertyTests
{
    /// <summary>The identity the show path binds the notifier to; no registration is involved here.</summary>
    private const string AppUserModelId = "Trustsoft.NotifyIcon.Tests.Properties";

    /// <summary>
    /// The handles the XML-only show half hands out: the manager statics, the notification factory,
    /// the XML document, the notifier and the notification.
    /// </summary>
    private const int BaseHandleCount = 5;

    /// <summary>
    /// An <c>HRESULT</c> that is neither the fake's default failure value nor a success code
    /// (<c>REGDB_E_CLASSNOTREG</c>, the realistic failure of a missing runtime class), so a test
    /// that asserts it proves the injected code flowed through instead of a constant.
    /// </summary>
    private const int InjectedHResult = unchecked((int)0x80040154);

    private const string Tag = "s02-tag";

    private const string Group = "s02-group";

    private static readonly DateTimeOffset Expiry = new(2031, 2, 3, 4, 5, 6, TimeSpan.FromHours(-5));

    /// <summary>The four notification-property steps, in the one order the show path must run them.</summary>
    private static readonly string[] NotificationPropertySteps =
    [
        nameof(IToastApi.SetNotificationTag),
        nameof(IToastApi.SetNotificationGroup),
        nameof(IToastApi.CreateDateTimePropertyValue),
        nameof(IToastApi.SetNotificationExpirationTime),
    ];

    /// <summary>
    /// The measured XML-only show half (docs/TOAST-MEASUREMENT.md, Contract 2), in order: the
    /// sequence a toast with no tag, group or expiry must still record unchanged.
    /// </summary>
    private static readonly string[] XmlOnlyShowSequence =
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
    ];

    /// <summary>A content carrying all three non-XML fields.</summary>
    private static ToastContent FullContent() => new()
    {
        Title = "S02 tag/group/expiry title",
        Body = "S02 body",
        Launch = "s02-launch",
        Tag = Tag,
        Group = Group,
        Expiry = Expiry,
    };

    /// <summary>A content carrying none of the three non-XML fields.</summary>
    private static ToastContent PlainContent() => new()
    {
        Title = "S02 plain title",
        Body = "S02 plain body",
        Launch = "s02-plain-launch",
    };

    /// <summary>
    /// A content carrying tag, group and expiry records those four steps in one exact place: after
    /// the notification exists and before the handlers are subscribed. The whole show half is
    /// asserted, not just the relative order, so a step running twice, running late or running for
    /// an absent field fails here.
    /// </summary>
    [Fact]
    public void Show_records_the_four_property_steps_in_their_exact_place_in_the_show_sequence()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, new ToastPayload(FullContent()));

        Assert.True(show.Show(AppUserModelId).Success);

        string[] expected =
        [
            nameof(IToastApi.GetToastNotificationManagerStatics),
            nameof(IToastApi.GetToastNotificationFactory),
            nameof(IToastApi.ActivateXmlDocument),
            nameof(IToastApi.CreateToastNotifier),
            nameof(IToastApi.GetNotifierSetting),
            nameof(IToastApi.LoadXml),
            nameof(IToastApi.CreateToastNotification),
            nameof(IToastApi.SetNotificationTag),
            nameof(IToastApi.SetNotificationGroup),
            nameof(IToastApi.CreateDateTimePropertyValue),
            nameof(IToastApi.SetNotificationExpirationTime),
            nameof(IToastApi.SubscribeActivated),
            nameof(IToastApi.SubscribeDismissed),
            nameof(IToastApi.SubscribeFailed),
            nameof(IToastApi.Show),
        ];

        Assert.Equal(expected, fake.Operations.ToArray());

        // Each of the four is run once, for its own field, in the order above - no step is repeated
        // for the tag and group on the way to the expiry.
        foreach (string step in NotificationPropertySteps)
        {
            int calls = fake.Operations.Count(operation => operation == step);
            Assert.Equal(1, calls);
        }

        show.Dispose();

        // The teardown adds the three unsubscribes and releases the five XML-half handles plus the
        // boxed property value - nothing else.
        int expectedOperations = expected.Length + 3 + BaseHandleCount + 1;
        int recorded = fake.Operations.Count;
        Assert.Equal(expectedOperations, recorded);
    }

    /// <summary>
    /// A toast with no tag, group or expiry records the measured XML-only sequence exactly: none of
    /// the four property steps runs and no property-value handle is ever created, which is what
    /// keeps the S01 measurement valid for a plain title/body toast.
    /// </summary>
    [Fact]
    public void Show_without_tag_group_or_expiry_records_the_xml_only_sequence_unchanged()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, new ToastPayload(PlainContent()));

        Assert.True(show.Show(AppUserModelId).Success);

        Assert.Equal(XmlOnlyShowSequence, fake.Operations.ToArray());

        foreach (string step in NotificationPropertySteps)
        {
            Assert.DoesNotContain(step, fake.Operations);
        }

        // No boxing step means no boxed handle exists to be released later.
        Assert.Equal(0, fake.CallCount(ToastOperation.CreateDateTimePropertyValue));

        show.Dispose();

        int released = fake.ReleasedHandles.Count;
        Assert.Equal(BaseHandleCount, released);
        Assert.Equal(BaseHandleCount, fake.ReleasedHandles.Distinct().Count());
        Assert.DoesNotContain(IntPtr.Zero, fake.ReleasedHandles);
    }

    /// <summary>
    /// The recorded arguments are the content's own values: the tag string, the group string and
    /// the tick value the documented conversion produced - and the boxed value handed to
    /// <c>put_ExpirationTime</c> is the one the boxing step returned. None of the three appears in
    /// the XML, which is the split that makes them notification properties in the first place.
    /// </summary>
    [Fact]
    public void Show_applies_the_contents_own_tag_group_and_expiry_arguments()
    {
        var fake = new FakeToastApi();
        ToastPayload payload = new(FullContent());
        var show = new ToastShow(fake, payload);

        Assert.True(show.Show(AppUserModelId).Success);

        Assert.Contains($"put_Tag=\"{Tag}\"", DetailOf(fake, nameof(IToastApi.SetNotificationTag)), StringComparison.Ordinal);
        Assert.Contains($"put_Group=\"{Group}\"", DetailOf(fake, nameof(IToastApi.SetNotificationGroup)), StringComparison.Ordinal);

        long expectedUniversalTime = ToastShow.ToWinRtUniversalTime(payload.Content.Expiry!.Value);
        Assert.Contains(
            $"universalTime={expectedUniversalTime}",
            DetailOf(fake, nameof(IToastApi.CreateDateTimePropertyValue)),
            StringComparison.Ordinal);

        Assert.Equal(
            HandleToken(fake, nameof(IToastApi.CreateDateTimePropertyValue)),
            HandleToken(fake, nameof(IToastApi.SetNotificationExpirationTime)));

        // The three fields are not in the document: a design that only rendered XML would have
        // dropped them while every XML assertion still passed.
        string xml = payload.ToXml();
        Assert.DoesNotContain(Tag, xml, StringComparison.Ordinal);
        Assert.DoesNotContain(Group, xml, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpirationTime", xml, StringComparison.Ordinal);

        show.Dispose();
    }

    /// <summary>
    /// Each of the four steps, failed in turn, is surfaced as that seam member's name plus the
    /// injected <c>HRESULT</c>, releases every handle acquired so far - one more at
    /// <c>put_ExpirationTime</c>, because the boxed property value joined the set - and leaves no
    /// subscription registered.
    /// </summary>
    /// <param name="operationName">The notification-property seam member scripted to fail.</param>
    /// <param name="expectedReleases">The number of handles acquired before the failing step.</param>
    [Theory]
    [InlineData(nameof(IToastApi.SetNotificationTag), 5)]
    [InlineData(nameof(IToastApi.SetNotificationGroup), 5)]
    [InlineData(nameof(IToastApi.CreateDateTimePropertyValue), 5)]
    [InlineData(nameof(IToastApi.SetNotificationExpirationTime), 6)]
    public void A_failed_property_step_is_surfaced_as_that_step_and_unwinds(string operationName, int expectedReleases)
    {
        var operation = Enum.Parse<ToastOperation>(operationName);
        var fake = new FakeToastApi { FailureHResult = InjectedHResult };
        fake.FailNext(operation);

        var show = new ToastShow(fake, new ToastPayload(FullContent()));

        ToastShowResult result = show.Show(AppUserModelId);

        Assert.False(result.Success);
        Assert.Equal(operationName, result.Operation);
        Assert.Equal(InjectedHResult, result.Code);
        Assert.False(show.IsShown);

        // The failure stops the sequence: nothing was shown and nothing was subscribed, because all
        // four steps precede the subscriptions.
        Assert.Equal(0, fake.CallCount(ToastOperation.Show));
        Assert.Empty(fake.SubscribedTokens);
        Assert.Empty(fake.UnsubscribedTokens);
        Assert.False(fake.RaiseActivated("late"));
        Assert.False(fake.RaiseDismissed(2));
        Assert.False(fake.RaiseFailed(InjectedHResult));

        // Every handle acquired before the failure is released exactly once, and none of them is
        // the null handle that ReleaseHandle ignores.
        int releases = fake.CallCount(ToastOperation.ReleaseHandle);
        Assert.Equal(expectedReleases, releases);
        Assert.Equal(expectedReleases, fake.ReleasedHandles.Distinct().Count());
        Assert.DoesNotContain(IntPtr.Zero, fake.ReleasedHandles);

        if (operation == ToastOperation.SetNotificationExpirationTime)
        {
            // The failing step was handed the boxed value, so the unwind must hand it back.
            IntPtr boxed = HandleToken(fake, nameof(IToastApi.CreateDateTimePropertyValue));
            Assert.Contains(boxed, fake.ReleasedHandles);
            Assert.Equal(1, fake.ReleasedHandles.Count(handle => handle == boxed));
        }

        // The failure is terminal: disposing afterwards invokes nothing further.
        int afterFailure = fake.Operations.Count;
        show.Dispose();
        Assert.Equal(afterFailure, fake.Operations.Count);
    }

    /// <summary>
    /// On the success path the boxed property value is live until teardown and released exactly
    /// once: nothing is released while the toast is shown, the first disposal hands back the six
    /// handles including the boxed value, and a second disposal releases nothing at all - so the
    /// object cannot leak the value or double-release it.
    /// </summary>
    [Fact]
    public void The_boxed_expiry_value_is_released_exactly_once_at_teardown()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, new ToastPayload(FullContent()));

        Assert.True(show.Show(AppUserModelId).Success);

        // A live toast still owns everything it acquired.
        Assert.Equal(0, fake.CallCount(ToastOperation.ReleaseHandle));

        IntPtr boxed = HandleToken(fake, nameof(IToastApi.CreateDateTimePropertyValue));
        Assert.NotEqual(IntPtr.Zero, boxed);
        Assert.Equal(boxed, HandleToken(fake, nameof(IToastApi.SetNotificationExpirationTime)));

        show.Dispose();

        int releases = fake.CallCount(ToastOperation.ReleaseHandle);
        Assert.Equal(BaseHandleCount + 1, releases);
        Assert.Equal(releases, fake.ReleasedHandles.Distinct().Count());
        Assert.Equal(1, fake.ReleasedHandles.Count(handle => handle == boxed));

        show.Dispose();

        Assert.Equal(releases, fake.CallCount(ToastOperation.ReleaseHandle));
        Assert.Equal(1, fake.ReleasedHandles.Count(handle => handle == boxed));
    }

    /// <summary>
    /// Each step is conditional on its own field: a content carrying only a tag, only a group or
    /// only an expiry records exactly that field's step (the expiry being two: the boxing and the
    /// assignment), and only the expiry adds a handle to the release accounting.
    /// </summary>
    /// <param name="withTag">Whether the content carries a tag.</param>
    /// <param name="withGroup">Whether the content carries a group.</param>
    /// <param name="withExpiry">Whether the content carries an expiry.</param>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Show_applies_only_the_notification_properties_the_content_carries(bool withTag, bool withGroup, bool withExpiry)
    {
        var content = new ToastContent
        {
            Title = "S02 conditional title",
            Launch = "s02-conditional-launch",
        };

        if (withTag)
        {
            content.Tag = Tag;
        }

        if (withGroup)
        {
            content.Group = Group;
        }

        if (withExpiry)
        {
            content.Expiry = Expiry;
        }

        List<string> expected = [];

        if (withTag)
        {
            expected.Add(nameof(IToastApi.SetNotificationTag));
        }

        if (withGroup)
        {
            expected.Add(nameof(IToastApi.SetNotificationGroup));
        }

        if (withExpiry)
        {
            expected.Add(nameof(IToastApi.CreateDateTimePropertyValue));
            expected.Add(nameof(IToastApi.SetNotificationExpirationTime));
        }

        var fake = new FakeToastApi();
        var show = new ToastShow(fake, new ToastPayload(content));

        Assert.True(show.Show(AppUserModelId).Success);

        Assert.Equal(
            expected,
            fake.Operations.Where(operation => NotificationPropertySteps.Contains(operation)).ToList());

        show.Dispose();

        int expectedReleases = BaseHandleCount + (withExpiry ? 1 : 0);
        int released = fake.ReleasedHandles.Count;
        Assert.Equal(expectedReleases, released);
    }

    /// <summary>
    /// An empty tag or group is treated as absent rather than written as an empty property: the
    /// show path applies a property only for a non-empty value, so an empty string cannot put the
    /// toast in a nameless group or give it a nameless tag.
    /// </summary>
    [Fact]
    public void Show_treats_an_empty_tag_or_group_as_absent()
    {
        var fake = new FakeToastApi();
        var show = new ToastShow(fake, new ToastPayload(new ToastContent
        {
            Title = "S02 empty tag and group",
            Tag = string.Empty,
            Group = string.Empty,
        }));

        Assert.True(show.Show(AppUserModelId).Success);

        foreach (string step in NotificationPropertySteps)
        {
            Assert.DoesNotContain(step, fake.Operations);
        }

        show.Dispose();

        int released = fake.ReleasedHandles.Count;
        Assert.Equal(BaseHandleCount, released);
    }

    /// <summary>
    /// The expiry conversion maps the documented 1601-01-01 WinRT epoch, keeps the instant when the
    /// <see cref="DateTimeOffset"/> carries a local offset, and round-trips a single 100-ns tick
    /// exactly.
    /// </summary>
    [Fact]
    public void ToWinRtUniversalTime_maps_the_1601_epoch_and_round_trips_one_tick()
    {
        // The 11,644,473,600 seconds between 1601-01-01 and 1970-01-01, in 100-ns units.
        Assert.Equal(116444736000000000L, ToastShow.ToWinRtUniversalTime(DateTimeOffset.UnixEpoch));

        // The conversion goes through UtcDateTime, so a plain local offset cannot shift the instant:
        // the result is the instant's UTC ticks minus the 1601-to-0001 epoch offset.
        var instant = new DateTimeOffset(2031, 2, 3, 4, 5, 6, 789, TimeSpan.FromHours(-5));
        Assert.Equal(instant.UtcDateTime.Ticks - 504_911_232_000_000_000L, ToastShow.ToWinRtUniversalTime(instant));

        // One 100-ns tick is preserved exactly: the conversion is a shift, never a rounding.
        Assert.Equal(1L, ToastShow.ToWinRtUniversalTime(instant) - ToastShow.ToWinRtUniversalTime(instant.AddTicks(-1)));
    }

    /// <summary>Returns the single recorded call's diagnostic detail for one seam operation.</summary>
    /// <param name="fake">The fake that recorded it.</param>
    /// <param name="operation">The seam member name.</param>
    /// <returns>The recorded detail string.</returns>
    private static string DetailOf(FakeToastApi fake, string operation) =>
        Assert.Single(fake.Calls.Where(call => call.Operation == operation)).Detail;

    /// <summary>
    /// Reads back the handle token the fake recorded for one handle-returning operation, so the
    /// handle a step was handed can be compared with the handles that were released.
    /// </summary>
    /// <param name="fake">The fake that recorded the call.</param>
    /// <param name="operation">The handle-returning seam member name.</param>
    /// <returns>The handle the fake handed out.</returns>
    private static IntPtr HandleToken(FakeToastApi fake, string operation)
    {
        string detail = DetailOf(fake, operation);
        int marker = detail.IndexOf("0x", StringComparison.Ordinal);
        Assert.True(marker >= 0, $"{operation}'s recorded detail carries no handle token: '{detail}'");

        string tail = detail[(marker + 2)..];
        int length = 0;
        while (length < tail.Length && Uri.IsHexDigit(tail[length]))
        {
            length++;
        }

        Assert.True(length > 0, $"{operation}'s recorded detail carries an empty handle token: '{detail}'");
        return new IntPtr(Convert.ToInt64(tail[..length], 16));
    }
}
