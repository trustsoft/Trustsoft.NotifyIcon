using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// The negative half of the S01 lifecycle: what must <em>not</em> happen when the shell refuses,
/// when a replacement fails, when a tooltip is longer than the field, and when the consumer owns no
/// window at all.
/// </summary>
/// <remarks>
/// <para>
/// These tests are deliberately separate from <see cref="TrayIconLifecycleTests"/> so they are
/// written against the delivered code rather than alongside it. They exist because the positive
/// tests cannot see the difference between "the update applied" and "the update failed but the
/// instance kept going as if it had applied": R007's second half is a claim about a state that must
/// be <em>preserved</em>, and R013's runtime policy is a claim about an exception that must
/// <em>not</em> be thrown.
/// </para>
/// <para>
/// <b>Requirements proven here:</b> R007 (a forced failed replacement leaves the previous
/// <c>HICON</c> registered and destroys the refused replacement instead of leaking it), R013 (a
/// startup failure throws a named <see cref="TrayIconException"/> with the operation and the code
/// and leaves no half-registration, while a runtime failure retries exactly once, raises
/// <see cref="TrayIcon.TrayError"/> and does not throw), and the property-truthfulness half of
/// D008 (<see cref="TrayIcon.Visible"/> never claims a registration the shell refused).
/// </para>
/// <para>
/// Every test runs on an STA thread (<see cref="StaFactAttribute"/>) because a successful
/// registration creates a real hidden <c>HwndSource</c> host window; only the shell calls come from
/// <see cref="FakeShellApi"/>.
/// </para>
/// </remarks>
public sealed class TrayIconNegativeTests
{
    /// <summary>The icon edge length used by these tests.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// A forced failed replacement must leave the previously registered icon exactly as it was, and
    /// must not leak the handle built for the refused replacement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the R007 requirement stated negatively, and it catches the two mirror-image mistakes
    /// at once. Destroying the previous handle on a <em>failed</em> replacement empties the
    /// notification area even though the shell kept the old icon; destroying nothing on a failed
    /// replacement leaks one <c>HICON</c> per failed change, which in a long-running tray process is
    /// a slow resource exhaustion with no symptom until it fails.
    /// </para>
    /// <para>
    /// <see cref="FakeShellApi.DestroyedIconHandles"/> rather than a counter is the assertion: the
    /// claim is about <em>which</em> handle survived, and a count cannot express that.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Failed_icon_replacement_preserves_the_previous_valid_icon()
    {
        const int forcedError = 87;

        var shell = new FakeShellApi { LastErrorToReport = forcedError };
        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;

        var raised = new List<TrayErrorEventArgs>();
        trayIcon.TrayError += (_, e) => raised.Add(e);

        IntPtr previous = trayIcon.RegisteredIconHandle;

        Assert.NotEqual(IntPtr.Zero, previous);

        int notifyCallsBefore = shell.ShellNotifyIconCalls.Count;
        int destroysBefore = shell.DestroyedIcons;

        // The shell refuses the replacement on every attempt, including the retry.
        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        trayIcon.IconSource = CreateSolid(0x90, 0x30, 0x30);

        shell.StopFailingAlways(ShellOperation.ShellNotifyIcon);

        // No exception escaped: a failed runtime update is reported, not thrown (R013).
        TrayErrorEventArgs error = Assert.Single(raised);

        Assert.Equal(TrayIconException.OperationModify, error.Operation);
        Assert.Equal(forcedError, error.Win32ErrorCode);
        Assert.True(error.Retried);
        Assert.IsType<TrayIconException>(error.Exception);

        // Exactly one call plus one retry, both the same operation with the same refused payload.
        Assert.Equal(2, shell.ShellNotifyIconCalls.Count - notifyCallsBefore);
        Assert.All(
            shell.ShellNotifyIconCalls.Skip(notifyCallsBefore),
            call => Assert.Equal(ShellConstants.NIM_MODIFY, call.Message));

        // The previously valid icon is still the registered one, and its handle was never released.
        Assert.Equal(previous, trayIcon.RegisteredIconHandle);
        Assert.DoesNotContain(previous, shell.DestroyedIconHandles);

        // The handle built for the refused replacement was released instead, so the failure leaks
        // nothing - the assertion that catches "clean up only on success".
        IntPtr replacement = shell.CreatedIconHandles[^1];

        Assert.NotEqual(previous, replacement);
        Assert.Contains(replacement, shell.DestroyedIconHandles);
        Assert.Equal(destroysBefore + 1, shell.DestroyedIcons);

        // Exactly one HICON is alive, and it is the still-registered previous handle: the failed
        // replacement neither leaked its own handle nor released the survivor.
        Assert.Equal(1, shell.OutstandingIcons);
        Assert.DoesNotContain(trayIcon.RegisteredIconHandle, shell.DestroyedIconHandles);

        // The instance is still usable: a later, honest removal works and releases the survivor.
        trayIcon.Visible = false;

        Assert.Contains(previous, shell.DestroyedIconHandles);
        Assert.Equal(0, shell.OutstandingIcons);
    }

