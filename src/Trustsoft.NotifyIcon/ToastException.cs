using System.Globalization;
using Trustsoft.NotifyIcon.Interop;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The single public exception type for toast failures. It carries the failing operation and the
/// code the failing call reported as machine-readable properties, so a consumer can branch on a
/// failure without parsing a message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operation plus code is the whole report (D055).</b> <see cref="Operation"/> is a stable,
/// non-empty string - either one of the <c>Operation*</c> constants declared on this type, or the
/// name of the seam member that failed (for example <c>"CreateToastNotification"</c> or
/// <c>"SaveShortcut"</c>), which is the same vocabulary the internal identity and show paths produce.
/// <see cref="ErrorCode"/> is the raw code that call returned: an <c>HRESULT</c> for every COM and
/// WinRT step, or a Win32 error for the one file-system step (deleting the registered shortcut).
/// </para>
/// <para>
/// <b>This mirrors <see cref="TrayIconException"/> deliberately.</b> A consumer that already knows
/// how to read a tray failure reads a toast failure the same way; the type is separate because a
/// toast failure throwing a "TrayIcon" exception would be misleading, which is exactly the trade
/// D055 records.
/// </para>
/// <para>
/// <b>The D055 posture: startup and registration failures throw; runtime failures do not.</b>
/// A notifier that cannot establish the identity it shows through throws this exception from
/// <see cref="ToastNotifier.Show"/> rather than silently dropping the toast, because an unpackaged
/// process has no other way to learn that nothing will be delivered. A failure that happens while
/// an already registered notifier is showing a toast is reported through the non-fatal
/// <c>ToastError</c> event and one Error-level line on the trace channel instead, so the caller
/// still learns that Windows did not confirm delivery - without a throw from
/// <see cref="ToastNotifier.Show"/>. This type is therefore the startup and registration report;
/// <c>ToastError</c> is the runtime one (D055, as replaced by D061).
/// </para>
/// <para>
/// <b><see cref="ErrorCode"/> of <c>0</c> is not success.</b> <c>0</c> means no code describes the
/// failure - a read-back that completed but disagreed with the written AppUserModelID, for example,
/// which no <c>HRESULT</c> reports. It must never be read as "the operation worked".
/// </para>
/// <para>
/// Nothing is reported as successful unless Windows confirmed it: a <c>S_OK</c> from the shell's
/// <c>Show</c> means the toast was accepted, not that the user saw it, and this exception is raised
/// only when a call actually failed.
/// </para>
/// </remarks>
public sealed class ToastException : Exception
{
    /// <summary>
    /// The <see cref="Operation"/> value for content the notifier refuses to show because the caller
    /// produced nothing displayable - a toast whose title is empty or whitespace.
    /// </summary>
    /// <remarks>
    /// This is one of the three <em>value-level</em> operation names: no <c>HRESULT</c> describes it,
    /// so <see cref="ErrorCode"/> is <c>E_INVALIDARG</c> (<c>0x80070057</c>) as the closest honest
    /// code rather than a code a call returned. It mirrors
    /// <c>ToastShow.OperationInvalidArgument</c> so the notifier and the show path cannot spell it
    /// differently.
    /// </remarks>
    public const string OperationInvalidArgument = ToastShow.OperationInvalidArgument;

    /// <summary>
    /// The <see cref="Operation"/> value for a second <see cref="ToastNotifier.Show"/> against a
    /// show object that was already used; a notifier creates a fresh show per call, so a consumer
    /// should never see it from the public surface.
    /// </summary>
    /// <remarks>
    /// A value-level name like <see cref="OperationInvalidArgument"/>: <see cref="ErrorCode"/> is
    /// <c>E_UNEXPECTED</c> (<c>0x8000FFFF</c>). It mirrors <c>ToastShow.OperationAlreadyShown</c>.
    /// </remarks>
    public const string OperationAlreadyShown = ToastShow.OperationAlreadyShown;

