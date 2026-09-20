using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The <see cref="IShellApi"/> members that appear in <see cref="FakeShellApi"/>'s call log,
/// including the ones that can also be scripted to fail.
/// </summary>
/// <remarks>
/// Member names mirror <see cref="IShellApi"/> one-for-one so a scripted failure reads like the
/// call it replaces. <see cref="ShellCall.Operation"/> carries the same name as a string.
/// </remarks>
internal enum ShellOperation
{
    /// <summary><see cref="IShellApi.ShellNotifyIcon"/>.</summary>
    ShellNotifyIcon,

    /// <summary>
    /// <see cref="IShellApi.ShellNotifyIconGetRect"/>. It appears in the call log like every other
    /// member, but the scripted-failure machinery (<see cref="FakeShellApi.FailNext"/>,
    /// <see cref="FakeShellApi.FailAlways"/>, <see cref="FakeShellApi.LastErrorToReport"/>) does not
    /// apply to it: its status channel is an <c>HRESULT</c>, which is scripted directly through
    /// <see cref="FakeShellApi.GetRectResult"/>. A Win32 error code such as 87 is not a valid
    /// failure <c>HRESULT</c>, so routing it through the last-error value would script a success
    /// code as a failure.
    /// </summary>
    ShellNotifyIconGetRect,

    /// <summary><see cref="IShellApi.RegisterWindowMessage"/>.</summary>
    RegisterWindowMessage,

    /// <summary><see cref="IShellApi.CreateIconIndirect"/>.</summary>
    CreateIconIndirect,

