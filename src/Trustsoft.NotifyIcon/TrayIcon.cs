using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The notification-area icon: a dependency-property driven element that owns the hidden host
/// window, the shell registration and the <c>HICON</c> lifetime behind one addressable object.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="FrameworkElement"/>, not a <see cref="DependencyObject"/> (D002).</b> The
/// element is never shown and never enters a visual tree, but it must be usable from XAML
/// markup: a <c>StaticResource</c> holding a <c>TrayIcon</c> subscribes to its routed event, and
/// the properties below are real dependency properties so that markup, styles and bindings work
/// against them the way they do for any other WPF element. A bare
/// <see cref="DependencyObject"/> would support neither the routed event nor resource lookup.
/// </para>
/// <para>
/// <b>One hidden top-level window per instance, created lazily.</b> The host
/// (<see cref="TrayMessageWindow"/>) is created on the first registration, on the dispatcher
/// thread, and destroyed by <see cref="Dispose"/>. Nothing is created until the icon is actually
/// shown, so an application that never sets <see cref="Visible"/> never creates a window.
/// </para>
/// <para>
/// <b>The registration sequence is a contract, not a preference.</b> The shell is told
/// <c>NIM_ADD</c> and then immediately <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c>, and the add
/// flags always include <c>NIF_SHOWTIP</c>. Version 4 makes the shell <em>suppress</em> the
/// standard tooltip unless that flag is present, so omitting it produces an icon whose
/// <see cref="ToolTipText"/> silently does nothing - no error, no exception, no trace (D012).
/// A <c>NIM_SETVERSION</c> failure deletes the registration that the successful add just
/// created: a half-registered icon (present, but with the old protocol) must never be left
/// behind.
/// </para>
/// <para>
/// <b>Two failure moments, two policies (D008).</b> A failure to <em>register</em> the icon is a
/// startup failure: it throws <see cref="TrayIconException"/> carrying the operation and the
/// Win32 error code, because an application whose only UI is the notification area would
/// otherwise degrade into a silently missing icon. A failure to <em>update</em> an already
/// registered icon is a runtime failure: the same call is retried exactly once
/// (<see cref="RuntimeRetryCount"/>) and, if the retry also fails, the failure is written to the
/// <see cref="NotifyIconTrace"/> channel and raised through the <see cref="TrayError"/> routed
/// event without terminating the process.
/// </para>
/// <para>
/// <b>Property state follows confirmed shell state (D008).</b> Registration state is only
/// updated in response to a result the shell confirmed, and a property change that could not be
/// applied at all is reverted before it is reported: a failed registration puts
/// <see cref="Visible"/> back to <see langword="false"/> and throws, and a change that no usable
/// dispatcher could carry out puts the property back to its previous value and throws
/// <see cref="InvalidOperationException"/>. The report is never silent, and the property never
/// claims a state that was not reached.
/// </para>
/// <para>
/// <b>HICON ownership is retain-and-destroy (R007).</b> The instance owns at most one icon
/// handle at a time: the one the shell has been given. A replacement destroys the <em>previous</em>
/// handle only after the shell accepted the new one, and a failed replacement destroys the
/// <em>new</em> handle instead - so neither a successful nor a failed change can leak an
/// <c>HICON</c>, and a failed change leaves the previously valid icon exactly as it was.
/// </para>
/// <para>
/// <b>Thread affinity is handled, not documented away (R015).</b> The assignment itself is
/// marshalled in the property accessor (<c>SetPropertyOnDispatcher</c>) - not in the change
/// callback, because WPF's own <c>SetValue</c> verifies thread access before any callback could
/// run - so an application may set these properties from a background thread. The single
/// documented exception is an instance that was created without any WPF dispatcher at all, which
/// has no thread to marshal to: that case raises <see cref="InvalidOperationException"/> and is
/// deliberately <em>not</em> a <see cref="TrayIconException"/>, because nothing about the
/// notification area failed.
/// </para>
/// <para>
/// <b>Disposal is mandatory and idempotent.</b> A tray-resident application must remove its icon
/// before it exits, or an orphaned icon stays in the notification area until the user hovers over
/// it. <see cref="Dispose"/> removes the registration, destroys the retained handle and destroys
/// the host window. S05 layers the process-exit fallback on top of it.
/// </para>
/// </remarks>
public class TrayIcon : FrameworkElement, IDisposable
{
    /// <summary>
    /// The name the <see cref="TrayErrorEvent"/> routed event is registered under, exposed as a
    /// constant so that markup, diagnostics and tests cannot spell it differently.
    /// </summary>
    public const string TrayErrorEventName = "TrayError";

