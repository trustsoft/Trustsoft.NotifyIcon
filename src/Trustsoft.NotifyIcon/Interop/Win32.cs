using System.Runtime.InteropServices;
using System.Text;

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
/// for the icon lifecycle, so they are not part of the test double and are not scriptable - with
/// the three exceptions that delegate to <see cref="IShellApi"/>, which are on the seam precisely
/// because the menu path's behaviour turns on their answers.
/// </para>
/// <para>
/// <b>Last error is not captured by this class.</b> Unlike <see cref="ShellApi"/>, no member that
/// declares its own P/Invoke caches the Win32 error, because no caller branches on it: each member
/// is an assertion helper whose return value is the evidence. <see cref="GetForegroundWindow"/>,
/// <see cref="GetWindowOwner"/> and the <c>GetWindowThreadProcessId</c> step of
/// <see cref="GetVisibleTopLevelWindowsOfProcess"/> delegate to <see cref="IShellApi"/> and
/// therefore <em>do</em> move the seam's thread-static last-error slot as a side effect of the
/// seam's own capture; that is one more reason not to read
/// <see cref="IShellApi.GetLastError"/> after a call made here. Where a failure code is genuinely
/// needed, capture <see cref="Marshal.GetLastPInvokeError"/> as the first statement after the call.
/// </para>
/// <para>
/// <b>Consumers:</b> the host-window tests use <see cref="GetAncestor"/> with
/// <see cref="GA_ROOT"/> to prove the tray host is a top-level window (D011),
/// <see cref="GetWindowLongPtr"/> with <see cref="GWL_EXSTYLE"/> to prove the tool-window style,
/// <see cref="IsWindowVisible"/> to prove the host is never on screen,
/// <see cref="SendMessage"/> to prove the host's message hook actually receives messages, and
/// <see cref="IsWindow"/> to prove disposal destroyed the window; the same tests use
/// <see cref="GetCurrentProcess"/> together with
/// <see cref="IShellApi.GetGuiResources"/> for the R007 handle-count evidence.
/// </para>
/// <para>
/// <b>The menu-anchor consumers (S03, and the S08 owner measurement).</b>
/// <see cref="TrayMenuAnchorWindow"/> itself uses <see cref="WS_POPUP"/> for the window it creates,
/// <see cref="SetWindowPos"/> to move that window and <see cref="IShellApi.SetForegroundWindow"/> -
/// through the seam, because the refusal is what S08 has to script - to give it the activation
/// relationship the shell routes a menu dismissal through. The dismissal proof in the test suite
/// uses <see cref="GetWindowOwner"/> to read what owns the popup, <see cref="GetWindowRect"/> to
/// tell the popup from the zero-sized host and the 1x1 anchor,
/// <see cref="GetVisibleTopLevelWindowsOfProcess"/> to enumerate the process's windows,
/// <see cref="GetForegroundWindow"/> to name the window an injected click activated when a check
/// fails, and <see cref="WS_VISIBLE"/> plus <see cref="WS_EX_NOACTIVATE"/> as negative assertions
/// on the anchor's style. Nothing outside those uses is declared here.
/// </para>
/// <para>
/// <b>Three members are delegations to the seam rather than local declarations.</b>
/// <see cref="GetForegroundWindow"/>, <see cref="GetWindowOwner"/> and the
/// <c>GetWindowThreadProcessId</c> step of <see cref="GetVisibleTopLevelWindowsOfProcess"/> call
/// <see cref="IShellApi"/> so that <see cref="ShellApi"/> is the single home of those entry points
/// (D013's single-file rule). They stay here as the assertion helpers D013 assigns to this class,
/// and they are the same OS calls they were before - the change is the indirection, which is what
/// lets a test script the reading.
/// </para>
/// </remarks>
internal static class Win32
{
    /// <summary>
    /// The seam these test-facing helpers delegate their scriptable calls to.
    /// </summary>
    /// <remarks>
    /// One stateless instance: <see cref="ShellApi"/> holds no per-instance state (its only field
    /// is a thread-static last-error slot), so sharing one keeps the declarations in
    /// <see cref="ShellApi"/> the only home of these entry points without making the helpers
    /// allocate.
    /// </remarks>
    private static readonly ShellApi Seam = new();

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
    /// <c>GWLP_HWNDPARENT</c> (winuser.h): the window field that holds a window's <em>owner</em>
    /// for a top-level popup.
    /// </summary>
    /// <remarks>
    /// The index is negative, so it is the same numeric value on 32-bit and 64-bit Windows; only
    /// the accessor differs (<c>SetWindowLongPtrW</c> versus <c>SetWindowLongW</c>), which
    /// <see cref="ShellApi.SetWindowOwner"/> selects by process bitness. It is passed to
    /// <see cref="IShellApi.SetWindowOwner"/> and is the owner slot an outside click's activation
    /// change is routed through.
    /// </remarks>
    internal const int GWLP_HWNDPARENT = -8;

