using System.Diagnostics.CodeAnalysis;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The <see cref="IToastApi"/> members that appear in <see cref="FakeToastApi"/>'s call log and
/// that can be scripted to fail.
/// </summary>
/// <remarks>
/// Member names mirror <see cref="IToastApi"/> one-for-one so a scripted failure reads like the
/// call it replaces; <see cref="ToastCall.Operation"/> carries the same name as a string.
/// </remarks>
internal enum ToastOperation
{
    /// <summary><see cref="IToastApi.CreateShellLink"/>.</summary>
    CreateShellLink,

    /// <summary><see cref="IToastApi.ConfigureShortcut"/>.</summary>
    ConfigureShortcut,

    /// <summary><see cref="IToastApi.GetShortcutPropertyStore"/>.</summary>
    GetShortcutPropertyStore,

    /// <summary><see cref="IToastApi.GetShortcutPersistFile"/>.</summary>
    GetShortcutPersistFile,

    /// <summary><see cref="IToastApi.SetAppUserModelId"/>.</summary>
    SetAppUserModelId,

    /// <summary><see cref="IToastApi.CommitPropertyStore"/>.</summary>
    CommitPropertyStore,

    /// <summary><see cref="IToastApi.SaveShortcut"/>.</summary>
    SaveShortcut,

    /// <summary><see cref="IToastApi.OpenShellLink"/>.</summary>
    OpenShellLink,

    /// <summary><see cref="IToastApi.GetAppUserModelId"/>.</summary>
    GetAppUserModelId,

    /// <summary><see cref="IToastApi.DeleteShortcut"/>. Its failure channel is the Win32
    /// last-error value, not an <c>HRESULT</c>.</summary>
    DeleteShortcut,

    /// <summary><see cref="IToastApi.ReleaseHandle"/>.</summary>
    ReleaseHandle,

    /// <summary><see cref="IToastApi.GetLastError"/>.</summary>
    GetLastError,

    /// <summary><see cref="IToastApi.GetToastNotificationManagerStatics"/>.</summary>
    GetToastNotificationManagerStatics,

    /// <summary><see cref="IToastApi.GetToastNotificationFactory"/>.</summary>
    GetToastNotificationFactory,

    /// <summary><see cref="IToastApi.ActivateXmlDocument"/>.</summary>
    ActivateXmlDocument,

    /// <summary><see cref="IToastApi.CreateToastNotifier"/>.</summary>
    CreateToastNotifier,

    /// <summary><see cref="IToastApi.GetNotifierSetting"/>.</summary>
    GetNotifierSetting,

    /// <summary><see cref="IToastApi.LoadXml"/>.</summary>
    LoadXml,

    /// <summary><see cref="IToastApi.CreateToastNotification"/>.</summary>
    CreateToastNotification,

    /// <summary><see cref="IToastApi.SetNotificationTag"/>. A notification property, not toast XML.</summary>
    SetNotificationTag,

    /// <summary><see cref="IToastApi.SetNotificationGroup"/>. A notification property, not toast XML.</summary>
    SetNotificationGroup,

    /// <summary><see cref="IToastApi.CreateDateTimePropertyValue"/>. The expiry's boxing step.</summary>
    CreateDateTimePropertyValue,

    /// <summary><see cref="IToastApi.SetNotificationExpirationTime"/>. A notification property, not toast XML.</summary>
    SetNotificationExpirationTime,

    /// <summary><see cref="IToastApi.Show"/>.</summary>
    Show,

    /// <summary><see cref="IToastApi.SubscribeActivated"/>.</summary>
    SubscribeActivated,

    /// <summary><see cref="IToastApi.SubscribeDismissed"/>.</summary>
    SubscribeDismissed,

    /// <summary><see cref="IToastApi.SubscribeFailed"/>.</summary>
    SubscribeFailed,

    /// <summary><see cref="IToastApi.UnsubscribeActivated"/>.</summary>
    UnsubscribeActivated,

    /// <summary><see cref="IToastApi.UnsubscribeDismissed"/>.</summary>
    UnsubscribeDismissed,

