using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The real <see cref="IShellApi"/> implementation: one <c>[DllImport]</c> per Win32 call,
/// each immediately capturing the last-error code.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only file in the library that declares <c>[DllImport]</c> for these
/// functions</b>, which is what makes the seam a genuine single choke point: a wrong entry
/// point, a wrong character set or a missing <c>SetLastError</c> has exactly one home, and the
/// test suite's recording wrapper around this class is a real check of the signatures rather
/// than a description of them.
/// </para>
/// <para>
/// <b>Last error is captured, not read later.</b> Every P/Invoke below declares
/// <c>SetLastError = true</c> and calls <see cref="CaptureLastError"/> as its next statement.
/// The value lives in a per-thread field and <see cref="GetLastError"/> returns that field.
/// This is deliberate: Win32's last-error slot is thread state that any other P/Invoke (or a
/// debugger, or an unmanaged call from a satellite library) can overwrite, so a design that
/// read the slot in <see cref="GetLastError"/> would report whatever call happened last - usually
/// the successful one inside the error handler - instead of the failure being reported. The
/// P/Invoke stub captures the error at the call boundary when <c>SetLastError</c> is set, and
/// <see cref="Marshal.GetLastPInvokeError"/> is the supported way to read that captured value;
/// a hand-rolled <c>kernel32!GetLastError</c> declaration would read the raw slot and lose it
/// again on the way back into managed code.
/// </para>
/// <para>
/// <b>All entry points are named explicitly with <c>ExactSpelling = true</c>.</b> The
/// <c>CreateDIBSection</c> pair are two typed views of one export, so the count of declarations
/// is one higher than the count of distinct functions; every other declaration is one function.
/// These functions exist as a single unsuffixed export, but relying on the runtime's
/// <c>CharSet</c>-driven suffix probing for them works only as long as the <c>CharSet</c> of the
/// declaration stays <c>None</c>/<c>Ansi</c> - and <c>Shell_NotifyIconW</c> and
/// <c>RegisterWindowMessageW</c> do need a <c>CharSet</c>. Naming every entry point and
/// disabling the probing keeps all declarations uniform and removes the class of bug where a
/// wide-character call silently binds to an A-suffixed or missing export.
/// </para>
/// <para>
/// Do not add members here that <see cref="IShellApi"/> does not declare: a helper used only by
/// a single caller belongs next to that caller, and everything on this class exists to be
/// shared through the seam.
/// </para>
/// </remarks>
internal sealed class ShellApi : IShellApi
{
    /// <summary>
    /// The captured Win32 error code for the most recent call on this thread.
    /// </summary>
    /// <remarks>
    /// Per-thread because Win32's last error is per-thread state. Static because the seam is
    /// stateless otherwise and each member is called on the thread that made the call.
    /// </remarks>
    [ThreadStatic]
    private static int _lastError;

    /// <inheritdoc />
    public bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data)
    {
        bool result = Shell_NotifyIconW(dwMessage, ref data);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public uint RegisterWindowMessage(string message)
    {
        uint result = RegisterWindowMessageW(message);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public IntPtr CreateIconIndirect(ref ICONINFO iconInfo)
    {
        IntPtr result = CreateIconIndirectNative(ref iconInfo);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER header, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        IntPtr result = CreateDIBSectionV5Native(hdc, ref header, usage, out bits, hSection, offset);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bitmapInfo, uint usage, out IntPtr bits, IntPtr hSection, uint offset)
    {
        IntPtr result = CreateDIBSectionInfoNative(hdc, ref bitmapInfo, usage, out bits, hSection, offset);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public bool DestroyIcon(IntPtr hIcon)
    {
        bool result = DestroyIconNative(hIcon);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public bool DeleteObject(IntPtr hObject)
    {
        bool result = DeleteObjectNative(hObject);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public uint GetGuiResources(IntPtr hProcess, uint uiFlags)
    {
        uint result = GetGuiResourcesNative(hProcess, uiFlags);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public int GetLastError() => _lastError;

    /// <summary>
    /// Stores the error code the just-returned P/Invoke reported, as the first statement after
    /// that call.
    /// </summary>
    private static void CaptureLastError() => _lastError = Marshal.GetLastPInvokeError();

    /// <remarks>
    /// Input/output: the shell may write back into the structure, so it is passed by reference
    /// and never copied. <c>CharSet.Unicode</c> matters for the three <c>ByValTStr</c> fields
    /// inside <see cref="NOTIFYICONDATAW"/>.
    /// </remarks>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    /// <remarks>
    /// <c>user32.dll</c> exports the wide variant as <c>RegisterWindowMessageW</c>; the
    /// unsuffixed name is a header macro, not an export.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    /// <remarks>
    /// Single unsuffixed export in <c>user32.dll</c>; named explicitly so it can never be probed
    /// as <c>CreateIconIndirectA</c>.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "CreateIconIndirect", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateIconIndirectNative(ref ICONINFO piconinfo);

    /// <remarks>
    /// <c>gdi32.dll</c> exports one unsuffixed <c>CreateDIBSection</c>, declared twice here in its
    /// two typed views (see <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPV5HEADER, uint, out IntPtr, IntPtr, uint)"/>).
    /// The <c>out</c> pixel pointer belongs to the DIB section and must not be freed by the
    /// caller: releasing the <c>HBITMAP</c> releases the pixels.
    /// </remarks>
    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateDIBSectionV5Native(IntPtr hdc, ref BITMAPV5HEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    /// <remarks>
    /// The version-3 view, used for the monochrome mask. See
    /// <see cref="IShellApi.CreateDIBSection(IntPtr, ref BITMAPINFO, uint, out IntPtr, IntPtr, uint)"/>.
    /// </remarks>
    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateDIBSectionInfoNative(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true, ExactSpelling = true)]
    private static extern bool DestroyIconNative(IntPtr hIcon);

    /// <remarks>
    /// <c>DeleteObject</c> accepts any GDI object handle; only <c>HBITMAP</c>s are passed here.
    /// </remarks>
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true, ExactSpelling = true)]
    private static extern bool DeleteObjectNative(IntPtr hObject);

    [DllImport("user32.dll", EntryPoint = "GetGuiResources", SetLastError = true, ExactSpelling = true)]
    private static extern uint GetGuiResourcesNative(IntPtr hProcess, uint uiFlags);
}