    /// <summary>
    /// <c>WS_EX_TOOLWINDOW</c> (winuser.h): the window is a tool window - it does not appear in
    /// the taskbar or in Alt-Tab.
    /// </summary>
    /// <remarks>
    /// The tray host sets this so that a library whose entire UI is the notification area never
    /// produces a phantom taskbar button or Alt-Tab entry, which would visibly contradict the
    /// "no main window" premise the library exists for.
    /// </remarks>
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;

    /// <summary>
    /// <c>WS_EX_APPWINDOW</c> (winuser.h): forces a top-level window onto the taskbar.
    /// </summary>
    /// <remarks>
    /// Only ever used as a negative assertion. Setting it on the tray host would defeat
    /// <see cref="WS_EX_TOOLWINDOW"/> and put the host on the taskbar.
    /// </remarks>
    internal const long WS_EX_APPWINDOW = 0x00040000L;

    /// <summary>
    /// <c>WS_EX_NOACTIVATE</c> (winuser.h): the window never becomes the foreground window and
    /// does not appear in the taskbar.
    /// </summary>
    /// <remarks>
    /// Only ever used as a negative assertion on <see cref="TrayMenuAnchorWindow"/>, which must
    /// <b>not</b> carry it. A non-activating window has no activation relationship for Windows to
    /// route a menu dismissal through, which is the measured reason the anchor exists as it does.
    /// </remarks>
    internal const long WS_EX_NOACTIVATE = 0x08000000L;

    /// <summary><c>WS_POPUP</c> (winuser.h): the window is a popup with no non-client frame.</summary>
    /// <remarks>
    /// The style <see cref="TrayMenuAnchorWindow"/> is created with. It is asserted present on the
    /// created window, because a plain overlapped window at the tray would be a framed, taskbar-
    /// and Alt-Tab-eligible artifact instead of the invisible anchor the popup needs.
    /// </remarks>
    internal const long WS_POPUP = 0x80000000L;

    /// <summary><c>WS_VISIBLE</c> (winuser.h): the window is shown.</summary>
    /// <remarks>
    /// Only ever used as a negative assertion. The tray host and the menu anchor must never carry
    /// it: the anchor is proven to own a dismissable popup without being visible, so nothing may be
    /// gained by showing it, and a library whose entire UI is the notification area must not put a
    /// window on screen.
    /// </remarks>
    internal const long WS_VISIBLE = 0x10000000L;

    /// <summary>
    /// <c>GW_OWNER</c> (winuser.h): the <c>GetWindow</c> command that returns a window's
    /// <em>owner</em> - distinct from its parent, and the value that decides whether a menu popup
    /// participates in the activation relationship that dismisses it.
    /// </summary>
    /// <remarks>
    /// The numeric command is still needed where the relationship is read as an assertion, and it
    /// is what <see cref="IShellApi.GetWindowOwner"/> is defined to ask for.
    /// </remarks>
    internal const uint GW_OWNER = 4;

    /// <summary>
    /// <c>SWP_NOSIZE</c> (winuser.h): <see cref="SetWindowPos"/> moves the window without changing
    /// its size.
    /// </summary>
    /// <remarks>
    /// <see cref="TrayMenuAnchorWindow.Position"/> moves the anchor; its size is fixed at 1x1 by
    /// construction and by the popup's layout, so resizing it would be an accident rather than a
    /// feature.
    /// </remarks>
    internal const uint SWP_NOSIZE = 0x0001;

    /// <summary>
    /// <c>SWP_NOZORDER</c> (winuser.h): <see cref="SetWindowPos"/> moves the window without
    /// changing its position in the Z order.
    /// </summary>
    /// <remarks>
    /// <see cref="TrayMenuAnchorWindow.Position"/> must not reorder anything: the anchor's one
    /// meaningful relationship is its activation/ownership one, and the default insertion
    /// (<c>hWndInsertAfter</c> null, meaning top of the Z order) would raise an invisible window
    /// above whatever the user is looking at.
    /// </remarks>
    internal const uint SWP_NOZORDER = 0x0004;