    /// <summary>
    /// The routed event through which a runtime (that is, post-registration) notification-area
    /// failure is surfaced after the single retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bubbles, and it is raised on an element that normally has no parent and no visual
    /// tree - raising a routed event on a standalone element is supported and simply invokes the
    /// handlers attached to that element (asserted behaviourally by the lifecycle tests, because
    /// the whole product premise is "an application with no window").
    /// </para>
    /// <para>
    /// The event is the only user-visible failure channel a windowless host has besides the trace
    /// channel, which is why its payload carries the operation, the Win32 error code, the
    /// exception and the retry flag rather than a bare notification.
    /// </para>
    /// </remarks>
    public static readonly RoutedEvent TrayErrorEvent = EventManager.RegisterRoutedEvent(
        TrayErrorEventName,
        RoutingStrategy.Bubble,
        typeof(EventHandler<TrayErrorEventArgs>),
        typeof(TrayIcon));

    /// <summary>
    /// Identifies the <see cref="IconSource"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource),
        typeof(ImageSource),
        typeof(TrayIcon),
        new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Identifies the <see cref="ToolTipText"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToolTipTextProperty = DependencyProperty.Register(
        nameof(ToolTipText),
        typeof(string),
        typeof(TrayIcon),
        new PropertyMetadata(string.Empty, OnToolTipTextChanged, CoerceToolTipText));