    /// <summary><see cref="IToastApi.UnsubscribeFailed"/>.</summary>
    UnsubscribeFailed,
}

/// <summary>
/// One recorded seam call, in the shape the contract tests assert against.
/// </summary>
/// <param name="Operation">
/// The <see cref="IToastApi"/> member name, equal to the corresponding
/// <see cref="ToastOperation"/> name, so
/// <c>call.Operation == nameof(IToastApi.SaveShortcut)</c> holds.
/// </param>
/// <param name="HResult">
/// The raw status the call returned: an <c>HRESULT</c> for every COM/WinRT member, or the Win32
/// error code for <see cref="IToastApi.DeleteShortcut"/>. <c>0</c> means success for both.
/// </param>
/// <param name="Detail">
/// Human-readable summary for diagnostics and failure reports. <b>Not an assertion contract</b> -
/// assert on <see cref="ToastCall.Operation"/>, <see cref="ToastCall.HResult"/> and the fake's
/// recorded collections instead.
/// </param>
internal readonly record struct ToastCall(string Operation, int HResult, string Detail);

/// <summary>
/// A fully scripted, recording <see cref="IToastApi"/> that never touches the real shell or the
/// WinRT toast stack.
/// </summary>
/// <remarks>
/// <para>
/// It produces the three things a live machine cannot: a deterministic call sequence (the
/// activation-factory order, the commit-before-save ordering, the subscribe/show/unsubscribe
/// order), injectable failures (a real <c>RoGetActivationFactory</c> or <c>Show</c> failure cannot
/// be forced on a machine whose notifications work), and exact handle-release accounting. Every
/// call is appended to <see cref="Calls"/> in order, including a scripted failure, so a failed or
/// throwing step is never invisible in the log.
/// </para>
/// <para>
/// <b>Scripted failures.</b> <see cref="FailNext"/> fails the next N calls to one operation and
/// then stops; <see cref="FailAlways"/> fails every call until cleared. A failing call returns the
/// failure value the real call would (<see cref="FailureHResult"/> for the <c>HRESULT</c> members,
/// <see langword="false"/> plus <see cref="LastErrorToReport"/> for
/// <see cref="IToastApi.DeleteShortcut"/>) and records that value in <see cref="Calls"/>.
/// </para>
/// <para>
/// <b>Event delivery without a live shell.</b> <see cref="RaiseActivated"/>,
/// <see cref="RaiseDismissed"/> and <see cref="RaiseFailed"/> invoke the handlers that were
/// subscribed through the seam, which is how the callback path is proven in-process without a
/// click on a real banner.
/// </para>
/// <para>
/// <b>Dequeued-callback capture.</b> With <see cref="CaptureHandlers"/> set, the fake also retains
/// the last handler handed to each subscribe member, so <see cref="ReplayActivated"/>,
/// <see cref="ReplayDismissed"/> and <see cref="ReplayFailed"/> can invoke it <em>after</em> the
/// matching unsubscribe removed it from the live handler table. That models the race the disposal
/// guarantee must survive: an unsubscribe detaches the registration, but a callback the shell had
/// already dequeued can still be invoked.
/// </para>
/// <para>
/// <b>Handles are distinct per call.</b> Each handle-returning member yields a fresh non-zero
/// handle, so a test can assert that the caller released exactly the handles it was handed rather
/// than counting releases blind.
/// </para>
/// </remarks>
internal sealed class FakeToastApi : IToastApi
{
    /// <summary>The <c>HRESULT</c> the fake reports for a scripted failure.</summary>
    /// <remarks><c>E_FAIL</c> (<c>0x80004005</c>) by default: a real failure code, not <c>0</c>,
    /// so a caller that ignores the return value cannot pass its own test by accident.</remarks>
    internal const int DefaultFailureHResult = unchecked((int)0x80004005);