    /// <summary>
    /// <c>SWP_NOACTIVATE</c> (winuser.h): <see cref="SetWindowPos"/> moves the window without
    /// activating it.
    /// </summary>
    /// <remarks>
    /// Activation is the anchor's <em>whole</em> value (see
    /// <see cref="TrayMenuAnchorWindow.MakeForeground"/>), so it is done deliberately and exactly
    /// once per menu open rather than as a side effect of moving the window.
    /// </remarks>
    internal const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Returns the ancestor of the given window identified by <paramref name="gaFlags"/>.
    /// </summary>
    /// <param name="hwnd">The window to start from.</param>
    /// <param name="gaFlags">The ancestor to select, normally <see cref="GA_ROOT"/>.</param>
    /// <returns>The ancestor handle, or <see cref="IntPtr.Zero"/> if the window has none.</returns>
    internal static IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => GetAncestorNative(hwnd, gaFlags);

    /// <summary>
    /// Reports whether the given window carries <c>WS_VISIBLE</c> (directly or through an owner).
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns><see langword="true"/> when the window is visible.</returns>
    /// <remarks>
    /// The tray host must never be visible: its whole purpose is to be a message sink that no one
    /// can see. Asserting this is how the tests pin that, rather than trusting that a zero-sized
    /// window is also an invisible one.
    /// </remarks>
    internal static bool IsWindowVisible(IntPtr hWnd) => IsWindowVisibleNative(hWnd);

    /// <summary>
    /// Reports whether the given handle still identifies a live window.
    /// </summary>
    /// <param name="hWnd">The window handle captured while the window existed.</param>
    /// <returns><see langword="true"/> when the window still exists.</returns>
    /// <remarks>
    /// The handle <em>value</em> may be reused by a later window, so this call is only meaningful
    /// for a handle that was captured and checked without an intervening window creation: the
    /// disposal test captures the handle and asserts <c>false</c> inside one test body.
    /// </remarks>
    internal static bool IsWindow(IntPtr hWnd) => IsWindowNative(hWnd);

    /// <summary>
    /// Sends a window message to the given window and waits for it to be processed.</summary>
    /// <param name="hWnd">The target window handle.</param>
    /// <param name="msg">The message id.</param>
    /// <param name="wParam">The first message parameter.</param>
    /// <param name="lParam">The second message parameter.</param>
    /// <returns>The value the window procedure returned.</returns>
    /// <remarks>
    /// This is the behavioural half of the hook-retention evidence: a hook that was created as a
    /// temporary and then collected produces a window that silently stops calling back, and no
    /// reflection assertion would notice. Sending a message from the owning thread and observing
    /// the callback does notice, because <c>SendMessage</c> to a window on the calling thread runs
    /// the window procedure - and therefore the hook - synchronously.
    /// </remarks>
    internal static IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        SendMessageWNative(hWnd, msg, wParam, lParam);

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

    /// <summary>
    /// Returns a window's owner (its <c>GW_OWNER</c>), or <see cref="IntPtr.Zero"/> when the window
    /// has none.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns>The owner window handle, or <see cref="IntPtr.Zero"/>.</returns>
    /// <remarks>
    /// This is the measurement at the centre of R003: a menu popup with no owner never dismisses on
    /// an outside click, so the dismissal proof reads the popup's <c>GW_OWNER</c> and asserts it is
    /// the anchor window. An ownerless popup is visually indistinguishable from a correct one, which
    /// is exactly why the value is asserted instead of the appearance. It delegates to
    /// <see cref="IShellApi.GetWindowOwner"/> so the entry point has one home; only the
    /// <c>GW_OWNER</c> relationship is ever asked for, which is why the command is no longer a
    /// parameter.
    /// </remarks>
    internal static IntPtr GetWindowOwner(IntPtr hWnd) => Seam.GetWindowOwner(hWnd);

    /// <summary>
    /// Reads a window's screen rectangle in physical pixels.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="rectangle">Receives the rectangle; unmodified on failure.</param>
    /// <returns><see langword="true"/> when the rectangle was read.</returns>
    /// <remarks>
    /// Used to locate the menu popup among this process's windows (the popup is the one window
    /// larger than the 1x1 anchor and the zero-sized host) and to prove that the anchor sits at the
    /// physical coordinates it was given. It is a diagnostic and assertion call only: the shipping
    /// placement arithmetic never reads a window rectangle.
    /// </remarks>
    internal static bool GetWindowRect(IntPtr hWnd, out NativeRect rectangle) =>
        GetWindowRectNative(hWnd, out rectangle);

