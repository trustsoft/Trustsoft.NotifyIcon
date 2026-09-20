using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Process GDI object counter used as objective evidence that icon replacement does not
/// leak handles (R007). Dependency-free on purpose: no scanner package, no
/// System.Diagnostics.PerformanceCounter.
/// </summary>
internal static class GdiHandles
{
    private const int GrGdiObjects = 0;

    /// <summary>
    /// Returns the current process' GDI object count.
    /// </summary>
    /// <returns>The number of GDI objects owned by the current process.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the OS reports no GDI count (a zero return with a set last-error code),
    /// which would make every handle-leak assertion meaningless.
    /// </exception>
    internal static int Count()
    {
        Marshal.SetLastPInvokeError(0);
        int count = GetGuiResources(GetCurrentProcess(), GrGdiObjects);

        if (count == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 0)
            {
                throw new InvalidOperationException(
                    $"GetGuiResources failed with Win32 error {error}; GDI handle counts cannot be trusted.");
            }
        }

        return count;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();
}
