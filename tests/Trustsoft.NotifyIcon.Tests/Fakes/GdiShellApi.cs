using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// An <see cref="IShellApi"/> that performs the <b>real</b> GDI and window-message work and refuses
/// to touch the notification area.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one assertion that neither other double can make: R007's headline claim is about
/// real GDI objects, and only a real <c>CreateDIBSection</c> / <c>CreateIconIndirect</c> /
/// <c>DestroyIcon</c> trio moves the process handle count that <see cref="GdiHandles.Count"/>
/// reads. <see cref="FakeShellApi"/> deliberately allocates no GDI object at all, and
/// <see cref="RecordingShellApi"/> would put a real icon in the developer's notification area,
/// which its own documentation forbids.
/// </para>
/// <para>
/// So the split is: <c>ShellNotifyIcon</c> is intercepted and reports success (or a scripted
/// failure) without calling the shell, while everything GDI goes to the real
/// <see cref="ShellApi"/>. <c>RegisterWindowMessage</c> is also real - it creates no visible
/// artifact, and using the real one keeps the host window's message id resolution on the real code
/// path.
/// </para>
/// <para>
/// The counters are the same seam-level evidence the scripted fake provides, so a test can assert
/// both "the handle identities balance" and "the process handle count came back down".
/// </para>
/// </remarks>
internal sealed class GdiShellApi : IShellApi
{
    private readonly ShellApi _real = new();
    private readonly List<uint> _shellNotifyIconMessages = [];
    private readonly List<NOTIFYICONDATAW> _shellNotifyIconData = [];

    /// <summary>Gets the <c>NIM_*</c> codes passed to <see cref="ShellNotifyIcon"/>, in order.</summary>
    internal IReadOnlyList<uint> ShellNotifyIconMessages => _shellNotifyIconMessages;

    /// <summary>Gets a copy of every structure passed to <see cref="ShellNotifyIcon"/>, in order.</summary>
    internal IReadOnlyList<NOTIFYICONDATAW> ShellNotifyIconDataSnapshots => _shellNotifyIconData;

    /// <summary>Gets or sets the result every <see cref="ShellNotifyIcon"/> call reports.</summary>
    internal bool ShellNotifyIconResult { get; set; } = true;

    /// <summary>Gets the number of icons successfully created by the real GDI path.</summary>
    internal int CreatedIcons { get; private set; }

    /// <summary>Gets the number of icons successfully destroyed by the real GDI path.</summary>
    internal int DestroyedIcons { get; private set; }

    /// <inheritdoc />
    public bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data)
    {
        _shellNotifyIconMessages.Add(dwMessage);
        _shellNotifyIconData.Add(data);

        return ShellNotifyIconResult;
    }

    /// <inheritdoc />
    public uint RegisterWindowMessage(string message) => _real.RegisterWindowMessage(message);

    /// <inheritdoc />
    /// <remarks>
    /// Delegated to the real export like <see cref="RegisterWindowMessage"/>: the call is read-only
    /// and creates no visible artifact, so it does not violate this double's "never touch the
    /// notification area" rule. Callers here pass identifiers for icons that do not exist, which
    /// is exactly the shape the real call is probed with.
    /// </remarks>
    public int ShellNotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out NativeRect rectangle) =>
        _real.ShellNotifyIconGetRect(ref identifier, out rectangle);

    /// <inheritdoc />
    public IntPtr CreateIconIndirect(ref ICONINFO iconInfo)
    {
        IntPtr icon = _real.CreateIconIndirect(ref iconInfo);

        if (icon != IntPtr.Zero)
        {
            CreatedIcons++;
        }

        return icon;
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER header, uint usage, out IntPtr bits, IntPtr hSection, uint offset) =>
        _real.CreateDIBSection(hdc, ref header, usage, out bits, hSection, offset);

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bitmapInfo, uint usage, out IntPtr bits, IntPtr hSection, uint offset) =>
        _real.CreateDIBSection(hdc, ref bitmapInfo, usage, out bits, hSection, offset);

    /// <inheritdoc />
    public bool DestroyIcon(IntPtr hIcon)
    {
        bool destroyed = _real.DestroyIcon(hIcon);

        if (destroyed)
        {
            DestroyedIcons++;
        }

        return destroyed;
    }

    /// <inheritdoc />
    public bool DeleteObject(IntPtr hObject) => _real.DeleteObject(hObject);

    /// <inheritdoc />
    public uint GetGuiResources(IntPtr hProcess, uint uiFlags) => _real.GetGuiResources(hProcess, uiFlags);

    /// <inheritdoc />
    public int GetLastError() => _real.GetLastError();
}