    /// <summary>
    /// Reads a window's class name.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns>
    /// The class name, or an empty string when the window does not exist or the name could not be
    /// read.
    /// </returns>
    /// <remarks>
    /// Diagnostics only: the dismissal proof identifies the popup by rectangle and ownership, never
    /// by class name, because the WPF class name is a runtime implementation detail. The name is
    /// captured so a failing check reports <em>which</em> window it was looking at rather than a
    /// bare handle.
    /// </remarks>
    internal static string GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        int length = GetClassNameNative(hWnd, buffer, buffer.Capacity);

        return length > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// Lists the visible top-level windows belonging to the given process.
    /// </summary>
    /// <param name="processId">The process id whose windows to return.</param>
    /// <returns>The matching window handles, in enumeration order.</returns>
    /// <remarks>
    /// <para>
    /// The enumeration helper the menu-dismissal measurement needs: it is how the test finds the
    /// popup without knowing anything about how WPF created it, and how it proves that a dismissed
    /// menu and a disposed anchor leave nothing behind. Top-level-ness is what <c>EnumWindows</c>
    /// enumerates by definition - child windows are not visited - so this returns exactly the
    /// windows an ownerless popup would be compared against.
    /// </para>
    /// <para>
    /// The callback delegate is a local on purpose: it must stay alive for the duration of the
    /// native call, and a local is the simplest way to guarantee that. The enumeration never stops
    /// early, so the whole first-level window list is always walked.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<IntPtr> GetVisibleTopLevelWindowsOfProcess(uint processId)
    {
        var windows = new List<IntPtr>();

        EnumWindowsCallback callback = (hWnd, _) =>
        {
            if (Seam.GetWindowThreadProcessId(hWnd, out uint windowProcessId) != 0
                && windowProcessId == processId
                && IsWindowVisibleNative(hWnd))
            {
                windows.Add(hWnd);
            }

            return true;
        };

        EnumWindowsNative(callback, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Returns the foreground window.
    /// </summary>
    /// <returns>The foreground window handle, or <see cref="IntPtr.Zero"/> when none.</returns>
    /// <remarks>
    /// Diagnostics only: when an injected outside click fails to dismiss the menu, the foreground
    /// window at that moment names where the click actually landed, which is the difference between
    /// "the popup was not dismissable" and "the click hit something that swallowed it". It delegates
    /// to <see cref="IShellApi.GetForegroundWindow"/> so the entry point has one home - and so a test
    /// that scripts the seam can make this reading deterministic.
    /// </remarks>
    internal static IntPtr GetForegroundWindow() => Seam.GetForegroundWindow();

    /// <summary>
    /// Moves a window to a new position, honouring the given <c>SWP_*</c> flags.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="hWndInsertAfter">The Z-order insertion point; ignored with
    /// <see cref="SWP_NOZORDER"/>.</param>
    /// <param name="x">The new left edge, in physical screen pixels.</param>
    /// <param name="y">The new top edge, in physical screen pixels.</param>
    /// <param name="width">The new width, in physical pixels; ignored with
    /// <see cref="SWP_NOSIZE"/>.</param>
    /// <param name="height">The new height, in physical pixels; ignored with
    /// <see cref="SWP_NOSIZE"/>.</param>
    /// <param name="flags">The <c>SWP_*</c> flags; the anchor passes
    /// <see cref="SWP_NOSIZE"/> | <see cref="SWP_NOZORDER"/> | <see cref="SWP_NOACTIVATE"/>.</param>
    /// <returns><see langword="true"/> when the window was moved.</returns>
    /// <remarks>
    /// Used by <see cref="TrayMenuAnchorWindow.Position"/>. The coordinates are physical pixels -
    /// the same units the shell reports an icon rectangle in - so no DPI conversion happens at this
    /// layer; the conversion belongs to <see cref="TrayIconPlacement"/>, which produces the
    /// offsets the placement engine consumes.
    /// </remarks>
    internal static bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags) =>
        SetWindowPosNative(hWnd, hWndInsertAfter, x, y, width, height, flags);

    [DllImport("user32.dll", EntryPoint = "GetAncestor", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetAncestorNative(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SendMessageWNative(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectNative(IntPtr hWnd, out NativeRect rectangle);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameNative(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPosNative(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindowsNative(EnumWindowsCallback callback, IntPtr lParam);

    /// <summary>
    /// The callback <c>EnumWindows</c> invokes for each top-level window.
    /// </summary>
    /// <param name="hWnd">The enumerated window.</param>
    /// <param name="lParam">The caller's value, unused here.</param>
    /// <returns><see langword="true"/> to continue enumerating.</returns>
    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetWindowLongPtrWNative(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true, ExactSpelling = true)]
    private static extern int GetWindowLongWNative(IntPtr hWnd, int nIndex);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcessNative();
}
