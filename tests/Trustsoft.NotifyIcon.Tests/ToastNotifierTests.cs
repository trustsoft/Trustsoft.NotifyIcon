using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Contract tests for <see cref="ToastNotifier"/> and <see cref="ToastException"/>, the public toast
/// surface M002/S02/T05 adds. They prove the things a live machine cannot: that the identity is
/// registered once, with the commit before the save and the read-back checked, before the first show;
/// that a registration failure becomes a named <see cref="ToastException"/> and not a silent
/// drop, while a show-path failure is reported through <see cref="ToastNotifier.ToastError"/> and
/// never throws; that a caller error touches the shell not at all; that each <see cref="ToastNotifier.Show"/>
/// creates an independent show; and that <see cref="ToastNotifier.Dispose"/> leaves neither a
/// subscription nor a shortcut behind.
/// </summary>
/// <remarks>
/// <para>
/// None of these tests touch the real shell, the real shortcut store or the real Windows toast stack:
/// they drive <see cref="FakeToastApi"/>, whose <see cref="FakeToastApi.AppUserModelIdToReadBack"/>
/// makes registration deterministic and whose failure injection can force a step that a healthy
/// machine would never fail. The live run is the sample's, not this suite's.
/// </para>
/// <para>
/// The tests are plain <see cref="FactAttribute"/> rather than STA facts because nothing here calls
/// COM or WinRT: the seam is the fake, so no apartment is involved.
/// </para>
/// </remarks>
public sealed class ToastNotifierTests
{
    /// <summary>The override id most tests register with, chosen so it cannot be the derived default.</summary>
    private const string OverrideId = "Vendor.Custom.App";

    /// <summary>The measured identity write sequence (docs/TOAST-MEASUREMENT.md, Contract 1).</summary>
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

    /// <summary>The measured show half, in order, for a content with no tag, group or expiry.</summary>
    private static readonly string[] ShowHalfSequence =
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

    /// <summary>
    /// Every type in the shipped public surface, used by the documentation guard: all twenty, not
    /// only the toast ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why all twenty and not just the notifier's own types.</b> The T04 inspector rule reads the
    /// exported surface out of the packed XML documentation and asserts the set is exactly twenty
    /// types. That rule is sound only while the exported set is pinned at twenty <em>and</em> every
    /// public member of every one of those types actually carries generated documentation - a type
    /// that ships with an undocumented member would satisfy the artifact rule while being unusable
    /// from a consumer's point of view. This guard is the second half of that chain (D010), so it
    /// walks the whole surface: the seven tray types, the six content-model types and the seven
    /// notifier types. The content-model guard
    /// (<c>ToastContentTests.Every_public_member_of_the_content_model_is_documented</c>) covers the
    /// six as well; the overlap is deliberate, because a documentation gap must fail here too.
    /// </para>
    /// </remarks>
    private static readonly Type[] DocumentedSurfaceTypes =
    [
        // Tray subsystem (M001).
        typeof(TrayIcon),
        typeof(TrayIconException),
        typeof(TrayErrorEventArgs),
        typeof(TrayIconClickEventArgs),
        typeof(TrayMenuActivation),
        typeof(BalloonTipIcon),
        typeof(BalloonTipOptions),
        // Toast content model (M002/S02).
        typeof(ToastContent),
        typeof(ToastSeverity),
        typeof(ToastSound),
        typeof(ToastButton),
        typeof(ToastImage),
        typeof(ToastImagePlacement),
        // Toast notifier and its event surface (M002/S02/T05, S03/T01, S04/T04).
        typeof(ToastNotifier),
        typeof(ToastException),
        typeof(ToastActivatedEventArgs),
        typeof(ToastDismissedEventArgs),
        typeof(ToastDismissalReason),
        typeof(ToastErrorEventArgs),
        typeof(ToastNotificationSetting),
    ];