    /// <summary>
    /// A refused registration throws a named exception and leaves the instance reporting the truth:
    /// not registered, not visible, no retained handle.
    /// </summary>
    /// <remarks>
    /// The failure value check (<see cref="TrayIcon.Visible"/> reverting to
    /// <see langword="false"/>) is what makes the property retryable and honest: a property that
    /// stayed <see langword="true"/> after a refused add would claim an icon that is not there, and
    /// a later assignment of <see langword="true"/> would be a no-op because the value never
    /// changed.
    /// </remarks>
    [StaFact]
    public void Visible_is_not_true_when_registration_failed()
    {
        const int forcedError = 5;

        var shell = new FakeShellApi { LastErrorToReport = forcedError };
        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(0x20, 0x60, 0xA0) };

        try
        {
            TrayIconException ex = Assert.Throws<TrayIconException>(() => trayIcon.Visible = true);

            Assert.Equal(TrayIconException.OperationAdd, ex.Operation);
            Assert.Equal(forcedError, ex.Win32ErrorCode);

            Assert.False(trayIcon.Visible);
            Assert.False(trayIcon.IsRegistered);
            Assert.Equal(IntPtr.Zero, trayIcon.RegisteredIconHandle);

            // The icon converted before the refused add was destroyed, so nothing is retained.
            Assert.Equal(1, shell.CreatedIcons);
            Assert.Equal(0, shell.OutstandingIcons);
        }
        finally
        {
            trayIcon.Dispose();
        }
    }

    /// <summary>
    /// After a refused registration, disposal is safe and does nothing twice.
    /// </summary>
    /// <remarks>
    /// Disposal is the path an application's exit handler takes regardless of what happened during
    /// startup, so it must tolerate a state where the icon was never registered: no
    /// <c>NIM_DELETE</c> for a registration that does not exist, no destroy of a handle that was
    /// already released, and no exception out of an exit path.
    /// </remarks>
    [StaFact]
    public void Dispose_after_a_failed_registration_does_not_throw_and_does_not_double_destroy()
    {
        const int forcedError = 5;

        var shell = new FakeShellApi { LastErrorToReport = forcedError };
        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(0x20, 0x60, 0xA0) };

        Assert.Throws<TrayIconException>(() => trayIcon.Visible = true);

        int destroysBefore = shell.DestroyedIcons;
        int notifyCallsBefore = shell.ShellNotifyIconCalls.Count;
        IntPtr hostBefore = trayIcon.HostHandle;

        Assert.NotEqual(IntPtr.Zero, hostBefore);

        trayIcon.Dispose();

        // Idempotence: the second call must be a no-op, not a second teardown.
        trayIcon.Dispose();

        Assert.Equal(destroysBefore, shell.DestroyedIcons);
        Assert.Equal(notifyCallsBefore, shell.ShellNotifyIconCalls.Count);
        Assert.Equal(0, shell.OutstandingIcons);
        Assert.Equal(IntPtr.Zero, trayIcon.HostHandle);
        Assert.False(trayIcon.IsRegistered);
        Assert.False(Win32.IsWindow(hostBefore));
    }

    /// <summary>
    /// Clearing <see cref="TrayIcon.IconSource"/> leaves the registered icon untouched, and a real
    /// replacement releases exactly the previous handle - never a null handle, never the incoming
    /// handle, and never the same handle twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deviation from the T09 plan text, recorded deliberately.</b> The plan described this case
    /// as "clearing <c>IconSource</c> issues a NIM_MODIFY without NIF_ICON (or with a zero hIcon)
    /// and destroys exactly the previously retained HICON". The delivered API documents and
    /// implements the opposite: assigning <see langword="null"/> is defined as "no change" because
    /// there is no shell operation that means "keep the registration but drop the image", and a
    /// modify with a null <c>hIcon</c> is not that operation. The assertions below therefore pin
    /// the delivered, documented contract while still proving the plan's actual concern: no null
    /// handle destroyed, no handle destroyed twice, and no wrong handle released.
    /// </para>
    /// </remarks>
    [StaFact]
    public void Setting_IconSource_to_null_leaves_the_registered_icon_untouched_and_destroys_nothing()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(0x20, 0x60, 0xA0),
            ToolTipText = "supported",
        };

        trayIcon.Visible = true;

        IntPtr previous = trayIcon.RegisteredIconHandle;
        int notifyCallsBefore = shell.ShellNotifyIconCalls.Count;
        int destroysBefore = shell.DestroyedIcons;

        trayIcon.IconSource = null;

        // Null means "no change": no shell call, the same handle still registered, nothing released.
        Assert.Equal(notifyCallsBefore, shell.ShellNotifyIconCalls.Count);
        Assert.Equal(destroysBefore, shell.DestroyedIcons);
        Assert.Equal(previous, trayIcon.RegisteredIconHandle);
        Assert.True(trayIcon.IsRegistered);

        // A real replacement, by contrast, releases exactly the previous handle - once, and never
        // the null handle and never the incoming one.
        trayIcon.IconSource = CreateSolid(0x10, 0x70, 0x40);

        IntPtr current = trayIcon.RegisteredIconHandle;

        Assert.NotEqual(previous, current);
        Assert.Contains(previous, shell.DestroyedIconHandles);
        Assert.DoesNotContain(current, shell.DestroyedIconHandles);
        Assert.DoesNotContain(IntPtr.Zero, shell.DestroyedIconHandles);
        Assert.Equal(shell.DestroyedIconHandles.Count, shell.DestroyedIconHandles.Distinct().Count());

        // Exactly one HICON is alive - the one the shell is displaying - and it is the current
        // registered handle, not the replaced one and not a null.
        Assert.Equal(1, shell.OutstandingIcons);
        Assert.Equal(current, trayIcon.RegisteredIconHandle);
    }

    /// <summary>
    /// A tooltip longer than the shell's <c>WCHAR szTip[128]</c> field is truncated by the library
    /// on both the add and the modify path, so the shell never receives an unterminated or
    /// silently clipped buffer.
    /// </summary>
    /// <remarks>
    /// The 128 <c>WCHAR</c> field holds at most 127 characters plus the terminator. Without
    /// deliberate truncation the marshaller would cut the string at an arbitrary point, and the
    /// resulting tooltip - or, worse, a string that depends on marshaller behaviour - is not
    /// something a caller could reason about. Asserting on the recorded structure is behavioural
    /// evidence inside the test rather than a grep of the source.
    /// </remarks>
    [StaFact]
    public void ToolTipText_longer_than_the_field_never_produces_an_unterminated_buffer()
    {
        const int fieldCapacityInWchars = 128;

        string initial = new('A', 500);
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(0x20, 0x60, 0xA0),
            ToolTipText = initial,
        };

        trayIcon.Visible = true;

        NOTIFYICONDATAW addData = shell.ShellNotifyIconDataSnapshots[0];

        Assert.True(addData.szTip.Length < fieldCapacityInWchars);
        Assert.Equal(fieldCapacityInWchars - 1, addData.szTip.Length);
        Assert.Equal(initial[..(fieldCapacityInWchars - 1)], addData.szTip);
        Assert.True((addData.uFlags & ShellConstants.NIF_TIP) != 0);
        Assert.True((addData.uFlags & ShellConstants.NIF_SHOWTIP) != 0);

        int notifyCallsBefore = shell.ShellNotifyIconCalls.Count;
        string updated = new('B', 500);

        trayIcon.ToolTipText = updated;

        Assert.Equal(notifyCallsBefore + 1, shell.ShellNotifyIconCalls.Count);
        Assert.Equal(ShellConstants.NIM_MODIFY, shell.ShellNotifyIconCalls[^1].Message);

        NOTIFYICONDATAW modifyData = shell.ShellNotifyIconDataSnapshots[^1];

        Assert.True(modifyData.szTip.Length < fieldCapacityInWchars);
        Assert.Equal(fieldCapacityInWchars - 1, modifyData.szTip.Length);
        Assert.Equal(updated[..(fieldCapacityInWchars - 1)], modifyData.szTip);
        Assert.True((modifyData.uFlags & ShellConstants.NIF_SHOWTIP) != 0);
    }

    /// <summary>
    /// A <see cref="TrayIcon"/> that belongs to no visual or logical tree still raises
    /// <see cref="TrayIcon.TrayError"/>, so a windowless consumer can observe failures (D008).
    /// </summary>
    /// <remarks>
    /// This is the whole premise of the routed event's design: the element is never shown and
    /// normally has no parent, so "the event bubbles" cannot mean "someone up the tree sees it". The
    /// handler is attached to the element itself - the only subscriber that exists in a windowless
    /// application - and the test proves that path end to end.
    /// </remarks>
    [StaFact]
    public void TrayError_without_a_visual_parent_is_still_raised_and_catchable()
    {
        var shell = new FakeShellApi();
        using var trayIcon = new TrayIcon(shell)
        {
            IconSource = CreateSolid(0x20, 0x60, 0xA0),
            ToolTipText = "initial",
        };

        // No window, no visual tree, no parent: exactly the deployment the library exists for.
        Assert.Null(trayIcon.Parent);
        Assert.Null(VisualTreeHelper.GetParent(trayIcon));

        bool handlerRan = false;
        TrayErrorEventArgs? observed = null;

        trayIcon.TrayError += (_, e) =>
        {
            handlerRan = true;
            observed = e;
        };

        trayIcon.Visible = true;

        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        trayIcon.ToolTipText = "changed";

        shell.StopFailingAlways(ShellOperation.ShellNotifyIcon);

        Assert.True(handlerRan);
        Assert.NotNull(observed);
        Assert.Equal(TrayIconException.OperationModify, observed!.Operation);
        Assert.True(observed.Retried);
        Assert.Equal(shell.LastErrorToReport, observed.Win32ErrorCode);
    }

    /// <summary>Builds a square, single-colour, fully opaque image.</summary>
    /// <param name="b">The blue channel value.</param>
    /// <param name="g">The green channel value.</param>
    /// <param name="r">The red channel value.</param>
    /// <returns>The image.</returns>
    private static BitmapSource CreateSolid(byte b, byte g, byte r)
    {
        var pixels = new byte[IconSize * IconSize * 4];

        for (int i = 0; i < IconSize * IconSize; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(IconSize, IconSize, 96, 96, PixelFormats.Bgra32, null, pixels, IconSize * 4);
    }
}
