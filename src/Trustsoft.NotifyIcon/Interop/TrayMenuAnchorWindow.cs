using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The tiny, invisible, real <b>top-level</b> window a tray icon's context menu is owned by: a 1x1
/// <c>WS_POPUP</c> window at the icon, made foreground for the duration of the menu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a second window exists at all (measured, MEM064).</b> A WPF <c>ContextMenu</c> opened
/// from the library's tray host is <em>ownerless</em> - the popup's <c>GetWindow(GW_OWNER)</c> is
/// <see cref="IntPtr.Zero"/> - and an ownerless popup is never dismissed by an outside click. This
/// was measured, not deduced: with the exact shape of the tray host (a hidden, zero-sized top-level
/// window) and <c>PlacementMode.AbsolutePoint</c> and no placement target, the menu opened at the
/// right place, stayed open after a real outside click, and reported an owner of <c>0x0</c>. The raw
/// measurement is recorded in <c>.gsd/s03-evidence/contextmenu-placement-probe.txt</c> with the
/// harness in <c>.gsd/probe-menu</c>, and the failure is pinned by a test that asserts it is still
/// the failing case (<c>TrayMenuDismissalTests</c>).
/// </para>
/// <para>
/// <b>Why not simply reuse the elements the library already has.</b> WPF refuses to open a menu
/// whose <c>PlacementTarget</c> has never been laid out: a bare <see cref="FrameworkElement"/> - and
/// the <see cref="TrayIcon"/> element, which never enters a visual tree - produced <em>no popup at
/// all</em>, for <c>AbsolutePoint</c>, <c>RelativePoint</c> and <c>MousePoint</c> alike. The
/// ownership relationship comes from the placement target's <em>window</em>, so the library needs a
/// window that (a) really exists, (b) sits at the tray icon, and (c) can be made the foreground
/// window. This class is that window, and it is the only hand-rolled piece of the menu path: the
/// popup itself, its placement clamping and its dismissal are all the framework's job.
/// </para>
/// <para>
/// <b>Activation, not visibility.</b> The window is never shown. It carries no <c>WS_VISIBLE</c>
/// (measured: the popup still opens and still dismisses), so a library whose entire UI is the
/// notification area puts nothing on screen. What it must have is the <em>activation
/// relationship</em>: <see cref="MakeForeground"/> is what makes the popup acquire this window as
/// its owner, and the measurement is unambiguous - the same window, the same placement target and
/// the same offsets with the foreground call omitted produced an ownerless popup that never
/// dismissed. This is exactly why <c>WS_EX_NOACTIVATE</c> is deliberately <em>not</em> set: a
/// non-activating window has no activation relationship for Windows to route the dismissal
/// through.
/// </para>
/// <para>
/// <b>Not the shell registration host.</b> The tray host
/// (<see cref="TrayMessageWindow"/>) remains the window the shell registration names and the window
/// that receives the icon's callbacks; that is unchanged. The <c>TrayMessageWindow.Handle</c>
/// documentation used to describe the host as "and, later, the popup menu owner", which the
/// measurement above contradicts, and the sentence now points here instead. Two windows exist
/// because the two jobs need different window properties: a zero-sized <c>WS_EX_TOOLWINDOW</c>
/// message sink for the shell, and a 1x1 activating popup owner for the menu. Attempting both in
/// one window would make the host visible or the menu undismissable.
/// </para>
/// <para>
/// <b>Placement target, laid out.</b> <see cref="RootVisual"/> is a laid-out 1x1
/// <see cref="Border"/> assigned as the <c>HwndSource</c>'s root visual, and it - not the window
/// handle - is what the menu is told to attach to. <c>HwndSource</c> lays its root visual out
/// synchronously at the window's size when it is assigned (measured: a valid 1x1 layout is
/// available immediately), which is what keeps a never-laid-out element out of the menu path
/// entirely.
/// </para>
/// <para>
/// <b>Lifetime.</b> Created when a menu opens and destroyed when it closes, matching the tray
/// host's "nothing is created until it is needed" precedent, so a consumer that never opens a menu
/// never owns this window. <see cref="Dispose"/> is safe to call twice, because the menu's
/// <c>Closed</c> handler and the icon's own disposal can both arrive at it, in either order.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> Like every <c>HwndSource</c>, the window is created on and must be
/// disposed from the thread that owns its dispatcher.
/// </para>
/// </remarks>
internal sealed class TrayMenuAnchorWindow : IDisposable
{
    /// <summary>
    /// The window name (and therefore the class-distinguishing title) handed to
    /// <see cref="HwndSourceParameters"/>. Diagnostic only: it is what a window-spy tool shows for
    /// the anchor, so it names the library that created it.
    /// </summary>
    private const string WindowName = "Trustsoft.NotifyIcon.TrayMenuAnchorWindow";

