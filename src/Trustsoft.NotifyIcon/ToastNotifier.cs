using System.Collections.Generic;
using System.IO;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The public entry point of the toast subsystem: it owns the AppUserModelID identity a toast needs
/// to be delivered and routable, and it shows <see cref="ToastContent"/> objects through the
/// measured show path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Standalone, not part of the tray (D052).</b> A toast needs no notification-area icon, so this
/// is an ordinary class with an ordinary constructor rather than a <c>FrameworkElement</c>; the
/// <see cref="TrayIcon"/> surface is untouched and the two subsystems are reached independently.
/// </para>
/// <para>
/// <b>One notifier, many toasts.</b> A notifier is a transient command, not a visual element: each
/// <see cref="Show"/> creates its own internal show object with its own event subscriptions, and the
/// notifier keeps them so <see cref="Dispose"/> can tear them all down. <see cref="Show"/> may
/// therefore be called any number of times; there is no "one toast per notifier" rule.
/// </para>
/// <para>
/// <b>Registration happens on the first show (D060).</b> An unpackaged process has no process
/// default AppUserModelID, so before the first toast is shown the notifier creates the per-user
/// Start-menu shortcut that carries the identity, writes the property, commits it before saving and
/// reads it back from a fresh shell link - refusing the show with a named failure if any step or the
/// read-back disagrees. The registration is attempted once per notifier and the shortcut it created
/// is removed by <see cref="Dispose"/>, so a disposed notifier leaves nothing registered.
/// </para>
/// <para>
/// <b>The failure split (D055/D061).</b> Registration and startup failures throw: a notifier that
/// cannot establish the identity it shows through throws <see cref="ToastException"/> from
/// <see cref="Show"/> carrying the failing operation and the code that call reported, because an
/// unpackaged process has no other way to learn that nothing will be delivered. A failure the show
/// path reports after a successful registration is non-fatal instead: it is published through
/// <see cref="ToastError"/> plus one Error-level line on the library's trace channel and
/// <see cref="Show"/> returns normally, so a toast Windows could not deliver never terminates a
/// windowless host. Caller errors are reported as the framework's own argument exceptions, because
/// they are not interop failures: a <see langword="null"/> content throws
/// <see cref="ArgumentNullException"/> and a title that is empty or whitespace throws
/// <see cref="ArgumentException"/> naming <c>content</c>.
/// </para>
/// <para>
/// <b>Not thread-safe, and it follows the thread that first shows.</b> The Windows toast stack is
/// initialized on the thread that makes the calls, so a notifier is expected to be created and used
/// from one thread (typically the UI thread of the application that owns the toasts). No internal
/// locking is performed; a notifier is not a shared cross-thread service.
/// </para>
/// <para>
/// <b>The events are raised on the thread the callback arrived on.</b> The measured WinRT
/// subscriptions deliver on the thread that created the notification while that thread pumps
/// messages, which for the normal case is the same thread that called <see cref="Show"/>. No
/// <see cref="System.Windows.Threading.Dispatcher"/> marshalling is introduced - unlike
/// <see cref="TrayIcon"/>, whose shell callbacks arrive on a window-procedure thread - so a handler
/// runs where the shell delivered it.
/// </para>
/// <para>
/// Activated reports that the toast's launch or button argument arrived; it is not a report that the user clicked the body.
/// </para>
/// <para>
/// That wording rule is deliberate rather than pedantic: the activation payload carries the launch
/// or button argument and nothing about which element produced it, an activation can arrive with no
/// instrumented click at all, and a specific activation cannot be credited to a specific click
/// (measured in M002/S01 and recorded in <c>docs/TOAST-MEASUREMENT.md</c>). Nobody, including this
/// library, may turn a delivered argument into a click-attribution claim.
/// </para>
/// <para>
/// Toasts and balloons are independent: showing a toast never suppresses, replaces or re-routes a balloon tip, and showing a balloon tip never replaces or re-routes a toast.
/// </para>
/// <para>
/// <b>A throwing handler never escapes.</b> The events are raised from a shell-owned callback path,
/// so each raise catches a consumer handler's exception, writes one Error-level line on the library's
/// trace channel naming the event, and continues: a bug in one handler must not cross back into the
/// toast stack and must not stop the events raised after it.
/// </para>
/// </remarks>
public sealed class ToastNotifier : IDisposable
{
    /// <summary>
    /// Raised when Windows delivers a launch or button argument for a toast this notifier showed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ToastActivatedEventArgs.Arguments"/> is the argument the shell delivered, verbatim;
    /// a <see langword="null"/> value means the toast carried no argument, which is a legitimate
    /// delivery rather than a failure.
    /// </para>
    /// <para>
    /// Several toasts can be live at once and the platform delivers no per-show event object, so the
    /// argument string is the only identifier: make launch and button arguments unique to tell
    /// activations apart. A handler exception is caught and traced, never propagated.
    /// </para>
    /// </remarks>
    public event EventHandler<ToastActivatedEventArgs>? Activated;

