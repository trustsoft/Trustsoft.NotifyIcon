using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The <see cref="IShellApi"/> members that <see cref="FakeShellApi"/> can script to fail and
/// that appear in its call log.
/// </summary>
/// <remarks>
/// Member names mirror <see cref="IShellApi"/> one-for-one so a scripted failure reads like the
/// call it replaces. <see cref="ShellCall.Operation"/> carries the same name as a string.
/// </remarks>
internal enum ShellOperation
{
    /// <summary><see cref="IShellApi.ShellNotifyIcon"/>.</summary>
    ShellNotifyIcon,

    /// <summary><see cref="IShellApi.RegisterWindowMessage"/>.</summary>
    RegisterWindowMessage,

    /// <summary><see cref="IShellApi.CreateIconIndirect"/>.</summary>
    CreateIconIndirect,

    /// <summary><see cref="IShellApi.DestroyIcon"/>.</summary>
    DestroyIcon,

    /// <summary><see cref="IShellApi.DeleteObject"/>.</summary>
    DeleteObject,

    /// <summary><see cref="IShellApi.GetGuiResources"/>.</summary>
    GetGuiResources,

    /// <summary><see cref="IShellApi.GetLastError"/>.</summary>
    GetLastError,
}

/// <summary>
/// One recorded seam call, in the exact shape the lifecycle and negative tests assert against.
/// </summary>
/// <param name="Operation">
/// The <see cref="IShellApi"/> member name, equal to the corresponding
/// <see cref="ShellOperation"/> name, so
/// <c>call.Operation == nameof(IShellApi.ShellNotifyIcon)</c> holds.
/// </param>
/// <param name="Message">
/// The shell operation code (<c>NIM_*</c>) for
/// <see cref="IShellApi.ShellNotifyIcon"/>; <c>0</c> for every other operation.
/// </param>
/// <param name="Flags">
/// <see cref="NOTIFYICONDATAW.uFlags"/> for <see cref="IShellApi.ShellNotifyIcon"/>; the
/// <c>uiFlags</c> argument for <see cref="IShellApi.GetGuiResources"/>; <c>1</c>/<c>0</c> for
/// <see cref="IShellApi.CreateIconIndirect"/> reflecting <c>ICONINFO.fIcon</c>; <c>0</c>
/// otherwise.
/// </param>
/// <param name="Detail">
/// Human-readable summary for diagnostics and failure reports. <b>Not an assertion
/// contract</b> - its wording may change. Assert on <see cref="Message"/>, <see cref="Flags"/>,
/// <see cref="FakeShellApi.ShellNotifyIconDataSnapshots"/>,
/// <see cref="FakeShellApi.RegisteredMessages"/> and the counters instead.
/// </param>
/// <remarks>
/// The whole point of the record is that the <c>NIM_ADD</c> then
/// <c>NIM_SETVERSION(4)</c> sequence and the <c>NIF_SHOWTIP</c> bit are observable without a
/// notification area, without a shell and without poking at the structure by reflection.
/// </remarks>
internal readonly record struct ShellCall(string Operation, uint Message, uint Flags, string Detail)
{
    /// <summary>Builds the record for a <see cref="IShellApi.ShellNotifyIcon"/> call.</summary>
    /// <param name="dwMessage">The shell operation code.</param>
    /// <param name="data">The structure handed to the shell.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data) =>
        new(
            nameof(IShellApi.ShellNotifyIcon),
            dwMessage,
            data.uFlags,
            $"uID={data.uID}; hIcon=0x{data.hIcon.ToInt64():X}; version={data.uTimeoutOrVersion}");

    /// <summary>Builds the record for a <see cref="IShellApi.RegisterWindowMessage"/> call.</summary>
    /// <param name="message">The message name that was requested.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromRegisterWindowMessage(string message) =>
        new(nameof(IShellApi.RegisterWindowMessage), 0, 0, $"message=\"{message}\"");

    /// <summary>Builds the record for a <see cref="IShellApi.CreateIconIndirect"/> call.</summary>
    /// <param name="iconInfo">The icon description handed to the API.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromCreateIconIndirect(ref ICONINFO iconInfo) =>
        new(
            nameof(IShellApi.CreateIconIndirect),
            0,
            iconInfo.fIcon ? 1u : 0u,
            $"hbmMask=0x{iconInfo.hbmMask.ToInt64():X}; hbmColor=0x{iconInfo.hbmColor.ToInt64():X}");

    /// <summary>Builds the record for a <see cref="IShellApi.DestroyIcon"/> call.</summary>
    /// <param name="hIcon">The icon handle being released.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromDestroyIcon(IntPtr hIcon) =>
        new(nameof(IShellApi.DestroyIcon), 0, 0, $"hIcon=0x{hIcon.ToInt64():X}");

    /// <summary>Builds the record for a <see cref="IShellApi.DeleteObject"/> call.</summary>
    /// <param name="hObject">The GDI object handle being released.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromDeleteObject(IntPtr hObject) =>
        new(nameof(IShellApi.DeleteObject), 0, 0, $"hObject=0x{hObject.ToInt64():X}");

    /// <summary>Builds the record for a <see cref="IShellApi.GetGuiResources"/> call.</summary>
    /// <param name="hProcess">The process handle being queried.</param>
    /// <param name="uiFlags">The counter selector.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromGetGuiResources(IntPtr hProcess, uint uiFlags) =>
        new(nameof(IShellApi.GetGuiResources), 0, uiFlags, $"hProcess=0x{hProcess.ToInt64():X}");

    /// <summary>Builds the record for a <see cref="IShellApi.GetLastError"/> call.</summary>
    /// <param name="error">The error code that was reported.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromGetLastError(int error) =>
        new(nameof(IShellApi.GetLastError), 0, 0, $"error={error}");
}

