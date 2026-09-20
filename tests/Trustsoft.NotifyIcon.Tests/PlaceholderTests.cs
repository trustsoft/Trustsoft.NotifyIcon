using System.Windows.Controls;
using Xunit;

namespace Trustsoft.NotifyIcon.Tests;

/// <summary>
/// Proves the STA test harness and the GDI handle counter both work before anything in
/// later tasks depends on them.
/// </summary>
public sealed class PlaceholderTests
{
    [StaFact]
    public void StaThread_CanConstructWpfControl()
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        var canvas = new Canvas();
        Assert.NotNull(canvas);
    }

    [StaFact]
    public void GdiHandles_Count_ReturnsPositiveNumber()
    {
        int count = GdiHandles.Count();
        Assert.True(count > 0, $"expected a positive GDI handle count, got {count}");
    }
}
