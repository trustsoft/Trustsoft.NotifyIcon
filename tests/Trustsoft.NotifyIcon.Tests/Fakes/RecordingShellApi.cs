using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// An <see cref="IShellApi"/> that records every call and delegates it to a real
/// <see cref="ShellApi"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half-live probe.</b> The rest of the suite runs against
/// <see cref="FakeShellApi"/>, so a wrong <c>EntryPoint</c>, a wrong <c>CharSet</c>, a missing
/// <c>SetLastError</c> or a calling-convention mistake in <c>ShellApi</c> would pass every other
/// test and fail only in a consumer's process. Wrapping the real implementation means the same
/// call shapes the lifecycle uses are exercised against the actual DLL export, and the recorded
/// log proves which call was made.
/// </para>
/// <para>
/// <b>Use it only in tests that are explicitly about signature agreement</b>, and never to
/// perform a real <c>NIM_ADD</c>: putting an icon in the developer's notification area from a
/// unit test is not acceptable. Side-effect-free calls (registering a message name, creating and
/// deleting a private DIB section, reading the process GDI count) and read-only failures are the
/// intended surface.
/// </para>
/// <para>
/// Delegation preserves <c>ref</c> aliasing: the caller's own <see cref="NOTIFYICONDATAW"/> and
/// <see cref="ICONINFO"/> instances are handed to the real implementation unchanged, so a
/// wrapper cannot hide a field mutation from the caller.
/// </para>
/// <para>
/// <b><see cref="ShellNotifyIconGetRect"/> is recorded after the call, not before.</b> Its status
/// is its <c>HRESULT</c>, and the seam contract makes a non-zero result a normal outcome rather
/// than an exception; recording the <c>HRESULT</c> in the log is what makes the delegation
/// assertion possible ("the wrapped real call really was attempted through this wrapper"). An
/// <c>EntryPointNotFoundException</c> from the real implementation still propagates, and it
/// names the entry point that failed.
/// </para>
/// </remarks>
internal sealed class RecordingShellApi : IShellApi
{
    private readonly ShellApi _inner;
    private readonly List<ShellCall> _calls = [];

    /// <summary>Initializes a new instance of the <see cref="RecordingShellApi"/> class over a fresh real <see cref="ShellApi"/>.</summary>
    internal RecordingShellApi()
        : this(new ShellApi())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RecordingShellApi"/> class.</summary>
    /// <param name="inner">The real implementation every call is delegated to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    internal RecordingShellApi(ShellApi inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Gets every recorded call, in order.</summary>
    /// <remarks>
    /// A call is recorded before it is delegated, so a call that throws (for example because an
    /// entry point does not exist) still appears in the log next to the exception that named it.
    /// </remarks>
    internal IReadOnlyList<ShellCall> Calls => _calls;

    /// <inheritdoc />
    public bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data)
    {
        _calls.Add(ShellCall.FromShellNotifyIcon(dwMessage, ref data));
        return _inner.ShellNotifyIcon(dwMessage, ref data);
    }

    /// <inheritdoc />
    public int ShellNotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out NativeRect rectangle)
    {
        // Recorded before delegation, so an entry point that does not exist on this Windows build
        // still leaves a log entry next to the exception that named it.
        int result = _inner.ShellNotifyIconGetRect(ref identifier, out rectangle);
        _calls.Add(ShellCall.FromShellNotifyIconGetRect(ref identifier, result));
        return result;
    }

    /// <inheritdoc />
    public uint RegisterWindowMessage(string message)
    {
        _calls.Add(ShellCall.FromRegisterWindowMessage(message));
        return _inner.RegisterWindowMessage(message);
    }

    /// <inheritdoc />
    public IntPtr CreateIconIndirect(ref ICONINFO iconInfo)
    {
        _calls.Add(ShellCall.FromCreateIconIndirect(ref iconInfo));
        return _inner.CreateIconIndirect(ref iconInfo);
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER header, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        _calls.Add(ShellCall.FromCreateDibSection(ref header, usage));
        return _inner.CreateDIBSection(hdc, ref header, usage, out bits, hSection, offset);
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bitmapInfo, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        _calls.Add(ShellCall.FromCreateDibSection(ref bitmapInfo, usage));
        return _inner.CreateDIBSection(hdc, ref bitmapInfo, usage, out bits, hSection, offset);
    }

    /// <inheritdoc />
    public bool DestroyIcon(IntPtr hIcon)
    {
        _calls.Add(ShellCall.FromDestroyIcon(hIcon));
        return _inner.DestroyIcon(hIcon);
    }

    /// <inheritdoc />
    public bool DeleteObject(IntPtr hObject)
    {
        _calls.Add(ShellCall.FromDeleteObject(hObject));
        return _inner.DeleteObject(hObject);
    }

    /// <inheritdoc />
    public uint GetGuiResources(IntPtr hProcess, uint uiFlags)
    {
        _calls.Add(ShellCall.FromGetGuiResources(hProcess, uiFlags));
        return _inner.GetGuiResources(hProcess, uiFlags);
    }

    /// <inheritdoc />
    public int GetLastError()
    {
        int error = _inner.GetLastError();
        _calls.Add(ShellCall.FromGetLastError(error));
        return error;
    }
}