    /// <summary>
    /// Identifies the <see cref="Visible"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty VisibleProperty = DependencyProperty.Register(
        nameof(Visible),
        typeof(bool),
        typeof(TrayIcon),
        new PropertyMetadata(false, OnVisibleChanged));

    /// <summary>
    /// The number of times a failed runtime shell call is retried before the failure is surfaced.
    /// </summary>
    /// <remarks>
    /// A named constant rather than an emergent number: the policy is "retry exactly once, then
    /// report and continue", and a test asserts the count by counting seam calls.
    /// </remarks>
    private const int RuntimeRetryCount = 1;

    /// <summary>
    /// The edge length in pixels the icon is rasterized at.
    /// </summary>
    /// <remarks>
    /// A system-metric icon size (small icons are 16x16 at 100% DPI) is a fixed value for M001;
    /// DPI- and setting-aware size selection is S03's contract, which is why the resolution is
    /// not spread over the lifecycle code.
    /// </remarks>
    private const int IconPixelSize = 16;

    /// <summary>
    /// The longest tooltip the shell can hold: <c>WCHAR szTip[128]</c> in the header, and
    /// <see cref="char"/> is a UTF-16 code unit, so 127 characters fit next to the terminator.
    /// </summary>
    private const int MaxToolTipLength = 127;

    /// <summary>
    /// The process-wide source of icon ids.
    /// </summary>
    /// <remarks>
    /// Process-unique and bounded to 16 bits because under <c>NOTIFYICON_VERSION_4</c> the shell
    /// reports the icon id in <c>HIWORD(lParam)</c> of the callback message (D012): an id above
    /// 0xFFFF would be truncated by the shell, and two icons sharing a truncated id would be
    /// indistinguishable to the event decoding in S02.
    /// </remarks>
    private static int s_nextIconId;

    /// <summary>The seam every shell call goes through; also the owner of the real GDI objects.</summary>
    private readonly IShellApi _shell;

    /// <summary>
    /// The dispatcher that owns this instance, or <see langword="null"/> when the instance was
    /// created without one.
    /// </summary>
    /// <remarks>
    /// Captured at construction instead of read from <see cref="DispatcherObject.Dispatcher"/>
    /// because WPF's own dispatcher is created on demand and therefore never null: the only way an
    /// instance can genuinely have no dispatcher is the internal verification constructor, and
    /// this field is what makes that case testable (R015).
    /// </remarks>
    private readonly Dispatcher? _dispatcher;

    /// <summary>The hidden host window, created on first registration and destroyed by disposal.</summary>
    private TrayMessageWindow? _host;

    /// <summary>The icon id this instance registered with the shell; 0 until the first registration.</summary>
    private uint _iconId;

    /// <summary>The <c>HICON</c> the shell currently has, or <see cref="IntPtr.Zero"/>.</summary>
    private IntPtr _registeredIcon;

    /// <summary>Whether the shell has confirmed the registration of this instance's icon.</summary>
    private bool _registered;

    /// <summary>Set once by <see cref="Dispose"/> so repeated disposal is a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// Set while a property is being put back to its previous value, so the revert itself does not
    /// re-enter the change callbacks.
    /// </summary>
    /// <remarks>
    /// Only an instance whose dispatcher cannot carry out changes needs this: it cannot apply the
    /// original change, and without the guard the revert would be another change it cannot apply.
    /// </remarks>
    private bool _suppressChangeCallbacks;

    /// <summary>
    /// Initializes a new instance that talks to the real shell and is owned by the calling
    /// thread's dispatcher.
    /// </summary>
    /// <remarks>
    /// Create the instance on the thread that owns the application's dispatcher (any WPF UI
    /// thread). It creates no window and registers no icon: both happen on the first
    /// <see cref="Visible"/> assignment.
    /// </remarks>
    public TrayIcon()
        : this(new ShellApi(), Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Initializes a new instance over the given seam, owned by the calling thread's dispatcher.
    /// </summary>
    /// <param name="shell">The seam to talk to the shell through.</param>
    /// <remarks>
    /// <b>Exists for verification (D009).</b> The shell call sequences, the retry policy and the
    /// handle-ownership rules cannot be exercised against a live notification area - a test host
    /// may not have one, and a real <c>Shell_NotifyIconW</c> failure cannot be forced - so the
    /// lifecycle tests construct the element over a scripted fake. The overload is
    /// <see langword="internal"/>; the shipped public surface is the parameterless constructor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    internal TrayIcon(IShellApi shell)
        : this(shell, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Initializes a new instance over the given seam and dispatcher.
    /// </summary>
    /// <param name="shell">The seam to talk to the shell through.</param>
    /// <param name="dispatcher">
    /// The dispatcher that owns the instance, or <see langword="null"/> to represent an instance
    /// created without any dispatcher - the case R015 requires to fail loudly. The null case is
    /// reachable <em>only</em> here: WPF creates a dispatcher on demand for every other
    /// construction path.
    /// </param>
    /// <remarks>
    /// Exists for verification (D009): the lifecycle tests pass a scripted seam, and the
    /// marshalling tests pass the dispatcher of the thread the element is meant to be bound to.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    internal TrayIcon(IShellApi shell, Dispatcher? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Raised when a notification-area operation on an already registered icon failed and the
    /// single retry did not fix it.
    /// </summary>
    /// <remarks>
    /// A handler that throws propagates, like any routed event handler; the library does not
    /// swallow a caller's bug. Nothing in the notification-area path depends on the handler
    /// returning normally.
    /// </remarks>
    public event EventHandler<TrayErrorEventArgs> TrayError
    {
        add => AddHandler(TrayErrorEvent, value);
        remove => RemoveHandler(TrayErrorEvent, value);
    }

    /// <summary>
    /// Gets or sets the image shown in the notification area.
    /// </summary>
    /// <value>
    /// The icon source, or <see langword="null"/> (the default) for "no icon yet". Setting the
    /// property from a background thread is supported and marshals to the UI thread.
    /// </value>
    /// <remarks>
    /// <para>
    /// Changing the value on a registered icon converts the image to an <c>HICON</c> and sends
    /// <c>NIM_MODIFY</c> with <c>NIF_ICON</c> (plus <c>NIF_TIP</c> when a tooltip is set: the
    /// shell does not persist <c>szTip</c> independently of the modify that carries it). The
    /// previous handle is destroyed only after the shell accepted the new one, so a failed change
    /// leaves the previously valid icon displayed (R007).
    /// </para>
    /// <para>
    /// Setting <see langword="null"/> does <b>not</b> clear the displayed icon: there is no shell
    /// operation that means "keep the registration but drop the image", so a null source is
    /// treated as "no change". Use <see cref="Visible"/> to hide the icon.
    /// </para>
    /// <para>
    /// An <see cref="ImageSource"/> that cannot be converted (a locked or foreign-thread drawing
    /// source, for example) raises <see cref="TrayIconException"/> with
    /// <see cref="TrayIconException.OperationConvertIcon"/>; that failure is a caller error rather
    /// than a shell failure, so it is reported rather than retried, and the registered icon is
    /// left untouched.
    /// </para>
    /// </remarks>
    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetPropertyOnDispatcher(IconSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip text the shell shows for the icon.
    /// </summary>
    /// <value>
    /// The tooltip, empty by default. <see langword="null"/> is normalised to the empty string.
    /// Setting the property from a background thread is supported and marshals to the UI thread.
    /// </value>
    /// <remarks>
    /// The text is truncated to <see cref="MaxToolTipLength"/> characters (127) before it reaches
    /// the shell, because <c>szTip</c> is <c>WCHAR[128]</c> including the terminator: a longer
    /// string would be silently cut by the marshaller at an arbitrary point. The truncation is
    /// deliberate and never splits a surrogate pair, and the modify that carries the new text also
    /// carries <c>NIF_SHOWTIP</c>, without which version 4 suppresses the tooltip entirely (D012).
    /// </remarks>
    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetPropertyOnDispatcher(ToolTipTextProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the icon is shown in the notification area.
    /// </summary>
    /// <value><see langword="false"/> by default. Setting the property from a background thread is
    /// supported and marshals to the UI thread.</value>
    /// <remarks>
    /// <para>
    /// Setting it to <see langword="true"/> registers the icon: the host window is created on
    /// first use, the icon is converted if <see cref="IconSource"/> is set, and the shell is told
    /// <c>NIM_ADD</c> and then <c>NIM_SETVERSION(4)</c>. Setting it to <see langword="false"/>
    /// removes the registration and destroys the retained <c>HICON</c>.
    /// </para>
    /// <para>
    /// A failed registration raises <see cref="TrayIconException"/> <em>and</em> leaves the
    /// property <see langword="false"/>, so the value always describes shell-confirmed state and a
    /// later assignment can retry the registration.
    /// </para>
    /// </remarks>
    public bool Visible
    {
        get => (bool)GetValue(VisibleProperty);
        set => SetPropertyOnDispatcher(VisibleProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the shell confirmed the registration of this instance's
    /// icon.
    /// </summary>
    /// <value><see langword="true"/> only between a confirmed <c>NIM_ADD</c> (plus
    /// <c>NIM_SETVERSION</c>) and a confirmed <c>NIM_DELETE</c>.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal bool IsRegistered => _registered;

    /// <summary>
    /// Gets the <c>HICON</c> the shell currently holds, or <see cref="IntPtr.Zero"/> when none is
    /// registered.
    /// </summary>
    /// <value>The retained handle, owned by this instance.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal IntPtr RegisteredIconHandle => _registeredIcon;

    /// <summary>
    /// Gets the host window handle, or <see cref="IntPtr.Zero"/> when no host has been created
    /// yet.
    /// </summary>
    /// <value>The <c>HWND</c> of the hidden top-level host window.</value>
    /// <remarks>Diagnostics and verification only; not part of the shipped public surface.</remarks>
    internal IntPtr HostHandle => _host?.Handle ?? IntPtr.Zero;

    /// <summary>
    /// Removes the icon from the notification area, releases the retained <c>HICON</c> and
    /// destroys the hidden host window. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marshalled to the owning dispatcher when the caller is on another thread, so an application
    /// may dispose from an exit handler on any thread. An instance created without a dispatcher
    /// disposes inline: there is no thread to marshal to, and refusing to dispose would leave the
    /// icon behind.
    /// </para>
    /// <para>
    /// A failing <c>NIM_DELETE</c> does not throw: it is retried once and then reported through
    /// the <see cref="TrayError"/> event and the trace channel, because disposal is typically the
    /// last thing a process does and there is no caller left to handle an exception. An exception
    /// thrown by a <see cref="TrayError"/> handler is the caller's own and propagates.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        Dispatcher? dispatcher = _dispatcher;

        if (dispatcher is null
            || dispatcher.HasShutdownStarted
            || dispatcher.HasShutdownFinished
            || dispatcher.CheckAccess())
        {
            // No dispatcher to marshal to, a dispatcher that can no longer execute anything, or
            // already on the owning thread: all three dispose inline. See the remarks.
            DisposeCore();
            return;
        }

        dispatcher.Invoke(DisposeCore);
    }

    /// <summary>
    /// Assigns a dependency property on the dispatcher that owns this instance (R015).
    /// </summary>
    /// <param name="property">The property to assign.</param>
    /// <param name="value">The new value.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the marshalling is here and not in the change callback.</b> WPF's own
    /// <c>DependencyObject.SetValue</c> verifies thread access <em>before</em> any change callback
    /// runs, and refuses with "The calling thread cannot access this object because a different
    /// thread owns it". A background-thread assignment that relied on the callback to marshal would
    /// therefore never reach the callback at all - it would fail inside WPF with a message about
    /// WPF's threading model instead of doing what the caller asked. Marshalling the whole
    /// assignment is also what makes the work the assignment triggers (the image conversion, the
    /// shell call, the ownership transfer, the host-window creation) happen on the owning thread,
    /// which is the only thread those things are legal on.
    /// </para>
    /// <para>
    /// Markup and bindings assign through <c>SetValue</c> directly and bypass this method. That is
    /// harmless - WPF evaluates them on the owning dispatcher thread - and the change callbacks
    /// keep an access check of their own, so a direct <c>SetValue</c> from a foreign thread is
    /// still refused by this library rather than by WPF.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The instance was created without a usable dispatcher; nothing is assigned in that case.
    /// </exception>
    private void SetPropertyOnDispatcher(DependencyProperty property, object? value) =>
        ApplyOnDispatcher(() => SetValue(property, value));

    /// <summary>
    /// Marshals <paramref name="action"/> onto the dispatcher that owns this instance (R015).
    /// </summary>
    /// <param name="action">The work to run on the owning thread.</param>
    /// <exception cref="InvalidOperationException">
    /// The instance was created without a WPF dispatcher, or its dispatcher has been shut down, so
    /// there is no thread that could own the host window. This is the documented carve-out of
    /// D008 and deliberately not a <see cref="TrayIconException"/>: no notification-area operation
    /// failed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// On the owning thread the action runs inline, so the common case costs a
    /// <see cref="Dispatcher.CheckAccess"/> and nothing else. From a background thread it runs
    /// through a synchronous <c>Dispatcher.Invoke</c>, which propagates an exception to the
    /// caller: a background-thread failure must be the background thread's exception, not a
    /// swallowed trace line.
    /// </para>
    /// <para>
    /// An instance whose <see cref="DispatcherObject.Dispatcher"/> has been shut down cannot accept
    /// work any more - WPF itself refuses with <see cref="InvalidOperationException"/> - so the
    /// refusal is made explicit here, where the message can name the requirement.
    /// </para>
    /// </remarks>
    private void ApplyOnDispatcher(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher? dispatcher = _dispatcher;

        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                "This TrayIcon instance was created without a WPF Dispatcher, so its properties cannot be marshalled to a UI thread. "
                + "Create the TrayIcon on a thread that owns a Dispatcher (any WPF UI thread does) and set its properties from there or from any other thread.");
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException(
                "This TrayIcon instance belongs to a Dispatcher that has already been shut down, so its properties can no longer be marshalled "
                + "to a UI thread and the hidden host window can no longer be created or destroyed.");
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>
    /// Applies a property change on the owning dispatcher, reverting the property when the change
    /// could not be applied at all.
    /// </summary>
    /// <param name="property">The property being changed.</param>
    /// <param name="previousValue">The value to restore; the change's old value.</param>
    /// <param name="action">The work to run on the owning thread.</param>
    /// <exception cref="InvalidOperationException">
    /// No usable dispatcher exists; it is raised <em>after</em> the property has been put back, so
    /// the caller sees both a loud failure and a property that still describes reality.
    /// </exception>
    /// <remarks>
    /// The revert only covers the case where nothing was applied. A <see cref="TrayIconException"/>
    /// is deliberately not caught here: each change owns its own rollback semantics (a failed icon
    /// replacement keeps the previous icon registered, a failed registration unregisters nothing)
    /// and reverts precisely the part of the state it touched.
    /// </remarks>
    private void ApplyOrRevert(DependencyProperty property, object? previousValue, Action action)
    {
        try
        {
            ApplyOnDispatcher(action);
        }
        catch (InvalidOperationException)
        {
            _suppressChangeCallbacks = true;

            try
            {
                SetCurrentValue(property, previousValue);
            }
            finally
            {
                _suppressChangeCallbacks = false;
            }

            throw;
        }
    }

    /// <summary>
    /// Normalises <see cref="ToolTipText"/> to a non-null string.
    /// </summary>
    /// <param name="d">The element whose property value is being resolved.</param>
    /// <param name="baseValue">The value being assigned.</param>
    /// <returns>The assigned value, or the empty string when it is <see langword="null"/>.</returns>
    /// <remarks>
    /// <c>null</c> is a legitimate way to say "no tooltip", but it is not a legitimate stored value
    /// for a property whose getter is typed as a non-nullable string: returning null would force
    /// every consumer to be nullable-aware for no reason. Coercion runs before the change callback,
    /// so the lifecycle only ever sees a real string.
    /// </remarks>
    private static object CoerceToolTipText(DependencyObject d, object baseValue) =>
        (string?)baseValue ?? string.Empty;

    /// <summary>
    /// The <see cref="IconSourceProperty"/> change callback: marshals, then applies the change.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        var source = (ImageSource?)e.NewValue;

        trayIcon.ApplyOrRevert(IconSourceProperty, e.OldValue, () => trayIcon.ApplyIconSource(source));
    }

    /// <summary>
    /// The <see cref="ToolTipTextProperty"/> change callback: marshals, then applies the change.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnToolTipTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        trayIcon.ApplyOrRevert(ToolTipTextProperty, e.OldValue, trayIcon.ApplyToolTipText);
    }

    /// <summary>
    /// The <see cref="VisibleProperty"/> change callback: marshals, then registers or removes.
    /// </summary>
    /// <param name="d">The element whose property changed.</param>
    /// <param name="e">The change payload.</param>
    private static void OnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trayIcon = (TrayIcon)d;

        if (trayIcon._suppressChangeCallbacks)
        {
            return;
        }

        bool visible = (bool)e.NewValue;

        trayIcon.ApplyOrRevert(
            VisibleProperty,
            e.OldValue,
            visible ? trayIcon.RegisterOrRevert : trayIcon.Unregister);
    }

    /// <summary>
    /// Registers the icon, or puts <see cref="Visible"/> back to <see langword="false"/> when the
    /// shell refused.
    /// </summary>
    /// <remarks>
    /// The revert is what keeps the property meaningful: the value is only allowed to say
    /// <see langword="true"/> for a registration the shell confirmed (D008). It also keeps the
    /// property retryable - a failed registration leaves <see langword="false"/>, so raising the
    /// property again attempts the registration again, instead of doing nothing because the value
    /// never changed. The exception that follows is the report, so the revert is never silent.
    /// <c>DependencyObject.SetCurrentValue</c> is used rather than <c>SetValue</c> so that a
    /// binding or style that drives the property from markup is left intact.
    /// </remarks>
    private void RegisterOrRevert()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            EnsureRegistered();
        }
        catch (TrayIconException)
        {
            SetCurrentValue(VisibleProperty, false);
            throw;
        }
    }

    /// <summary>
    /// Runs the registration sequence: host window, icon handle, <c>NIM_ADD</c>,
    /// <c>NIM_SETVERSION(4)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the contract: <c>NIM_ADD</c> immediately followed by
    /// <c>NIM_SETVERSION(NOTIFYICON_VERSION_4)</c>, with <c>NIF_SHOWTIP</c> among the add flags
    /// (D012). Failures follow the startup policy: the last error is read immediately after the
    /// failing call, and a thrown <see cref="TrayIconException"/> names the operation.
    /// </para>
    /// <para>
    /// Nothing is leaked on the failure paths: an <c>HICON</c> built for an add that the shell
    /// refused is destroyed before the throw, and the registration a successful add created is
    /// deleted when <c>NIM_SETVERSION</c> fails, so a half-registered icon is never left in the
    /// notification area.
    /// </para>
    /// </remarks>
    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        TrayMessageWindow host = EnsureHost();

        if (_iconId == 0)
        {
            _iconId = AllocateIconId();
        }

        IntPtr icon = IconSource is null ? IntPtr.Zero : HiconFactory.CreateIcon(_shell, IconSource, IconPixelSize);
        var data = CreateIconData(host);

        // NIF_MESSAGE carries uCallbackMessage, NIF_TIP carries szTip, and NIF_SHOWTIP is what
        // makes the shell show that tooltip at all under version 4 (D012). NIF_ICON is added only
        // when there is an icon to show: a zero hIcon with NIF_ICON set asks the shell to display
        // nothing.
        data.uFlags = ShellConstants.NIF_MESSAGE | ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
        data.uCallbackMessage = host.CallbackMessageId;

        // NIF_TIP is only meaningful with szTip filled in. This is the one call that establishes
        // the tooltip, so leaving szTip empty here would mean the icon has no tooltip until the
        // text happens to change - and the truncation rule applies exactly as it does on update.
        data.szTip = TruncateToolTipText(ToolTipText);

        if (icon != IntPtr.Zero)
        {
            data.hIcon = icon;
            data.uFlags |= ShellConstants.NIF_ICON;
        }

        if (!_shell.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data))
        {
            // Read the code first: it belongs to the call that just failed and is overwritten by
            // the next Win32 call (IShellApi.GetLastError).
            int error = _shell.GetLastError();

            if (icon != IntPtr.Zero)
            {
                _shell.DestroyIcon(icon);
            }

            throw new TrayIconException(TrayIconException.OperationAdd, error);
        }

        var versionData = CreateIconData(host);
        versionData.uTimeoutOrVersion = ShellConstants.NOTIFYICON_VERSION_4;

        if (!_shell.ShellNotifyIcon(ShellConstants.NIM_SETVERSION, ref versionData))
        {
            int error = _shell.GetLastError();

            // A half-registered icon is worse than none: the icon would be present but speaking
            // the old protocol, so S02's event decoding would be wrong. Remove it before reporting.
            var rollback = CreateIconData(host);
            _shell.ShellNotifyIcon(ShellConstants.NIM_DELETE, ref rollback);

            if (icon != IntPtr.Zero)
            {
                _shell.DestroyIcon(icon);
            }

            throw new TrayIconException(TrayIconException.OperationSetVersion, error);
        }

        _registered = true;
        _registeredIcon = icon;
    }

    /// <summary>
    /// Removes the registration and releases the retained <c>HICON</c>.
    /// </summary>
    /// <remarks>
    /// Runs through <see cref="ApplyShellChange"/>, so a refused <c>NIM_DELETE</c> is retried once
    /// and then surfaced instead of throwing: this method is reached from disposal, and from a
    /// property assignment where an exception would be more alarming than the stale icon. When the
    /// delete does not succeed, the registration is left intact - the icon is still there, and
    /// pretending otherwise would lose the handle needed to remove it later.
    /// </remarks>
    private void Unregister()
    {
        if (!_registered || _host is null)
        {
            return;
        }

        var data = CreateIconData(_host);

        if (ApplyShellChange(ShellConstants.NIM_DELETE, ref data, TrayIconException.OperationRemove))
        {
            _registered = false;
            ReleaseRegisteredIcon();
        }
    }

    /// <summary>
    /// Applies a new <see cref="IconSource"/> to a registered icon.
    /// </summary>
    /// <param name="source">The new source, or <see langword="null"/> for "no change".</param>
    /// <remarks>
    /// The conversion happens first and can throw a <see cref="TrayIconException"/> naming
    /// <see cref="TrayIconException.OperationConvertIcon"/>; nothing has been sent to the shell at
    /// that point, so the registered icon is untouched. Once the new handle exists, the previous
    /// one is destroyed only if the shell accepted the replacement - a failed replacement destroys
    /// the new handle instead, which is what keeps both "the previous icon stays visible" (R007)
    /// and "no handle leaks" true at the same time.
    /// </remarks>
    private void ApplyIconSource(ImageSource? source)
    {
        if (_disposed || source is null || !_registered || _host is null)
        {
            return;
        }

        IntPtr replacement = HiconFactory.CreateIcon(_shell, source, IconPixelSize);
        var data = CreateIconData(_host);
        data.uFlags = ShellConstants.NIF_ICON;
        data.hIcon = replacement;

        string tip = TruncateToolTipText(ToolTipText);

        if (tip.Length > 0)
        {
            data.uFlags |= ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
            data.szTip = tip;
        }

        if (ApplyShellChange(ShellConstants.NIM_MODIFY, ref data, TrayIconException.OperationModify))
        {
            IntPtr previous = _registeredIcon;
            _registeredIcon = replacement;

            if (previous != IntPtr.Zero)
            {
                DestroyIcon(previous);
            }
        }
        else
        {
            // The shell kept the icon it had, so the handle built for the refused replacement is
            // not registered anywhere and must be released here or it leaks (R007).
            DestroyIcon(replacement);
        }
    }

    /// <summary>
    /// Applies the current <see cref="ToolTipText"/> to a registered icon.
    /// </summary>
    /// <remarks>
    /// <c>NIF_SHOWTIP</c> travels with <c>NIF_TIP</c> on purpose: the flag is what makes version 4
    /// show the standard tooltip, so a tooltip update that omitted it would appear to succeed and
    /// change nothing on screen.
    /// </remarks>
    private void ApplyToolTipText()
    {
        if (_disposed || !_registered || _host is null)
        {
            return;
        }

        var data = CreateIconData(_host);
        data.uFlags = ShellConstants.NIF_TIP | ShellConstants.NIF_SHOWTIP;
        data.szTip = TruncateToolTipText(ToolTipText);

        ApplyShellChange(ShellConstants.NIM_MODIFY, ref data, TrayIconException.OperationModify);
    }

    /// <summary>
    /// Sends one runtime shell operation under the retry-then-surface policy of D008.
    /// </summary>
    /// <param name="message">The <c>NIM_*</c> operation code.</param>
    /// <param name="data">The icon data to send, unchanged on the retry.</param>
    /// <param name="operation">The operation name for the failure report.</param>
    /// <returns><see langword="true"/> when the shell confirmed the operation.</returns>
    /// <remarks>
    /// The same call is repeated at most <see cref="RuntimeRetryCount"/> times - the retry exists
    /// because the shell occasionally rejects an update while Explorer is busy, and a single
    /// retry covers that without looping. When the retry also fails, the failure is traced and
    /// raised as <see cref="TrayErrorEvent"/> with <c>Retried = true</c> and this method returns
    /// <see langword="false"/>; it never throws and never terminates the process, because losing an
    /// icon update is strictly better than losing the application. The caller decides what an
    /// unconfirmed operation means for its own state.
    /// </remarks>
    private bool ApplyShellChange(uint message, ref NOTIFYICONDATAW data, string operation)
    {
        if (_shell.ShellNotifyIcon(message, ref data))
        {
            return true;
        }

        int error = _shell.GetLastError();

        for (int attempt = 0; attempt < RuntimeRetryCount; attempt++)
        {
            if (_shell.ShellNotifyIcon(message, ref data))
            {
                return true;
            }

            error = _shell.GetLastError();
        }

        var exception = new TrayIconException(
            operation,
            error,
            "The call was retried once and failed again, so the notification-area state may now be stale.");

        NotifyIconTrace.Error(operation, error, exception, retried: true);
        RaiseEvent(new TrayErrorEventArgs(operation, error, exception, retried: true, TrayErrorEvent));

        return false;
    }

    /// <summary>
    /// Destroys an owned <c>HICON</c>, tracing a failure instead of letting a handle leak silently.
    /// </summary>
    /// <param name="hIcon">The handle to release; <see cref="IntPtr.Zero"/> is ignored.</param>
    /// <remarks>
    /// A refused <c>DestroyIcon</c> cannot be retried usefully (the handle is either valid or it
    /// is not) and cannot be reported through <see cref="TrayError"/> without inventing an
    /// operation that did not fail. Tracing it keeps the leak visible to a support log instead of
    /// hiding it, which is what R007's no-leak claim needs in order to stay honest.
    /// </remarks>
    private void DestroyIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return;
        }

        if (_shell.DestroyIcon(hIcon))
        {
            return;
        }

        int error = _shell.GetLastError();

        NotifyIconTrace.Error(
            TrayIconException.OperationRemove,
            error,
            new TrayIconException(
                TrayIconException.OperationRemove,
                error,
                "An icon handle could not be destroyed and remains allocated for the lifetime of the process."),
            retried: false);
    }

    /// <summary>
    /// Destroys the retained <c>HICON</c> and clears the field.
    /// </summary>
    private void ReleaseRegisteredIcon()
    {
        IntPtr handle = _registeredIcon;
        _registeredIcon = IntPtr.Zero;

        DestroyIcon(handle);
    }

    /// <summary>
    /// Releases everything this instance owns: the registration, the retained <c>HICON</c> and the
    /// host window.
    /// </summary>
    /// <remarks>
    /// Runs on the owning dispatcher thread (see <see cref="Dispose"/>). The order matters: the
    /// shell is told about the removal while the host window still exists, the icon handle is
    /// released next, and the window is destroyed last. The retained handle is released even when
    /// the shell refused the removal: disposal is terminal, so holding on to the handle could only
    /// leak it, and the registration it belonged to is being abandoned either way. Every field is
    /// cleared before this method returns, so a second disposal cannot repeat any of it.
    /// </remarks>
    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TrayMessageWindow? host = _host;

        if (_registered && host is not null)
        {
            var data = CreateIconData(host);

            if (ApplyShellChange(ShellConstants.NIM_DELETE, ref data, TrayIconException.OperationRemove))
            {
                _registered = false;
            }
        }

        ReleaseRegisteredIcon();

        _host = null;
        host?.Dispose();
    }

    /// <summary>
    /// Creates the host window on first use, on the thread that is executing this call.
    /// </summary>
    /// <returns>The host window.</returns>
    /// <remarks>
    /// Lazy on purpose: an instance that never becomes <see cref="Visible"/> never creates a
    /// window, which is what lets an application construct the element during startup without
    /// producing any OS resource at all.
    /// </remarks>
    private TrayMessageWindow EnsureHost() => _host ??= new TrayMessageWindow(_shell, OnHostMessage);

    /// <summary>
    /// Receives every message the host window is sent, including the shell's notification
    /// callbacks and the <c>TaskbarCreated</c> broadcast.
    /// </summary>
    /// <param name="message">The message id.</param>
    /// <param name="wParam">The first message parameter.</param>
    /// <param name="lParam">The second message parameter.</param>
    /// <remarks>
    /// S01 owns the sink and nothing else: the notification decoding (<c>LOWORD(lParam)</c> under
    /// version 4) is S02's contract and the Explorer-restart re-registration is S05's. The sink is
    /// wired here so the host has exactly one owner and neither later slice has to re-create,
    /// subclass or re-hook the window. It must not throw: it runs inside a window procedure.
    /// </remarks>
    private void OnHostMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        _ = message;
        _ = wParam;
        _ = lParam;
    }

    /// <summary>
    /// Creates an icon-data structure for this instance's icon on the given host.
    /// </summary>
    /// <param name="host">The host window the icon is anchored to.</param>
    /// <returns>A structure with the correct <c>cbSize</c>, the host handle and the icon id.</returns>
    /// <remarks>
    /// Every shell call goes through this factory, which is why the tests can assert that
    /// <c>cbSize</c> is the struct size on <em>every</em> recorded call without trusting the call
    /// sites to remember it.
    /// </remarks>
    private NOTIFYICONDATAW CreateIconData(TrayMessageWindow host) => NOTIFYICONDATAW.Create(host.Handle, _iconId);

    /// <summary>
    /// Truncates tooltip text to what <c>szTip</c> can hold.
    /// </summary>
    /// <param name="text">The text to truncate, possibly <see langword="null"/>.</param>
    /// <returns>A non-null string of at most <see cref="MaxToolTipLength"/> UTF-16 code units.</returns>
    /// <remarks>
    /// The cut never lands between a surrogate pair: half a pair would be an invalid string the
    /// shell would render as a replacement character, which is a worse outcome than one character
    /// less of tooltip.
    /// </remarks>
    private static string TruncateToolTipText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= MaxToolTipLength)
        {
            return text;
        }

        int length = char.IsHighSurrogate(text[MaxToolTipLength - 1]) ? MaxToolTipLength - 1 : MaxToolTipLength;

        return text[..length];
    }

    /// <summary>
    /// Allocates a process-unique icon id bounded to the 16 bits the shell reports it in.
    /// </summary>
    /// <returns>A non-zero value in <c>1..0xFFFF</c>.</returns>
    /// <remarks>
    /// The counter wraps, so a process that creates 65535 icons starts handing out ids from the
    /// beginning again - acceptable because the shell only ever needs the id to be unique among
    /// the icons that are currently registered, and 0 is skipped because it is indistinguishable
    /// from "no icon id".
    /// </remarks>
    private static uint AllocateIconId()
    {
        while (true)
        {
            uint id = (uint)(Interlocked.Increment(ref s_nextIconId) & 0xFFFF);

            if (id != 0)
            {
                return id;
            }
        }
    }
}
