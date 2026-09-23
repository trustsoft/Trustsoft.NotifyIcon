namespace Trustsoft.NotifyIcon;

/// <summary>
/// Carries a toast failure the library survived, for <see cref="ToastNotifier.ToastError"/>: the
/// failing operation, the code that call reported, and the exception behind it when there is one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same shape as <see cref="ToastException"/> and <see cref="TrayErrorEventArgs"/>.</b>
/// <see cref="Operation"/> plus <see cref="ErrorCode"/> is the whole machine-readable report, so a
/// consumer branches on data rather than on message text - the vocabulary is the one
/// <see cref="ToastException"/> documents, including
/// <see cref="ToastException.OperationNotificationFailed"/> for the shell's asynchronous delivery
/// failure. The event is the non-fatal channel D055 fixes for a runtime failure: it is raised
/// instead of throwing, because a toast that could not be delivered must not terminate a
/// windowless host.
/// </para>
/// <para>
/// <b><see cref="Exception"/> is optional here, unlike on <see cref="TrayErrorEventArgs"/>.</b> The
/// two failure sources this event reports do not always have an exception to show: the shell's
/// asynchronous <c>Failed</c> callback reports a bare <c>HRESULT</c> and no exception, so a
/// <see langword="null"/> value is normal and expected. A failure that did produce an exception
/// carries it here in full, while the trace line carries it collapsed to one line.
/// </para>
/// <para>
/// <b><see cref="ErrorCode"/> of <c>0</c> is not success.</b> <c>0</c> means no code described the
/// failure - a handler that threw, for example, which no <c>HRESULT</c> reports. It must never be
/// read as "the operation worked".
/// </para>
/// </remarks>
public sealed class ToastErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance for a failure that needs no extra explanation.
    /// </summary>
    /// <param name="operation">
    /// The failing operation; one of the <c>Operation*</c> constants on <see cref="ToastException"/>,
    /// or the name of the seam member that failed.
    /// </param>
    /// <param name="errorCode">
    /// The <c>HRESULT</c> or Win32 error code the failing call reported, or <c>0</c> when no code
    /// describes the failure (see the remarks on this type).
    /// </param>
    /// <param name="exception">The exception the failure produced, or <see langword="null"/> when there was none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty.</exception>
    public ToastErrorEventArgs(string operation, int errorCode, Exception? exception)
    {
        // The operation name is part of the machine-readable contract: a null or empty value would
        // make a consumer's branch on Operation impossible, so it is rejected here rather than
        // silently stored - the same validation TrayErrorEventArgs performs.
        ArgumentException.ThrowIfNullOrEmpty(operation);

        Operation = operation;
        ErrorCode = errorCode;
        Exception = exception;
    }

    /// <summary>
    /// Gets the operation that failed: one of the <c>Operation*</c> constants on
    /// <see cref="ToastException"/>, or the name of the internal step that failed.
    /// </summary>
    /// <value>A stable, non-empty operation name such as <see cref="ToastException.OperationNotificationFailed"/>.</value>
    public string Operation { get; }

    /// <summary>
    /// Gets the code the failing call reported: an <c>HRESULT</c> for a COM or WinRT step, a Win32
    /// error for a file-system step, or <c>0</c> when no call failed.
    /// </summary>
    /// <value>
    /// A negative <c>HRESULT</c>, a positive Win32 error, or <c>0</c> for a failure no code
    /// described. <c>0</c> never means the operation succeeded.
    /// </value>
    public int ErrorCode { get; }

    /// <summary>
    /// Gets the exception the failure produced, or <see langword="null"/> when the failure had none.
    /// </summary>
    /// <value>
    /// The full exception object (type, message, stack trace, inner exceptions) when one exists -
    /// for example the shell's asynchronous failure, which reports a bare code and no exception, or
    /// a handler that threw. The trace line carries only a one-line rendering of it.
    /// </value>
    public Exception? Exception { get; }
}