    /// <summary>
    /// The <see cref="Operation"/> value for the shell's asynchronous delivery failure: Windows
    /// raised the toast notification's <c>Failed</c> callback for a toast this application had
    /// already shown, so the toast was accepted by the shell and then not delivered.
    /// </summary>
    /// <remarks>
    /// <see cref="ErrorCode"/> is the <c>HRESULT</c> the shell reported - this failure is raised on
    /// the callback path, not returned by a call this library makes, so it is never <c>0</c> by
    /// accident of a value-level comparison. It mirrors <c>ToastShow.OperationNotificationFailed</c>
    /// so the notifier's event and this exception cannot spell the same failure differently.
    /// </remarks>
    public const string OperationNotificationFailed = ToastShow.OperationNotificationFailed;

    /// <summary>
    /// The <see cref="Operation"/> value for the identity-level silent failure: the shortcut was
    /// written, the read-back completed, and the value it read did not match the value that was
    /// written (or was absent).
    /// </summary>
    /// <remarks>
    /// <see cref="ErrorCode"/> is <c>0</c>, because no call failed - this is a value disagreement
    /// that the seam cannot report as an <c>HRESULT</c>. It mirrors
    /// <c>ToastIdentity.OperationReadBackMismatch</c>.
    /// </remarks>
    public const string OperationReadBackMismatch = ToastIdentity.OperationReadBackMismatch;

    /// <summary>
    /// Initializes a new instance for a failure that needs no extra explanation.
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants or a seam member name.</param>
    /// <param name="errorCode">
    /// The <c>HRESULT</c> or Win32 error code the failing call reported, or <c>0</c> when no code
    /// describes the failure (see the remarks on this type).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    public ToastException(string operation, int errorCode)
        : this(operation, errorCode, string.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance for a failure whose message must say something the operation and
    /// the code cannot (for example which identity value was expected and which was read back).
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants or a seam member name.</param>
    /// <param name="errorCode">The <c>HRESULT</c> or Win32 error code, or <c>0</c> when none applies.</param>
    /// <param name="detail">
    /// An optional English sentence appended to the standard message. Pass
    /// <see cref="string.Empty"/> when the standard message is enough.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    public ToastException(string operation, int errorCode, string detail)
        : base(BuildMessage(operation, errorCode, detail))
    {
        Operation = operation;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the operation that failed: one of the <c>Operation*</c> constants on this type, or the
    /// name of the internal step that failed for a seam-level failure.
    /// </summary>
    /// <value>A stable, non-empty operation name such as <see cref="OperationReadBackMismatch"/>.</value>
    public string Operation { get; }

    /// <summary>
    /// Gets the code the failing call reported: an <c>HRESULT</c> for a COM or WinRT step, a Win32
    /// error for the file-system step, or <c>0</c> when no call failed and the failure was a value
    /// disagreement.
    /// </summary>
    /// <value>
    /// A negative <c>HRESULT</c>, a positive Win32 error, or <c>0</c> for a value-level failure.
    /// <c>0</c> never means the operation succeeded.
    /// </value>
    public int ErrorCode { get; }

    /// <summary>
    /// Builds the English message (D010) from the operation and the code, then appends the optional
    /// detail.
    /// </summary>
    /// <param name="operation">The failing operation.</param>
    /// <param name="errorCode">The code the failing call reported, or <c>0</c>.</param>
    /// <param name="detail">Optional extra sentence; empty to omit.</param>
    /// <returns>The message text, carrying the code in both decimal and hexadecimal form.</returns>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    private static string BuildMessage(string operation, int errorCode, string detail)
    {
        // The operation name is part of the machine-readable contract: a null or empty value would
        // make a consumer's branch on Operation impossible, so it is rejected here rather than
        // silently stored. Every library call site passes a seam member name or one of the
        // constants above.
        ArgumentException.ThrowIfNullOrEmpty(operation);

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "The toast operation '{0}' failed with code {1} (0x{1:X8}).",
            operation,
            errorCode);

        return string.IsNullOrEmpty(detail) ? message : message + " " + detail;
    }
}
