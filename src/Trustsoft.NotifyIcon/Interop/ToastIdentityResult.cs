namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The result of a <see cref="ToastIdentity"/> operation: success carries the registered or
/// read-back id, and failure carries a <em>named</em> operation plus a code, in the seam's
/// failure-as-data spirit (an operation plus an <c>HRESULT</c> or a Win32 error).
/// </summary>
/// <remarks>
/// <para>
/// <b>No exceptions on this type.</b> It is the data shape the future public toast API converts
/// into its own failure channel; a <see cref="Success"/> of <see langword="false"/> is the entire
/// report, never a side effect of an unhandled throw.
/// </para>
/// <para>
/// <b><see cref="Operation"/> is the failing step's name</b> - a
/// <c>nameof(IToastApi.X)</c> for a seam-level failure, or
/// <see cref="ToastIdentity.OperationReadBackMismatch"/> for the value-level disagreement. It is
/// <see cref="string.Empty"/> on success. <see cref="Code"/> is the <c>HRESULT</c> or Win32 error,
/// <c>0</c> when no code describes the failure (a read-back mismatch, or success).
/// </para>
/// </remarks>
internal readonly struct ToastIdentityResult
{
    private ToastIdentityResult(bool success, string? appUserModelId, string shortcutPath, string operation, int code)
    {
        Success = success;
        AppUserModelId = appUserModelId;
        ShortcutPath = shortcutPath;
        Operation = operation;
        Code = code;
    }

    /// <summary>Gets whether the operation succeeded.</summary>
    internal bool Success { get; }

    /// <summary>
    /// Gets the AppUserModelID: the registered id on success, the read-back value on a successful
    /// read, or the actual read-back value that disagreed on a mismatch (for diagnostics).
    /// </summary>
    internal string? AppUserModelId { get; }

    /// <summary>Gets the shortcut file path the operation concerned.</summary>
    internal string ShortcutPath { get; }

    /// <summary>Gets the failing operation's name, or <see cref="string.Empty"/> on success.</summary>
    internal string Operation { get; }

    /// <summary>Gets the <c>HRESULT</c> or Win32 error code, or <c>0</c> when none applies.</summary>
    internal int Code { get; }

    /// <summary>Builds a successful result carrying the id (which may be <see langword="null"/> for a read).</summary>
    internal static ToastIdentityResult Succeeded(string? appUserModelId, string shortcutPath) =>
        new(true, appUserModelId, shortcutPath, string.Empty, 0);

    /// <summary>Builds a failed result naming the operation and its code.</summary>
    internal static ToastIdentityResult Failed(string operation, int code, string shortcutPath, string? appUserModelId = null) =>
        new(false, appUserModelId, shortcutPath, operation, code);
}
