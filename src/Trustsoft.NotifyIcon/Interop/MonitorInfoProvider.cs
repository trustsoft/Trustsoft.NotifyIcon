using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The <c>MONITORINFO</c> structure from <c>winuser.h</c>, in exact header field order. It is what
/// <c>GetMonitorInfoW</c> writes the monitor's rectangles and flags into.
/// </summary>
/// <remarks>
/// <para>
/// Header definition (<c>winuser.h</c>, <c>tagMONITORINFO</c>), which is the ground truth:
/// </para>
/// <code>
/// typedef struct tagMONITORINFO {
///     DWORD cbSize;
///     RECT  rcMonitor;
///     RECT  rcWork;
///     DWORD dwFlags;
/// } MONITORINFO;
/// </code>
/// <para>
/// Marshalled layout on x64: <c>cbSize</c> 0, <c>rcMonitor</c> 4, <c>rcWork</c> 20,
/// <c>dwFlags</c> 36, <b>40 bytes in total</b> - all 4-byte fields, so there is no padding anywhere
/// and the size is 40 on every architecture this library targets.
/// </para>
/// <para>
/// <b><c>rcWork</c> is the field the menu placement needs, not <c>rcMonitor</c>.</b> The work area
/// excludes the taskbar and any other appbar, so a tray at the bottom of the screen has a work area
/// whose <c>bottom</c> is above the taskbar while <c>rcMonitor</c>'s is not - and a menu placed
/// against the monitor rectangle would open under the taskbar.
/// </para>
/// <para>
/// The type is <see langword="internal"/> and is not part of the shipping public API (D002/D015);
/// the fields are <see langword="public"/> so the layout test can read them by reflection, exactly
/// as <see cref="NOTIFYICONDATAW"/>, <see cref="ICONINFO"/> and <see cref="NativeRect"/> do.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    /// <summary>
    /// The structure size the caller must fill in before the call, which is the only field the call
    /// reads: <c>GetMonitorInfoW</c> validates it and fails with <c>ERROR_INVALID_PARAMETER</c> if it
    /// is wrong.
    /// </summary>
    public uint cbSize;

    /// <summary>The monitor's own rectangle in physical screen pixels, including any taskbar area.</summary>
    public NativeRect rcMonitor;

    /// <summary>
    /// The monitor's work area in physical screen pixels: the monitor rectangle minus the taskbar
    /// and every other appbar. This is the boundary the menu anchor is clamped into.
    /// </summary>
    public NativeRect rcWork;

    /// <summary>The monitor's flags; <c>MONITORINFOF_PRIMARY</c> (<c>0x1</c>) marks the primary monitor.</summary>
    public uint dwFlags;
}

/// <summary>
/// Reads the monitor that owns a screen rectangle: its work area and its effective DPI.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type and not two free functions on <see cref="Win32"/>.</b> D013 splits the Win32
/// surface in two: the shell seam (<see cref="IShellApi"/>, one <c>[DllImport]</c> file) and a
/// minimal <see cref="Win32"/> helper class whose members are used only by assertions. This is
/// neither - it is monitor and DPI state the menu path reads in production - so it lives in its own
/// file, the way <c>TrayMessageWindow</c> owns the window calls it needs. Nothing else in the
/// library asks a monitor question.
/// </para>
/// <para>
/// <b><c>GetDpiForMonitor(MDT_EFFECTIVE_DPI)</c> rather than <c>GetDpiForWindow</c> (D026).</b>
/// Measured: a manifestless WPF process reports <c>GetProcessDpiAwareness = 1</c>
/// (<c>SYSTEM_AWARE</c>), and <c>GetDpiForWindow</c> only equals the monitor's DPI when the process
/// is per-monitor aware. <c>MDT_EFFECTIVE_DPI</c> is documented to be awareness-independent, and it
/// matched <c>GetDpiForWindow</c> (both 144) on this machine at Display Scale 150 % - which is why
/// it is the reader for a process whose awareness the library does not control and must never set:
/// a WPF library whose dispatcher already created windows cannot set awareness at all
/// (<c>SetProcessDpiAwarenessContext</c> must run before the first <c>HWND</c> exists), so degrading
/// gracefully is the only honest option.
/// </para>
/// <para>
/// <b>Raw results, caller-owned policy.</b> Both members return a bare boolean and record the
/// status of the call that failed in <see cref="LastError"/>: the Win32 last-error code for the two
/// <c>BOOL</c>-returning calls, captured with <see cref="Marshal.GetLastPInvokeError"/> as the first
/// statement after the call, and the <c>HRESULT</c> for <c>GetDpiForMonitor</c>, which states its
/// own failure reason. Nothing here defaults, traces or throws - deciding what a missing reading
/// means belongs to the caller, which is the same split <see cref="IShellApi"/> uses.
/// </para>
/// <para>
/// <b>Not thread-safe, and it does not need to be.</b> The instance carries one field, written and
/// read on the dispatcher thread by the menu path; a caller that wanted to share one across threads
/// would have to synchronise, so the contract is stated rather than paid for.
/// </para>
/// </remarks>
internal class MonitorInfoProvider
{
    /// <summary>
    /// <c>MONITOR_DEFAULTTONEAREST</c> from <c>winuser.h</c>: return the monitor nearest the
    /// rectangle, which is what makes "the tray monitor" an answer even for a rectangle that lies
    /// between two monitors or outside every one of them.
    /// </summary>
    internal const uint MonitorDefaultToNearest = 2;

