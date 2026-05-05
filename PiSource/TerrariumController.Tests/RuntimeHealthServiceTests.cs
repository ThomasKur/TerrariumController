using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class RuntimeHealthServiceTests
{
    [Fact]
    public void IsReady_FalseByDefault()
    {
        var service = new RuntimeHealthService();
        Assert.False(service.GetSnapshot().IsReady);
    }

    [Fact]
    public void IsReady_FalseWhenOnlyDbAndGpioReady()
    {
        var service = new RuntimeHealthService();
        service.MarkDatabaseReady();
        service.MarkGpioReady();

        Assert.False(service.GetSnapshot().IsReady);
    }

    [Fact]
    public void IsReady_FalseWhenLoopStartedButNoCycleCompleted()
    {
        var service = new RuntimeHealthService();
        service.MarkDatabaseReady();
        service.MarkGpioReady();
        service.MarkControlLoopStarted();

        Assert.False(service.GetSnapshot().IsReady);
    }

    [Fact]
    public void IsReady_TrueAfterAllMarkersSet()
    {
        var service = new RuntimeHealthService();
        service.MarkDatabaseReady();
        service.MarkGpioReady();
        service.MarkControlLoopStarted();
        service.MarkSuccessfulCycle(DateTime.UtcNow);

        var snapshot = service.GetSnapshot();
        Assert.True(snapshot.IsReady);
        Assert.Equal("ok", snapshot.LastCycleStatus);
    }

    [Fact]
    public void MarkCycleFailure_RecordsStatusButDoesNotFlipIsReady()
    {
        var service = new RuntimeHealthService();
        service.MarkDatabaseReady();
        service.MarkGpioReady();
        service.MarkControlLoopStarted();
        service.MarkSuccessfulCycle(DateTime.UtcNow);

        service.MarkCycleFailure("control-loop-error");

        var snapshot = service.GetSnapshot();
        // IsReady still true because LastSuccessfulCycleUtc still has a value
        Assert.True(snapshot.IsReady);
        Assert.Equal("control-loop-error", snapshot.LastCycleStatus);
    }

    [Fact]
    public void MarkControlLoopStarted_SetsStatusToLoopStarted_WhenPreviouslyStarting()
    {
        var service = new RuntimeHealthService();
        service.MarkControlLoopStarted();

        Assert.Equal("loop-started", service.GetSnapshot().LastCycleStatus);
    }

    [Fact]
    public void GetSnapshot_ReturnsCurrentUtcTimestamp()
    {
        var before = DateTime.UtcNow;
        var service = new RuntimeHealthService();
        var snapshot = service.GetSnapshot();
        var after = DateTime.UtcNow;

        Assert.InRange(snapshot.SnapshotUtc, before, after);
    }

    [Fact]
    public void LastSuccessfulCycleUtc_ReflectsPassedTimestamp()
    {
        var cycleTime = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var service = new RuntimeHealthService();
        service.MarkDatabaseReady();
        service.MarkGpioReady();
        service.MarkControlLoopStarted();
        service.MarkSuccessfulCycle(cycleTime);

        Assert.Equal(cycleTime, service.GetSnapshot().LastSuccessfulCycleUtc);
    }
}