    /// <summary>
    /// The three events <see cref="ToastNotifier"/> must expose, each with its typed handler - the
    /// exact set M002/S03/T01 adds, asserted as a whole so adding, removing or retyping an event is a
    /// deliberate edit.
    /// </summary>
    private static readonly (string Name, Type HandlerType)[] NotifierEvents =
    [
        (nameof(ToastNotifier.Activated), typeof(EventHandler<ToastActivatedEventArgs>)),
        (nameof(ToastNotifier.Dismissed), typeof(EventHandler<ToastDismissedEventArgs>)),
        (nameof(ToastNotifier.ToastError), typeof(EventHandler<ToastErrorEventArgs>)),
    ];

    /// <summary>
    /// The first <see cref="ToastNotifier.Show"/> registers the identity with the measured sequence
    /// (commit before save, read-back on a fresh link) and then runs the measured show half - the
    /// registration is complete before any WinRT factory is acquired.
    /// </summary>
    [Fact]
    public void First_show_registers_the_identity_with_commit_before_save_and_then_shows()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        Assert.Equal(IdentityWriteSequence, fake.Operations.Take(IdentityWriteSequence.Length).ToArray());

        int commit = IndexOf(fake, nameof(IToastApi.CommitPropertyStore));
        int save = IndexOf(fake, nameof(IToastApi.SaveShortcut));
        int readBack = IndexOf(fake, nameof(IToastApi.GetAppUserModelId));
        int showStart = IndexOf(fake, nameof(IToastApi.GetToastNotificationManagerStatics));

        Assert.True(commit < save, "the AppUserModelID must be committed before the shortcut is saved");
        Assert.True(save < readBack, "the read-back must run after the shortcut is saved");
        Assert.True(readBack < showStart, "the identity must be registered and read back before the toast is shown");

        Assert.Equal(ShowHalfSequence, fake.Operations.Skip(showStart).ToArray());

        // The payload the show path loads is the one built from the content the caller handed in.
        ToastCall loadXml = fake.Calls.First(call => call.Operation == nameof(IToastApi.LoadXml));
        Assert.Contains("Build finished", loadXml.Detail, StringComparison.Ordinal);
        Assert.Contains("3 projects built in 12.4s", loadXml.Detail, StringComparison.Ordinal);
        Assert.Contains("scenario=\"reminder\"", loadXml.Detail, StringComparison.Ordinal);

        // The notifier is bound to the registered identity, not to a process default.
        ToastCall createNotifier = fake.Calls.First(call => call.Operation == nameof(IToastApi.CreateToastNotifier));
        Assert.Contains(OverrideId, createNotifier.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no override the notifier registers the entry-assembly-derived default
    /// (<see cref="ToastIdentity.DeriveDefaultAppUserModelId"/>), which is what D060 fixes as the
    /// default identity.
    /// </summary>
    [Fact]
    public void First_show_without_an_override_registers_the_derived_default_identity()
    {
        string defaultId = ToastIdentity.DeriveDefaultAppUserModelId();
        var fake = new FakeToastApi { AppUserModelIdToReadBack = defaultId };
        using var notifier = new ToastNotifier(fake);

        notifier.Show(Content());

        ToastCall set = fake.Calls.First(call => call.Operation == nameof(IToastApi.SetAppUserModelId));
        Assert.Contains(defaultId, set.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A registration failure throws a <see cref="ToastException"/> naming the failing identity
    /// operation and carrying its code, shows nothing, and is not retried on a later show.
    /// </summary>
    [Fact]
    public void A_registration_failure_throws_a_named_exception_and_shows_nothing()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.SaveShortcut);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        ToastException error = Assert.Throws<ToastException>(() => notifier.Show(Content()));

        Assert.Equal(nameof(IToastApi.SaveShortcut), error.Operation);
        Assert.Equal(fake.FailureHResult, error.ErrorCode);
        Assert.Equal(0, fake.CallCount(ToastOperation.CreateToastNotification));
        Assert.Equal(0, fake.CallCount(ToastOperation.Show));

        // A failed registration is remembered: the next show reports the same failure rather than
        // re-running registration or showing an identity-less toast.
        ToastException again = Assert.Throws<ToastException>(() => notifier.Show(Content()));

        Assert.Equal(nameof(IToastApi.SaveShortcut), again.Operation);
        Assert.Equal(1, fake.CallCount(ToastOperation.CreateShellLink));
    }

    /// <summary>
    /// The silent-failure case: the write path succeeds but the read-back disagrees. That is the
    /// value-level failure no <c>HRESULT</c> reports, so it carries the pinned operation name and a
    /// code of <c>0</c> - which must not be read as success.
    /// </summary>
    [Fact]
    public void A_read_back_mismatch_throws_with_the_value_level_operation_and_code_zero()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = "Something.Else" };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        ToastException error = Assert.Throws<ToastException>(() => notifier.Show(Content()));