    /// <summary>
    /// <c>MDT_EFFECTIVE_DPI</c> from <c>shellscalingapi.h</c>: the monitor's effective DPI, the value
    /// the Display Scale setting corresponds to (96 at 100 %, 144 at 150 %).
    /// </summary>
    internal const int MdtEffectiveDpi = 0;

    /// <summary>
    /// The size of <see cref="MonitorInfo"/> in bytes, as the header defines it: 40 on every
    /// supported target. Measured once here and handed to the call, so a wrong <c>cbSize</c> fails
    /// loudly at the call instead of silently reading uninitialised memory.
    /// </summary>
    internal static readonly uint MonitorInfoSizeOf = (uint)Marshal.SizeOf<MonitorInfo>();

    /// <summary>
    /// Gets or sets the status of the most recent failed call, or <c>0</c> when the last call
    /// succeeded.
    /// </summary>
    /// <value>
    /// The Win32 last-error code of a failed <c>MonitorFromRect</c> or <c>GetMonitorInfoW</c>, or
    /// the <c>HRESULT</c> of a failed <c>GetDpiForMonitor</c>; <c>0</c> after a success.
    /// </value>
    /// <remarks>
    /// <para>
    /// Read it only after a member returned <see langword="false"/>; a successful call sets it to
    /// <c>0</c> rather than leaving a stale code behind, so a caller that reads it at the wrong
    /// moment sees "no failure" instead of a number from an unrelated call.
    /// </para>
    /// <para>
    /// Settable, which is deliberate: a test double that scripted a failure has to be able to report
    /// a code for it, and the menu path's fallback trace names this value.
    /// </para>
    /// </remarks>
    internal int LastError { get; set; }