    /// <summary>Both <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPV5HEADER, uint, out IntPtr, IntPtr, uint)"/>
    /// overloads: one native export, two typed views.</summary>
    CreateDIBSection,

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
/// <see cref="FakeShellApi.ShellNotifyIconGetRectIdentifiers"/>,
/// <see cref="FakeShellApi.RegisteredMessages"/> and the counters instead.
/// </param>
/// <param name="ThreadId">
/// The managed thread id of the caller that made the call, which is the only way to prove the
/// marshalling contract (R015): a property set from a background thread must reach the seam on the
/// UI thread, and a test can see that by comparing this value with the dispatcher thread's id.
/// </param>
/// <remarks>
/// The whole point of the record is that the <c>NIM_ADD</c> then
/// <c>NIM_SETVERSION(4)</c> sequence and the <c>NIF_SHOWTIP</c> bit are observable without a
/// notification area, without a shell and without poking at the structure by reflection.
/// </remarks>
internal readonly record struct ShellCall(string Operation, uint Message, uint Flags, string Detail, int ThreadId)
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
            $"uID={data.uID}; hIcon=0x{data.hIcon.ToInt64():X}; version={data.uTimeoutOrVersion}",
            Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.ShellNotifyIconGetRect"/> call.</summary>
    /// <param name="identifier">The identifier the call was made with.</param>
    /// <param name="result">The <c>HRESULT</c> the call returned.</param>
    /// <returns>The recorded call.</returns>
    /// <remarks>
    /// The <c>HRESULT</c> is carried in <see cref="Detail"/> rather than in <see cref="Flags"/>,
    /// which is reserved for flag and selector values - an <c>HRESULT</c> is neither. Assert on
    /// the member's return value for the status and on
    /// <see cref="FakeShellApi.ShellNotifyIconGetRectIdentifiers"/> for the fields.
    /// </remarks>
    internal static ShellCall FromShellNotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, int result) =>
        new(
            nameof(IShellApi.ShellNotifyIconGetRect),
            0,
            0,
            $"hWnd=0x{identifier.hWnd.ToInt64():X}; uID={identifier.uID}; cbSize={identifier.cbSize}; hr=0x{result:X8}",
            Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.RegisterWindowMessage"/> call.</summary>
    /// <param name="message">The message name that was requested.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromRegisterWindowMessage(string message) =>
        new(nameof(IShellApi.RegisterWindowMessage), 0, 0, $"message=\"{message}\"", Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.CreateIconIndirect"/> call.</summary>
    /// <param name="iconInfo">The icon description handed to the API.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromCreateIconIndirect(ref ICONINFO iconInfo) =>
        new(
            nameof(IShellApi.CreateIconIndirect),
            0,
            iconInfo.fIcon ? 1u : 0u,
            $"hbmMask=0x{iconInfo.hbmMask.ToInt64():X}; hbmColor=0x{iconInfo.hbmColor.ToInt64():X}",
            Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPV5HEADER, uint, out IntPtr, IntPtr, uint)"/> call.</summary>
    /// <param name="header">The version-5 header describing the bitmap.</param>
    /// <param name="usage">The colour-table interpretation.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromCreateDibSection(ref BITMAPV5HEADER header, uint usage) =>
        new(
            nameof(IShellApi.CreateDIBSection),
            0,
            usage,
            $"V5 size={header.bV5Size}; width={header.bV5Width}; height={header.bV5Height}; bitCount={header.bV5BitCount}; sizeImage={header.bV5SizeImage}",
            Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPINFO, uint, out IntPtr, IntPtr, uint)"/> call.</summary>
    /// <param name="bitmapInfo">The version-3 header and colour table describing the bitmap.</param>
    /// <param name="usage">The colour-table interpretation.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromCreateDibSection(ref BITMAPINFO bitmapInfo, uint usage) =>
        new(
            nameof(IShellApi.CreateDIBSection),
            0,
            usage,
            $"V3 size={bitmapInfo.bmiHeader.biSize}; width={bitmapInfo.bmiHeader.biWidth}; height={bitmapInfo.bmiHeader.biHeight}; bitCount={bitmapInfo.bmiHeader.biBitCount}; sizeImage={bitmapInfo.bmiHeader.biSizeImage}",
            Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.DestroyIcon"/> call.</summary>
    /// <param name="hIcon">The icon handle being released.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromDestroyIcon(IntPtr hIcon) =>
        new(nameof(IShellApi.DestroyIcon), 0, 0, $"hIcon=0x{hIcon.ToInt64():X}", Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.DeleteObject"/> call.</summary>
    /// <param name="hObject">The GDI object handle being released.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromDeleteObject(IntPtr hObject) =>
        new(nameof(IShellApi.DeleteObject), 0, 0, $"hObject=0x{hObject.ToInt64():X}", Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.GetGuiResources"/> call.</summary>
    /// <param name="hProcess">The process handle being queried.</param>
    /// <param name="uiFlags">The counter selector.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromGetGuiResources(IntPtr hProcess, uint uiFlags) =>
        new(nameof(IShellApi.GetGuiResources), 0, uiFlags, $"hProcess=0x{hProcess.ToInt64():X}", Environment.CurrentManagedThreadId);

    /// <summary>Builds the record for a <see cref="IShellApi.GetLastError"/> call.</summary>
    /// <param name="error">The error code that was reported.</param>
    /// <returns>The recorded call.</returns>
    internal static ShellCall FromGetLastError(int error) =>
        new(nameof(IShellApi.GetLastError), 0, 0, $"error={error}", Environment.CurrentManagedThreadId);
}

/// <summary>
/// A fully scripted, recording <see cref="IShellApi"/> that never touches the real shell.
/// </summary>
/// <remarks>
/// <para>
/// It produces three things the live shell cannot: a deterministic call sequence, injectable
/// failures (a real <c>Shell_NotifyIconW</c> failure cannot be forced, and a test host may have
/// no notification area at all) and exact ownership counters. Every call is appended to
/// <see cref="Calls"/> in order, and <see cref="IShellApi.ShellNotifyIconGetRect"/> is appended with
/// the <c>HRESULT</c> it reported, so a throwing or failing call is never invisible in the log.
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
/// <para>
/// <b>DIB sections are emulated, and deliberately not with real GDI objects.</b>
/// <c>CreateDIBSection</c> allocates zero-filled unmanaged memory for the pixels and returns a
/// distinct fake handle, and <c>DeleteObject</c> captures that memory's contents before
/// releasing it (<see cref="DibSectionRequest.ReleasedContent"/>). That means a conversion driven
/// by this fake allocates <em>no</em> real GDI object at all, which is what makes a
/// handle-count assertion in a unit test meaningful: the count can only stay flat if nothing
/// bypassed the seam, and the release calls are still counted exactly.
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
    private readonly List<NOTIFYICONIDENTIFIER> _shellNotifyIconGetRectData = [];
    private readonly List<IntPtr> _createdIconHandles = [];
    private readonly List<IntPtr> _destroyedIconHandles = [];
    private readonly List<string> _registeredMessages = [];
    private readonly List<DibSectionRequest> _dibSections = [];
    private readonly Dictionary<IntPtr, EmulatedDib> _emulatedDibs = [];
    private readonly Dictionary<ShellOperation, int> _remainingFailures = [];
    private readonly Dictionary<ShellOperation, int> _callCounts = [];
    private readonly Dictionary<(ShellOperation Operation, int CallNumber), bool> _ordinalFailures = [];
    private readonly HashSet<ShellOperation> _permanentFailures = [];

    private IntPtr _nextDibSectionHandle = new(0x2001);
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
    /// Gets a copy of every <see cref="NOTIFYICONIDENTIFIER"/> passed to
    /// <see cref="IShellApi.ShellNotifyIconGetRect"/>, in call order.
    /// </summary>
    /// <remarks>
    /// The assertion surface for the placement task: the identifier is what tells the shell which
    /// icon to locate, so "the seam was asked about the host window and the registered id" is an
    /// assertion about these values. Like <see cref="ShellNotifyIconDataSnapshots"/> this is a copy
    /// taken at call time, which is a log property and not the seam's <c>ref</c>-aliasing rule;
    /// <see cref="IdentifierIdWriteBack"/> is the knob that covers the aliasing.
    /// </remarks>
    internal IReadOnlyList<NOTIFYICONIDENTIFIER> ShellNotifyIconGetRectIdentifiers => _shellNotifyIconGetRectData;

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

    /// <summary>
    /// Gets one entry per successful <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPV5HEADER, uint, out IntPtr, IntPtr, uint)"/>
    /// call, in call order: the colour bitmap first, then the monochrome mask, per conversion.
    /// </summary>
    /// <remarks>
    /// The recorded headers are the evidence that the DIBs were described correctly - top-down
    /// (negative height), 32bpp for the colour bitmap, 1bpp with a two-byte-rounded row stride
    /// for the mask. <see cref="DibSectionRequest.ReleasedContent"/> additionally holds the
    /// bytes the bitmap contained at the moment the factory released it, which is how a test can
    /// assert on what the mask actually said without owning the GDI object.
    /// </remarks>
    internal IReadOnlyList<DibSectionRequest> DibSectionRequests => _dibSections;

    /// <summary>Gets the number of icons successfully created.</summary>
    /// <remarks>Failed creations are not counted; use <see cref="Calls"/> for attempts.</remarks>
    internal int CreatedIcons => _createdIconHandles.Count;

    /// <summary>Gets the number of successful <see cref="IShellApi.DestroyIcon"/> calls.</summary>
    internal int DestroyedIcons { get; private set; }

    /// <summary>
    /// Gets the icon handles released by successful <see cref="IShellApi.DestroyIcon"/> calls, in
    /// call order.
    /// </summary>
    /// <remarks>
    /// A count cannot express which handle was released, and the retain-and-destroy rule is
    /// exactly a statement about identities: after a <em>failed</em> replacement the previously
    /// registered handle must still be alive, which a test can only assert by looking for its
    /// absence here.
    /// </remarks>
    internal IReadOnlyList<IntPtr> DestroyedIconHandles => _destroyedIconHandles;

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

    /// <summary>
    /// Gets or sets the <c>HRESULT</c> <see cref="IShellApi.ShellNotifyIconGetRect"/> returns.
    /// </summary>
    /// <remarks>
    /// The default is <c>E_FAIL</c> (<c>0x80004005</c>), a real failure code: the live case this
    /// call has to survive is "the shell could not locate the icon" (it is in the overflow
    /// flyout, or hidden), and a default of <c>S_OK</c> would let a caller that ignores the result
    /// pass its own tests. Set it to <c>0</c> for the success path.
    /// </remarks>
    internal int GetRectResult { get; set; } = unchecked((int)0x80004005);

    /// <summary>
    /// Gets or sets the rectangle a succeeding <see cref="IShellApi.ShellNotifyIconGetRect"/> hands
    /// back through its <c>out</c> parameter.
    /// </summary>
    /// <remarks>
    /// Used only when <see cref="GetRectResult"/> is <c>0</c>; a failure writes an empty
    /// rectangle, because the call located no icon.
    /// </remarks>
    internal NativeRect GetRectRectangle { get; set; }

    /// <summary>
    /// Gets or sets a value the fake writes into the caller's identifier through the <c>ref</c>
    /// parameter. <see langword="null"/> (the default) writes nothing, which is what the real
    /// <c>const</c> input does.
    /// </summary>
    /// <remarks>
    /// This is the aliasing assertion for <see cref="IShellApi.ShellNotifyIconGetRect"/>: the write
    /// happens inside the fake, so the caller can observe it on its own instance only if the seam
    /// passes the identifier by reference. A by-value signature would still compile, still record
    /// the call and still return the same <c>HRESULT</c>; this knob is what distinguishes the two.
    /// </remarks>
    internal uint? IdentifierIdWriteBack { get; set; }

    /// <summary>Scripts the next call to one operation to fail, once.</summary>
    /// <param name="operation">The operation that should fail.</param>
    /// <remarks>
    /// Repeated calls queue up: two <see cref="FailNext"/> calls for the same operation fail the
    /// next two calls. A call to a different operation does not consume the scheduled failure,
    /// so <c>FailNext(ShellNotifyIcon)</c> means "the next <c>ShellNotifyIcon</c> call fails".
    /// </remarks>
    internal void FailNext(ShellOperation operation) =>
        _remainingFailures[operation] = _remainingFailures.GetValueOrDefault(operation) + 1;

    /// <summary>Scripts the given 1-based call number of one operation to fail, once.</summary>
    /// <param name="operation">The operation whose call should fail.</param>
    /// <param name="callNumber">The 1-based ordinal of the call to that operation.</param>
    /// <remarks>
    /// <see cref="FailNext"/> cannot express "the second call to this operation fails", and that
    /// is exactly the case a partial-failure test needs: the conversion allocates two DIB
    /// sections, and the interesting failure - does the already-created colour bitmap still get
    /// released? - only exists when the <em>second</em> allocation fails. Counting is per
    /// operation, so calls to other operations do not shift the ordinal.
    /// </remarks>
    internal void FailCallNumber(ShellOperation operation, int callNumber) =>
        _ordinalFailures[(operation, callNumber)] = true;

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
        _ordinalFailures.Clear();
        _callCounts.Clear();
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
    public int ShellNotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out NativeRect rectangle)
    {
        _calls.Add(ShellCall.FromShellNotifyIconGetRect(ref identifier, GetRectResult));
        _shellNotifyIconGetRectData.Add(identifier);

        if (IdentifierIdWriteBack is uint writtenId)
        {
            // Written through the ref parameter: the caller sees this on its own instance only
            // because the seam passes the identifier by reference rather than by value.
            identifier.uID = writtenId;
        }

        if (GetRectResult != 0)
        {
            // Deliberately stricter than the OS: a failing HRESULT means no rectangle was
            // located, so the fake hands back an empty one instead of leaving uninitialised
            // numbers for a caller that forgets to check the result.
            rectangle = default;
            return GetRectResult;
        }

        rectangle = GetRectRectangle;
        return 0;
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
        _destroyedIconHandles.Add(hIcon);
        return true;
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER header, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        _calls.Add(ShellCall.FromCreateDibSection(ref header, usage));

        return AllocateDibSection(
            header.bV5Width,
            header.bV5Height,
            header.bV5BitCount,
            header.bV5SizeImage,
            usage,
            out bits);
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bitmapInfo, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        _calls.Add(ShellCall.FromCreateDibSection(ref bitmapInfo, usage));

        return AllocateDibSection(
            bitmapInfo.bmiHeader.biWidth,
            bitmapInfo.bmiHeader.biHeight,
            bitmapInfo.bmiHeader.biBitCount,
            bitmapInfo.bmiHeader.biSizeImage,
            usage,
            out bits);
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

        // A real DeleteObject releases the DIB section and its pixels. Emulating that keeps the
        // fake from leaking a heap allocation per conversion, and - more usefully - captures the
        // bytes the bitmap held at the moment it was released, which is the only window in which
        // a test can look at what the factory wrote into it.
        if (_emulatedDibs.Remove(hObject, out EmulatedDib? dib))
        {
            var content = new byte[dib.Size];
            Marshal.Copy(dib.Pixels, content, 0, dib.Size);

            for (int i = 0; i < _dibSections.Count; i++)
            {
                if (_dibSections[i].Handle == hObject)
                {
                    _dibSections[i].ReleasedContent = content;
                }
            }

            Marshal.FreeHGlobal(dib.Pixels);
        }

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
    /// Emulates one <c>CreateDIBSection</c>: allocates zero-filled unmanaged memory for the
    /// pixels (which is what a real DIB section starts as) and hands back a distinct fake
    /// handle.
    /// </summary>
    /// <param name="width">The bitmap width from the header.</param>
    /// <param name="height">The bitmap height from the header; not used for the size, because
    /// the caller's <paramref name="sizeImage"/> is what a real DIB section allocates.</param>
    /// <param name="bitCount">The bit depth from the header.</param>
    /// <param name="sizeImage">The pixel buffer size from the header.</param>
    /// <param name="usage">The colour-table interpretation.</param>
    /// <param name="bits">Receives the emulated pixel buffer address.</param>
    /// <returns>The fake handle, or <see cref="IntPtr.Zero"/> when the call is scripted to fail
    /// (in which case nothing is allocated and <paramref name="bits"/> is zero).</returns>
    private IntPtr AllocateDibSection(int width, int height, ushort bitCount, uint sizeImage, uint usage, out IntPtr bits)
    {
        int size = sizeImage > 0
            ? checked((int)sizeImage)
            : ComputeDibSectionSize(width, height, bitCount);

        IntPtr pixels = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, pixels, size);

        if (ConsumeFailure(ShellOperation.CreateDIBSection))
        {
            bits = IntPtr.Zero;
            Marshal.FreeHGlobal(pixels);
            return IntPtr.Zero;
        }

        _lastError = 0;

        IntPtr handle = _nextDibSectionHandle;
        _nextDibSectionHandle = new IntPtr(handle.ToInt64() + 1);
        _emulatedDibs[handle] = new EmulatedDib(pixels, size);
        _dibSections.Add(new DibSectionRequest(handle, pixels, width, height, bitCount, (uint)size, usage));

        bits = pixels;
        return handle;
    }

    /// <summary>Computes the pixel buffer size a real DIB section would allocate.</summary>
    /// <param name="width">The bitmap width in pixels.</param>
    /// <param name="height">The bitmap height in pixels; the sign is ignored.</param>
    /// <param name="bitCount">The bit depth.</param>
    /// <returns>The size in bytes.</returns>
    private static int ComputeDibSectionSize(int width, int height, ushort bitCount) =>
        ((width * bitCount + 15) / 16 * 2) * Math.Abs(height);

    /// <summary>
    /// Decides whether this call fails, consuming a queued one-shot failure when it does.
    /// </summary>
    /// <param name="operation">The operation being invoked.</param>
    /// <returns><see langword="true"/> when the call is scripted to fail.</returns>
    private bool ConsumeFailure(ShellOperation operation)
    {
        int callNumber = _callCounts.GetValueOrDefault(operation) + 1;
        _callCounts[operation] = callNumber;

        if (_permanentFailures.Contains(operation))
        {
            _lastError = LastErrorToReport;
            return true;
        }

        if (_ordinalFailures.Remove((operation, callNumber)))
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

    /// <summary>The unmanaged pixel buffer of one emulated DIB section.</summary>
    private sealed class EmulatedDib
    {
        /// <summary>Initializes a new instance of the <see cref="EmulatedDib"/> class.</summary>
        /// <param name="pixels">The pixel buffer address.</param>
        /// <param name="size">The buffer size in bytes.</param>
        internal EmulatedDib(IntPtr pixels, int size)
        {
            Pixels = pixels;
            Size = size;
        }

        /// <summary>Gets the address of the pixel buffer.</summary>
        internal IntPtr Pixels { get; }

        /// <summary>Gets the buffer size in bytes.</summary>
        internal int Size { get; }
    }
}

/// <summary>
/// One <c>CreateDIBSection</c> the fake served, with the header it was given and - once the
/// caller deletes the bitmap - the bytes that were in it at that moment.
/// </summary>
/// <remarks>
/// The header fields are copied out rather than kept as a reference to a caller's struct,
/// because the caller's struct is a local that changes between conversions. Asserting on these
/// values is how a test pins the things that are invisible in the returned handle: a negative
/// (top-down) height, 32bpp versus 1bpp, and the row stride the buffer was sized with.
/// </remarks>
internal sealed class DibSectionRequest
{
    /// <summary>Initializes a new instance of the <see cref="DibSectionRequest"/> class.</summary>
    /// <param name="handle">The fake <c>HBITMAP</c> handle.</param>
    /// <param name="pixels">The address of the emulated pixel buffer.</param>
    /// <param name="width">The bitmap width from the header.</param>
    /// <param name="height">The bitmap height from the header.</param>
    /// <param name="bitCount">The bit depth from the header.</param>
    /// <param name="sizeImage">The pixel buffer size from the header.</param>
    /// <param name="usage">The colour-table interpretation the caller asked for.</param>
    internal DibSectionRequest(IntPtr handle, IntPtr pixels, int width, int height, ushort bitCount, uint sizeImage, uint usage)
    {
        Handle = handle;
        Pixels = pixels;
        Width = width;
        Height = height;
        BitCount = bitCount;
        SizeImage = sizeImage;
        Usage = usage;
    }

    /// <summary>Gets the fake <c>HBITMAP</c> handle the call returned.</summary>
    internal IntPtr Handle { get; }

    /// <summary>Gets the address of the emulated pixel buffer.</summary>
    internal IntPtr Pixels { get; }

    /// <summary>Gets the bitmap width in pixels.</summary>
    internal int Width { get; }

    /// <summary>Gets the bitmap height in pixels; negative means top-down.</summary>
    internal int Height { get; }

    /// <summary>Gets the bit depth.</summary>
    internal ushort BitCount { get; }

    /// <summary>Gets the pixel buffer size in bytes as the caller declared it.</summary>
    internal uint SizeImage { get; }

    /// <summary>Gets the colour-table interpretation the caller asked for.</summary>
    internal uint Usage { get; }

    /// <summary>Gets the row stride in bytes as implied by the buffer size and the height.</summary>
    /// <remarks>Zero when the height is zero, which never happens for an icon bitmap.</remarks>
    internal int RowStride => Height == 0 ? 0 : (int)(SizeImage / (uint)Math.Abs(Height));

    /// <summary>
    /// Gets or sets the bytes the bitmap held when the caller deleted it - the only point at
    /// which a test can see what was written into the DIB section.
    /// </summary>
    internal byte[]? ReleasedContent { get; set; }
}
