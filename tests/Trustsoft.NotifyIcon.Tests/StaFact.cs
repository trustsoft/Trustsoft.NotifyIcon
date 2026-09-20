using System.Diagnostics;
using System.Windows.Threading;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Marks a test whose body needs a live WPF <see cref="Dispatcher"/> on the thread that runs it -
/// the tray lifecycle creates a real <c>HwndSource</c> host window, and the marshalling contract
/// (R015) is defined in terms of <see cref="Dispatcher.CheckAccess"/>.
/// </summary>
/// <remarks>
/// <para>
/// It exists alongside the plain <see cref="StaFactAttribute"/> used elsewhere in this project,
/// which grants apartment state and nothing more. A test that only needs STA keeps using
/// <c>[StaFact]</c>; a test that sets a property from a background thread and expects the change
/// to be driven on the UI thread needs a dispatcher that is actually running, and says so with
/// this attribute instead of hoping the two are the same thing.
/// </para>
/// <para>
/// The implementation is the WPF flavour of the STA test harness already referenced by this
/// project's test csproj (<c>Xunit.StaFact</c>): it runs the test body on an STA thread that owns
/// a dispatcher and tears that dispatcher down afterwards, which is exactly the environment the
/// element is designed for. Deriving from it rather than re-implementing a test-case discoverer
/// keeps the discovery/running plumbing in one vetted place.
/// </para>
/// <para>
/// <b>Message pumping is explicit.</b> A test that hands work to a background thread and waits for
/// the resulting <c>Dispatcher.Invoke</c> to come back must pump the queue while it waits,
/// otherwise the dispatcher thread blocks on the background thread and the background thread
/// blocks on the dispatcher. <see cref="DispatcherHarness.PumpUntil"/> is that pump, and it is
/// deliberately part of the harness rather than hidden inside the attribute: what is being tested
/// is that the library marshals, and a test that did not pump would deadlock instead of failing,
/// which is the least useful outcome.
/// </para>
/// </remarks>
public sealed class DispatcherFactAttribute : WpfFactAttribute
{
}

/// <summary>
/// Dispatcher helpers shared by the tests that exercise cross-thread marshalling.
/// </summary>
internal static class DispatcherHarness
{
    /// <summary>
    /// Pumps the current thread's dispatcher queue until <paramref name="condition"/> holds.
    /// </summary>
    /// <param name="condition">The condition to wait for; evaluated after every pump tick.</param>
    /// <param name="timeout">How long to pump before giving up; defaults to ten seconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">
    /// The condition never held within the timeout. Raising it here means a broken marshalling path
    /// fails the test with a clear message instead of hanging the run.
    /// </exception>
    /// <remarks>
    /// A <see cref="DispatcherTimer"/> at <see cref="DispatcherPriority.Background"/> polls the
    /// condition while <see cref="Dispatcher.PushFrame"/> processes the queue: work posted by
    /// another thread runs at normal priority first, so the pump observes it promptly without
    /// busy-waiting on the CPU.
    /// </remarks>
    internal static void PumpUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(10);
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(5),
        };

        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed > limit)
            {
                frame.Continue = false;
            }
        };

        try
        {
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }

        if (!condition())
        {
            throw new TimeoutException(
                $"The dispatcher was pumped for {stopwatch.Elapsed.TotalSeconds:F1}s but the condition was never met.");
        }
    }
}
