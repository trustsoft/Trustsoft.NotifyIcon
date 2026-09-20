using System.Windows.Controls;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves the STA test harness and the GDI handle counter both work before anything in
/// later tasks depends on them.
/// </summary>
public sealed class PlaceholderTests
{
    /// <summary>
    /// A plain <c>[Fact]</c> that touches WPF types throws
    /// <c>InvalidOperationException: The calling thread must be STA</c>; this test fails
    /// loudly if the <c>[StaFact]</c> harness ever stops putting the body on an STA thread.
    /// </summary>
    [StaFact]
    public void StaThread_CanConstructWpfControl()
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        Assert.NotNull(new Canvas());
    }

    /// <summary>
    /// Proves the GDI counter responds exactly to allocation and release, and that the
    /// <c>[StaTheory]</c> harness keeps data-row tests on an STA thread.
    /// </summary>
    /// <param name="gdiObjectsToCreate">Number of memory DCs to hold open while measuring.</param>
    [StaTheory]
    [InlineData(1)]
    [InlineData(4)]
    public void GdiHandles_Count_TracksGdiObjectLifetimeExactly(int gdiObjectsToCreate)
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        int baseline = GdiHandles.Count();
        var held = new List<IDisposable>();

        try
        {
            for (int i = 0; i < gdiObjectsToCreate; i++)
            {
                held.Add(GdiHandles.CreateMemoryDc());
            }

            Assert.Equal(baseline + gdiObjectsToCreate, GdiHandles.Count());

            foreach (IDisposable handle in held)
            {
                handle.Dispose();
            }

            held.Clear();

            Assert.Equal(baseline, GdiHandles.Count());
        }
        finally
        {
            foreach (IDisposable handle in held)
            {
                handle.Dispose();
            }
        }
    }
}