    /// <summary>
    /// Raised when Windows reports that a toast this notifier showed was dismissed.
    /// </summary>
    /// <remarks>
    /// <see cref="ToastDismissedEventArgs.Reason"/> is never outside the public vocabulary: an
    /// unreadable or unrecognised reason is reported as
    /// <see cref="ToastDismissalReason.Unknown"/>, never guessed at. A handler exception is caught
    /// and traced, never propagated.
    /// </remarks>
    public event EventHandler<ToastDismissedEventArgs>? Dismissed;

    /// <summary>
    /// Raised when a toast failure happened that the library survived - the shell's asynchronous
    /// delivery failure, or any later runtime failure the notifier reports instead of throwing.
    /// </summary>
    /// <remarks>
    /// This is the non-fatal channel D055 fixes: it is raised instead of throwing, because a toast
    /// that could not be delivered must not terminate a windowless host. The same failure also
    /// produces one Error-level line on the library's trace channel (the source is named
    /// <c>Trustsoft.NotifyIcon</c>), so a process that never subscribes still has a record.
    /// <see cref="ToastErrorEventArgs.Exception"/> is <see langword="null"/> for the shell's
    /// asynchronous failure, which reports a bare code. A handler exception is caught and traced,
    /// never propagated.
    /// </remarks>
    public event EventHandler<ToastErrorEventArgs>? ToastError;

    private readonly IToastApi _api;
    private readonly ToastIdentity _identity;

    /// <summary>
    /// Every show this notifier has created and not yet torn down. Kept so <see cref="Dispose"/>
    /// can unsubscribe and release each one, which is what makes "after dispose no handler fires"
    /// true (milestone success criterion 3).
    /// </summary>
    private readonly List<ToastShow> _liveShows = [];

    private string? _appUserModelId;
    private string? _registeredAppUserModelId;
    private string? _shortcutPath;

    private bool _registrationAttempted;
    private bool _registrationFailed;
    private string _registrationFailureOperation = string.Empty;
    private int _registrationFailureCode;
    private bool _createdShortcut;
    private bool _disposed;

    /// <summary>
    /// Initializes a new notifier that talks to the real Windows toast stack and the real
    /// per-user shortcut store.
    /// </summary>
    /// <remarks>
    /// Creating the notifier has no side effect: no shortcut is written and no WinRT factory is
    /// acquired until the first <see cref="Show"/>.
    /// </remarks>
    public ToastNotifier()
        : this(new ToastApi())
    {
    }

    /// <summary>
    /// Initializes a new notifier over the given seam.
    /// </summary>
    /// <param name="api">The seam to talk to the toast stack and the shortcut store through.</param>
    /// <remarks>
    /// <b>Exists for verification.</b> The registration sequence, the read-back check, the show
    /// sequence and the disposal accounting cannot be exercised against a live shell in a unit test
    /// - a real toast failure cannot be forced and a click cannot be produced - so the notifier
    /// tests construct the type over a scripted <c>FakeToastApi</c>. The overload is
    /// <see langword="internal"/>; the shipped public surface is the parameterless constructor, the
    /// same shape <see cref="TrayIcon"/>'s seam constructor has.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="api"/> is <see langword="null"/>.</exception>
    internal ToastNotifier(IToastApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _identity = new ToastIdentity(api);
    }

