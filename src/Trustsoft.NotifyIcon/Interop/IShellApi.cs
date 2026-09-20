namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The single seam through which the library talks to Windows: the notification area
/// (<c>shell32</c>), the window manager (<c>user32</c>), GDI (<c>gdi32</c>) and the kernel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> It is required, not convenient. The shell call sequences and the
/// failure policy this library must prove cannot be exercised against a live notification
/// area: a test host may not even have one, and forcing a real <c>Shell_NotifyIconW</c>
/// failure is impossible. The seam makes the sequence observable (a recording fake) and the
/// failure injectable (a scripted fake), which is what the later tasks assert against:
/// <c>NIM_ADD</c> immediately followed by <c>NIM_SETVERSION(4)</c>, the mandatory
/// <c>NIF_SHOWTIP</c> flag, the retry-once-then-surface policy, the retain-and-destroy HICON
/// ownership rule and the GDI handle accounting.
/// </para>
/// <para>
/// <b>Raw results, no policy.</b> Every member returns exactly what the underlying Win32 call
/// returned, including its failure value (<see langword="false"/>, <c>0</c>,
/// <see cref="IntPtr.Zero"/>). Interpreting a failure - raising a named exception, retrying
/// once, tracing - belongs to the caller. Nothing is reported as success here unless the OS
/// confirmed it (D008).
/// </para>
/// <para>
/// <b>The seam is the only place P/Invoke happens for these functions.</b>
/// <see cref="ShellApi"/> is the only implementation that declares <c>[DllImport]</c> for them,
/// so a wrong entry point, a wrong character set or a missing <c>SetLastError</c> has exactly
/// one home to inspect. The test suite pairs it with a scripted fake (deterministic sequences)
/// and a recording wrapper around the real implementation (signature agreement).
/// </para>
/// <para>
/// <b>Last error is state, not a return value.</b> The Win32 last-error value is only
/// meaningful for the call that just failed and is not preserved across managed calls, so this
/// seam captures it inside the same member that made the call and exposes it through
/// <see cref="GetLastError"/>. Read it only after a member reported failure; a successful call
/// never promises anything about it.
/// </para>
/// </remarks>
internal interface IShellApi
{
    /// <summary>
    /// Calls <c>Shell_NotifyIconW</c> with the given message code and data.
    /// </summary>
    /// <param name="dwMessage">
    /// The operation code (<c>NIM_ADD</c>, <c>NIM_MODIFY</c>, <c>NIM_DELETE</c>,
    /// <c>NIM_SETVERSION</c>, ... from <see cref="ShellConstants"/>).
    /// </param>
    /// <param name="data">
    /// The <see cref="NOTIFYICONDATAW"/> describing the icon. Passed by reference.
    /// </param>
    /// <returns>The raw boolean result of the shell call; <see langword="false"/> means the
    /// operation did not happen and <see cref="GetLastError"/> then carries the reason.</returns>
    /// <remarks>
    /// <para>
    /// <b>The implementation MUST hand the caller's own struct to the shell by reference; it
    /// must not copy it into a new instance.</b> The <c>ref</c> aliasing is part of the
    /// contract: <c>NOTIFYICONDATAW</c> is an input/output structure, and an implementation that
    /// copies it (or a fake that hands out a different instance) would silently discard any
    /// value the shell writes back, with no way for a test to see it.
    /// </para>
    /// <para>
    /// <b>Consumed by T07</b> (<c>TrayIcon</c> lifecycle: <c>NIM_ADD</c>, <c>NIM_SETVERSION</c>,
    /// <c>NIM_MODIFY</c>, <c>NIM_DELETE</c>) and by the lifecycle and negative tests that script
    /// its result.
    /// </para>
    /// </remarks>
    bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW data);

    /// <summary>
    /// Calls <c>RegisterWindowMessageW</c> to obtain a session-unique message id for the given
    /// message name.
    /// </summary>
    /// <param name="message">The message name, for example <c>"TaskbarCreated"</c>.</param>
    /// <returns>
    /// The registered message id, or <c>0</c> on failure. <b>A result of 0 must be treated as a
    /// hard failure by the caller</b>: message id 0 can never match an incoming message, so a
    /// consumer that cached 0 would silently stop receiving the message it registered for.
    /// </returns>
    /// <remarks>
    /// <b>Consumed by T05</b> (<c>TrayMessageWindow</c> registers <c>"TaskbarCreated"</c> to
    /// reserve the explorer-restart recovery point; the id is cached, and a 0 result becomes the
    /// host's named <c>TaskbarCreatedAvailable = false</c> failure state).
    /// </remarks>
    uint RegisterWindowMessage(string message);

    /// <summary>
    /// Calls <c>CreateIconIndirect</c> to build an icon from the bitmaps described by
    /// <paramref name="iconInfo"/>.
    /// </summary>
    /// <param name="iconInfo">
    /// The icon description. <c>CreateIconIndirect</c> <em>copies</em> the two bitmaps, so the
    /// caller keeps ownership of <c>hbmMask</c> and <c>hbmColor</c> and must delete them.
    /// </param>
    /// <returns>
    /// The new <c>HICON</c>, or <see cref="IntPtr.Zero"/> on failure, in which case
    /// <see cref="GetLastError"/> carries the reason so the caller can raise a named exception.
    /// </returns>
    /// <remarks>
    /// <b>Consumed by T06</b> (<c>HiconFactory</c>, the <c>ImageSource</c> to <c>HICON</c>
    /// conversion). The returned icon belongs to the caller: the factory never destroys it, and
    /// <c>TrayIcon</c> (T07) owns it under the retain-and-destroy rule.
    /// </remarks>
    IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    /// <summary>
    /// Calls <c>DestroyIcon</c> to release an icon created by <see cref="CreateIconIndirect"/>.
    /// </summary>
    /// <param name="hIcon">The icon handle to release.</param>
    /// <returns><see langword="true"/> when the icon was destroyed; <see langword="false"/>
    /// otherwise (for example for a null or already-freed handle).</returns>
    /// <remarks>
    /// <b>Consumed by T07</b>, which destroys every HICON it owns exactly once - on replacement
    /// success, on disposal, and on the failure paths that produce an icon that is not kept. It
    /// is not called by T06: a destroy inside the conversion factory would be a double free.
    /// </remarks>
    bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Calls <c>DeleteObject</c> to release a GDI object (here: an <c>HBITMAP</c>).
    /// </summary>
    /// <param name="hObject">The GDI object handle to delete.</param>
    /// <returns><see langword="true"/> when the object was deleted.</returns>
    /// <remarks>
    /// <b>Consumed by T06 only</b>: the conversion factory frees its two temporary
    /// <c>HBITMAP</c>s on every exit path, which is the R007 leak evidence. The icon lifecycle
    /// (T07) never calls it - HICONs are released with <see cref="DestroyIcon"/>, and HICON is
    /// not a GDI object <c>DeleteObject</c> accepts.
    /// </remarks>
    bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Calls <c>GetGuiResources</c> to read a per-process GUI resource counter.
    /// </summary>
    /// <param name="hProcess">The process handle, normally
    /// <see cref="Win32.GetCurrentProcess"/>.</param>
    /// <param name="uiFlags">The counter to read, normally
    /// <see cref="ShellConstants.GR_GDIOBJECTS"/>.</param>
    /// <returns>The count, or <c>0</c> on failure.</returns>
    /// <remarks>
    /// <b>Consumed by tests and diagnostics only</b>, never by the shipping code paths. It sits
    /// on the seam so that the GDI leak evidence for R007 goes through the same indirection as
    /// everything else, and so the counter can be compared against an independent, test-local
    /// declaration of the same call. Use it as a <em>delta</em> against a measured baseline: a
    /// freshly started process legitimately owns zero GDI objects, so a positive count is not a
    /// meaningful assertion and a zero count is not an error.
    /// </remarks>
    uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    /// <summary>
    /// Returns the Win32 error code captured for the most recent seam call.
    /// </summary>
    /// <returns>The raw error code, or <c>0</c> when nothing has failed.</returns>
    /// <remarks>
    /// <para>
    /// <b>The real implementation must capture this as the very first thing after the failing
    /// P/Invoke, before any other managed call.</b> The last-error value is not preserved across
    /// managed calls and any other P/Invoke that runs in between - including one that succeeds -
    /// can overwrite it. <see cref="ShellApi"/> therefore stores the error inside the same member
    /// that made the call, and this member only reads that stored value. Do not read Win32's
    /// thread last-error slot from here: by the time this method runs, the value belongs to
    /// whatever call happened last, not to the call that failed.
    /// </para>
    /// <para>
    /// <b>Read it only after a member returned a failure value.</b> No Win32 call clears the
    /// last error on success, so the value after a successful call is stale and meaningless; the
    /// test double is deliberately stricter and clears it, so a test that reads it after a
    /// success fails rather than passing on a stale number.
    /// </para>
    /// <para>
    /// <b>Consumed by T06</b> (conversion failures carry the code into
    /// <c>TrayIconException</c>), <b>T07</b> (<c>Add</c>, <c>SetVersion</c>, <c>Modify</c>,
    /// <c>Remove</c> failures) and <b>T09</b> (real-signature probe).
    /// </para>
    /// </remarks>
    int GetLastError();
}
