using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Process GDI object counter plus the minimal GDI object factory used to prove that the
/// counter actually responds to allocation and release.
/// </summary>
/// <remarks>
/// <para>
/// Dependency-free on purpose: no scanner package and no
/// <c>System.Diagnostics.PerformanceCounter</c>. Task T06 uses <see cref="Count"/> as the
/// objective evidence for R007 (replacing the tray icon must not leak GDI handles).
/// </para>
/// <para>
/// Use this counter as a <em>delta</em> against a locally measured baseline. A freshly
/// started process genuinely owns zero GDI objects (verified on this machine: a console host
/// reports <c>GetGuiResources(hProcess, GR_GDIOBJECTS) == 0</c> with a zero last-error), so
/// "the count is positive" is not a meaningful assertion. Comparing a measured baseline
/// against a measured post-operation count is.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Creates a memory device context, which owns exactly one GDI object, so a caller can
    /// assert an exact <c>+1</c> step in <see cref="Count"/> and an exact return to baseline
    /// once the returned handle is disposed.
    /// </summary>
    /// <returns>A handle that releases the device context when disposed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the device context cannot be created.</exception>
    internal static IDisposable CreateMemoryDc()
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);

        if (dc == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateCompatibleDC failed with Win32 error {Marshal.GetLastPInvokeError()}.");
        }

        return new MemoryDc(dc);
    }

    private sealed class MemoryDc : IDisposable
    {
        private IntPtr _handle;

        internal MemoryDc(IntPtr handle) => _handle = handle;

        public void Dispose()
        {
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;

            if (handle != IntPtr.Zero && !DeleteDC(handle))
            {
                throw new InvalidOperationException(
                    $"DeleteDC failed with Win32 error {Marshal.GetLastPInvokeError()}; the GDI count is now unreliable.");
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);
}
