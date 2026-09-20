using System.Windows.Interop;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The hidden <b>top-level</b> message window every tray icon is anchored to: it receives the
/// shell's notification callbacks and the session-wide <c>TaskbarCreated</c> broadcast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Top-level, not message-only (D011).</b> The host must receive <c>TaskbarCreated</c>, which
/// the shell delivers with <c>HWND_BROADCAST</c>. Broadcasts reach top-level windows only -
/// message-only windows (<c>hWndParent = HWND_MESSAGE</c>) are a distinct, non-top-level window
/// category that a broadcast never reaches, so a message-only host would silently never learn
/// that Explorer restarted and the icon would stay missing with no error anywhere. This window is
/// therefore created with a zero parent, and the test suite proves the consequence empirically
/// with <c>GetAncestor(hwnd, GA_ROOT) == hwnd</c> rather than relying on
/// <see cref="HwndSourceParameters.ParentWindow"/>'s undocumented "zero means top-level" reading.
/// </para>
/// <para>
/// <b>Invisible by construction.</b> <c>WS_VISIBLE</c> is never set, the window is zero-sized and
/// positioned off the way of any UI, and it carries <see cref="Win32.WS_EX_TOOLWINDOW"/> without
/// <see cref="Win32.WS_EX_APPWINDOW"/>, so it has no taskbar button and no Alt-Tab entry. A
/// library whose entire UI is the notification area must not produce a phantom window.
/// </para>
/// <para>
/// <b>The hook is held by a strong field on purpose (MEM013).</b> WPF keeps
/// <see cref="HwndSourceHook"/> delegates weakly, so a hook that exists only as a local (or only
/// as the argument to <c>AddHook</c>) can be collected while the window lives; the window then
/// keeps working and simply stops calling back, which looks exactly like a shell problem. The
/// field is what keeps the callback alive, so it must never be "cleaned up" - and the test suite
/// proves it behaviourally by sending a message and observing the callback, not only by
/// reflection.
/// </para>
/// <para>
/// <b>Raw stream, no policy.</b> The hook forwards every message to the registered callback,
/// including messages nothing consumes yet, because the event decoding (S02) must not have to
/// reopen or subclass this window. Nothing is filtered, logged or swallowed here. The callback
/// runs synchronously inside the window procedure on the host thread, so it must handle its own
/// failures rather than letting them escape: an exception thrown out of a window procedure is not
/// a catchable, attributable failure.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> The window is created on, and must be disposed from, the thread that
/// owns its dispatcher - the same thread rule WPF imposes on every <c>HwndSource</c>.
/// </para>
/// </remarks>
internal sealed class TrayMessageWindow : IDisposable
{
    /// <summary>
    /// The window name (and therefore the class-distinguishing title) handed to
    /// <see cref="HwndSourceParameters"/>. Diagnostic only: it is what a window-spy tool shows for
    /// the host, so it names the library that created it.
    /// </summary>
    private const string WindowName = "Trustsoft.NotifyIcon.TrayMessageWindow";

    /// <summary>
    /// The registered message name the shell broadcasts when the taskbar (Explorer) is
    /// (re)created. The string is a documented de-facto contract and is <b>not</b> an SDK symbol,
    /// which is why the id is resolved at runtime through
    /// <see cref="IShellApi.RegisterWindowMessage"/> instead of being hardcoded.
    /// </summary>
    private const string TaskbarCreatedMessageName = "TaskbarCreated";

    /// <summary>The native host window. Owns the <c>HWND</c> and destroys it on disposal.</summary>
    private readonly HwndSource _hwndSource;

    /// <summary>The consumer's message sink, called for every message the host receives.</summary>
    private readonly Action<uint, IntPtr, IntPtr> _onMessage;

    /// <summary>
    /// The session-unique id of <c>TaskbarCreated</c>, or <c>0</c> when registration failed.
    /// </summary>
    private readonly uint _taskbarCreatedMessageId;

