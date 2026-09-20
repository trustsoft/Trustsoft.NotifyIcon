using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trustsoft.NotifyIcon.Interop;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves R015: a property set from a background thread is marshalled to the UI thread instead of
/// throwing, and the only refusal is an instance that has no usable dispatcher - which raises
/// <see cref="InvalidOperationException"/>, deliberately not a
/// <see cref="TrayIconException"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every test here needs more than apartment state, so it uses
/// <see cref="DispatcherFactAttribute"/>: the body runs on a thread that owns a running
/// <see cref="Dispatcher"/>, which is what makes <see cref="Dispatcher.CheckAccess"/> meaningful
/// and what lets a background thread's <c>Dispatcher.Invoke</c> be served.
/// </para>
/// <para>
/// <b>What "marshalled" is asserted to mean.</b> Not that no exception was thrown - a swallowed
/// change would satisfy that - but that the seam saw the call <em>on the dispatcher thread</em>.
/// The recorded thread id is the whole evidence: the shell call carries the identity of the thread
/// that made it, and a lifecycle that ran the work inline on the calling background thread would
/// show the background thread's id instead.
/// </para>
/// <para>
/// The background thread is joined only after the dispatcher has been pumped (see
/// <see cref="DispatcherHarness.PumpUntil"/>), because the two would otherwise deadlock: the
/// background thread blocks inside <c>Dispatcher.Invoke</c> until the dispatcher thread processes
/// the posted work, and the dispatcher thread would be blocked in <c>Join</c>.
/// </para>
/// </remarks>
public sealed class TrayIconMarshallingTests
{
    /// <summary>The icon edge length used by these tests.</summary>
    private const int IconSize = 16;

    /// <summary>
    /// An <see cref="TrayIcon.IconSource"/> and a <see cref="TrayIcon.ToolTipText"/> change made on
    /// a background thread must reach the shell on the dispatcher thread.
    /// </summary>
    [DispatcherFact]
    public void Background_thread_IconSource_and_ToolTipText_changes_reach_the_shell_on_the_dispatcher_thread()
    {
        var shell = new FakeShellApi();
        BitmapSource replacement = CreateSolid(IconSize, 0x10, 0x90, 0x40);

        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        trayIcon.Visible = true;

        int dispatcherThread = Environment.CurrentManagedThreadId;
        int modificationsBefore = shell.ShellNotifyIconCalls.Count;

        RunOnBackgroundThread(
            () =>
            {
                trayIcon.ToolTipText = "Set from a worker thread";
                trayIcon.IconSource = replacement;
            },
            out Exception? failure,
            out int backgroundThread);

        Assert.Null(failure);

        // The premise of the test: the two threads really are different threads.
        Assert.NotEqual(dispatcherThread, backgroundThread);

        ShellCall[] marshalled = [.. shell.ShellNotifyIconCalls.Skip(modificationsBefore)];

        Assert.Equal(2, marshalled.Length);
        Assert.All(marshalled, call => Assert.Equal(ShellConstants.NIM_MODIFY, call.Message));
        Assert.All(marshalled, call => Assert.Equal(dispatcherThread, call.ThreadId));

        // The change was applied, not merely tolerated: the shell holds the new icon and tooltip.
        Assert.Equal("Set from a worker thread", shell.ShellNotifyIconDataSnapshots[^1].szTip);
        Assert.NotEqual(IntPtr.Zero, trayIcon.RegisteredIconHandle);
    }

    /// <summary>
    /// <see cref="TrayIcon.Visible"/> set from a background thread registers the icon - host window,
    /// <c>NIM_ADD</c> and <c>NIM_SETVERSION</c> - on the dispatcher thread.
    /// </summary>
    [DispatcherFact]
    public void Background_thread_Visible_set_registers_the_icon_on_the_dispatcher_thread()
    {
        var shell = new FakeShellApi();

        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        int dispatcherThread = Environment.CurrentManagedThreadId;

        RunOnBackgroundThread(() => trayIcon.Visible = true, out Exception? failure, out int backgroundThread);

        Assert.Null(failure);
        Assert.NotEqual(dispatcherThread, backgroundThread);

        Assert.True(trayIcon.IsRegistered);

        Assert.Equal(2, shell.ShellNotifyIconCalls.Count);
        Assert.All(shell.ShellNotifyIconCalls, call => Assert.Equal(dispatcherThread, call.ThreadId));

        // The hidden host window was created on the dispatcher thread too - it could not have been
        // created anywhere else, and this is the evidence that the marshalling carried the whole
        // registration rather than the property value alone.
        Assert.True(Win32.IsWindow(trayIcon.HostHandle));
    }

    /// <summary>
    /// A registration failure raised while the assignment is being served on the dispatcher thread
    /// reaches the background caller as the named <see cref="TrayIconException"/>, operation and
    /// code intact: marshalling must not turn a reportable startup failure into a WPF threading
    /// exception or into a swallowed one.
    /// </summary>
    [DispatcherFact]
    public void Background_thread_registration_failure_reaches_the_caller_as_TrayIconException()
    {
        var shell = new FakeShellApi { LastErrorToReport = 5 };

        shell.FailAlways(ShellOperation.ShellNotifyIcon);

        using var trayIcon = new TrayIcon(shell) { IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0) };

        RunOnBackgroundThread(() => trayIcon.Visible = true, out Exception? failure, out _);

        var exception = Assert.IsType<TrayIconException>(failure);