/// <summary>
/// A fully scripted, recording <see cref="IShellApi"/> that never touches the real shell.
/// </summary>
/// <remarks>
/// <para>
/// It produces three things the live shell cannot: a deterministic call sequence, injectable
/// failures (a real <c>Shell_NotifyIconW</c> failure cannot be forced, and a test host may have
/// no notification area at all) and exact ownership counters. Every call is appended to
/// <see cref="Calls"/> in order before any result is decided, so a throwing or failing call is
/// never invisible in the log.
/// </para>
/// <para>
/// <b>Scripted failures.</b> <see cref="FailNext"/> fails the next N calls to one operation and
/// then stops; <see cref="FailAlways"/> fails every call until cleared. A failing call returns
/// the failure value the real API would (<see langword="false"/>, <c>0</c>,
/// <see cref="IntPtr.Zero"/>) and reports <see cref="LastErrorToReport"/> through
/// <see cref="GetLastError"/>, exactly like a real failed call.
/// </para>
/// <para>
/// <b>Last error is deliberately strict.</b> A successful call clears the reported error to 0.
/// Real Win32 does not clear the last error on success, so this is strictly stricter than the
/// OS: a test that reads the error after a success fails here instead of passing on a stale
/// number that would not reproduce in production. Read the error only after a failure.
/// </para>
/// <para>
/// <b>Snapshots.</b> <see cref="ShellNotifyIconDataSnapshots"/> keeps a copy of each
/// <see cref="NOTIFYICONDATAW"/> as it was passed. Copying is right for a log (the caller's
/// struct keeps changing between calls) and is unrelated to the seam's <c>ref</c>-aliasing
/// requirement, which the real implementation satisfies by passing the caller's own instance
/// straight to the shell.
/// </para>
/// </remarks>
internal sealed class FakeShellApi : IShellApi
{
    /// <summary>The message id <see cref="RegisterWindowMessage"/> returns when it succeeds.</summary>
    /// <remarks>
    /// A plausible registered-message id: the range the shell uses for
    /// <c>RegisterWindowMessageW</c> results is 0xC000-0xFFFF on Windows. Non-zero, because 0 is
    /// the documented failure value and a consumer must be able to tell them apart.
    /// </remarks>
    internal const uint DefaultRegisteredMessageId = 0xC0DE;

    /// <summary>The first icon handle returned by <see cref="CreateIconIndirect"/>.</summary>
    internal static readonly IntPtr DefaultIconHandle = new(0x1001);

    private readonly List<ShellCall> _calls = [];
    private readonly List<NOTIFYICONDATAW> _shellNotifyIconData = [];
    private readonly List<IntPtr> _createdIconHandles = [];
    private readonly List<string> _registeredMessages = [];
    private readonly Dictionary<ShellOperation, int> _remainingFailures = [];
    private readonly HashSet<ShellOperation> _permanentFailures = [];

    private int _lastError;

    /// <summary>Gets every recorded call, in order.</summary>
    internal IReadOnlyList<ShellCall> Calls => _calls;