        Assert.Equal(ToastIdentity.OperationReadBackMismatch, error.Operation);
        Assert.Equal(ToastException.OperationReadBackMismatch, error.Operation);
        Assert.Equal(0, error.ErrorCode);
        Assert.Equal(0, fake.CallCount(ToastOperation.CreateToastNotification));
    }

    /// <summary>
    /// A <see langword="null"/> content is a caller error and touches the seam not at all - no
    /// shortcut is written and nothing is shown.
    /// </summary>
    [Fact]
    public void Null_content_throws_ArgumentNullException_without_touching_the_seam()
    {
        var fake = new FakeToastApi();
        using var notifier = new ToastNotifier(fake);

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => notifier.Show(null!));

        Assert.Equal("content", error.ParamName);
        Assert.Empty(fake.Calls);
    }

    /// <summary>
    /// A title that is empty or only whitespace is a caller error (D058): it would be an empty
    /// banner, so it is refused before the shell is touched, with the parameter named.
    /// </summary>
    /// <param name="title">The unusable title under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    public void An_empty_or_whitespace_title_throws_ArgumentException_naming_the_parameter(string title)
    {
        var fake = new FakeToastApi();
        using var notifier = new ToastNotifier(fake);

        ArgumentException error = Assert.Throws<ArgumentException>(() => notifier.Show(new ToastContent { Title = title }));

        Assert.Equal("content", error.ParamName);
        Assert.Empty(fake.Calls);
    }

    /// <summary>
    /// The override is the identity the shortcut is written with, and it is frozen once the first
    /// show has attempted registration: a late set throws rather than silently misrouting the
    /// notifier.
    /// </summary>
    [Fact]
    public void The_override_is_what_is_registered_and_a_late_override_throws()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        Assert.Equal(OverrideId, notifier.AppUserModelId);

        notifier.Show(Content());

        ToastCall set = fake.Calls.First(call => call.Operation == nameof(IToastApi.SetAppUserModelId));
        Assert.Contains(OverrideId, set.Detail, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<InvalidOperationException>(() => notifier.AppUserModelId = "Other.App");
    }

    /// <summary>
    /// Two shows produce two independent internal shows - separate notifications and separate event
    /// subscriptions - while the identity is registered exactly once.
    /// </summary>
    [Fact]
    public void Two_shows_produce_two_independent_shows_registered_once()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content("first"));
        notifier.Show(Content("second"));

        Assert.Equal(1, fake.CallCount(ToastOperation.CreateShellLink));
        Assert.Equal(1, fake.CallCount(ToastOperation.SetAppUserModelId));
        Assert.Equal(1, fake.CallCount(ToastOperation.OpenShellLink));

        Assert.Equal(2, fake.CallCount(ToastOperation.CreateToastNotification));
        Assert.Equal(2, fake.CallCount(ToastOperation.SubscribeActivated));
        Assert.Equal(2, fake.CallCount(ToastOperation.SubscribeDismissed));
        Assert.Equal(2, fake.CallCount(ToastOperation.SubscribeFailed));
        Assert.Equal(2, fake.CallCount(ToastOperation.Show));

        // Three subscriptions per show and two shows means six tokens, and they are all distinct:
        // each show subscribed its own handlers, so disposing one cannot remove the other's.
        Assert.Equal(6, fake.SubscribedTokens.Count);
        Assert.Equal(6, fake.SubscribedTokens.Distinct().Count());

        notifier.Dispose();

        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeActivated));
        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeDismissed));
        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeFailed));
    }

    /// <summary>
    /// Disposal unsubscribes and releases every live show, removes the shortcut this notifier
    /// created exactly once, and a second disposal records nothing further - so a disposed notifier
    /// leaves no subscription and nothing registered (milestone success criterion 3).
    /// </summary>
    [Fact]
    public void Dispose_releases_both_shows_removes_the_shortcut_once_and_is_idempotent()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content("first"));
        notifier.Show(Content("second"));

        notifier.Dispose();

        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeActivated));
        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeDismissed));
        Assert.Equal(2, fake.CallCount(ToastOperation.UnsubscribeFailed));
        Assert.Equal(1, fake.CallCount(ToastOperation.DeleteShortcut));

        int afterFirstDispose = fake.Calls.Count;

        notifier.Dispose();

        Assert.Equal(afterFirstDispose, fake.Calls.Count);
        Assert.Equal(1, fake.CallCount(ToastOperation.DeleteShortcut));
    }

    /// <summary>
    /// A notifier that never showed disposes without touching the seam: no shortcut was created, so
    /// nothing is deleted and no factory was ever acquired.
    /// </summary>
    [Fact]
    public void A_notifier_that_never_showed_disposes_without_touching_the_seam()
    {
        var fake = new FakeToastApi();
        var notifier = new ToastNotifier(fake);

        notifier.Dispose();

        Assert.Empty(fake.Calls);
    }

    /// <summary>
    /// A failed show unwinds its own subscriptions and handles and is reported through the non-fatal
    /// <see cref="ToastNotifier.ToastError"/> channel with the failing seam member as the operation -
    /// without throwing, which is the split D055 fixes and D061 substituted for D058's interim throw -
    /// and it does not poison the notifier: a later show succeeds with a fresh show object.
    /// </summary>
    [Fact]
    public void A_show_failure_raises_ToastError_without_throwing_unwinds_and_leaves_the_notifier_usable()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        fake.FailNext(ToastOperation.Show);
        using var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        List<ToastErrorEventArgs> errors = [];
        notifier.ToastError += (_, e) => errors.Add(e);

        // The runtime failure must not throw: a toast that could not be delivered is reported, not
        // fatal (D055/D061). Statement body on purpose - Record.Exception's overloads are ambiguous
        // for an expression lambda whose value could be ignored.
        Exception? thrown = Record.Exception(() => { notifier.Show(Content()); });

        Assert.Null(thrown);

        ToastErrorEventArgs error = Assert.Single(errors);

        Assert.Equal(nameof(IToastApi.Show), error.Operation);
        Assert.Equal(fake.FailureHResult, error.ErrorCode);

        // A show-path failure is a returned code, so there is no exception behind it - the same
        // shape the shell's asynchronous failure reports.
        Assert.Null(error.Exception);

        // The failed show removed its three subscriptions and released its five handles
        // (statics, factory, document, notifier, notification); the three write-path handles and the
        // two read-back handles were released during registration, which is ten in all.
        Assert.Equal(1, fake.CallCount(ToastOperation.UnsubscribeActivated));
        Assert.Equal(1, fake.CallCount(ToastOperation.UnsubscribeDismissed));
        Assert.Equal(1, fake.CallCount(ToastOperation.UnsubscribeFailed));
        Assert.Equal(10, fake.CallCount(ToastOperation.ReleaseHandle));

        // The notifier stays usable: the identity is already registered, so the next show is fresh.
        fake.StopFailingAlways(ToastOperation.Show);
        notifier.Show(Content("second"));

        Assert.Equal(2, fake.CallCount(ToastOperation.CreateToastNotification));
        Assert.Equal(2, fake.CallCount(ToastOperation.Show));
        Assert.Equal(1, fake.CallCount(ToastOperation.CreateShellLink));
    }

    /// <summary>
    /// Disposal is terminal and must not throw: a failed shortcut deletion is traced, not raised,
    /// because there is typically no caller left to handle it.
    /// </summary>
    [Fact]
    public void A_failed_shortcut_removal_during_dispose_does_not_throw()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());

        fake.FailNext(ToastOperation.DeleteShortcut);

        Exception? thrown = Record.Exception(notifier.Dispose);

        Assert.Null(thrown);
        Assert.Equal(1, fake.CallCount(ToastOperation.DeleteShortcut));

        // The one live show was still torn down despite the failed removal.
        Assert.Equal(1, fake.CallCount(ToastOperation.UnsubscribeActivated));
    }

    /// <summary>
    /// A disposed notifier refuses further shows instead of creating a show it can no longer track -
    /// which would leak a subscription past disposal.
    /// </summary>
    [Fact]
    public void Show_after_Dispose_throws_ObjectDisposedException()
    {
        var fake = new FakeToastApi { AppUserModelIdToReadBack = OverrideId };
        var notifier = new ToastNotifier(fake) { AppUserModelId = OverrideId };

        notifier.Show(Content());
        notifier.Dispose();

        int beforeSecondShow = fake.Calls.Count;

        Assert.Throws<ObjectDisposedException>(() => notifier.Show(Content()));
        Assert.Equal(beforeSecondShow, fake.Calls.Count);
    }

    /// <summary>
    /// <see cref="ToastException"/> round-trips the two machine-readable fields, names the operation
    /// and the code in its message (in hexadecimal too), and rejects a missing operation.
    /// </summary>
    [Fact]
    public void ToastException_carries_the_operation_and_the_code()
    {
        var mismatch = new ToastException(ToastException.OperationReadBackMismatch, 0);

        Assert.Equal("ReadBackMismatch", mismatch.Operation);
        Assert.Equal(0, mismatch.ErrorCode);
        Assert.Contains("ReadBackMismatch", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains("failed", mismatch.Message, StringComparison.Ordinal);

        var failure = new ToastException(
            nameof(IToastApi.SaveShortcut),
            FakeToastApi.DefaultFailureHResult,
            "The shortcut could not be serialized.");

        Assert.Equal(nameof(IToastApi.SaveShortcut), failure.Operation);
        Assert.Equal(FakeToastApi.DefaultFailureHResult, failure.ErrorCode);
        Assert.Contains("0x80004005", failure.Message, StringComparison.Ordinal);
        Assert.Contains("The shortcut could not be serialized.", failure.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentNullException>(() => new ToastException(null!, 0));
        Assert.Throws<ArgumentException>(() => new ToastException(string.Empty, 0));
    }

    /// <summary>
    /// The public constants mirror the internal value-level vocabulary the show and identity paths
    /// produce, so the notifier and the exception cannot spell the same failure differently.
    /// </summary>
    [Fact]
    public void ToastException_constants_mirror_the_internal_operation_names()
    {
        Assert.Equal(ToastShow.OperationInvalidArgument, ToastException.OperationInvalidArgument);
        Assert.Equal(ToastShow.OperationAlreadyShown, ToastException.OperationAlreadyShown);
        Assert.Equal(ToastShow.OperationNotificationFailed, ToastException.OperationNotificationFailed);
        Assert.Equal(ToastIdentity.OperationReadBackMismatch, ToastException.OperationReadBackMismatch);

        Assert.Equal("InvalidArgument", ToastException.OperationInvalidArgument);
        Assert.Equal("AlreadyShown", ToastException.OperationAlreadyShown);
        Assert.Equal("NotificationFailed", ToastException.OperationNotificationFailed);
        Assert.Equal("ReadBackMismatch", ToastException.OperationReadBackMismatch);
    }

    /// <summary>
    /// The shape D052/D057 fix: a standalone sealed class implementing <see cref="IDisposable"/>,
    /// not a <c>FrameworkElement</c>, with a settable <see cref="ToastNotifier.AppUserModelId"/> and
    /// exactly the three typed activation events M002/S03/T01 adds - the pin that makes a fourth
    /// event, a retyped handler or a silently dropped event a deliberate edit.
    /// </summary>
    [Fact]
    public void Notifier_is_a_standalone_disposable_with_an_overridable_identity_and_the_three_activation_events()
    {
        Type type = typeof(ToastNotifier);

        Assert.True(type.IsPublic, $"{type.FullName} must be public: it is the toast entry point (D057).");
        Assert.True(type.IsSealed, $"{type.FullName} must be sealed: D052 fixes it as a standalone entry point, not a base class.");
        Assert.False(typeof(System.Windows.FrameworkElement).IsAssignableFrom(type), "D052: the notifier is not a FrameworkElement.");
        Assert.True(typeof(IDisposable).IsAssignableFrom(type), "D052: the notifier is disposable.");

        PropertyInfo property = type.GetProperty(nameof(ToastNotifier.AppUserModelId))!;

        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(string), property.PropertyType);

        // S03 replaces S02's "no events yet" pin with the positive one: exactly these three, each
        // with the args type its name promises, so a consumer's handler signature is a contract.
        EventInfo[] events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(NotifierEvents.Length, events.Length);

        foreach ((string name, Type handlerType) in NotifierEvents)
        {
            EventInfo? declared = type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.NotNull(declared);
            Assert.Equal(handlerType, declared!.EventHandlerType);

            // EventInfo exposes no IsStatic of its own; a field-like event's staticness is that of
            // the add accessor it installs.
            Assert.False(declared.AddMethod!.IsStatic, $"{name} must be an instance event.");
        }

        // No fourth event and no public constants on the notifier: the surface is the three events,
        // the identity and setting properties, Show and Dispose.
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    /// <summary>
    /// Every public type and member of the whole shipped surface is documented in the generated
    /// documentation file that ships beside the assembly (D010).
    /// </summary>
    /// <remarks>
    /// CS1591 is suppressed by name, so nothing else would notice an undocumented member. This is the
    /// enforcement point for all twenty public types - the tray surface, the content model and the
    /// notifier surface - mirroring (and overlapping on the content model with) the earlier
    /// content-model documentation guard, and it is the second half of the T04 inspector rule that
    /// pins the artifact's exported set at twenty. Events are walked too (<c>E:</c> entries): an event
    /// is a public member like any other, and a missing event entry is exactly the kind of gap a
    /// reader of the generated documentation would hit.
    /// </remarks>
    [Fact]
    public void Every_public_member_of_the_notifier_surface_is_documented()
    {
        string documentationPath = Path.ChangeExtension(typeof(TrayIcon).Assembly.Location, ".xml");

        Assert.True(
            File.Exists(documentationPath),
            $"No generated documentation file was found at '{documentationPath}', so the documented surface cannot be checked. "
            + "Build the solution first: dotnet build Trustsoft.NotifyIcon.sln -c Release.");

        HashSet<string> documented = XDocument.Load(documentationPath)
            .Descendants("member")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = [];

        foreach (Type type in DocumentedSurfaceTypes)
        {
            if (!documented.Contains($"T:{type.FullName}"))
            {
                missing.Add($"T:{type.FullName}");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!documented.Contains($"P:{type.FullName}.{property.Name}"))
                {
                    missing.Add($"P:{type.FullName}.{property.Name}");
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!documented.Contains($"F:{type.FullName}.{field.Name}"))
                {
                    missing.Add($"F:{type.FullName}.{field.Name}");
                }
            }

            foreach (EventInfo declaredEvent in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!documented.Contains($"E:{type.FullName}.{declaredEvent.Name}"))
                {
                    missing.Add($"E:{type.FullName}.{declaredEvent.Name}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"D010: {missing.Count} public member(s) of the T05 notifier surface carry no XML documentation: [{string.Join(", ", missing)}]. "
            + "CS1591 is suppressed in Directory.Build.props, so nothing else would notice; document the member in English.");
    }

    /// <summary>
    /// A representative content: a title, a body, a non-default severity and a launch argument - the
    /// minimal shape S02's demo sentence describes.
    /// </summary>
    /// <param name="title">The title to use; the default is the demo's.</param>
    /// <returns>The content.</returns>
    private static ToastContent Content(string title = "Build finished") => new()
    {
        Title = title,
        Body = "3 projects built in 12.4s",
        Severity = ToastSeverity.Reminder,
        Launch = "sample-toast-1",
    };

    /// <summary>Finds the first recorded call to one operation.</summary>
    /// <param name="fake">The recording fake.</param>
    /// <param name="operation">The seam member name.</param>
    /// <returns>The index of the first matching call, or <c>-1</c> when it was never called.</returns>
    private static int IndexOf(FakeToastApi fake, string operation)
    {
        for (int i = 0; i < fake.Operations.Count; i++)
        {
            if (string.Equals(fake.Operations[i], operation, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