    /// <summary>
    /// The hook WPF calls for each message. Deliberately a field, not a local (MEM013): WPF holds
    /// hooks weakly, so this reference is the only thing keeping the callback alive. It is
    /// assigned a delegate in the constructor, read by <see cref="Dispose"/>, and nulled there so
    /// a disposed host does not pin the consumer's callback closure for the process lifetime.
    /// </summary>
    private HwndSourceHook? _hook;

    /// <summary>Set once by <see cref="Dispose"/> so repeated disposal is a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates the hidden top-level host window and registers it as a
    /// <c>TaskbarCreated</c> recipient.
    /// </summary>
    /// <param name="shell">
    /// The seam used to resolve the <c>TaskbarCreated</c> message id. This is the only seam call
    /// the host makes: window creation, window styles and message delivery go straight to the OS,
    /// so the host cannot be faked and its top-level-ness is measured rather than assumed.
    /// </param>
    /// <param name="onMessage">
    /// The sink for every message the host receives, as
    /// <c>(message, wParam, lParam)</c>. Called synchronously on the host thread inside the window
    /// procedure; it must not throw.
    /// </param>
    /// <param name="callbackMessageId">
    /// The message id the shell will be told to use for this icon's notifications
    /// (<see cref="NOTIFYICONDATAW.uCallbackMessage"/>). Stored and exposed as
    /// <see cref="CallbackMessageId"/> so the owner of the icon data and the owner of the window
    /// cannot drift apart.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shell"/> or <paramref name="onMessage"/> is <see langword="null"/>. Both are
    /// rejected before any window or message id is created, so a rejected construction has no side
    /// effects to clean up.
    /// </exception>
    /// <remarks>
    /// A failed <c>TaskbarCreated</c> registration does <b>not</b> throw: the host is still a
    /// perfectly usable message window, only the Explorer-restart recovery point is missing, and
    /// that is recorded as <see cref="TaskbarCreatedAvailable"/> plus
    /// <see cref="TaskbarCreatedRegistrationError"/> for the caller to surface. Throwing here would
    /// turn a recoverable degradation into a startup failure.
    /// </remarks>
    internal TrayMessageWindow(
        IShellApi shell,
        Action<uint, IntPtr, IntPtr> onMessage,
        uint callbackMessageId = ShellConstants.TrayCallbackMessage)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(onMessage);

        _onMessage = onMessage;
        CallbackMessageId = callbackMessageId;

        _taskbarCreatedMessageId = shell.RegisterWindowMessage(TaskbarCreatedMessageName);
        TaskbarCreatedAvailable = _taskbarCreatedMessageId != 0;

        if (!TaskbarCreatedAvailable)
        {
            // Only meaningful for the call that just failed (see IShellApi.GetLastError), so it is
            // read here and nowhere else. Deliberately not written to the trace channel: this
            // constructor holds no failure policy, and the caller that owns the recovery path is
            // the one that can say what the missing message means for the icon.
            TaskbarCreatedRegistrationError = shell.GetLastError();
        }

