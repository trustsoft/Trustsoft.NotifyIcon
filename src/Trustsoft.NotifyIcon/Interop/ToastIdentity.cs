using System.IO;
using System.Reflection;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The library's identity service for AppUserModelID registration: it drives the
/// <see cref="IToastApi"/> Contract-1 members to create the per-user Start-menu shortcut that
/// carries the identity, reads the property back from a <em>fresh</em> shell link and fails with a
/// named error when the read-back disagrees, supports an explicit override id, derives the default
/// from the entry assembly, and removes the shortcut it created.
/// </summary>
/// <remarks>
/// <para>
/// <b>Above the seam, below the caller.</b> <see cref="IToastApi"/> returns raw results with no
/// interpretation; this class is the first layer that interprets them. Its failures are still data,
/// in the seam's spirit: a <see cref="ToastIdentityResult"/> carries an <em>operation name plus a
/// code</em> - the failing <c>nameof(IToastApi.X)</c> together with a negative <c>HRESULT</c> for
/// the COM steps, <c>nameof(IToastApi.DeleteShortcut)</c> together with the Win32 error for removal,
/// or the dedicated <see cref="OperationReadBackMismatch"/> (code <c>0</c>) for the value-level
/// disagreement that no COM call reports. Nothing here raises an exception; the future public toast
/// API (and its <c>ToastException</c>) is the layer that converts these results into exceptions.
/// </para>
/// <para>
/// <b>The read-back check is the point.</b> A shortcut that exists but carries no
/// <c>System.AppUserModelID</c> is the exact silent failure this slice exists to rule out: the
/// write path can return <c>S_OK</c> at every step and still leave the identity unroutable. Reading
/// the property back from a freshly opened shell link (never the writer's own object) and comparing
/// it to the written value is what turns that silence into <see cref="OperationReadBackMismatch"/>.
/// </para>
/// <para>
/// <b>The default id is the entry assembly's name.</b> An unpackaged WPF process's entry assembly is
/// the consumer's executable, whose simple name is the natural identity; a host with no entry
/// assembly (a test host, a COM server) falls back to the library's own name rather than failing.
/// </para>
/// </remarks>
internal sealed class ToastIdentity
{
    /// <summary>
    /// The operation name for a read-back that completed but did not agree with what was written -
    /// the value-level failure (missing property, or a different value) that no <c>HRESULT</c>
    /// describes. Its code is <c>0</c>.
    /// </summary>
    internal const string OperationReadBackMismatch = "ReadBackMismatch";

    /// <summary>The description written onto the shortcut (cosmetic; it does not affect the id).</summary>
    private const string ShortcutDescription = "AppUserModelID registration for toast notifications";

    private readonly IToastApi _api;

    /// <summary>
    /// Initializes the service over a toast seam.
    /// </summary>
    /// <param name="api">
    /// The seam to drive. The production value is <c>new ShortcutLink()</c>; a test supplies a
    /// recording fake.
    /// </param>
    internal ToastIdentity(IToastApi api) => _api = api;

    /// <summary>
    /// Registers the identity: creates the shortcut, writes the AppUserModelID, reads it back from a
    /// fresh shell link and fails with a named error when the read-back disagrees.
    /// </summary>
    /// <param name="overrideAppUserModelId">
    /// The explicit identity to write. When <see langword="null"/> or empty, the default is derived
    /// from the entry assembly.
    /// </param>
    /// <param name="shortcutPath">
    /// The shortcut file path. When <see langword="null"/> or empty, it is derived from the identity
    /// into the per-user Start menu.
    /// </param>
    /// <returns>
    /// The result: on success, <see cref="ToastIdentityResult.AppUserModelId"/> is the registered id;
    /// on failure, <see cref="ToastIdentityResult.Operation"/> names the failing step and
    /// <see cref="ToastIdentityResult.Code"/> carries its <c>HRESULT</c> (or <c>0</c> for a mismatch).
    /// </returns>
    internal ToastIdentityResult Register(string? overrideAppUserModelId = null, string? shortcutPath = null)
    {
        string appUserModelId = string.IsNullOrEmpty(overrideAppUserModelId)
            ? DeriveDefaultAppUserModelId()
            : overrideAppUserModelId;

        string path = string.IsNullOrEmpty(shortcutPath)
            ? DefaultShortcutPath(appUserModelId)
            : shortcutPath;

        ToastIdentityResult write = Write(appUserModelId, path);
        if (!write.Success)
        {
            return write;
        }

        // The read-back is the check: it must be read from a fresh shell link (not the writer's own
        // object), and it must equal what was written, or the registration is a silent failure.
        ToastIdentityResult readBack = ReadBack(path);
        if (!readBack.Success)
        {
            return readBack;
        }

        if (!string.Equals(readBack.AppUserModelId, appUserModelId, StringComparison.Ordinal))
        {
            return ToastIdentityResult.Failed(
                OperationReadBackMismatch,
                0,
                path,
                readBack.AppUserModelId);
        }

        return ToastIdentityResult.Succeeded(appUserModelId, path);
    }