    /// <summary>Gets the recorded calls to <see cref="IShellApi.ShellNotifyIcon"/> only.</summary>
    /// <remarks>
    /// The convenience the lifecycle task needs: the <c>NIM_ADD</c> then
    /// <c>NIM_SETVERSION(4)</c> assertion is an assertion about this filtered sequence, and it
    /// must not be disturbed by interleaved <c>GetLastError</c> or other calls.
    /// </remarks>
    internal IReadOnlyList<ShellCall> ShellNotifyIconCalls =>
        [.. _calls.Where(call => call.Operation == nameof(IShellApi.ShellNotifyIcon))];

    /// <summary>
    /// Gets a copy of every <see cref="NOTIFYICONDATAW"/> passed to
    /// <see cref="IShellApi.ShellNotifyIcon"/>, in call order.
    /// </summary>
    /// <remarks>
    /// Use this for assertions about structure contents that the flat
    /// <see cref="ShellCall"/> shape cannot carry - the protocol version written for
    /// <c>NIM_SETVERSION</c>, the tooltip text, the callback message id.
    /// </remarks>
    internal IReadOnlyList<NOTIFYICONDATAW> ShellNotifyIconDataSnapshots => _shellNotifyIconData;

    /// <summary>Gets the handles returned by successful <see cref="IShellApi.CreateIconIndirect"/> calls.</summary>
    internal IReadOnlyList<IntPtr> CreatedIconHandles => _createdIconHandles;

    /// <summary>Gets the message names passed to <see cref="IShellApi.RegisterWindowMessage"/>, in call order.</summary>
    internal IReadOnlyList<string> RegisteredMessages => _registeredMessages;

    /// <summary>Gets the number of icons successfully created.</summary>
    /// <remarks>Failed creations are not counted; use <see cref="Calls"/> for attempts.</remarks>
    internal int CreatedIcons => _createdIconHandles.Count;

    /// <summary>Gets the number of successful <see cref="IShellApi.DestroyIcon"/> calls.</summary>
    internal int DestroyedIcons { get; private set; }

    /// <summary>Gets the number of successful <see cref="IShellApi.DeleteObject"/> calls.</summary>
    /// <remarks>
    /// This is the direct evidence that the conversion factory's bitmap cleanup ran: one
    /// success plus one failure path must produce exactly two deletes per conversion, and a
    /// count assertion on the seam cannot be satisfied by accident the way a counter delta can.
    /// </remarks>
    internal int DeletedObjects { get; private set; }

    /// <summary>
    /// Gets the icons created but not yet destroyed, as
    /// <see cref="CreatedIcons"/> minus <see cref="DestroyedIcons"/>.
    /// </summary>
    /// <remarks>
    /// The ownership leak indicator for HICONs: a lifecycle that replaces or unregisters icons
    /// while driving this to zero - and back to zero after disposal - is not leaking them.
    /// </remarks>
    internal int OutstandingIcons => CreatedIcons - DestroyedIcons;

    /// <summary>
    /// Gets or sets the Win32 error code <see cref="GetLastError"/> reports after a scripted
    /// failure.
    /// </summary>
    /// <remarks>
    /// 87 (<c>ERROR_INVALID_PARAMETER</c>) by default because that is the code the shell
    /// typically reports for a malformed call; tests that assert a specific code
    /// propagating into <c>TrayIconException</c> usually set it explicitly (5 =
    /// <c>ERROR_ACCESS_DENIED</c> is the other common choice).
    /// </remarks>
    internal int LastErrorToReport { get; set; } = 87;

    /// <summary>Gets or sets the message id <see cref="IShellApi.RegisterWindowMessage"/> returns on success.</summary>
    internal uint RegisteredMessageId { get; set; } = DefaultRegisteredMessageId;

    /// <summary>
    /// Gets or sets the handle the next successful <see cref="IShellApi.CreateIconIndirect"/>
    /// will return.
    /// </summary>
    /// <remarks>
    /// Incremented after every success so each created icon gets a distinct handle: the
    /// lifecycle tests have to compare "the previously retained HICON" against "the newly
    /// converted HICON", which is impossible if every creation returns the same value.
    /// </remarks>
    internal IntPtr NextIconHandle { get; set; } = DefaultIconHandle;

    /// <summary>Gets or sets the count <see cref="IShellApi.GetGuiResources"/> returns.</summary>
    internal uint GdiObjectCount { get; set; } = 7;