    private readonly List<ToastCall> _calls = [];
    private readonly Dictionary<ToastOperation, int> _remainingFailures = [];
    private readonly HashSet<ToastOperation> _permanentFailures = [];
    private readonly List<IntPtr> _releasedHandles = [];
    private readonly List<long> _subscribedTokens = [];
    private readonly List<long> _unsubscribedTokens = [];
    private readonly List<(string Operation, long Token)> _unsubscribeCalls = [];
    private readonly Dictionary<long, Delegate> _handlers = [];
    private readonly Dictionary<ToastOperation, long> _subscriptionTokens = [];
    private readonly Dictionary<ToastOperation, Delegate> _capturedHandlers = [];

    private IntPtr _nextHandle = new(0x5001);
    private long _nextToken = 0x1001;
    private int _lastError;

    /// <summary>Gets every recorded call, in order.</summary>
    internal IReadOnlyList<ToastCall> Calls => _calls;

    /// <summary>Gets every recorded operation name, in call order.</summary>
    /// <remarks>
    /// The assertion surface for the sequence contracts: the ordering traps are asserted by
    /// checking the positions of <c>nameof(IToastApi.CommitPropertyStore)</c> and
    /// <c>nameof(IToastApi.SaveShortcut)</c>, or of the subscribe/show/unsubscribe members, in this
    /// list.
    /// </remarks>
    internal IReadOnlyList<string> Operations => [.. _calls.Select(call => call.Operation)];

    /// <summary>Gets the handles passed to <see cref="IToastApi.ReleaseHandle"/>, in call order.</summary>
    internal IReadOnlyList<IntPtr> ReleasedHandles => _releasedHandles;

    /// <summary>Gets the tokens the fake handed out for subscriptions, in call order.</summary>
    internal IReadOnlyList<long> SubscribedTokens => _subscribedTokens;

    /// <summary>Gets the tokens passed to an unsubscribe member, in call order.</summary>
    internal IReadOnlyList<long> UnsubscribedTokens => _unsubscribedTokens;

    /// <summary>
    /// Gets the unsubscribe members that were called with their token, in call order.
    /// </summary>
    internal IReadOnlyList<(string Operation, long Token)> UnsubscribeCalls => _unsubscribeCalls;

    /// <summary>Gets the number of calls made to one operation.</summary>
    /// <param name="operation">The operation to count.</param>
    /// <returns>The number of recorded calls to that operation.</returns>
    internal int CallCount(ToastOperation operation)
    {
        string name = operation.ToString();
        return _calls.Count(call => call.Operation == name);
    }

    /// <summary>Scripts the next call to one operation to fail, once.</summary>
    /// <param name="operation">The operation that should fail.</param>
    /// <remarks>
    /// Repeated calls queue up: two <see cref="FailNext"/> calls for the same operation fail the
    /// next two calls. A call to a different operation does not consume the scheduled failure.
    /// </remarks>
    internal void FailNext(ToastOperation operation) =>
        _remainingFailures[operation] = _remainingFailures.GetValueOrDefault(operation) + 1;

    /// <summary>Scripts every call to one operation to fail until <see cref="StopFailingAlways"/>.</summary>
    /// <param name="operation">The operation that should always fail.</param>
    internal void FailAlways(ToastOperation operation) => _permanentFailures.Add(operation);

    /// <summary>Stops the permanent failure scripted by <see cref="FailAlways"/>.</summary>
    /// <param name="operation">The operation that should succeed again.</param>
    internal void StopFailingAlways(ToastOperation operation) => _permanentFailures.Remove(operation);

    /// <summary>Removes every scripted failure, one-shot and permanent.</summary>
    internal void ClearScriptedFailures()
    {
        _remainingFailures.Clear();
        _permanentFailures.Clear();
    }

    /// <summary>Invokes the <c>Activated</c> handler subscribed through the seam.</summary>
    /// <param name="arguments">The argument string to deliver.</param>
    /// <returns><see langword="true"/> when a handler was subscribed and invoked.</returns>
    /// <remarks>The in-process proof of the activation path, with no live banner involved.</remarks>
    internal bool RaiseActivated(string? arguments)
    {
        ToastActivatedHandler? handler = GetHandler<ToastActivatedHandler>(ToastOperation.SubscribeActivated);
        if (handler is null)
        {
            return false;
        }

        handler(arguments);
        return true;
    }

