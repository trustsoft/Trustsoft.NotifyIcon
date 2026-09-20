// Makes the internal shell seam (IShellApi, shell constants, NOTIFYICONDATAW) reachable
// from the test assembly without widening the shipped public API (D009).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Trustsoft.NotifyIcon.Tests")]