        Assert.Equal(TrayIconException.OperationAdd, exception.Operation);
        Assert.Equal(5, exception.Win32ErrorCode);
        Assert.False(trayIcon.IsRegistered);
    }

    /// <summary>
    /// An instance created without a WPF dispatcher refuses every property change with
    /// <see cref="InvalidOperationException"/>, names the requirement in the message, sends nothing
    /// to the shell and assigns nothing at all - the refusal happens before the assignment, so
    /// every property keeps the value it had.
    /// </summary>
    /// <remarks>
    /// This is the documented carve-out of D008 and the only public exception besides
    /// <see cref="TrayIconException"/>. The null-dispatcher instance is reachable only through the
    /// internal verification constructor: WPF's own <c>Dispatcher</c> property creates a dispatcher
    /// on demand and is therefore never null, which is exactly why the case has to be constructed
    /// deliberately to be tested at all.
    /// </remarks>
    [StaFact]
    public void Instance_without_dispatcher_raises_InvalidOperationException()
    {
        var shell = new FakeShellApi();

        using var trayIcon = new TrayIcon(shell, dispatcher: null);

        var visible = Assert.Throws<InvalidOperationException>(() => trayIcon.Visible = true);

        Assert.Contains("Dispatcher", visible.Message, StringComparison.Ordinal);
        Assert.IsNotType<TrayIconException>(visible);

        Assert.Throws<InvalidOperationException>(() => trayIcon.ToolTipText = "tip");
        Assert.Throws<InvalidOperationException>(() => trayIcon.IconSource = CreateSolid(IconSize, 0x20, 0x60, 0xA0));

        // Nothing reached the shell, and every property still describes the state that was reached.
        Assert.Empty(shell.Calls);
        Assert.False(trayIcon.Visible);
        Assert.Equal(string.Empty, trayIcon.ToolTipText);
        Assert.Null(trayIcon.IconSource);
    }

    /// <summary>
    /// A direct <c>SetValue</c> - the path markup and bindings use - is refused by this library too
    /// when the instance has no usable dispatcher, and the value is put back so the property cannot
    /// claim a state that was never reached.
    /// </summary>
    /// <remarks>
    /// The public property accessors marshal and therefore never reach the change callbacks on an
    /// unusable instance; this test covers the other entry point, where WPF has already stored the
    /// value before the callback runs. The revert is the whole point: without it the requested
    /// value would survive a change that was refused.
    /// </remarks>
    [StaFact]
    public void Direct_SetValue_on_an_instance_without_dispatcher_is_refused_and_reverted()
    {
        var shell = new FakeShellApi();

        using var trayIcon = new TrayIcon(shell, dispatcher: null);

        Assert.Throws<InvalidOperationException>(
            () => trayIcon.SetValue(TrayIcon.VisibleProperty, true));

        // Read through GetValue: the CLR accessor is not the interesting part here, the stored
        // value is, and it must be the value that was there before the refused change.
        Assert.False((bool)trayIcon.GetValue(TrayIcon.VisibleProperty));
        Assert.Empty(shell.Calls);
    }

    /// <summary>
    /// A dispatcher that has been shut down cannot carry a change any more, so the same refusal
    /// applies - with the message naming the shutdown rather than a missing dispatcher.
    /// </summary>
    [StaFact]
    public void Shut_down_dispatcher_raises_InvalidOperationException()
    {
        var shell = new FakeShellApi();
        TrayIcon? trayIcon = null;
        Dispatcher? owning = null;
        using var created = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            owning = Dispatcher.CurrentDispatcher;
            trayIcon = new TrayIcon(shell, owning);
            created.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(created.Wait(TimeSpan.FromSeconds(10)), "The dispatcher thread did not create the TrayIcon in time.");

        Dispatcher dispatcher = Assert.IsType<Dispatcher>(owning);

        dispatcher.InvokeShutdown();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The dispatcher thread did not exit after InvokeShutdown.");
        Assert.True(dispatcher.HasShutdownFinished);

        var exception = Assert.Throws<InvalidOperationException>(() => trayIcon!.Visible = true);

        Assert.Contains("shut down", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<TrayIconException>(exception);
        Assert.Empty(shell.Calls);

        // The value cannot even be *read* from this thread - DependencyObject.GetValue verifies
        // thread access as well - so what is assertable from here is that this library refused the
        // assignment and that nothing reached the shell. The read is asserted too, because it is
        // the reason the marshalling has to live in the setter: there is no cross-thread API on a
        // DependencyObject at all, so the property accessor is the only place it can live.
        Assert.Throws<InvalidOperationException>(() => _ = trayIcon!.Visible);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a fresh MTA background thread while pumping the dispatcher
    /// until that thread has finished.
    /// </summary>
    /// <param name="action">The work to run on the background thread.</param>
    /// <param name="failure">The exception the work raised, if any.</param>
    /// <param name="threadId">The background thread's managed id, as observed by the work.</param>
    /// <exception cref="TimeoutException">
    /// The background thread never finished - which is what a broken marshalling path looks like
    /// from here, and is reported as a failure rather than as a hanging test run.
    /// </exception>
    private static void RunOnBackgroundThread(Action action, out Exception? failure, out int threadId)
    {
        Exception? captured = null;
        int id = 0;

        var thread = new Thread(() =>
        {
            id = Environment.CurrentManagedThreadId;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        // Pump until the background thread has returned: its Dispatcher.Invoke can only complete
        // while this thread is processing the queue.
        DispatcherHarness.PumpUntil(() => !thread.IsAlive);
        thread.Join(TimeSpan.FromSeconds(10));

        failure = captured;
        threadId = id;
    }

    /// <summary>
    /// Creates a solid straight-alpha <c>Bgra32</c> bitmap of the given size.
    /// </summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <param name="b">The blue value.</param>
    /// <param name="g">The green value.</param>
    /// <param name="r">The red value.</param>
    /// <returns>A bitmap source usable as an icon source.</returns>
    private static BitmapSource CreateSolid(int size, byte b, byte g, byte r)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }
}