    /// <summary>Invokes the <c>Dismissed</c> handler subscribed through the seam.</summary>
    /// <param name="reason">The dismissal reason to deliver.</param>
    /// <returns><see langword="true"/> when a handler was subscribed and invoked.</returns>
    internal bool RaiseDismissed(int reason)
    {
        ToastDismissedHandler? handler = GetHandler<ToastDismissedHandler>(ToastOperation.SubscribeDismissed);
        if (handler is null)
        {
            return false;
        }

        handler(reason);
        return true;
    }

    /// <summary>Invokes the <c>Failed</c> handler subscribed through the seam.</summary>
    /// <param name="errorCode">The error code to deliver.</param>
    /// <returns><see langword="true"/> when a handler was subscribed and invoked.</returns>
    internal bool RaiseFailed(int errorCode)
    {
        ToastFailedHandler? handler = GetHandler<ToastFailedHandler>(ToastOperation.SubscribeFailed);
        if (handler is null)
        {
            return false;
        }

        handler(errorCode);
        return true;
    }

    /// <summary>Invokes the retained <c>Activated</c> handler, even after the matching unsubscribe.</summary>
    /// <param name="arguments">The argument string to deliver.</param>
    /// <returns>
    /// <see langword="true"/> when a handler had been captured and was invoked; <see langword="false"/>
    /// when nothing was captured (including while <see cref="CaptureHandlers"/> is off).
    /// </returns>
    /// <remarks>Models a callback the shell dequeued before the unsubscribe took effect.</remarks>
    internal bool ReplayActivated(string? arguments)
    {
        if (!TryGetCaptured<ToastActivatedHandler>(ToastOperation.SubscribeActivated, out ToastActivatedHandler? handler))
        {
            return false;
        }

        handler(arguments);
        return true;
    }

    /// <summary>Invokes the retained <c>Dismissed</c> handler, even after the matching unsubscribe.</summary>
    /// <param name="reason">The dismissal reason to deliver.</param>
    /// <returns>
    /// <see langword="true"/> when a handler had been captured and was invoked; <see langword="false"/>
    /// when nothing was captured (including while <see cref="CaptureHandlers"/> is off).
    /// </returns>
    internal bool ReplayDismissed(int reason)
    {
        if (!TryGetCaptured<ToastDismissedHandler>(ToastOperation.SubscribeDismissed, out ToastDismissedHandler? handler))
        {
            return false;
        }

        handler(reason);
        return true;
    }

    /// <summary>Invokes the retained <c>Failed</c> handler, even after the matching unsubscribe.</summary>
    /// <param name="errorCode">The error code to deliver.</param>
    /// <returns>
    /// <see langword="true"/> when a handler had been captured and was invoked; <see langword="false"/>
    /// when nothing was captured (including while <see cref="CaptureHandlers"/> is off).
    /// </returns>
    internal bool ReplayFailed(int errorCode)
    {
        if (!TryGetCaptured<ToastFailedHandler>(ToastOperation.SubscribeFailed, out ToastFailedHandler? handler))
        {
            return false;
        }

        handler(errorCode);
        return true;
    }

    /// <summary>
    /// Gets or sets whether the fake retains the last handler handed to each subscribe member, so
    /// the <c>Replay*</c> members can invoke it after the matching unsubscribe removed it from the
    /// live handler table.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> so no existing test's behaviour changes: every handler
    /// still records nothing extra and the live handler table is untouched.
    /// </remarks>
    internal bool CaptureHandlers { get; set; }

    /// <summary>
    /// Gets or sets the value <see cref="IToastApi.GetAppUserModelId"/> reads back.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="null"/>, which models a shortcut that exists but carries no
    /// <c>AppUserModelID</c> - the silent failure the read-back check exists to catch. Set it to the
    /// written id for the success path.
    /// </remarks>
    internal string? AppUserModelIdToReadBack { get; set; }

