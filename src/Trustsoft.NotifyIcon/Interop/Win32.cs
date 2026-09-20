using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The Win32 calls that are <em>not</em> shell calls and therefore do not belong on
/// <see cref="IShellApi"/>: window topology, window styles and the current-process pseudo
/// handle.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal: one entry per call a task actually makes, no convenience wrappers and
/// no general-purpose Win32 surface. These calls are used for assertions and diagnostics, not
/// for the icon lifecycle, so they are not part of the test double and are not scriptable.
/// </para>
/// <para>
/// <b>Last error is not captured here.</b> Unlike <see cref="ShellApi"/>, this class does not
/// cache the Win32 error, because no caller branches on it: each member is an assertion helper
/// whose return value is the evidence. Do not call <see cref="IShellApi.GetLastError"/> expecting
/// to see an error from a call made here - the seam's value belongs to the seam's own calls.
/// Where a failure code is genuinely needed, capture
/// <see cref="Marshal.GetLastPInvokeError"/> as the first statement after the call.
/// </para>
/// <para>
/// <b>Consumers:</b> T05 adds <c>IsWindowVisible</c>, <c>IsWindow</c> and <c>SendMessage</c> here
/// for its host-window assertions, and uses <see cref="GetAncestor"/> with
/// <see cref="GA_ROOT"/> to prove the host is a top-level window (D011);
/// <see cref="GetWindowLongPtr"/> with <see cref="GWL_EXSTYLE"/> proves the tool-window style;
/// tests use <see cref="GetCurrentProcess"/> together with
/// <see cref="IShellApi.GetGuiResources"/> for the R007 handle-count evidence.
/// </para>
/// </remarks>
internal static class Win32
{
    /// <summary>
    /// <c>GA_ROOT</c> (winuser.h): retrieve the root window of the specified window's ancestry.
    /// </summary>
    /// <remarks>
    /// <c>GetAncestor(hwnd, GA_ROOT) == hwnd</c> is the definition of "this window is
    /// top-level", which is the property the hidden tray host window must have so it can receive
    /// the broadcast messages (such as <c>TaskbarCreated</c>) that message-only windows never
    /// see.
    /// </remarks>
    internal const uint GA_ROOT = 2;

    /// <summary><c>GWL_STYLE</c> (winuser.h): the window style bits.</summary>
    internal const int GWL_STYLE = -16;

    /// <summary><c>GWL_EXSTYLE</c> (winuser.h): the extended window style bits.</summary>
    internal const int GWL_EXSTYLE = -20;

    /// <summary>
    /// Returns the ancestor of the given window identified by <paramref name="gaFlags"/>.
    /// </summary>
    /// <param name="hwnd">The window to start from.</param>
    /// <param name="gaFlags">The ancestor to select, normally <see cref="GA_ROOT"/>.</param>
    /// <returns>The ancestor handle, or <see cref="IntPtr.Zero"/> if the window has none.</returns>
    internal static IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => GetAncestorNative(hwnd, gaFlags);

    /// <summary>
    /// Reads a window-style field as a pointer-sized value, selecting the correct Win32 entry
    /// point for the current process bitness.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="nIndex">The field index, normally <see cref="GWL_STYLE"/> or
    /// <see cref="GWL_EXSTYLE"/>.</param>
    /// <returns>The field value; 0 is also the failure value, so a caller that must distinguish
    /// failure from a zero style should treat 0 as "not set".</returns>
    /// <remarks>
    /// <c>GetWindowLongPtrW</c> does not exist on 32-bit Windows - only <c>GetWindowLongW</c>
    /// does - while on 64-bit Windows <c>GetWindowLongW</c> would truncate the result. The
    /// bitness branch picks the exported function that matches the process, and the declaration
    /// that is not used is simply never resolved.
    /// </remarks>
    internal static long GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtrWNative(hWnd, nIndex).ToInt64()
            : GetWindowLongWNative(hWnd, nIndex);

    /// <summary>
    /// Returns the pseudo-handle of the current process, for calls that take a process handle.
    /// </summary>
    /// <returns>The <c>(HANDLE)-1</c> pseudo-handle; it is always valid and must not be closed.</returns>
    internal static IntPtr GetCurrentProcess() => GetCurrentProcessNative();

    [DllImport("user32.dll", EntryPoint = "GetAncestor", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetAncestorNative(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetWindowLongPtrWNative(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true, ExactSpelling = true)]
    private static extern int GetWindowLongWNative(IntPtr hWnd, int nIndex);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcessNative();
}
