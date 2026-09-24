using System.Reflection;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for the toast seam (<see cref="IToastApi"/>). They prove the two things T02 is
/// responsible for: (1) the interface is shaped the way the M002/S01 live measurement demands - the
/// measured call sequence is exposed as seam members, failures are returned as data, and no WinRT
/// type appears in a signature - and (2) that sequence, its failures and its callbacks are all
/// observable through <see cref="FakeToastApi"/> without a live shell, a notification area or any
/// WinRT runtime.
/// </summary>
public sealed class FakeToastApiTests
{
    /// <summary>The measured identity write sequence, in order (docs/TOAST-MEASUREMENT.md).</summary>
    private static readonly string[] IdentityWriteSequence =
    [
        nameof(IToastApi.CreateShellLink),
        nameof(IToastApi.ConfigureShortcut),
        nameof(IToastApi.GetShortcutPropertyStore),
        nameof(IToastApi.GetShortcutPersistFile),
        nameof(IToastApi.SetAppUserModelId),
        nameof(IToastApi.CommitPropertyStore),
        nameof(IToastApi.SaveShortcut),
    ];

    /// <summary>The measured toast show sequence, in order (docs/TOAST-MEASUREMENT.md).</summary>
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

    [Fact]
    public void SeamSurface_ExposesNoWinRTType()
    {
        // "No WinRT type may appear in the seam's own signatures" - asserted structurally rather
        // than by review, so a later edit that leaks Windows.UI.Notifications onto the seam fails
        // here instead of in a consumer's compiler.
        var seamTypes = new List<Type> { typeof(IToastApi), typeof(ToastActivatedHandler), typeof(ToastDismissedHandler), typeof(ToastFailedHandler) };

        foreach (Type seamType in seamTypes)
        {
            foreach (MethodInfo method in seamType.GetMethods())
            {
                AssertNotWinRt(method, method.ReturnType);

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertNotWinRt(method, parameter.ParameterType);
                }
            }
        }
    }

    [Fact]
    public void IdentityWritePath_IsObservableAndCommitsBeforeSaving()
    {
        var fake = new FakeToastApi();

        Assert.Equal(0, fake.CreateShellLink(out IntPtr shellLink));
        Assert.Equal(0, fake.ConfigureShortcut(shellLink, "app.exe", "App", "app.exe", string.Empty));
        Assert.Equal(0, fake.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore));
        Assert.Equal(0, fake.GetShortcutPersistFile(shellLink, out IntPtr persistFile));
        Assert.Equal(0, fake.SetAppUserModelId(propertyStore, "Vendor.App"));
        Assert.Equal(0, fake.CommitPropertyStore(propertyStore));
        Assert.Equal(0, fake.SaveShortcut(persistFile, "App.lnk"));

        Assert.Equal(IdentityWriteSequence, fake.Operations.ToArray());

        // The measured trap, asserted directly: the property is committed before the shortcut is
        // saved. Saving first would serialize a property store without the id in it.
        Assert.True(
            IndexOf(fake, nameof(IToastApi.CommitPropertyStore)) < IndexOf(fake, nameof(IToastApi.SaveShortcut)),
            "the AppUserModelID must be committed before the shortcut is saved");
    }

    [Fact]
    public void IdentityReadBack_ReportsTheValueTheShortcutCarries()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = "Vendor.App" };

        Assert.Equal(0, fake.OpenShellLink("App.lnk", out IntPtr shellLink));
        Assert.Equal(0, fake.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore));
        Assert.Equal(0, fake.GetAppUserModelId(propertyStore, out string? appUserModelId));

        Assert.Equal("Vendor.App", appUserModelId);
    }

    [Fact]
    public void IdentityReadBack_MissingProperty_IsObservableAsNull()
    {
        // A shortcut that exists but carries no AppUserModelID is the exact silent failure the
        // read-back exists to catch: S_OK with no value, which the caller must treat as a failure.
        var fake = new FakeToastApi { AppUserModelIdToReadBack = null };

        Assert.Equal(0, fake.OpenShellLink("App.lnk", out IntPtr shellLink));
        Assert.Equal(0, fake.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore));
        Assert.Equal(0, fake.GetAppUserModelId(propertyStore, out string? appUserModelId));

        Assert.Null(appUserModelId);
    }

    [Fact]
    public void ScriptedFailure_IsReturnedAsDataAndStillRecorded()
    {
        var fake = new FakeToastApi();
        fake.FailNext(ToastOperation.CommitPropertyStore);

        Assert.Equal(0, fake.CreateShellLink(out IntPtr shellLink));
        Assert.Equal(0, fake.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore));
        Assert.Equal(FakeToastApi.DefaultFailureHResult, fake.CommitPropertyStore(propertyStore));

        ToastCall commit = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.CommitPropertyStore)));
        Assert.Equal(FakeToastApi.DefaultFailureHResult, commit.HResult);

        // The failure is data, not an exception: the seam returned it and the caller decides. The
        // operation plus the code is exactly the tuple the library's failure channel reports.
        Assert.True(commit.HResult < 0);
    }

    [Fact]
    public void ToastShowPath_IsObservableInTheMeasuredOrder()
    {
        var fake = new FakeToastApi();
        string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>Hello</text></binding></visual></toast>";

        Assert.Equal(0, fake.GetToastNotificationManagerStatics(out IntPtr statics));
        Assert.Equal(0, fake.GetToastNotificationFactory(out IntPtr factory));
        Assert.Equal(0, fake.ActivateXmlDocument(out IntPtr document));
        Assert.Equal(0, fake.CreateToastNotifier(statics, "Vendor.App", out IntPtr notifier));
        Assert.Equal(0, fake.GetNotifierSetting(notifier, out _));
        Assert.Equal(0, fake.LoadXml(document, xml));
        Assert.Equal(0, fake.CreateToastNotification(factory, document, out IntPtr notification));
        Assert.Equal(0, fake.SubscribeActivated(notification, _ => { }, out _));
        Assert.Equal(0, fake.SubscribeDismissed(notification, _ => { }, out _));
        Assert.Equal(0, fake.SubscribeFailed(notification, _ => { }, out _));
        Assert.Equal(0, fake.Show(notifier, notification));
        Assert.Equal(0, fake.UnsubscribeActivated(notification, fake.SubscribedTokens[0]));
        Assert.Equal(0, fake.UnsubscribeDismissed(notification, fake.SubscribedTokens[1]));
        Assert.Equal(0, fake.UnsubscribeFailed(notification, fake.SubscribedTokens[2]));

        Assert.Equal(ToastShowSequence, fake.Operations.ToArray());

        // The XML handed to the shell is observable from the seam without a live shell.
        ToastCall loadXml = Assert.Single(fake.Calls.Where(call => call.Operation == nameof(IToastApi.LoadXml)));
        Assert.Contains("ToastGeneric", loadXml.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscribedHandlers_DeliverInProcessWithoutALiveShell()
    {
        var fake = new FakeToastApi();

        string? activatedArguments = null;
        int dismissedReason = -1;
        int failedErrorCode = -1;

        Assert.Equal(0, fake.SubscribeActivated(IntPtr.Zero, arguments => activatedArguments = arguments, out _));
        Assert.Equal(0, fake.SubscribeDismissed(IntPtr.Zero, reason => dismissedReason = reason, out _));
        Assert.Equal(0, fake.SubscribeFailed(IntPtr.Zero, errorCode => failedErrorCode = errorCode, out _));

        Assert.True(fake.RaiseActivated("probe-activation"));
        Assert.True(fake.RaiseDismissed(2));
        Assert.True(fake.RaiseFailed(unchecked((int)0x80004005)));

        Assert.Equal("probe-activation", activatedArguments);
        Assert.Equal(2, dismissedReason);
        Assert.Equal(unchecked((int)0x80004005), failedErrorCode);
    }

    [Fact]
    public void UnsubscribedHandlers_NoLongerDeliver()
    {
        var fake = new FakeToastApi();
        bool fired = false;

        Assert.Equal(0, fake.SubscribeActivated(IntPtr.Zero, _ => fired = true, out long token));
        Assert.Equal(0, fake.UnsubscribeActivated(IntPtr.Zero, token));

        Assert.False(fake.RaiseActivated("after-teardown"));
        Assert.False(fired);
    }

    /// <summary>
    /// With capture on, the fake retains a subscribed handler past the matching unsubscribe, and the
    /// <c>Replay*</c> members invoke it - the in-process model of a callback the shell had already
    /// dequeued when the unsubscribe landed.
    /// </summary>
    [Fact]
    public void CapturedHandlers_ReplayAfterTheMatchingUnsubscribe()
    {
        var fake = new FakeToastApi { CaptureHandlers = true };

        string? activatedArguments = null;
        int dismissedReason = -1;
        int failedErrorCode = -1;

        Assert.Equal(0, fake.SubscribeActivated(IntPtr.Zero, arguments => activatedArguments = arguments, out long activatedToken));
        Assert.Equal(0, fake.SubscribeDismissed(IntPtr.Zero, reason => dismissedReason = reason, out long dismissedToken));
        Assert.Equal(0, fake.SubscribeFailed(IntPtr.Zero, errorCode => failedErrorCode = errorCode, out long failedToken));

        Assert.Equal(0, fake.UnsubscribeActivated(IntPtr.Zero, activatedToken));
        Assert.Equal(0, fake.UnsubscribeDismissed(IntPtr.Zero, dismissedToken));
        Assert.Equal(0, fake.UnsubscribeFailed(IntPtr.Zero, failedToken));

        // The live handler table no longer has any of them...
        Assert.False(fake.RaiseActivated("live-table-empty"));
        Assert.False(fake.RaiseDismissed(0));
        Assert.False(fake.RaiseFailed(0));

        // ...but the retained delegates are still invocable, which is the race the capture models.
        Assert.True(fake.ReplayActivated("dequeued"));
        Assert.True(fake.ReplayDismissed(2));
        Assert.True(fake.ReplayFailed(unchecked((int)0x80004005)));

        Assert.Equal("dequeued", activatedArguments);
        Assert.Equal(2, dismissedReason);
        Assert.Equal(unchecked((int)0x80004005), failedErrorCode);
    }

    /// <summary>
    /// Capture is opt-in: with it off nothing is retained and every <c>Replay*</c> member reports
    /// that nothing was delivered, so the existing tests' behaviour cannot change.
    /// </summary>
    [Fact]
    public void Replay_ReturnsFalseWhenCaptureIsOff()
    {
        var fake = new FakeToastApi();

        Assert.False(fake.CaptureHandlers);

        Assert.Equal(0, fake.SubscribeActivated(IntPtr.Zero, _ => { }, out _));
        Assert.Equal(0, fake.SubscribeDismissed(IntPtr.Zero, _ => { }, out _));
        Assert.Equal(0, fake.SubscribeFailed(IntPtr.Zero, _ => { }, out _));

        Assert.False(fake.ReplayActivated("never-retained"));
        Assert.False(fake.ReplayDismissed(0));
        Assert.False(fake.ReplayFailed(0));
    }

    /// <summary>
    /// Capture changes nothing about the live handler table: after an unsubscribe a raise still
    /// reports that nothing was delivered, capture or not.
    /// </summary>
    [Fact]
    public void Capture_LeavesTheLiveRaiseSemanticsUnchanged()
    {
        var fake = new FakeToastApi { CaptureHandlers = true };
        bool fired = false;

        Assert.Equal(0, fake.SubscribeActivated(IntPtr.Zero, _ => fired = true, out long token));
        Assert.True(fake.RaiseActivated("while-live"));
        Assert.True(fired);

        Assert.Equal(0, fake.UnsubscribeActivated(IntPtr.Zero, token));

        Assert.False(fake.RaiseActivated("after-unsubscribe"));
    }

    [Fact]
    public void DisposalContract_RecordsEveryReleasedHandle()
    {
        var fake = new FakeToastApi();

        Assert.Equal(0, fake.CreateShellLink(out IntPtr shellLink));
        Assert.Equal(0, fake.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore));
        Assert.Equal(0, fake.ReleaseHandle(propertyStore));
        Assert.Equal(0, fake.ReleaseHandle(shellLink));

        Assert.Equal([propertyStore, shellLink], fake.ReleasedHandles.ToArray());
        Assert.Equal(2, fake.CallCount(ToastOperation.ReleaseHandle));
    }

    [Fact]
    public void DeleteShortcutFailure_SurfacesThroughGetLastError()
    {
        var fake = new FakeToastApi { LastErrorToReport = 5 };
        fake.FailAlways(ToastOperation.DeleteShortcut);

        Assert.False(fake.DeleteShortcut("App.lnk"));
        Assert.Equal(5, fake.GetLastError());

        // Clearing the script lets the same call succeed again and the error channel go quiet.
        fake.ClearScriptedFailures();
        Assert.True(fake.DeleteShortcut("App.lnk"));
        Assert.Equal(0, fake.GetLastError());
    }

    /// <summary>
    /// The four notification-property members (the ones that apply tag, group and expiry to the
    /// notification object rather than to the toast XML) are recorded with their arguments and can
    /// each be scripted to fail like every other seam member.
    /// </summary>
    [Fact]
    public void NotificationPropertyMembers_AreRecordedWithTheirArguments()
    {
        var fake = new FakeToastApi();

        Assert.Equal(0, fake.SetNotificationTag(IntPtr.Zero, "tag-1"));
        Assert.Equal(0, fake.SetNotificationGroup(IntPtr.Zero, "group-1"));
        Assert.Equal(0, fake.CreateDateTimePropertyValue(116444736000000000L, out IntPtr propertyValue));
        Assert.Equal(0, fake.SetNotificationExpirationTime(IntPtr.Zero, propertyValue));
        Assert.Equal(0, fake.ReleaseHandle(propertyValue));

        // The recorded order is the order the seam was called in.
        Assert.Equal(
            [
                nameof(IToastApi.SetNotificationTag),
                nameof(IToastApi.SetNotificationGroup),
                nameof(IToastApi.CreateDateTimePropertyValue),
                nameof(IToastApi.SetNotificationExpirationTime),
                nameof(IToastApi.ReleaseHandle),
            ],
            fake.Operations.ToArray());

        // The arguments are the caller's own values, observable without a live shell.
        Assert.Contains("tag-1", SingleDetail(fake, nameof(IToastApi.SetNotificationTag)), StringComparison.Ordinal);
        Assert.Contains("group-1", SingleDetail(fake, nameof(IToastApi.SetNotificationGroup)), StringComparison.Ordinal);
        Assert.Contains("116444736000000000", SingleDetail(fake, nameof(IToastApi.CreateDateTimePropertyValue)), StringComparison.Ordinal);

        // The property value is a fresh non-zero handle that joins the released set, so the show
        // path's release accounting can be asserted against it.
        Assert.NotEqual(IntPtr.Zero, propertyValue);
        Assert.Contains(propertyValue, fake.ReleasedHandles);
    }

    /// <summary>Each of the four notification-property members can be scripted to fail.</summary>
    /// <param name="operationName">The seam member scripted to fail.</param>
    [Theory]
    [InlineData(nameof(IToastApi.SetNotificationTag))]
    [InlineData(nameof(IToastApi.SetNotificationGroup))]
    [InlineData(nameof(IToastApi.CreateDateTimePropertyValue))]
    [InlineData(nameof(IToastApi.SetNotificationExpirationTime))]
    public void NotificationPropertyMembers_CanBeScriptedToFail(string operationName)
    {
        var operation = Enum.Parse<ToastOperation>(operationName);
        var fake = new FakeToastApi();
        fake.FailNext(operation);

        int hr;

        switch (operation)
        {
            case ToastOperation.SetNotificationTag:
                hr = fake.SetNotificationTag(IntPtr.Zero, "scripted-tag");
                break;

            case ToastOperation.SetNotificationGroup:
                hr = fake.SetNotificationGroup(IntPtr.Zero, "scripted-group");
                break;

            case ToastOperation.CreateDateTimePropertyValue:
                hr = fake.CreateDateTimePropertyValue(1L, out IntPtr failedPropertyValue);

                // A failed boxing step hands out no handle, so there is nothing to release.
                Assert.Equal(IntPtr.Zero, failedPropertyValue);
                break;

            default:
                hr = fake.SetNotificationExpirationTime(IntPtr.Zero, new IntPtr(0x1234));
                break;
        }

        Assert.Equal(FakeToastApi.DefaultFailureHResult, hr);

        // The failure is data and is still recorded with its code - never an exception, and never
        // invisible in the log.
        ToastCall call = Assert.Single(fake.Calls.Where(candidate => candidate.Operation == operationName));
        Assert.Equal(FakeToastApi.DefaultFailureHResult, call.HResult);
        Assert.True(call.HResult < 0);
    }

    /// <summary>Finds the single recorded call to one operation and returns its diagnostic detail.</summary>
    /// <param name="fake">The fake that recorded it.</param>
    /// <param name="operation">The seam member name.</param>
    /// <returns>The recorded detail string.</returns>
    private static string SingleDetail(FakeToastApi fake, string operation) =>
        Assert.Single(fake.Calls.Where(call => call.Operation == operation)).Detail;

    private static int IndexOf(FakeToastApi fake, string operation) =>
        fake.Operations.ToList().IndexOf(operation);

    private static void AssertNotWinRt(MemberInfo member, Type type)
    {
        if (type.IsByRef || type.IsArray)
        {
            AssertNotWinRt(member, type.GetElementType()!);
            return;
        }

        if (type.Namespace is string ns)
        {
            Assert.False(
                ns.StartsWith("Windows.", StringComparison.Ordinal),
                $"{member.Name} exposes the WinRT type '{type.FullName}' on the toast seam.");
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                AssertNotWinRt(member, argument);
            }
        }
    }
}