    /// <summary>Gets or sets the value <see cref="IToastApi.GetNotifierSetting"/> reports.</summary>
    internal int NotifierSettingToReport { get; set; }

    /// <summary>Gets or sets the <c>HRESULT</c> a scripted failure returns.</summary>
    internal int FailureHResult { get; set; } = DefaultFailureHResult;

    /// <summary>Gets or sets the Win32 error code <see cref="IToastApi.GetLastError"/> reports after a scripted failure.</summary>
    internal int LastErrorToReport { get; set; } = 87;

    private TDelegate? GetHandler<TDelegate>(ToastOperation operation)
        where TDelegate : Delegate
    {
        if (_subscriptionTokens.TryGetValue(operation, out long token)
            && _handlers.TryGetValue(token, out Delegate? stored)
            && stored is TDelegate typed)
        {
            return typed;
        }

        return null;
    }

    private bool TryGetCaptured<TDelegate>(ToastOperation operation, [NotNullWhen(true)] out TDelegate? handler)
        where TDelegate : Delegate
    {
        if (_capturedHandlers.TryGetValue(operation, out Delegate? stored) && stored is TDelegate typed)
        {
            handler = typed;
            return true;
        }

        handler = null;
        return false;
    }

    private bool ConsumeFailure(ToastOperation operation)
    {
        if (_permanentFailures.Contains(operation))
        {
            _lastError = LastErrorToReport;
            return true;
        }

        if (_remainingFailures.TryGetValue(operation, out int remaining) && remaining > 0)
        {
            _remainingFailures[operation] = remaining - 1;
            _lastError = LastErrorToReport;
            return true;
        }

        _lastError = 0;
        return false;
    }

    private IntPtr NextHandle()
    {
        IntPtr handle = _nextHandle;
        _nextHandle = new IntPtr(handle.ToInt64() + 1);
        return handle;
    }

    private int RecordHandle(ToastOperation operation, IntPtr handle, string detail)
    {
        _calls.Add(new ToastCall(operation.ToString(), 0, detail + $"; handle=0x{handle.ToInt64():X}"));
        return 0;
    }