    /// <summary>Scripts the next call to one operation to fail, once.</summary>
    /// <param name="operation">The operation that should fail.</param>
    /// <remarks>
    /// Repeated calls queue up: two <see cref="FailNext"/> calls for the same operation fail the
    /// next two calls. A call to a different operation does not consume the scheduled failure,
    /// so <c>FailNext(ShellNotifyIcon)</c> means "the next <c>ShellNotifyIcon</c> call fails".
    /// </remarks>
    internal void FailNext(ShellOperation operation) =>
        _remainingFailures[operation] = _remainingFailures.GetValueOrDefault(operation) + 1;

    /// <summary>Scripts every call to one operation to fail until <see cref="StopFailingAlways"/>.</summary>
    /// <param name="operation">The operation that should always fail.</param>
    /// <remarks>
    /// This is the "the shell will not cooperate" case: a retry must retry and then give up
    /// rather than loop, so a test needs the failure to survive the retry.
    /// </remarks>
    internal void FailAlways(ShellOperation operation) => _permanentFailures.Add(operation);

    /// <summary>Stops the permanent failure scripted by <see cref="FailAlways"/>.</summary>
    /// <param name="operation">The operation that should succeed again.</param>
    internal void StopFailingAlways(ShellOperation operation) => _permanentFailures.Remove(operation);

    /// <summary>Removes every scripted failure, one-shot and permanent.</summary>
    internal void ClearScriptedFailures()
    {
        _remainingFailures.Clear();
        _permanentFailures.Clear();
    }

    /// <inheritdoc />
    public bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data)
    {
        _calls.Add(ShellCall.FromShellNotifyIcon(dwMessage, ref data));
        _shellNotifyIconData.Add(data);

        if (ConsumeFailure(ShellOperation.ShellNotifyIcon))
        {
            return false;
        }

        _lastError = 0;
        return true;
    }

    /// <inheritdoc />
    public uint RegisterWindowMessage(string message)
    {
        _calls.Add(ShellCall.FromRegisterWindowMessage(message));
        _registeredMessages.Add(message);

        if (ConsumeFailure(ShellOperation.RegisterWindowMessage))
        {
            return 0;
        }

        _lastError = 0;
        return RegisteredMessageId;
    }

    /// <inheritdoc />
    public IntPtr CreateIconIndirect(ref ICONINFO iconInfo)
    {
        _calls.Add(ShellCall.FromCreateIconIndirect(ref iconInfo));

        if (ConsumeFailure(ShellOperation.CreateIconIndirect))
        {
            return IntPtr.Zero;
        }

        _lastError = 0;
        IntPtr handle = NextIconHandle;
        _createdIconHandles.Add(handle);
        NextIconHandle = new IntPtr(handle.ToInt64() + 1);
        return handle;
    }

    /// <inheritdoc />
    public bool DestroyIcon(IntPtr hIcon)
    {
        _calls.Add(ShellCall.FromDestroyIcon(hIcon));

        if (ConsumeFailure(ShellOperation.DestroyIcon))
        {
            return false;
        }

        _lastError = 0;
        DestroyedIcons++;
        return true;
    }

    /// <inheritdoc />
    public bool DeleteObject(IntPtr hObject)
    {
        _calls.Add(ShellCall.FromDeleteObject(hObject));

        if (ConsumeFailure(ShellOperation.DeleteObject))
        {
            return false;
        }

        _lastError = 0;
        DeletedObjects++;
        return true;
    }

    /// <inheritdoc />
    public uint GetGuiResources(IntPtr hProcess, uint uiFlags)
    {
        _calls.Add(ShellCall.FromGetGuiResources(hProcess, uiFlags));

        if (ConsumeFailure(ShellOperation.GetGuiResources))
        {
            return 0;
        }

        _lastError = 0;
        return GdiObjectCount;
    }

    /// <inheritdoc />
    public int GetLastError()
    {
        _calls.Add(ShellCall.FromGetLastError(_lastError));
        return _lastError;
    }

    /// <summary>
    /// Decides whether this call fails, consuming a queued one-shot failure when it does.
    /// </summary>
    /// <param name="operation">The operation being invoked.</param>
    /// <returns><see langword="true"/> when the call is scripted to fail.</returns>
    private bool ConsumeFailure(ShellOperation operation)
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

        return false;
    }
}
