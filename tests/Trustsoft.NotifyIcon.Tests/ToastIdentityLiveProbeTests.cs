using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The live re-measurement for T03: it runs the <b>library's own</b> <see cref="ShortcutLink"/> +
/// <see cref="ToastIdentity"/> against the real machine to prove that a shortcut the library
/// creates really carries the AppUserModelID it wrote - the read-back that turns the slice's silent
/// failure (a shortcut that exists but carries no property) into a measured fact. Its raw evidence
/// is appended to <c>docs/TOAST-MEASUREMENT.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the only test in the suite that writes a real <c>.lnk</c> into the per-user Start menu,
/// so it is a deliberately named and self-cleaning probe (the shortcut is removed in a
/// <c>finally</c>, and a throwaway id is used). It is kept apart from the deterministic
/// <see cref="ToastIdentityTests"/> contract suite so the normal <c>dotnet test</c> filter
/// (<c>~ToastIdentityTests</c>) does not exercise the shell; run it explicitly to (re)capture the
/// live read-back evidence.
/// </para>
/// <para>
/// <c>[StaFact]</c> matters: <c>CLSID_ShellLink</c> is apartment-threaded, and an STA thread
/// creates it in-process without a COM marshalling proxy.
/// </para>
/// </remarks>
public sealed class ToastIdentityLiveProbeTests
{
    /// <summary>A throwaway identity for the probe; never a real application's id.</summary>
    private const string ProbeAppUserModelId = "Trustsoft.NotifyIcon.T03.LiveProbe";

    /// <summary>
    /// Registers the throwaway identity, reads it back, asserts the round trip, then removes the
    /// shortcut and asserts the read-back now fails (the shortcut is gone). Each step prints raw
    /// evidence to standard output, which the console logger captures.
    /// </summary>
    [StaFact]
    public void Register_reads_back_and_removes_the_identity_live_on_this_machine()
    {
        var identity = new ToastIdentity(new ShortcutLink());
        string path = ToastIdentity.DefaultShortcutPath(ProbeAppUserModelId);

        Console.WriteLine($"[live] identity: machine={Environment.MachineName}; os={Environment.OSVersion.VersionString}; shortcut={path}");

        try
        {
            ToastIdentityResult register = identity.Register(ProbeAppUserModelId, path);
            Console.WriteLine($"[live] identity: register aumid='{ProbeAppUserModelId}' success={register.Success} operation='{register.Operation}' code=0x{register.Code:X8}");
            Assert.True(register.Success, $"registration failed: {register.Operation} 0x{register.Code:X8}");
            Assert.Equal(ProbeAppUserModelId, register.AppUserModelId);

            ToastIdentityResult readBack = identity.ReadBack(path);
            Console.WriteLine($"[live] identity: read-back success={readBack.Success} value='{readBack.AppUserModelId ?? "(null)"}'");
            Assert.True(readBack.Success, $"read-back failed: {readBack.Operation} 0x{readBack.Code:X8}");
            Assert.Equal(ProbeAppUserModelId, readBack.AppUserModelId);

            Console.WriteLine("[live] identity: read-back matches expected = True");
        }
        finally
        {
            ToastIdentityResult remove = identity.Remove(path);
            Console.WriteLine($"[live] identity: remove success={remove.Success} operation='{remove.Operation}' code=0x{remove.Code:X8}");
        }

        // After removal the shortcut is gone, so a read-back must fail at the open step - the
        // "shortcut absent" case, distinct from the "shortcut present but no property" case.
        ToastIdentityResult after = identity.ReadBack(path);
        Console.WriteLine($"[live] identity: read-back after remove success={after.Success} operation='{after.Operation}' code=0x{after.Code:X8}");
        Assert.False(after.Success, "after removal the shortcut must no longer open");
    }
}