    /// <inheritdoc />
    public int CreateShellLink(out IntPtr shellLink)
    {
        if (ConsumeFailure(ToastOperation.CreateShellLink))
        {
            shellLink = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(CreateShellLink), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        shellLink = NextHandle();
        return RecordHandle(ToastOperation.CreateShellLink, shellLink, "CLSID_ShellLink");
    }

    /// <inheritdoc />
    public int ConfigureShortcut(IntPtr shellLink, string targetPath, string description, string iconPath, string arguments)
    {
        if (ConsumeFailure(ToastOperation.ConfigureShortcut))
        {
            _calls.Add(new ToastCall(nameof(ConfigureShortcut), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(
            nameof(ConfigureShortcut),
            0,
            $"target=\"{targetPath}\"; description=\"{description}\"; icon=\"{iconPath}\"; arguments=\"{arguments}\""));
        return 0;
    }

    /// <inheritdoc />
    public int GetShortcutPropertyStore(IntPtr shellLink, out IntPtr propertyStore)
    {
        if (ConsumeFailure(ToastOperation.GetShortcutPropertyStore))
        {
            propertyStore = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(GetShortcutPropertyStore), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        propertyStore = NextHandle();
        return RecordHandle(ToastOperation.GetShortcutPropertyStore, propertyStore, "QueryInterface IPropertyStore");
    }

    /// <inheritdoc />
    public int GetShortcutPersistFile(IntPtr shellLink, out IntPtr persistFile)
    {
        if (ConsumeFailure(ToastOperation.GetShortcutPersistFile))
        {
            persistFile = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(GetShortcutPersistFile), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        persistFile = NextHandle();
        return RecordHandle(ToastOperation.GetShortcutPersistFile, persistFile, "QueryInterface IPersistFile");
    }

    /// <inheritdoc />
    public int SetAppUserModelId(IntPtr propertyStore, string appUserModelId)
    {
        if (ConsumeFailure(ToastOperation.SetAppUserModelId))
        {
            _calls.Add(new ToastCall(nameof(SetAppUserModelId), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(SetAppUserModelId), 0, $"PKEY_AppUserModel_ID=\"{appUserModelId}\""));
        return 0;
    }

    /// <inheritdoc />
    public int CommitPropertyStore(IntPtr propertyStore)
    {
        if (ConsumeFailure(ToastOperation.CommitPropertyStore))
        {
            _calls.Add(new ToastCall(nameof(CommitPropertyStore), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(CommitPropertyStore), 0, "IPropertyStore.Commit"));
        return 0;
    }

    /// <inheritdoc />
    public int SaveShortcut(IntPtr persistFile, string shortcutPath)
    {
        if (ConsumeFailure(ToastOperation.SaveShortcut))
        {
            _calls.Add(new ToastCall(nameof(SaveShortcut), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(SaveShortcut), 0, $"IPersistFile.Save(\"{shortcutPath}\")"));
        return 0;
    }

    /// <inheritdoc />
    public int OpenShellLink(string shortcutPath, out IntPtr shellLink)
    {
        if (ConsumeFailure(ToastOperation.OpenShellLink))
        {
            shellLink = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(OpenShellLink), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        shellLink = NextHandle();
        return RecordHandle(ToastOperation.OpenShellLink, shellLink, $"open(\"{shortcutPath}\")");
    }

    /// <inheritdoc />
    public int GetAppUserModelId(IntPtr propertyStore, out string? appUserModelId)
    {
        if (ConsumeFailure(ToastOperation.GetAppUserModelId))
        {
            appUserModelId = null;
            _calls.Add(new ToastCall(nameof(GetAppUserModelId), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        appUserModelId = AppUserModelIdToReadBack;
        _calls.Add(new ToastCall(
            nameof(GetAppUserModelId),
            0,
            AppUserModelIdToReadBack is null ? "vt=VT_EMPTY" : $"vt=VT_LPWSTR value=\"{AppUserModelIdToReadBack}\""));
        return 0;
    }

    /// <inheritdoc />
    public bool DeleteShortcut(string shortcutPath)
    {
        if (ConsumeFailure(ToastOperation.DeleteShortcut))
        {
            _calls.Add(new ToastCall(nameof(DeleteShortcut), LastErrorToReport, $"scripted failure win32={LastErrorToReport}"));
            return false;
        }

        _lastError = 0;
        _calls.Add(new ToastCall(nameof(DeleteShortcut), 0, $"delete(\"{shortcutPath}\")"));
        return true;
    }

    /// <inheritdoc />
    public int ReleaseHandle(IntPtr instance)
    {
        _calls.Add(new ToastCall(nameof(ReleaseHandle), 0, $"release=0x{instance.ToInt64():X}"));
        _releasedHandles.Add(instance);
        _lastError = 0;
        return 0;
    }

    /// <inheritdoc />
    public int GetLastError()
    {
        _calls.Add(new ToastCall(nameof(GetLastError), 0, $"error={_lastError}"));
        return _lastError;
    }

    /// <inheritdoc />
    public int GetToastNotificationManagerStatics(out IntPtr statics)
    {
        if (ConsumeFailure(ToastOperation.GetToastNotificationManagerStatics))
        {
            statics = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(GetToastNotificationManagerStatics), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        statics = NextHandle();
        return RecordHandle(ToastOperation.GetToastNotificationManagerStatics, statics, "ToastNotificationManager");
    }

    /// <inheritdoc />
    public int GetToastNotificationFactory(out IntPtr factory)
    {
        if (ConsumeFailure(ToastOperation.GetToastNotificationFactory))
        {
            factory = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(GetToastNotificationFactory), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        factory = NextHandle();
        return RecordHandle(ToastOperation.GetToastNotificationFactory, factory, "ToastNotification");
    }

    /// <inheritdoc />
    public int ActivateXmlDocument(out IntPtr xmlDocument)
    {
        if (ConsumeFailure(ToastOperation.ActivateXmlDocument))
        {
            xmlDocument = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(ActivateXmlDocument), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        xmlDocument = NextHandle();
        return RecordHandle(ToastOperation.ActivateXmlDocument, xmlDocument, "Windows.Data.Xml.Dom.XmlDocument");
    }

    /// <inheritdoc />
    public int CreateToastNotifier(IntPtr statics, string appUserModelId, out IntPtr notifier)
    {
        if (ConsumeFailure(ToastOperation.CreateToastNotifier))
        {
            notifier = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(CreateToastNotifier), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        notifier = NextHandle();
        return RecordHandle(ToastOperation.CreateToastNotifier, notifier, $"CreateToastNotifierWithId(\"{appUserModelId}\")");
    }

    /// <inheritdoc />
    public int GetNotifierSetting(IntPtr notifier, out int setting)
    {
        if (ConsumeFailure(ToastOperation.GetNotifierSetting))
        {
            setting = 0;
            _calls.Add(new ToastCall(nameof(GetNotifierSetting), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        setting = NotifierSettingToReport;
        _calls.Add(new ToastCall(nameof(GetNotifierSetting), 0, $"setting={NotifierSettingToReport}"));
        return 0;
    }

    /// <inheritdoc />
    public int LoadXml(IntPtr xmlDocument, string xml)
    {
        if (ConsumeFailure(ToastOperation.LoadXml))
        {
            _calls.Add(new ToastCall(nameof(LoadXml), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(LoadXml), 0, $"xml=\"{xml}\""));
        return 0;
    }

    /// <inheritdoc />
    public int CreateToastNotification(IntPtr factory, IntPtr xmlDocument, out IntPtr notification)
    {
        if (ConsumeFailure(ToastOperation.CreateToastNotification))
        {
            notification = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(CreateToastNotification), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        notification = NextHandle();
        return RecordHandle(ToastOperation.CreateToastNotification, notification, "CreateToastNotification");
    }

    /// <inheritdoc />
    public int SetNotificationTag(IntPtr notification, string tag)
    {
        if (ConsumeFailure(ToastOperation.SetNotificationTag))
        {
            _calls.Add(new ToastCall(nameof(SetNotificationTag), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(SetNotificationTag), 0, $"put_Tag=\"{tag}\""));
        return 0;
    }

    /// <inheritdoc />
    public int SetNotificationGroup(IntPtr notification, string group)
    {
        if (ConsumeFailure(ToastOperation.SetNotificationGroup))
        {
            _calls.Add(new ToastCall(nameof(SetNotificationGroup), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(SetNotificationGroup), 0, $"put_Group=\"{group}\""));
        return 0;
    }

    /// <inheritdoc />
    public int CreateDateTimePropertyValue(long winrtUniversalTime, out IntPtr propertyValue)
    {
        if (ConsumeFailure(ToastOperation.CreateDateTimePropertyValue))
        {
            propertyValue = IntPtr.Zero;
            _calls.Add(new ToastCall(nameof(CreateDateTimePropertyValue), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        propertyValue = NextHandle();
        return RecordHandle(ToastOperation.CreateDateTimePropertyValue, propertyValue, $"CreateDateTime(universalTime={winrtUniversalTime})");
    }

    /// <inheritdoc />
    public int SetNotificationExpirationTime(IntPtr notification, IntPtr propertyValue)
    {
        if (ConsumeFailure(ToastOperation.SetNotificationExpirationTime))
        {
            _calls.Add(new ToastCall(nameof(SetNotificationExpirationTime), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(SetNotificationExpirationTime), 0, $"put_ExpirationTime(propertyValue=0x{propertyValue.ToInt64():X})"));
        return 0;
    }

    /// <inheritdoc />
    public int Show(IntPtr notifier, IntPtr notification)
    {
        if (ConsumeFailure(ToastOperation.Show))
        {
            _calls.Add(new ToastCall(nameof(Show), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _calls.Add(new ToastCall(nameof(Show), 0, $"Show(toast=0x{notification.ToInt64():X})"));
        return 0;
    }

    /// <inheritdoc />
    public int SubscribeActivated(IntPtr notification, ToastActivatedHandler handler, out long token)
    {
        token = 0;
        if (ConsumeFailure(ToastOperation.SubscribeActivated))
        {
            _calls.Add(new ToastCall(nameof(SubscribeActivated), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        token = _nextToken++;
        _handlers[token] = handler;
        _subscriptionTokens[ToastOperation.SubscribeActivated] = token;
        _subscribedTokens.Add(token);

        if (CaptureHandlers)
        {
            _capturedHandlers[ToastOperation.SubscribeActivated] = handler;
        }

        _calls.Add(new ToastCall(nameof(SubscribeActivated), 0, $"add_Activated token=0x{token:X}"));
        return 0;
    }

    /// <inheritdoc />
    public int SubscribeDismissed(IntPtr notification, ToastDismissedHandler handler, out long token)
    {
        token = 0;
        if (ConsumeFailure(ToastOperation.SubscribeDismissed))
        {
            _calls.Add(new ToastCall(nameof(SubscribeDismissed), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        token = _nextToken++;
        _handlers[token] = handler;
        _subscriptionTokens[ToastOperation.SubscribeDismissed] = token;
        _subscribedTokens.Add(token);

        if (CaptureHandlers)
        {
            _capturedHandlers[ToastOperation.SubscribeDismissed] = handler;
        }

        _calls.Add(new ToastCall(nameof(SubscribeDismissed), 0, $"add_Dismissed token=0x{token:X}"));
        return 0;
    }

    /// <inheritdoc />
    public int SubscribeFailed(IntPtr notification, ToastFailedHandler handler, out long token)
    {
        token = 0;
        if (ConsumeFailure(ToastOperation.SubscribeFailed))
        {
            _calls.Add(new ToastCall(nameof(SubscribeFailed), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        token = _nextToken++;
        _handlers[token] = handler;
        _subscriptionTokens[ToastOperation.SubscribeFailed] = token;
        _subscribedTokens.Add(token);

        if (CaptureHandlers)
        {
            _capturedHandlers[ToastOperation.SubscribeFailed] = handler;
        }

        _calls.Add(new ToastCall(nameof(SubscribeFailed), 0, $"add_Failed token=0x{token:X}"));
        return 0;
    }

    /// <inheritdoc />
    public int UnsubscribeActivated(IntPtr notification, long token)
    {
        if (ConsumeFailure(ToastOperation.UnsubscribeActivated))
        {
            _calls.Add(new ToastCall(nameof(UnsubscribeActivated), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _handlers.Remove(token);
        _unsubscribedTokens.Add(token);
        _unsubscribeCalls.Add((nameof(UnsubscribeActivated), token));
        _calls.Add(new ToastCall(nameof(UnsubscribeActivated), 0, $"remove_Activated token=0x{token:X}"));
        return 0;
    }

    /// <inheritdoc />
    public int UnsubscribeDismissed(IntPtr notification, long token)
    {
        if (ConsumeFailure(ToastOperation.UnsubscribeDismissed))
        {
            _calls.Add(new ToastCall(nameof(UnsubscribeDismissed), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _handlers.Remove(token);
        _unsubscribedTokens.Add(token);
        _unsubscribeCalls.Add((nameof(UnsubscribeDismissed), token));
        _calls.Add(new ToastCall(nameof(UnsubscribeDismissed), 0, $"remove_Dismissed token=0x{token:X}"));
        return 0;
    }

    /// <inheritdoc />
    public int UnsubscribeFailed(IntPtr notification, long token)
    {
        if (ConsumeFailure(ToastOperation.UnsubscribeFailed))
        {
            _calls.Add(new ToastCall(nameof(UnsubscribeFailed), FailureHResult, "scripted failure"));
            return FailureHResult;
        }

        _handlers.Remove(token);
        _unsubscribedTokens.Add(token);
        _unsubscribeCalls.Add((nameof(UnsubscribeFailed), token));
        _calls.Add(new ToastCall(nameof(UnsubscribeFailed), 0, $"remove_Failed token=0x{token:X}"));
        return 0;
    }
}
