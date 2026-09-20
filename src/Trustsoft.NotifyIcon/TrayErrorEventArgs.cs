using System.Windows;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// Carries the details of a notification-area failure that the library survived: the operation,
/// the Win32 error code, the exception behind it, and whether the single retry had already run.
/// </summary>
/// <remarks>
/// <para>
/// Per D008 a runtime failure is retried exactly once and then reported through the
/// <c>TrayError</c> routed event plus a trace line instead of an exception, because terminating
/// the process over one failed icon update would be worse than the failed update. That decision
/// only works if the event is genuinely actionable: the whole product premise is an app with no
/// window (R001), so the routed event and the trace line are the only channels such an app has.
/// Hence this type carries <see cref="Operation"/>, <see cref="Win32ErrorCode"/>,
/// <see cref="Exception"/> and <see cref="Retried"/> rather than a bare flag.
/// </para>
/// <para>
/// <see cref="Retried"/> separates "the shell refused this update once and accepted it on the
/// retry - the caller may ignore it" from "the update failed twice and the displayed icon is now
/// stale - the caller should probably tell the user". Both are reported, and the flag is the only
/// way to tell them apart from the event alone.
/// </para>
/// <para>
/// The args are stamped with the routed event they belong to. The library raises the event with
/// the <see cref="TrayErrorEventArgs(string, int, Exception, bool, RoutedEvent)"/> overload;
/// the overload without a <see cref="RoutedEvent"/> exists for callers that supply it
/// themselves (WPF requires <see cref="RoutedEventArgs.RoutedEvent"/> to be non-null before
/// <c>RaiseEvent</c>).
/// </para>
/// </remarks>
public sealed class TrayErrorEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance whose routed event is supplied by the raiser.
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants on
    /// <see cref="TrayIconException"/>.</param>
    /// <param name="win32ErrorCode">The Win32 error code, or <c>0</c> when no Win32 call failed.</param>
    /// <param name="exception">The exception the failure produced; never null.</param>
    /// <param name="retried">
    /// <c>true</c> when the reported failure happened after the single retry, <c>false</c> when it
    /// is the retryable failure itself.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public TrayErrorEventArgs(string operation, int win32ErrorCode, Exception exception, bool retried)
        : base()
    {
        // No routed event here: RoutedEventArgs' parameterless constructor leaves the event null,
        // and the raiser (or the bound overload below) supplies it. WPF requires a non-null
        // RoutedEvent before RaiseEvent, so an instance built this way must be stamped first.
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(exception);

        Operation = operation;
        Win32ErrorCode = win32ErrorCode;
        Exception = exception;
        Retried = retried;
    }

    /// <summary>
    /// Initializes a new instance bound to the routed event being raised. This is the overload
    /// the library uses, because <c>RaiseEvent</c> requires the args to know their event.
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants on
    /// <see cref="TrayIconException"/>.</param>
    /// <param name="win32ErrorCode">The Win32 error code, or <c>0</c> when no Win32 call failed.</param>
    /// <param name="exception">The exception the failure produced; never null.</param>
    /// <param name="retried">
    /// <c>true</c> when the reported failure happened after the single retry, <c>false</c> when it
    /// is the retryable failure itself.
    /// </param>
    /// <param name="routedEvent">The routed event these arguments are being raised for.</param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="exception"/> or <paramref name="routedEvent"/> is null.
    /// </exception>
    public TrayErrorEventArgs(
        string operation,
        int win32ErrorCode,
        Exception exception,
        bool retried,
        RoutedEvent routedEvent)
        : base(routedEvent)
    {
        // Operation is part of the machine-readable contract the consumer branches on, so a null
        // or empty value is a programming error rather than something to store.
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(exception);

        // RoutedEventArgs' own constructor accepts a null event (verified empirically on
        // net8.0-windows: base(null) does not throw), which would produce args that RaiseEvent
        // cannot route. Rejecting it here turns a confusing failure at raise time into a clear
        // one at construction.
        ArgumentNullException.ThrowIfNull(routedEvent);

        Operation = operation;
        Win32ErrorCode = win32ErrorCode;
        Exception = exception;
        Retried = retried;
    }

    /// <summary>
    /// Gets the operation that failed, as one of the <c>Operation*</c> constants on
    /// <see cref="TrayIconException"/>.
    /// </summary>
    /// <value>A stable, non-empty operation name such as
    /// <see cref="TrayIconException.OperationModify"/>.</value>
    public string Operation { get; }

    /// <summary>
    /// Gets the Win32 error code the failing call reported, or <c>0</c> when the failure did not
    /// come from a Win32 call.
    /// </summary>
    public int Win32ErrorCode { get; }

    /// <summary>
    /// Gets the exception the failure produced. It is also what the trace channel reports, in
    /// single-line form; this property carries the full object (type, message, stack trace,
    /// inner exceptions) for a consumer that wants to inspect it.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets a value indicating whether the reported failure happened after the single retry.
    /// </summary>
    /// <value>
    /// <c>false</c> for a failure the library retried; <c>true</c> when the retry had already been
    /// spent and the update is now known to be lost.
    /// </value>
    public bool Retried { get; }

    /// <summary>
    /// Dispatches the handler with the strongly typed argument, instead of the reflection-based
    /// fallback WPF uses for event args it does not know.
    /// </summary>
    /// <param name="genericHandler">The handler registered for the routed event.</param>
    /// <param name="genericTarget">The element the event is being raised on.</param>
    /// <remarks>
    /// The base implementation box-calls the delegate for an unknown args type; every event that
    /// carries typed args overrides this method, and the type cast is safe because the
    /// <c>TrayError</c> routed event is registered with <c>EventHandler&lt;TrayErrorEventArgs&gt;</c>.
    /// </remarks>
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ((EventHandler<TrayErrorEventArgs>)genericHandler)(genericTarget, this);
    }
}