    /// <summary>
    /// Gets or sets the AppUserModelID override the identity is registered with, or
    /// <see langword="null"/> to derive the default from the entry assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property is settable only until the first <see cref="Show"/> has attempted registration:
    /// after that the identity has been written to a shortcut, so a late change could only misroute
    /// the toasts this notifier shows, and the setter throws <see cref="InvalidOperationException"/>
    /// rather than silently ignoring the value.
    /// </para>
    /// <para>
    /// A <see langword="null"/> or empty value means "derive the default", which is the entry
    /// assembly's simple name (falling back to the library's own name when there is no entry
    /// assembly, as in a test host).
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The identity has already been registered.</exception>
    public string? AppUserModelId
    {
        get => _appUserModelId;
        set
        {
            if (_registrationAttempted)
            {
                throw new InvalidOperationException(
                    "AppUserModelId must be set before the first Show: the identity is registered on the first show, and "
                    + "changing it afterwards could only misroute the toasts this notifier already shows.");
            }

            _appUserModelId = value;
        }
    }

    /// <summary>
    /// Gets the notification setting the last <see cref="Show"/> read for the identity, or
    /// <see langword="null"/> when no show has read one yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An outcome, not a failure.</b> The platform's <c>GetSetting</c> value says what Windows
    /// will do with the identity's notifications - <see cref="ToastNotificationSetting.Enabled"/> in
    /// the ordinary case and one of the disabled values when the user, an administrator or the
    /// application's own manifest turned them off. A disabled value is reported here and
    /// <em>never</em> raised on <see cref="ToastError"/>, and it never makes <see cref="Show"/>
    /// throw: a routine OS state is not a defect, and a host that treated it as one would report a
    /// failure for every toast on a machine whose user simply chose not to be notified.
    /// </para>
    /// <para>
    /// <b>Null means "not read", not "enabled".</b> The property is <see langword="null"/> until a
    /// show has reached the setting step, and it stays <see langword="null"/> when a show failed
    /// before that step (a registration failure, or a failure while acquiring the factories or the
    /// document). The raw value <c>0</c> means <see cref="ToastNotificationSetting.Enabled"/>; the
    /// two must never be conflated, which is why this property is nullable rather than an enum whose
    /// zero would silently double as "nothing was read".
    /// </para>
    /// <para>
    /// <b>The last show wins.</b> A later show refreshes it; a show that failed after the setting
    /// step still records the setting it read, because the reading happened. The value is what the
    /// platform reported at that moment: measured, it can lag a registry change by one process, so it
    /// is not a live view of the notification settings.
    /// </para>
    /// </remarks>
    public ToastNotificationSetting? NotificationSetting { get; private set; }