    /// <summary>
    /// The anchor's side length in physical pixels. The window only exists to be a real window that
    /// owns the popup and carries an activation relationship, so it is as small as a window can be;
    /// the menu's own size comes entirely from its items.
    /// </summary>
    private const int AnchorSize = 1;

    /// <summary>The native anchor window. Owns the <c>HWND</c> and destroys it on disposal.</summary>
    private readonly HwndSource _hwndSource;

    /// <summary>
    /// The seam the anchor's foreground claim is made through.
    /// </summary>
    /// <remarks>
    /// The claim is the one call this class makes that a session can refuse, so it is routed
    /// through <see cref="IShellApi"/> rather than a direct P/Invoke: a test scripts the refusal to
    /// reproduce the hostile state deterministically (docs/TEST-ENVIRONMENT.md), and
    /// <see cref="ShellApi"/> stays the only home of the entry point.
    /// </remarks>
    private readonly IShellApi _shell;

    /// <summary>
    /// The laid-out 1x1 visual the menu is attached to. Held by a field so it outlives the layout
    /// pass that gives it its size and so <see cref="RootVisual"/> can hand out a stable instance.
    /// </summary>
    private readonly FrameworkElement _rootVisual;

    /// <summary>Set once by <see cref="Dispose"/> so repeated disposal is a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates the anchor window, invisible, at the given physical screen position, claiming the
    /// foreground through a fresh real <see cref="ShellApi"/>.
    /// </summary>
    /// <param name="physicalX">The left edge in physical screen pixels.</param>
    /// <param name="physicalY">The top edge in physical screen pixels.</param>
    /// <remarks>
    /// The two-argument overload is for callers (tests measuring the real construction) that need
    /// the real foreground call; the library's own menu path uses
    /// <see cref="TrayMenuAnchorWindow(IShellApi, int, int)"/> so the claim is the session's and not
    /// a test-local side channel.
    /// </remarks>
    internal TrayMenuAnchorWindow(int physicalX, int physicalY)
        : this(new ShellApi(), physicalX, physicalY)
    {
    }

    /// <summary>
    /// Creates the anchor window, invisible, at the given physical screen position.
    /// </summary>
    /// <param name="shell">
    /// The seam the foreground claim is made through. Passed by the menu path so a test can refuse
    /// the claim exactly the way Windows refuses it.
    /// </param>
    /// <param name="physicalX">
    /// The left edge in <b>physical screen pixels</b> - the same units
    /// <c>Shell_NotifyIconGetRect</c> reports an icon rectangle in. No DPI conversion happens here:
    /// the physical-to-offset conversion is <see cref="TrayIconPlacement"/>'s job, and doing any of
    /// it here would be the second application of the scale factor.
    /// </param>
    /// <param name="physicalY">The top edge in physical screen pixels.</param>
    /// <remarks>
    /// The window style is <see cref="Win32.WS_POPUP"/> (a popup has no frame, no caption and no
    /// taskbar presence of its own) with <see cref="Win32.WS_EX_TOOLWINDOW"/> (keeps it out of
    /// Alt-Tab even in the moment it holds the foreground) and deliberately <b>without</b>
    /// <c>WS_EX_NOACTIVATE</c> and <c>WS_VISIBLE</c> - see the type remarks for why each of those
    /// omissions is measured rather than stylistic. Zero parent makes the window top-level, which is
    /// what makes it a legal popup owner at all.
    /// </remarks>
    internal TrayMenuAnchorWindow(IShellApi shell, int physicalX, int physicalY)
    {
        _rootVisual = new Border { Width = AnchorSize, Height = AnchorSize };
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        var parameters = new HwndSourceParameters(WindowName)
        {
            WindowStyle = unchecked((int)Win32.WS_POPUP),
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
            ParentWindow = IntPtr.Zero,
            Width = AnchorSize,
            Height = AnchorSize,
            PositionX = physicalX,
            PositionY = physicalY,
            UsesPerPixelOpacity = false,
        };

        _hwndSource = new HwndSource(parameters);

        // Assigning the root visual is what lays it out (the window decides its size), and a laid
        // out visual is a precondition for the menu: a never-laid-out element produces no popup at
        // all, which is why this is not a bare FrameworkElement.
        _hwndSource.RootVisual = _rootVisual;
    }

