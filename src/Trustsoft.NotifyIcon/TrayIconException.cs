using System.Globalization;

namespace Trustsoft.NotifyIcon;

/// <summary>
/// The single public exception type for notification-area failures. It carries the failing
/// operation and the Win32 error code as machine-readable properties, so a consumer can branch
/// on a failure without parsing a message.
/// </summary>
/// <remarks>
/// <para>
/// R013 requires the failure to be actionable, which is why <see cref="Operation"/> is a stable
/// public string - one of the <c>Operation*</c> constants declared on this type - rather than a
/// display name invented at the call site. The constants live on the exception type so that no
/// call site and no test can spell an operation differently by accident; a rename is then a
/// deliberate edit that fails the tests pinning the values.
/// </para>
/// <para>
/// Per D008 this is the <em>only</em> public tray exception. Two very different moments use it:
/// a <b>startup</b> failure (the icon could not be created at all) throws, because a windowless
/// app whose only UI is the notification area would otherwise degrade into a silently missing
/// icon; a <b>runtime</b> failure (an update of an already registered icon) is retried once and
/// then surfaced through the <see cref="TrayErrorEventArgs"/> payload of the <c>TrayError</c>
/// routed event plus a trace line, without terminating the process.
/// </para>
/// <para>
/// <see cref="Win32ErrorCode"/> is <c>0</c> whenever no Win32 call was involved in the failure -
/// an <c>ImageSource</c> that cannot be converted to an icon, for example - so <c>0</c> must not
/// be read as "the operation succeeded". A Win32 call may also fail without setting a last-error
/// code; that case is indistinguishable here, which is why the message reports the code rather
/// than claiming it explains the failure.
/// </para>
/// </remarks>
public sealed class TrayIconException : Exception
{
    /// <summary>
    /// The <see cref="Operation"/> value for <c>Shell_NotifyIcon(NIM_ADD)</c> - registering the
    /// icon, the one shell step whose failure must reach the caller.
    /// </summary>
    public const string OperationAdd = "Add";

    /// <summary>
    /// The <see cref="Operation"/> value for <c>Shell_NotifyIcon(NIM_SETVERSION)</c> - selecting
    /// the v4 notification protocol immediately after a successful add.
    /// </summary>
    public const string OperationSetVersion = "SetVersion";

    /// <summary>
    /// The <see cref="Operation"/> value for <c>Shell_NotifyIcon(NIM_MODIFY)</c> - updating an
    /// already registered icon.
    /// </summary>
    public const string OperationModify = "Modify";

    /// <summary>
    /// The <see cref="Operation"/> value for <c>Shell_NotifyIcon(NIM_DELETE)</c> - removing the
    /// icon, including the removal that runs during disposal.
    /// </summary>
    public const string OperationRemove = "Remove";

    /// <summary>
    /// The <see cref="Operation"/> value for <c>RegisterWindowMessage</c> - resolving the
    /// <c>TaskbarCreated</c> message the host window needs to survive an Explorer restart.
    /// </summary>
    public const string OperationRegisterMessage = "RegisterMessage";

    /// <summary>
    /// The <see cref="Operation"/> value for producing the icon handle the shell is given,
    /// whether it comes from a resource or from an <c>ImageSource</c> conversion.
    /// </summary>
    public const string OperationLoadIcon = "LoadIcon";

    /// <summary>
    /// The <see cref="Operation"/> value for converting an <c>ImageSource</c> into an HICON.
    /// </summary>
    public const string OperationConvertIcon = "ConvertIcon";

    /// <summary>
    /// Initializes a new instance for a failure that needs no extra explanation.
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants.</param>
    /// <param name="win32ErrorCode">The Win32 error code, or <c>0</c> when no Win32 call failed.</param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    public TrayIconException(string operation, int win32ErrorCode)
        : this(operation, win32ErrorCode, string.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance for a failure whose message must say something the operation
    /// and the error code cannot (for example which <c>ImageSource</c> shape is unsupported).
    /// </summary>
    /// <param name="operation">The failing operation; one of the <c>Operation*</c> constants.</param>
    /// <param name="win32ErrorCode">The Win32 error code, or <c>0</c> when no Win32 call failed.</param>
    /// <param name="detail">
    /// An optional English sentence appended to the standard message. Pass
    /// <see cref="string.Empty"/> when the standard message is enough.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    public TrayIconException(string operation, int win32ErrorCode, string detail)
        : base(BuildMessage(operation, win32ErrorCode, detail))
    {
        Operation = operation;
        Win32ErrorCode = win32ErrorCode;
    }

    /// <summary>
    /// Gets the operation that failed, as one of the <c>Operation*</c> constants on this type.
    /// </summary>
    /// <value>A stable, non-empty operation name such as <see cref="OperationAdd"/>.</value>
    public string Operation { get; }

    /// <summary>
    /// Gets the Win32 error code that the failing call reported, or <c>0</c> when the failure did
    /// not come from a Win32 call.
    /// </summary>
    /// <value>A last-error code, or <c>0</c> for a non-Win32 failure such as an unusable image.</value>
    public int Win32ErrorCode { get; }

    /// <summary>
    /// Builds the English message (D010) from the operation and the code, then appends the
    /// optional detail.
    /// </summary>
    /// <param name="operation">The failing operation.</param>
    /// <param name="win32ErrorCode">The Win32 error code.</param>
    /// <param name="detail">Optional extra sentence; empty to omit.</param>
    /// <returns>The message text.</returns>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null or empty.</exception>
    private static string BuildMessage(string operation, int win32ErrorCode, string detail)
    {
        // The operation name is part of the machine-readable contract: a null or empty value
        // would make a consumer's branch on Operation impossible, so it is rejected here rather
        // than silently stored. Every library call site passes one of the constants above.
        ArgumentException.ThrowIfNullOrEmpty(operation);

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "The notification-area operation '{0}' failed with Win32 error {1}.",
            operation,
            win32ErrorCode);

        return string.IsNullOrEmpty(detail) ? message : message + " " + detail;
    }
}