    /// <summary>
    /// Reads the work area of the monitor nearest <paramref name="rectangle"/>.
    /// </summary>
    /// <param name="rectangle">
    /// The rectangle whose monitor is wanted, normally the icon rectangle from
    /// <see cref="IShellApi.ShellNotifyIconGetRect"/>: the shell's own answer to "which screen is
    /// this icon on".
    /// </param>
    /// <param name="workArea">
    /// Receives the monitor's work area in physical screen pixels with exclusive <c>right</c> and
    /// <c>bottom</c> edges; the default rectangle when the call fails.
    /// </param>
    /// <returns><see langword="true"/> when the work area was read.</returns>
    /// <remarks>
    /// <see cref="MonitorDefaultToNearest"/> is what makes this total in practice: a rectangle that
    /// lies outside every monitor - an icon in the overflow flyout the shell still reports a
    /// plausible rectangle for, or a virtual desktop the process cannot address - still resolves to
    /// the nearest monitor rather than failing. The failure path exists for the case where even that
    /// returns nothing, and it never throws: this runs on the click path, where a menu that opened
    /// with a defaulted work area is better than an exception out of a window procedure.
    /// </remarks>
    internal virtual bool TryGetWorkArea(NativeRect rectangle, out NativeRect workArea)
    {
        workArea = default;

        IntPtr monitor = MonitorFromRectNative(ref rectangle, MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
        {
            LastError = Marshal.GetLastPInvokeError();
            return false;
        }

        var info = new MonitorInfo { cbSize = MonitorInfoSizeOf };

        if (!GetMonitorInfoNative(monitor, ref info))
        {
            LastError = Marshal.GetLastPInvokeError();
            return false;
        }

        LastError = 0;
        workArea = info.rcWork;
        return true;
    }

    /// <summary>
    /// Reads the effective DPI of the monitor nearest <paramref name="rectangle"/>.
    /// </summary>
    /// <param name="rectangle">The rectangle whose monitor is wanted; see <see cref="TryGetWorkArea"/>.</param>
    /// <param name="dpi">
    /// Receives the effective DPI, where 96 means 100 %; <c>0</c> when the call fails.
    /// </param>
    /// <returns><see langword="true"/> when a usable, non-zero DPI was read.</returns>
    /// <remarks>
    /// <para>
    /// A returned <c>0</c> is treated as a failure rather than passed on: zero has no scale factor
    /// (<see cref="TrayIconPlacement.ScaleFor"/> rejects it), so handing it out would turn a missing
    /// reading into an exception in the placement arithmetic instead of a documented default at this
    /// call site. <see cref="LastError"/> stays <c>0</c> for that case, because nothing failed - the
    /// call succeeded and reported no DPI.
    /// </para>
    /// <para>
    /// Both returned values are read even though only the x value is used: the export declares two
    /// <c>out</c> parameters and GDI+ calls are not somewhere to improvise a signature.
    /// </para>
    /// </remarks>
    internal virtual bool TryGetDpi(NativeRect rectangle, out uint dpi)
    {
        dpi = 0;

        IntPtr monitor = MonitorFromRectNative(ref rectangle, MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
        {
            LastError = Marshal.GetLastPInvokeError();
            return false;
        }

        int hresult = GetDpiForMonitorNative(monitor, MdtEffectiveDpi, out uint dpiX, out _);

        if (hresult != 0)
        {
            LastError = hresult;
            return false;
        }

        if (dpiX == 0)
        {
            LastError = 0;
            return false;
        }

        LastError = 0;
        dpi = dpiX;
        return true;
    }

    /// <remarks>
    /// Mirrors <c>winuser.h</c>:
    /// <c>HMONITOR MonitorFromRect(_In_ LPCRECT lprc, _In_ DWORD dwFlags)</c>. The rectangle is an
    /// input only, so it is passed by reference (the pointer the header declares) and never copied;
    /// <c>NULL</c> means the call failed, which is why the caller reads the last error immediately.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "MonitorFromRect", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr MonitorFromRectNative(ref NativeRect rectangle, uint flags);

    /// <remarks>
    /// Mirrors <c>winuser.h</c>:
    /// <c>BOOL GetMonitorInfoW(_In_ HMONITOR hMonitor, _Inout_ LPMONITORINFO lpmi)</c>. Input/output
    /// because the caller writes <c>cbSize</c> into the structure the call then fills in, so the
    /// caller's own instance is passed by reference and never copied. The <c>W</c> suffix is spelled
    /// out because the unsuffixed name is a header macro rather than an export.
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoNative(IntPtr monitor, ref MonitorInfo info);

    /// <remarks>
    /// Mirrors <c>shellscalingapi.h</c>:
    /// <c>HRESULT GetDpiForMonitor(_In_ HMONITOR hmonitor, _In_ MONITOR_DPI_TYPE dpiType, _Out_ UINT *dpiX, _Out_ UINT *dpiY)</c>.
    /// The return type is an <c>HRESULT</c> - <c>0</c> is <c>S_OK</c> and a non-zero value is the
    /// failure - which is why its status is not read as a boolean and why it does not go through
    /// <see cref="Marshal.GetLastPInvokeError"/>: the export states its own reason. The export lives
    /// in <c>shcore.dll</c> (Windows 8.1 and later), not in <c>user32</c>.
    /// </remarks>
    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor", SetLastError = false, ExactSpelling = true)]
    private static extern int GetDpiForMonitorNative(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