    /// <summary>
    /// Shows one toast: it registers the identity on the first call, then builds and runs one show
    /// for the content.
    /// </summary>
    /// <param name="content">The toast to show; the notifier reads it, it does not take ownership.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="ToastContent.Title"/> is empty or whitespace - a toast with no visible text would
    /// be an empty banner - or the content is otherwise unusable.
    /// </exception>
    /// <exception cref="ToastException">
    /// The identity could not be registered, or the content's <see cref="ToastImage.Source"/> could
    /// not be persisted as the PNG the shell needs (carrying
    /// <see cref="ToastException.OperationImageResolution"/>).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The notifier has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// <b>Order matters and is the contract.</b> The content is validated first (a caller error must
    /// not touch the shell), then a disposed notifier is refused, then - only on the first call - the
    /// identity is registered, then the content's image source is resolved (deliberately after
    /// registration, so a registration failure cannot strand a temp file), then one show is built and
    /// added to the live list <em>before</em> it runs, then it is shown. Adding the show before
    /// running it means a <see cref="Dispose"/> that races the show, or a show that fails, still has
    /// something to unwind.
    /// </para>
    /// <para>
    /// <b>A failed image resolution is a value-level failure and it throws.</b> When the content's
    /// <see cref="ToastImage.Source"/> cannot be read, encoded or written, the resolver's
    /// <see cref="ToastException"/> (operation <see cref="ToastException.OperationImageResolution"/>)
    /// propagates unchanged: no toast is shown and no show-path seam member is invoked, exactly as a
    /// refused title never reaches the shell. The message keeps the source type and the underlying
    /// failure, and no file is left behind.
    /// </para>
    /// <para>
    /// <b>A failed show does not throw (D055/D061).</b> Once the identity is registered, a show-path
    /// failure is reported through <see cref="ToastError"/> - with the failing operation and the code
    /// that call reported - and one Error-level line on the trace channel, and the method returns
    /// normally. The failed show has already unwound its own subscriptions, handles and temp image
    /// file by then, so the notifier is unaffected and a later <see cref="Show"/> is a fresh attempt.
    /// </para>
    /// <para>
    /// <b>The notification setting is reported, not judged.</b> The platform's setting is read as part
    /// of the measured sequence, and once read it is published as an outcome on
    /// <see cref="NotificationSetting"/> with one Verbose trace line naming it. Disabled values are
    /// deliberately not failures: the shell returns <c>S_OK</c> from <c>Show</c> for a
    /// notifications-disabled identity and raises the delivery failure out of band on the
    /// notification's <c>Failed</c> callback, which reaches <see cref="ToastError"/> as
    /// <see cref="ToastException.OperationNotificationFailed"/> with the raw
    /// <c>WPN_E_NOTIFICATION_DISABLED</c> code - so the library reports the state and lets the
    /// shell's own callback be the failure.
    /// </para>
    /// </remarks>
    public void Show(ToastContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(content.Title))
        {
            throw new ArgumentException(
                "ToastContent.Title must be a non-empty, non-whitespace string: it is the toast's first text line, so a "
                + "message with no visible text would be shown as an empty banner.",
                nameof(content));
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(ToastNotifier),
                "A disposed ToastNotifier cannot show toasts: disposal removes the identity the notifier shows through.");
        }

        EnsureRegistered();

        ToastImageFile? imageFile = ResolveImage(content);

        var show = new ToastShow(_api, new ToastPayload(content, imageFile?.Reference, imageFile?.Path));

        _liveShows.Add(show);

        // The show's three measured WinRT subscriptions feed this notifier's events (S03). The
        // callbacks are assigned before the show runs, so an activation that races the display finds
        // a handler already in place rather than being dropped.
        show.Activated = OnShowActivated;
        show.Dismissed = OnShowDismissed;
        show.Failed = OnShowFailed;

        NotifyIconTrace.Verbose(
            $"toast notifier: show title='{content.Title}' severity={content.Severity} launch='{content.Launch ?? string.Empty}' aumid='{_registeredAppUserModelId}'");

        ToastShowResult result = show.Show(_registeredAppUserModelId);

        if (result.SettingRead)
        {
            // The platform's setting as an outcome. Statement order matters: the value is recorded
            // and traced before the failure below is reported, so a show that failed after the
            // setting step still leaves the reading - and a show that never reached the step leaves
            // the property null rather than claiming the setting is Enabled.
            var setting = (ToastNotificationSetting)result.NotificationSetting;

            NotificationSetting = setting;

            NotifyIconTrace.Verbose($"toast notifier: notification setting={setting} ({(int)setting})");
        }

        if (!result.Success)
        {
            // The failed show has already unwound its own handles and temp image file; drop it from
            // the live list and release it, then report the failure through the non-fatal channel:
            // one Error-level trace line plus the ToastError event (the split D055 fixed and D061
            // replaced D058's interim throw with). Nothing is thrown here: a toast that could not be
            // delivered must not terminate a windowless host.
            _liveShows.Remove(show);
            show.Dispose();

            RaiseError(result.Operation, result.Code, exception: null);
        }
    }

    /// <summary>
    /// Unsubscribes and releases every show this notifier created, then removes the shortcut it
    /// registered. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Idempotent, and terminal.</b> After the first call every live show has been disposed (each
    /// one unsubscribing its three handlers and releasing its handles) and the shortcut has been
    /// removed, so a second call records nothing further; <see cref="Show"/> on a disposed notifier
    /// throws <see cref="ObjectDisposedException"/>.
    /// </para>
    /// <para>
    /// <b>A removal failure does not throw.</b> Disposal is typically the last thing a process does,
    /// so a failed delete is traced and then ignored - there is no caller left to handle an
    /// exception, and the process' exit drops any remaining registration anyway. A notifier that
    /// never registered removes nothing and touches the seam not at all.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        NotifyIconTrace.Verbose(
            $"toast notifier: dispose liveShows={_liveShows.Count} createdShortcut={_createdShortcut}");

        foreach (ToastShow show in _liveShows)
        {
            show.Dispose();
        }

        _liveShows.Clear();

        if (!_createdShortcut || _shortcutPath is null)
        {
            return;
        }

        _createdShortcut = false;

        ToastIdentityResult removal = _identity.Remove(_shortcutPath);

        NotifyIconTrace.Verbose(
            $"toast notifier: remove shortcut='{_shortcutPath}' removed={removal.Success} operation='{removal.Operation}' code={removal.Code}");
    }

    /// <summary>
    /// Registers the identity on the first show, deriving the default when no override is set, and
    /// throws a named failure when registration fails.
    /// </summary>
    /// <remarks>
    /// Registration is attempted once per notifier. A failed attempt is remembered, so a later show
    /// reports the same registration failure rather than the misleading "no identity" error the show
    /// path would raise from a null id.
    /// </remarks>
    /// <exception cref="ToastException">Registration failed; the exception names the failing operation.</exception>
    private void EnsureRegistered()
    {
        if (_registrationFailed)
        {
            throw new ToastException(_registrationFailureOperation, _registrationFailureCode);
        }

        if (_registrationAttempted)
        {
            return;
        }

        _registrationAttempted = true;

        NotifyIconTrace.Verbose($"toast notifier: register attempt override='{_appUserModelId ?? "(default)"}'");

        ToastIdentityResult registration = _identity.Register(_appUserModelId);

        if (!registration.Success)
        {
            _registrationFailed = true;
            _registrationFailureOperation = registration.Operation;
            _registrationFailureCode = registration.Code;

            NotifyIconTrace.Verbose(
                $"toast notifier: register failed at {registration.Operation} code=0x{registration.Code:X8}; no toast is shown");

            throw new ToastException(registration.Operation, registration.Code);
        }

        _registeredAppUserModelId = registration.AppUserModelId;
        _shortcutPath = registration.ShortcutPath;
        _createdShortcut = true;

        NotifyIconTrace.Verbose(
            $"toast notifier: registered aumid='{_registeredAppUserModelId}' shortcut='{_shortcutPath}'");
    }

    /// <summary>
    /// Persists the content's typed image source for one show, when it carries one.
    /// </summary>
    /// <param name="content">The content about to be shown.</param>
    /// <returns>
    /// The owner of the PNG that was written - exposing the absolute <c>file:///</c> reference and
    /// the file path - or <see langword="null"/> when the content carries no
    /// <see cref="ToastImage.Source"/>.
    /// </returns>
    /// <exception cref="ToastException">
    /// The source could not be persisted. <see cref="ToastException.Operation"/> is
    /// <see cref="ToastException.OperationImageResolution"/> and no file is left behind; the
    /// exception is the resolver's own and is not rewritten here.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Per show, and never written back into the content.</b> A fresh PNG is written for every
    /// show, so two shows of the same content never share - and never collide over - a file, and the
    /// content stays a dumb data shape whose <see cref="ToastImage.Reference"/> is untouched. That is
    /// what keeps <see cref="ToastContent"/>'s documented promise true: constructing or filling one
    /// touches nothing outside the process.
    /// </para>
    /// <para>
    /// <b>The typed source wins, and the ignored reference is visible.</b> When both members are set
    /// the source is the image and the pre-formed reference is dropped, with one Verbose trace line
    /// naming the reference so a capture shows the choice rather than having to infer it.
    /// </para>
    /// <para>
    /// <b>Called only after registration.</b> A registration failure therefore cannot leave a temp
    /// file behind, and a resolution failure happens before any show-path seam member is invoked.
    /// </para>
    /// </remarks>
    private static ToastImageFile? ResolveImage(ToastContent content)
    {
        if (content.Image is not { Source: { } source } image)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(image.Reference))
        {
            NotifyIconTrace.Verbose(
                $"toast notifier: image source wins over reference='{image.Reference}' (the reference is ignored)");
        }

        ToastImageFile imageFile = ToastImageFile.Create(source);

        // One line a capture can quote: the path the library owns, the reference it handed to the
        // shell, the byte length of the PNG and its pixel extent. The shell reads the file when it
        // renders the banner and the Action Center entry, so this is the reading that ties the show
        // to the file on disk.
        NotifyIconTrace.Verbose(
            $"toast notifier: image resolved path='{imageFile.Path}' reference='{imageFile.Reference}' bytes={FileLength(imageFile.Path)} pixels={imageFile.PixelWidth}x{imageFile.PixelHeight}");

        return imageFile;
    }

    /// <summary>
    /// Reads a file's byte length for the resolution trace line, without letting a concurrent removal
    /// turn a diagnostic into a failure.
    /// </summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>The length in bytes, or <c>0</c> when the file cannot be measured.</returns>
    private static long FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Raises <see cref="Activated"/> for one delivered activation argument.
    /// </summary>
    /// <param name="arguments">The launch or button argument Windows delivered, or <see langword="null"/> when the toast carried none.</param>
    /// <remarks>
    /// <para>
    /// Internal rather than private so the disposal guarantee can be exercised directly: a caller
    /// outside the show path can prove that a raise reaching a disposed notifier does nothing,
    /// without a live shell and without reflection.
    /// </para>
    /// <para>
    /// A disposed notifier refuses every raise. This is the race-narrowing complement to the
    /// show-side detach: <see cref="Dispose"/> sets <c>_disposed</c> before it disposes the live
    /// shows, so a callback that reaches the notifier after that point raises nothing.
    /// </para>
    /// </remarks>
    internal void OnShowActivated(string? arguments)
    {
        if (_disposed)
        {
            return;
        }

        RaiseSafely(
            nameof(Activated),
            () => Activated?.Invoke(this, new ToastActivatedEventArgs(arguments)));
    }

    /// <summary>
    /// Raises <see cref="Dismissed"/> for one raw dismissal reason the shell reported.
    /// </summary>
    /// <param name="reason">The raw reason, mapped to the public vocabulary before it is published.</param>
    /// <remarks>
    /// Internal for the same reason as <see cref="OnShowActivated"/>, and it refuses to raise after
    /// disposal for the same reason.
    /// </remarks>
    internal void OnShowDismissed(int reason)
    {
        if (_disposed)
        {
            return;
        }

        RaiseSafely(
            nameof(Dismissed),
            () => Dismissed?.Invoke(this, new ToastDismissedEventArgs(MapDismissalReason(reason))));
    }

    /// <summary>
    /// Raises <see cref="ToastError"/> for the shell's asynchronous delivery failure: Windows could
    /// not deliver a toast that had already been accepted for display.
    /// </summary>
    /// <param name="errorCode">The <c>HRESULT</c> the shell reported on the notification's <c>Failed</c> callback.</param>
    /// <remarks>
    /// Internal for the same reason as <see cref="OnShowActivated"/>, and it refuses to raise after
    /// disposal for the same reason.
    /// </remarks>
    internal void OnShowFailed(int errorCode)
    {
        if (_disposed)
        {
            return;
        }

        RaiseError(ToastException.OperationNotificationFailed, errorCode, exception: null);
    }

    /// <summary>
    /// Reports one toast failure the library survived: one Verbose line naming the failure's taxonomy
    /// class, then exactly one Error-level trace line, then <see cref="ToastError"/>.
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants or a seam member name.</param>
    /// <param name="errorCode">The code the failure reported, or <c>0</c> when none describes it.</param>
    /// <param name="exception">The exception behind the failure, or <see langword="null"/> when there was none.</param>
    /// <remarks>
    /// <para>
    /// Internal for the same reason as <see cref="OnShowActivated"/>. Both failure sources of the
    /// toast subsystem - the shell's asynchronous callback and the show path's own failure - report
    /// through this one method, so the event and the trace line can never disagree about what
    /// happened. A disposed notifier refuses to report: it writes no line and raises no event, which
    /// is the same refusal every other raise path applies after disposal.
    /// </para>
    /// <para>
    /// <b>The taxonomy line is Verbose, and the Error line is untouched.</b> One Verbose line names
    /// the code in the platform's own vocabulary
    /// (<see cref="ToastFailureTaxonomy.NameOf(int)"/>) so a support capture that raised the trace
    /// level reads the failure as a name rather than as a bare <c>HRESULT</c>. The Error-level line's
    /// event id, severity, shape and wording stay exactly as S03 pinned them - the name is additive
    /// detail on a different level, never appended to the line a listener filters on.
    /// </para>
    /// </remarks>
    internal void RaiseError(string operation, int errorCode, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        NotifyIconTrace.Verbose(
            $"toast failure taxonomy: code=0x{errorCode:X8} name='{ToastFailureTaxonomy.NameOf(errorCode)}'");

        NotifyIconTrace.ToastError(operation, errorCode, exception);

        RaiseSafely(
            nameof(ToastError),
            () => ToastError?.Invoke(this, new ToastErrorEventArgs(operation, errorCode, exception)));
    }

    /// <summary>
    /// Maps a raw dismissal reason onto the public vocabulary, totally: every value the shell can
    /// report becomes a named member, and everything else becomes
    /// <see cref="ToastDismissalReason.Unknown"/>.
    /// </summary>
    /// <param name="reason">The raw value the shell reported.</param>
    /// <returns>The named reason, or <see cref="ToastDismissalReason.Unknown"/> for any value outside the known set.</returns>
    /// <remarks>
    /// The mapping is deliberately total and deliberately never falls back to
    /// <see cref="ToastDismissalReason.UserCanceled"/>: claiming the user dismissed a toast they did
    /// not touch is worse than reporting that the reason is unknown.
    /// </remarks>
    private static ToastDismissalReason MapDismissalReason(int reason) => reason switch
    {
        (int)ToastDismissalReason.UserCanceled => ToastDismissalReason.UserCanceled,
        (int)ToastDismissalReason.ApplicationHidden => ToastDismissalReason.ApplicationHidden,
        (int)ToastDismissalReason.TimedOut => ToastDismissalReason.TimedOut,
        _ => ToastDismissalReason.Unknown,
    };

    /// <summary>
    /// Invokes the handlers of one event, keeping a throwing consumer handler from crossing back
    /// into the shell's callback path.
    /// </summary>
    /// <param name="operation">The event's name, used as the operation of the trace line.</param>
    /// <param name="raise">The event invocation to perform.</param>
    /// <remarks>
    /// A consumer handler's exception is the consumer's bug, but the callback arrived from a
    /// shell-owned thread, so an escaping exception would cross back into the toast stack. It is
    /// reported on the trace channel at Error level - so a default-configured listener sees it -
    /// and never rethrown. It is deliberately not published as <see cref="ToastError"/>: the toast
    /// was delivered, it is the handler that failed.
    /// </remarks>
    private static void RaiseSafely(string operation, Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception exception)
        {
            NotifyIconTrace.ToastError(operation, 0, exception);
        }
    }
}