    /// <summary>
    /// Gets the native handle of the anchor window, for callers that must name a window - either
    /// as the menu's placement target's window or in an assertion.
    /// </summary>
    /// <value>The <c>HWND</c> while the anchor is alive, or <see cref="IntPtr.Zero"/> once it has
    /// been disposed.</value>
    internal IntPtr Handle => _hwndSource.Handle;

    /// <summary>
    /// Gets the laid-out 1x1 visual to assign to the menu's
    /// <see cref="System.Windows.Controls.ContextMenu.PlacementTarget"/>.
    /// </summary>
    /// <value>The root visual of the anchor window, which is what gives the popup this window as
    /// its owner.</value>
    /// <remarks>
    /// The menu is attached to this element rather than to <see cref="Handle"/> because that is the
    /// API <c>ContextMenu.PlacementTarget</c> has: the framework then derives the owner window from
    /// the element's window. The element is never added to any other visual tree and never
    /// rendered; its only purpose is to be laid out inside the anchor.
    /// </remarks>
    internal FrameworkElement RootVisual => _rootVisual;

    /// <summary>
    /// Moves the anchor to a new physical screen position.
    /// </summary>
    /// <param name="physicalX">The new left edge in physical screen pixels.</param>
    /// <param name="physicalY">The new top edge in physical screen pixels.</param>
    /// <returns><see langword="true"/> when the window moved; <see langword="false"/> when it could
    /// not (for example after disposal, when there is no window left to move).</returns>
    /// <remarks>
    /// The size and the Z order are deliberately untouched
    /// (<see cref="Win32.SWP_NOSIZE"/> | <see cref="Win32.SWP_NOZORDER"/>) and the window is not
    /// activated (<see cref="Win32.SWP_NOACTIVATE"/>): activation is
    /// <see cref="MakeForeground"/>'s job and is performed once per menu open, not as a side effect
    /// of a move.
    /// </remarks>
    internal bool Position(int physicalX, int physicalY) =>
        Win32.SetWindowPos(
            _hwndSource.Handle,
            IntPtr.Zero,
            physicalX,
            physicalY,
            AnchorSize,
            AnchorSize,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

    /// <summary>
    /// Makes the anchor the foreground window, which is what gives the menu's popup a real owner.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window became foreground; <see langword="false"/> when
    /// Windows refused, which also means the popup cannot be trusted to dismiss.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The call is load-bearing, not etiquette. Measured: with this call skipped, the identical
    /// anchor window and placement target produced a popup whose owner was <see cref="IntPtr.Zero"/>
    /// and which an outside click did not close - the failure R003's dismissal clause is about. The
    /// return value is therefore surfaced rather than swallowed, so the menu path can decide whether
    /// the menu it is about to open will behave.
    /// </para>
    /// <para>
    /// Windows may legitimately refuse the request (the foreground lock), and the button that
    /// opened the menu belongs to the shell rather than to this process, so the caller must treat a
    /// <see langword="false"/> result as "the menu may not dismiss" rather than as an error.
    /// </para>
    /// </remarks>
    internal bool MakeForeground() => _shell.SetForegroundWindow(_hwndSource.Handle);

    /// <summary>
    /// Repairs the open popup's owner so that it is this anchor window, and returns the owner value
    /// before and after the repair.
    /// </summary>
    /// <param name="popup">
    /// The popup window the menu's own presentation source resolved to, or <see cref="IntPtr.Zero"/>
    /// when the open produced none.
    /// </param>
    /// <returns>The readings of the repair; see <see cref="MenuPopupOwnership"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why the library writes this value.</b> WPF decides the popup's owner inside
    /// <c>Popup.BuildWindow</c>, and only when the placement target resolves to an <c>HwndSource</c>
    /// that is connected to the foreground window at that instant. A session in which Windows
    /// refuses the foreground claim therefore produces an ownerless popup that an outside click does
    /// not dismiss - the value flips with the environment rather than with the code (measured, F5
    /// and D044). The value becomes the library's own by writing it here and reading it back, which
    /// is the one direction D043 allowed the S03 "the anchor or absent" bound to be tightened into
    /// an equality.
    /// </para>
    /// <para>
    /// <b>Nothing is written when the owner already is the anchor</b>, so the ordinary case - the
    /// process's click made the anchor foreground and WPF resolved the owner itself - is byte for
    /// byte the behaviour it was before this method existed.
    /// </para>
    /// <para>
    /// <b>A write is never trusted, only read back.</b> <c>SetWindowLongPtr</c> reports the
    /// <em>previous</em> owner and its zero result is ambiguous, so the value that reaches the trace
    /// line and the internal readings is the one <see cref="IShellApi.GetWindowOwner"/> reports
    /// afterwards. A refused write therefore shows up as <c>ownerAfter=0x0</c> in the open line, and
    /// the open still does not throw: the menu stays open, it just may not dismiss.
    /// </para>
    /// <para>
    /// <b>A destroyed anchor writes nothing.</b> If the menu closed while it was being opened, the
    /// anchor is already disposed and its handle is zero; writing zero as an owner would orphan the
    /// popup, so the repair reads and reports instead of writing a handle that no longer exists.
    /// </para>
    /// </remarks>
    internal MenuPopupOwnership OwnPopup(IntPtr popup)
    {
        if (popup == IntPtr.Zero)
        {
            return new MenuPopupOwnership(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        IntPtr handle = _hwndSource.Handle;
        IntPtr before = _shell.GetWindowOwner(popup);

        if (handle == IntPtr.Zero || before == handle)
        {
            return new MenuPopupOwnership(popup, before, before);
        }

        _shell.SetWindowOwner(popup, handle);

        return new MenuPopupOwnership(popup, before, _shell.GetWindowOwner(popup));
    }

    /// <summary>
    /// Re-claims the foreground for this anchor through the attach-thread sequence, when the claim
    /// made before the menu opened did not take effect.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this anchor was already the foreground window or became it;
    /// <see langword="false"/> when Windows refused again, which leaves the menu open and possibly
    /// undismissable rather than an error.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The measured mechanism (D044).</b> The owner write alone repairs the value but not the
    /// behaviour: the popup is owned by the anchor and the outside click still leaves it open. What
    /// makes the dismissal work is the <em>activation</em> relationship, and the documented way to
    /// obtain it after a refusal is to attach this thread's input queue to the foreground window's
    /// thread, raise the anchor, claim the foreground, and detach. All four steps are measured: the
    /// popup owned by the anchor, the anchor the desktop's foreground window, and the outside click
    /// dismissing the menu with no popup window left.
    /// </para>
    /// <para>
    /// <b>It is a no-op on the ordinary path.</b> When the earlier claim took effect the anchor is
    /// already the foreground window and nothing is attached, raised or re-claimed - which is what
    /// keeps the click-driven path unchanged rather than merely equivalent.
    /// </para>
    /// <para>
    /// <b><c>SwitchToThisWindow</c> is the recorded last resort</b>, taken only when there is no
    /// foreground window to attach to: <c>AttachThreadInput</c> needs a second thread, and a desktop
    /// with no foreground window has none. It is deliberately not the primary route (D044).
    /// </para>
    /// <para>
    /// <b>Every failure is a reading.</b> A failed attach, a failed raise and a refused claim are
    /// recorded by the caller's trace line and internal readings; the method itself never throws,
    /// because it runs while the menu is being opened from a window procedure.
    /// </para>
    /// </remarks>
    internal bool ReclaimForeground()
    {
        IntPtr handle = _hwndSource.Handle;

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr foreground = _shell.GetForegroundWindow();

        if (foreground == handle)
        {
            // The claim made before the popup existed took effect; there is nothing to reclaim.
            return true;
        }

        if (foreground == IntPtr.Zero)
        {
            // No foreground window to borrow an input queue from: the recorded last resort.
            _shell.SwitchToThisWindow(handle, altTab: false);
            return _shell.SetForegroundWindow(handle);
        }

        uint ourThread = _shell.GetCurrentThreadId();
        uint foregroundThread = _shell.GetWindowThreadProcessId(foreground, out _);

        // Attaching a thread to itself is not a join, and this thread already owns the foreground
        // input state when the foreground window is one of its own.
        bool attached = foregroundThread != 0
            && foregroundThread != ourThread
            && _shell.AttachThreadInput(ourThread, foregroundThread, fAttach: true);

        try
        {
            _shell.BringWindowToTop(handle);
            return _shell.SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                _shell.AttachThreadInput(ourThread, foregroundThread, fAttach: false);
            }
        }
    }

    /// <summary>
    /// Destroys the anchor window. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never throws: this runs on the menu-close path and during icon disposal, where a failure has
    /// no useful owner. The root visual is detached before the window is destroyed so the destroyed
    /// window cannot lay out a visual it no longer owns, and the source destroys the <c>HWND</c> -
    /// the same "release what can call back, then destroy" ordering
    /// <see cref="TrayMessageWindow.Dispose"/> uses for its message hook. That class needs a hook
    /// removal here there is no equivalent of: this window installs no message hook to remove,
    /// because nothing in the dismissal path is the library's to handle - Windows routes the
    /// dismissal through the ownership/activation relationship the window establishes.
    /// </para>
    /// <para>
    /// A second call is a no-op rather than an exception, because the menu's <c>Closed</c> handler
    /// and <see cref="TrayIcon"/>'s disposal can both reach this instance, in either order and
    /// possibly twice.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _hwndSource.RootVisual = null;
        _hwndSource.Dispose();
    }
}

/// <summary>
/// The readings of one popup-owner repair: which window was resolved as the popup and what its owner
/// was before and after the repair.
/// </summary>
/// <param name="Popup">
/// The popup window the menu's own presentation source resolved to, or <see cref="IntPtr.Zero"/> when
/// the open produced none.
/// </param>
/// <param name="OwnerBefore">The popup's <c>GW_OWNER</c> before the repair - what WPF's own construction decided.</param>
/// <param name="OwnerAfter">The popup's <c>GW_OWNER</c> read back after the repair, or the same value when nothing was written.</param>
/// <remarks>
/// <para>
/// A value rather than three loose fields so the open path can record the readings in one piece: the
/// trace line and the instance's internal diagnostics both carry exactly what was measured, and a
/// test can assert "WPF built it ownerless and the library repaired it" from the same two numbers
/// the support log shows.
/// </para>
/// <para>
/// <b>The pair is the evidence, not the write.</b> A popup whose owner was already the anchor reports
/// the same value twice, which is the ordinary click-driven case; a popup that WPF left ownerless and
/// the library repaired reports <see cref="IntPtr.Zero"/> then the anchor - the hostile-state case
/// this repair exists for.
/// </para>
/// </remarks>
internal readonly record struct MenuPopupOwnership(IntPtr Popup, IntPtr OwnerBefore, IntPtr OwnerAfter);