    /// <summary>
    /// Reads the AppUserModelID the shortcut at <paramref name="shortcutPath"/> currently carries.
    /// </summary>
    /// <param name="shortcutPath">The shortcut file path.</param>
    /// <returns>
    /// A result whose <see cref="ToastIdentityResult.AppUserModelId"/> is the read value (which is
    /// <see langword="null"/> when the shortcut exists but carries no property - the silent-failure
    /// case - or when the shortcut does not exist, in which case the open itself fails).
    /// </returns>
    internal ToastIdentityResult ReadBack(string shortcutPath)
    {
        int hr = _api.OpenShellLink(shortcutPath, out IntPtr shellLink);
        if (hr < 0)
        {
            return ToastIdentityResult.Failed(nameof(IToastApi.OpenShellLink), hr, shortcutPath);
        }

        hr = _api.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore);
        if (hr < 0)
        {
            _api.ReleaseHandle(shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.GetShortcutPropertyStore), hr, shortcutPath);
        }

        hr = _api.GetAppUserModelId(propertyStore, out string? appUserModelId);

        _api.ReleaseHandle(propertyStore);
        _api.ReleaseHandle(shellLink);

        if (hr < 0)
        {
            return ToastIdentityResult.Failed(nameof(IToastApi.GetAppUserModelId), hr, shortcutPath);
        }

        // A null value here is not a seam failure - the read completed, and "no property present"
        // is the value-level outcome the caller (Register) turns into a mismatch.
        return ToastIdentityResult.Succeeded(appUserModelId, shortcutPath);
    }

    /// <summary>
    /// Removes the shortcut the library created at <paramref name="shortcutPath"/>.
    /// </summary>
    /// <param name="shortcutPath">The shortcut file path.</param>
    /// <returns>
    /// Success when the file is gone (or was already absent); failure otherwise, with
    /// <see cref="ToastIdentityResult.Operation"/> = <c>nameof(IToastApi.DeleteShortcut)</c> and
    /// <see cref="ToastIdentityResult.Code"/> carrying the Win32 error.
    /// </returns>
    internal ToastIdentityResult Remove(string shortcutPath)
    {
        if (_api.DeleteShortcut(shortcutPath))
        {
            return ToastIdentityResult.Succeeded(null, shortcutPath);
        }

        return ToastIdentityResult.Failed(nameof(IToastApi.DeleteShortcut), _api.GetLastError(), shortcutPath);
    }

    /// <summary>
    /// Derives the default AppUserModelID from the entry assembly's simple name.
    /// </summary>
    /// <returns>
    /// The entry assembly name; the library's own assembly name when there is no entry assembly
    /// (a test host, a COM server, a dynamic host).
    /// </returns>
    internal static string DeriveDefaultAppUserModelId()
    {
        string? name = Assembly.GetEntryAssembly()?.GetName().Name;

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return typeof(ToastIdentity).Assembly.GetName().Name ?? "Trustsoft.NotifyIcon";
    }

    /// <summary>
    /// Derives the default shortcut path for an identity: the per-user Start menu, named after the id.
    /// </summary>
    /// <param name="appUserModelId">The identity the shortcut carries.</param>
    /// <returns>The full <c>.lnk</c> path.</returns>
    internal static string DefaultShortcutPath(string appUserModelId)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return Path.Combine(programs, appUserModelId + ".lnk");
    }

    /// <summary>
    /// The write half of registration: the measured Contract-1 sequence, in the seam's canonical
    /// order, releasing every handle it acquires on every exit path.
    /// </summary>
    /// <param name="appUserModelId">The identity to write.</param>
    /// <param name="shortcutPath">The shortcut file path.</param>
    /// <returns>The result of the write, before any read-back check.</returns>
    private ToastIdentityResult Write(string appUserModelId, string shortcutPath)
    {
        int hr = _api.CreateShellLink(out IntPtr shellLink);
        if (hr < 0)
        {
            return ToastIdentityResult.Failed(nameof(IToastApi.CreateShellLink), hr, shortcutPath);
        }

        string targetPath = Environment.ProcessPath ?? typeof(ToastIdentity).Assembly.Location;

        hr = _api.ConfigureShortcut(shellLink, targetPath, ShortcutDescription, targetPath, string.Empty);
        if (hr < 0)
        {
            _api.ReleaseHandle(shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.ConfigureShortcut), hr, shortcutPath);
        }

        hr = _api.GetShortcutPropertyStore(shellLink, out IntPtr propertyStore);
        if (hr < 0)
        {
            _api.ReleaseHandle(shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.GetShortcutPropertyStore), hr, shortcutPath);
        }

        hr = _api.GetShortcutPersistFile(shellLink, out IntPtr persistFile);
        if (hr < 0)
        {
            _api.ReleaseHandle(propertyStore);
            _api.ReleaseHandle(shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.GetShortcutPersistFile), hr, shortcutPath);
        }

        hr = _api.SetAppUserModelId(propertyStore, appUserModelId);
        if (hr < 0)
        {
            Release(persistFile, propertyStore, shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.SetAppUserModelId), hr, shortcutPath);
        }

        hr = _api.CommitPropertyStore(propertyStore);
        if (hr < 0)
        {
            Release(persistFile, propertyStore, shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.CommitPropertyStore), hr, shortcutPath);
        }

        hr = _api.SaveShortcut(persistFile, shortcutPath);
        if (hr < 0)
        {
            Release(persistFile, propertyStore, shellLink);
            return ToastIdentityResult.Failed(nameof(IToastApi.SaveShortcut), hr, shortcutPath);
        }

        Release(persistFile, propertyStore, shellLink);

        return ToastIdentityResult.Succeeded(appUserModelId, shortcutPath);
    }

    /// <summary>Releases the three handles the write path acquired, in reverse acquisition order.</summary>
    private void Release(IntPtr persistFile, IntPtr propertyStore, IntPtr shellLink)
    {
        _api.ReleaseHandle(persistFile);
        _api.ReleaseHandle(propertyStore);
        _api.ReleaseHandle(shellLink);
    }
}
