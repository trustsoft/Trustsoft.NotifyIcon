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
/// <b>Last error is captured, not read later.</b> Every P/Invoke below except
/// <c>Shell_NotifyIconGetRectNative</c> declares <c>SetLastError = true</c> and calls
/// <see cref="CaptureLastError"/> as its next statement. The value lives in a per-thread field
/// and <see cref="GetLastError"/> returns that field. This is deliberate: Win32's last-error
/// slot is thread state that any other P/Invoke (or a debugger, or an unmanaged call from a
/// satellite library) can overwrite, so a design that read the slot in <see cref="GetLastError"/>
/// would report whatever call happened last - usually the successful one inside the error handler -
/// instead of the failure being reported. The P/Invoke stub captures the error at the call
/// boundary when <c>SetLastError</c> is set, and <see cref="Marshal.GetLastPInvokeError"/> is the
/// supported way to read that captured value; a hand-rolled <c>kernel32!GetLastError</c>
/// declaration would read the raw slot and lose it again on the way back into managed code.
/// </para>
/// <para>
/// <b>The one deliberate exception is the HRESULT-returning export.</b>
/// <c>Shell_NotifyIconGetRect</c> states its own failure reason in its return value, and Win32 does
/// not document that it writes the thread last-error slot; capturing whatever happened to be in
/// that slot would attach an unrelated code to the failure and manufacture exactly the misleading
/// "reason" the paragraph above exists to prevent. Its declaration therefore declares
/// <c>SetLastError = true</c> (the exporter sets it, keeping the declarations uniform) but does not
/// call <see cref="CaptureLastError"/>, and
/// <see cref="IShellApi.ShellNotifyIconGetRect"/> documents <see cref="GetLastError"/> as not being
/// that call's failure channel. Do not "fix" the missing capture: it is a contract, and
/// <c>ShellNotifyIconGetRectTests</c> asserts the HRESULT is the only status it reports.
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
    public int ShellNotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out NativeRect rectangle)
    {
        // No last-error capture here, on purpose. The HRESULT is this call's entire status, and
        // the export is not documented to write the thread last-error slot; capturing it would
        // report an unrelated value through the seam's GetLastError and invite a caller to treat
        // it as this call's reason. See the class remarks and the interface member's contract.
        return ShellNotifyIconGetRectNative(ref identifier, out rectangle);
    }

    /// <inheritdoc />
    public bool GetCursorPosition(out int x, out int y)
    {
        bool result = GetCursorPosNative(out int pointX, out int pointY);
        CaptureLastError();

        // The out parameters are always assigned, including on failure: C#'s definite-assignment
        // rule forces something, and a documented (0, 0) is a better answer than an uninitialised
        // coordinate a caller might place a menu at. The boolean is the contract.
        x = result ? pointX : 0;
        y = result ? pointY : 0;
        return result;
    }

    /// <inheritdoc />
    public IntPtr GetForegroundWindow()
    {
        IntPtr result = GetForegroundWindowNative();
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public bool SetForegroundWindow(IntPtr hWnd)
    {
        bool result = SetForegroundWindowNative(hWnd);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public IntPtr GetWindowOwner(IntPtr hWnd)
    {
        IntPtr result = GetWindowNative(hWnd, Win32.GW_OWNER);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public IntPtr SetWindowOwner(IntPtr hWnd, IntPtr hWndOwner)
    {
        // One export, two accessors: SetWindowLongPtrW does not exist on 32-bit Windows and
        // SetWindowLongW would truncate the result on 64-bit Windows. The index is negative, so the
        // same numeric value selects the owner slot on both; only the entry point differs. The
        // returned value is the *previous* owner, so a zero is normal and not a failure signal - the
        // caller reads the owner back with GetWindowOwner rather than interpreting this.
        IntPtr previous = IntPtr.Size == 8
            ? SetWindowLongPtrWNative(hWnd, Win32.GWLP_HWNDPARENT, hWndOwner)
            : new IntPtr(SetWindowLongWNative(hWnd, Win32.GWLP_HWNDPARENT, hWndOwner.ToInt32()));

        CaptureLastError();
        return previous;
    }

    /// <inheritdoc />
    public uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId)
    {
        uint result = GetWindowThreadProcessIdNative(hWnd, out processId);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public uint GetCurrentThreadId()
    {
        uint result = GetCurrentThreadIdNative();
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach)
    {
        bool result = AttachThreadInputNative(idAttach, idAttachTo, fAttach);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public bool BringWindowToTop(IntPtr hWnd)
    {
        bool result = BringWindowToTopNative(hWnd);
        CaptureLastError();
        return result;
    }

    /// <inheritdoc />
    public void SwitchToThisWindow(IntPtr hWnd, bool altTab)
    {
        // The export returns nothing, so there is no status to read back and nothing for the caller
        // to branch on - the sequence's outcome is the SetForegroundWindow result that follows it.
        // The last error is still captured, so a diagnosis cannot be polluted by a stale slot.
        SwitchToThisWindowNative(hWnd, altTab);
        CaptureLastError();
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
    /// Mirrors <c>shellapi.h</c>:
    /// <c>SHSTDAPI Shell_NotifyIconGetRect(_In_ const NOTIFYICONIDENTIFIER* identifier, _Out_ RECT* iconLocation)</c>.
    /// Two consequences of that declaration are load-bearing: the return type is <c>HRESULT</c>
    /// (a 4-byte <c>int</c>, <c>0</c> = <c>S_OK</c>, non-zero = failure), <b>not</b> <c>BOOL</c> -
    /// which is why this declaration is not in the <c>bool</c>-returning block above and why
    /// marshalling its result as <see langword="false"/> on success would silently invert every
    /// call; and the identifier is a <c>const</c> input, so the caller's own instance is passed
    /// by reference and never copied. <c>ExactSpelling</c> is set because the export is a single
    /// unsuffixed name and must never be probed for an <c>A</c>/<c>W</c> variant.
    /// <para>
    /// <b>No <see cref="CaptureLastError"/> call follows this one</b> - see the class remarks. Its
    /// status is the returned <c>HRESULT</c> alone.
    /// </para>
    /// </remarks>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect", SetLastError = true, ExactSpelling = true)]
    private static extern int ShellNotifyIconGetRectNative(ref NOTIFYICONIDENTIFIER identifier, out NativeRect iconLocation);

    /// <remarks>
    /// <c>user32.dll</c> exports one unsuffixed <c>GetCursorPos</c> that writes a <c>POINT</c>
    /// (<c>LONG x; LONG y;</c>) - a blittable pair of 4-byte signed values, so it is marshalled as
    /// two <c>out int</c> parameters rather than as a one-field struct that would exist only to be
    /// unwrapped again. The coordinates are physical screen pixels in the virtual-screen coordinate
    /// space, negative on a monitor left of or above the primary one.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosNative(out int pointX, out int pointY);

    /// <remarks>
    /// <c>user32.dll</c> exports the wide variant as <c>RegisterWindowMessageW</c>; the
    /// unsuffixed name is a header macro, not an export.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    /// <remarks>
    /// The desktop's foreground window. A single unsuffixed export with no parameters;
    /// <c>ExactSpelling</c> keeps it from being probed. A result of <see cref="IntPtr.Zero"/> is
    /// "no window holds the foreground", which is a reading rather than a failure.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindowNative();

    /// <remarks>
    /// <c>SetForegroundWindow</c> returns <c>BOOL</c>, and the false case is the foreground lock
    /// refusing the process rather than an error. <c>ExactSpelling</c> names the single unsuffixed
    /// export explicitly.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(IntPtr hWnd);

    /// <remarks>
    /// The <c>GetWindow</c> form used by the seam; only <c>GW_OWNER</c> (the owner relationship) is
    /// ever requested, which is why the command is not a parameter on the interface member.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "GetWindow", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetWindowNative(IntPtr hWnd, uint uCmd);

    /// <remarks>
    /// The 64-bit accessor for the owner field. On 32-bit Windows this export does not exist and
    /// the declaration is simply never resolved; <c>SetWindowLongW</c> below is the accessor used
    /// there. Both take the same negative index and return the previous field value. This is the
    /// write half of the export; <see cref="Win32.GetWindowLongPtr"/> keeps the read half for the
    /// style assertions D013 assigns to that helper class.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetWindowLongPtrWNative(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <remarks>
    /// The 32-bit accessor for the same field. A 32-bit window handle fits the <c>LONG</c> slot
    /// exactly, so no truncation is possible on the platform that uses this entry point.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true, ExactSpelling = true)]
    private static extern int SetWindowLongWNative(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <remarks>
    /// <c>user32.dll</c> exports one unsuffixed <c>GetWindowThreadProcessId</c>. The thread id is the
    /// return value and the process id comes back through the out parameter; a zero return means the
    /// call failed (a real thread id is never zero).
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true, ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessIdNative(IntPtr hWnd, out uint processId);

    /// <remarks>
    /// <c>kernel32.dll</c>, not <c>user32.dll</c>: the thread id belongs to the kernel. The
    /// declaration lives here because the attach-thread sequence needs both ends of the pair, and
    /// this class is the seam's only <c>[DllImport]</c> home.
    /// </remarks>
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId", SetLastError = true, ExactSpelling = true)]
    private static extern uint GetCurrentThreadIdNative();

    /// <remarks>
    /// <c>BOOL</c>-returning, and a <see langword="false"/> result means the queues were not joined
    /// or separated rather than that an error occurred. <c>ExactSpelling</c> names the single
    /// unsuffixed export.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInputNative(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    /// <remarks>
    /// <c>BOOL</c>-returning. The boolean marshalling attribute on the <c>fAttach</c> parameter is not
    /// optional decoration: without it this would be marshalled as a 4-byte <c>int</c> while the
    /// export reads a 1-byte <c>BOOL</c>.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "BringWindowToTop", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTopNative(IntPtr hWnd);

    /// <remarks>
    /// The export reports no status, so the declaration is <c>void</c> on purpose - declaring a
    /// <c>bool</c> here would invent a result the OS never produced. Its <c>fAltTab</c> parameter is a
    /// <c>BOOL</c> like <c>AttachThreadInput</c>'s flag.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "SwitchToThisWindow", SetLastError = true, ExactSpelling = true)]
    private static extern void SwitchToThisWindowNative(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

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