        // No WS_VISIBLE and no WS_EX_APPWINDOW: either one would make the host a visible artifact
        // (taskbar button, Alt-Tab entry) that contradicts "an app with no window". ParentWindow
        // zero is what makes the window top-level - the property D011 depends on and the tests
        // measure with GetAncestor/GA_ROOT.
        var parameters = new HwndSourceParameters(WindowName)
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
            ParentWindow = IntPtr.Zero,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            UsesPerPixelOpacity = false,
        };

        _hwndSource = new HwndSource(parameters);

        // Assigned to the field BEFORE AddHook so the strong reference exists for the whole time
        // WPF can hold the hook. See the type remarks: WPF's reference is weak (MEM013).
        _hook = OnWindowMessage;
        _hwndSource.AddHook(_hook);
    }

    /// <summary>
    /// Gets the message id the shell is told to use for this icon's notifications.
    /// </summary>
    /// <value>The value written to <see cref="NOTIFYICONDATAW.uCallbackMessage"/>.</value>
    internal uint CallbackMessageId { get; }

    /// <summary>
    /// Gets a value indicating whether the <c>TaskbarCreated</c> message id was resolved.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when <see cref="IShellApi.RegisterWindowMessage"/> returned a non-zero
    /// id, so an incoming message can be matched against it and Explorer-restart recovery can work.
    /// </value>
    /// <remarks>
    /// A <see langword="false"/> value is a named failure state, not merely a zero: id <c>0</c> is
    /// the registration failure value and can never equal an incoming message, so a host that
    /// cached <c>0</c> as if it were an id would silently never see the broadcast. Callers that
    /// depend on recovery read this flag rather than comparing ids.
    /// </remarks>
    internal bool TaskbarCreatedAvailable { get; }

    /// <summary>
    /// Gets the Win32 error code reported by the failed <c>TaskbarCreated</c> registration, or
    /// <c>0</c> when the registration succeeded.
    /// </summary>
    /// <value>A last-error code captured from the registration call that failed.</value>
    /// <remarks>Diagnostics only: it explains <see cref="TaskbarCreatedAvailable"/> being
    /// <see langword="false"/>, and it is captured at the only moment the code is valid.</remarks>
    internal int TaskbarCreatedRegistrationError { get; }

    /// <summary>
    /// Gets the native handle of the host window, for callers that must name a window owner - the
    /// shell registration (<see cref="NOTIFYICONDATAW.hWnd"/>) and, later, the popup menu owner.
    /// </summary>
    /// <value>The <c>HWND</c> while the host is alive, or <see cref="IntPtr.Zero"/> once it has
    /// been disposed.</value>
    internal IntPtr Handle => _hwndSource.Handle;

    /// <summary>
    /// Reports whether an incoming message is the registered <c>TaskbarCreated</c> broadcast.
    /// </summary>
    /// <param name="msg">The message id, as delivered to the hook.</param>
    /// <returns>
    /// <see langword="true"/> only when the registration succeeded and <paramref name="msg"/>
    /// equals the registered id.
    /// </returns>
    /// <remarks>
    /// The availability check is not redundant: without it, a failed registration (<c>0</c>) would
    /// match message <c>0</c> and report a broadcast that never happened.
    /// </remarks>
    internal bool IsTaskbarCreatedMessage(uint msg) =>
        TaskbarCreatedAvailable && msg == _taskbarCreatedMessageId;

    /// <summary>
    /// Destroys the host window and detaches the hook. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// Never throws: disposal is the last thing a windowless host does during shutdown, and a
    /// failure there has no useful owner. The hook is removed before the window is destroyed so the
    /// callback is not invoked for the destruction messages, and the field is cleared afterwards so
    /// the consumer's callback closure is not pinned by a dead host.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        HwndSourceHook? hook = _hook;
        if (hook is not null)
        {
            _hwndSource.RemoveHook(hook);
            _hook = null;
        }

        _hwndSource.Dispose();
    }

    /// <summary>
    /// The window procedure: forwards every message to the registered callback.
    /// </summary>
    /// <param name="hwnd">The receiving window handle.</param>
    /// <param name="msg">The message id.</param>
    /// <param name="wParam">The first message parameter.</param>
    /// <param name="lParam">The second message parameter.</param>
    /// <param name="handled">Unused; the host never marks a message handled.</param>
    /// <returns>Always <see cref="IntPtr.Zero"/> - default processing still runs.</returns>
    /// <remarks>
    /// Allocation-free apart from the delegate invocation, because this runs for every message the
    /// window receives. The whole raw stream is forwarded, not only the messages today's consumer
    /// understands, so a later consumer can add decoding without touching this class.
    /// </remarks>
    private IntPtr OnWindowMessage(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        _onMessage((uint)msg, wParam, lParam);
        return IntPtr.Zero;
    }
}
