using System.Runtime.InteropServices;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves the last-error discipline of the real <see cref="ShellApi"/>: the code reported through
/// <see cref="IShellApi.GetLastError"/> belongs to the seam call that failed, not to whatever the
/// thread's last-error slot happens to hold when it is read.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between a useful error and a misleading one. Win32's last error is
/// thread state that any other P/Invoke can overwrite, so an implementation that read the slot
/// inside <c>GetLastError()</c> would report the error of whatever call happened to run last -
/// typically a successful one inside the error handler - and every <c>TrayIconException</c> would
/// carry a wrong code. <see cref="ShellApi"/> captures the value inside the same member that made
/// the call, and these tests hold that in place.
/// </para>
/// <para>
/// <b>No icon is registered by these tests.</b> <c>NIM_ADD</c> is called with a null window, which
/// cannot succeed and cannot put anything in a notification area; the released handles are null,
/// so there is nothing to free. The remaining real-signature assertions (including a successful
/// icon create/destroy round trip) belong to T09's <c>RealShellApiSignatureProbeTests</c>, which
/// is written against the delivered seam.
/// </para>
/// </remarks>
public sealed class ShellApiLastErrorTests
{
    /// <summary>
    /// First failure supplies the error, then the thread's last-error slot is clobbered and
    /// another P/Invoke runs; the seam must still report the failure's code.
    /// </summary>
    [Fact]
    public void Captured_error_survives_intervening_pinvoke_calls_and_thread_slot_changes()
    {
        var api = new ShellApi();
        var data = NOTIFYICONDATAW.Create(IntPtr.Zero, 1);

        // Measured on this machine: the shell returns FALSE and reports E_FAIL (0x80004005) for
        // an add against a null window, so this is a real failure with a real error code.
        Assert.False(api.ShellNotifyIcon(ShellConstants.NIM_ADD, ref data));

        int captured = api.GetLastError();
        Assert.NotEqual(0, captured);

        // Clobber the thread slot, run unrelated P/Invokes (the counter clears the slot and then
        // calls GetGuiResources/GetCurrentProcess), then clobber it once more.
        Marshal.SetLastPInvokeError(0);
        _ = GdiHandles.Count();
        Marshal.SetLastPInvokeError(0);

        // The thread slot no longer holds the failure's code...
        Assert.Equal(0, Marshal.GetLastPInvokeError());

        // ...but the seam still reports the code of the call that failed.
        Assert.Equal(captured, api.GetLastError());
    }

    /// <summary>
    /// The handle-releasing members resolve their real entry points and return the failure value
    /// the contract promises for an invalid handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A null handle is not destroyable, so both calls return <see langword="false"/>. This also
    /// exercises the <c>DestroyIcon</c> and <c>DeleteObject</c> declarations against the real
    /// DLLs: a wrong entry point name would surface here as <c>EntryPointNotFoundException</c>
    /// rather than as a subtle misbehaviour in a consumer's process.
    /// </para>
    /// <para>
    /// <b>Do not assume a non-zero error code accompanies every failure</b> - measured here,
    /// <c>DestroyIcon(IntPtr.Zero)</c> reported 1402 while <c>DeleteObject(IntPtr.Zero)</c>
    /// returned <see langword="false"/> with a last-error of 0. A caller that must decide between
    /// "failed, here is why" and "failed, the OS did not say why" has to treat 0 as possible;
    /// the code passed into <c>TrayIconException</c> is therefore allowed to be 0.
    /// </para>
    /// </remarks>
    [Fact]
    public void Failed_handle_release_calls_return_false_through_the_seam()
    {
        var api = new ShellApi();

        Assert.False(api.DestroyIcon(IntPtr.Zero));
        Assert.False(api.DeleteObject(IntPtr.Zero));
    }
}
