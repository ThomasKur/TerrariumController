using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Data;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class LoggingServiceTests
{
    // -------------------------------------------------------------------------
    // LogRelayStateChangeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LogRelayStateChangeAsync_CreatesEntryWithCorrectType()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        await service.LogRelayStateChangeAsync(2, true, "Temperature Threshold");

        var entry = await context.LogEntries.SingleAsync();
        Assert.Equal("StateChange", entry.LogType);
        Assert.Equal(2, entry.RelayId);
        Assert.True(entry.RelayState);
        Assert.Contains("Relay 2", entry.Details);
        Assert.Contains("ON", entry.Details);
    }

    [Fact]
    public async Task LogRelayStateChangeAsync_StoresSensor1Values()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        await service.LogRelayStateChangeAsync(1, true, "Temperature Threshold", sensorId: 1, temperature: 27.5, humidity: 65.0);

        var entry = await context.LogEntries.SingleAsync();
        Assert.Equal(27.5, entry.Sensor1Temperature);
        Assert.Equal(65.0, entry.Sensor1Humidity);
        Assert.Null(entry.Sensor2Temperature);
        Assert.Null(entry.Sensor3Temperature);
    }

    [Fact]
    public async Task LogRelayStateChangeAsync_StoresSensor2And3Values()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        await service.LogRelayStateChangeAsync(2, false, "test", sensorId: 2, temperature: 30.0, humidity: 55.0);
        await service.LogRelayStateChangeAsync(3, true, "test", sensorId: 3, temperature: 31.0, humidity: 60.0);

        var entries = await context.LogEntries.OrderBy(e => e.RelayId).ToListAsync();
        Assert.Equal(30.0, entries[0].Sensor2Temperature);
        Assert.Equal(31.0, entries[1].Sensor3Temperature);
    }

    // -------------------------------------------------------------------------
    // LogHourlySnapshotAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LogHourlySnapshotAsync_CreatesSnapshotEntryWithSensorData()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        var readings = new Dictionary<int, (double? temp, double? humidity)>
        {
            [1] = (26.0, 70.0),
            [2] = (28.5, 60.0),
            [3] = (29.0, 55.0)
        };

        await service.LogHourlySnapshotAsync(readings);

        var entry = await context.LogEntries.SingleAsync();
        Assert.Equal("HourlySnapshot", entry.LogType);
        Assert.Equal(26.0, entry.Sensor1Temperature);
        Assert.Equal(70.0, entry.Sensor1Humidity);
        Assert.Equal(28.5, entry.Sensor2Temperature);
        Assert.Equal(29.0, entry.Sensor3Temperature);
    }

    // -------------------------------------------------------------------------
    // PruneOldEntriesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PruneOldEntriesAsync_RemovesEntriesOlderThanRetentionPeriod()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 3);

        // Old entry: 4 months ago (outside retention)
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddMonths(-4),
            LogType = "StateChange",
            Details = "old"
        });
        // Recent entry: 1 month ago (inside retention)
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddMonths(-1),
            LogType = "StateChange",
            Details = "recent"
        });
        await context.SaveChangesAsync();

        await service.PruneOldEntriesAsync();

        var remaining = await context.LogEntries.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("recent", remaining[0].Details);
    }

    [Fact]
    public async Task PruneOldEntriesAsync_KeepsAllEntriesWithinRetentionWindow()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        for (int i = 0; i < 5; i++)
        {
            context.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow.AddMonths(-i),
                LogType = "StateChange",
                Details = $"entry {i}"
            });
        }
        await context.SaveChangesAsync();

        await service.PruneOldEntriesAsync();

        Assert.Equal(5, await context.LogEntries.CountAsync());
    }

    // -------------------------------------------------------------------------
    // GetLogEntriesAsync / GetLogCountAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLogEntriesAsync_ReturnsPaginatedResults()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        for (int i = 0; i < 5; i++)
        {
            context.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                LogType = "StateChange",
                Details = $"entry {i}"
            });
        }
        await context.SaveChangesAsync();

        var page1 = await service.GetLogEntriesAsync(1, 3);
        var page2 = await service.GetLogEntriesAsync(2, 3);

        Assert.Equal(3, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [Fact]
    public async Task GetLogCountAsync_ReturnsCorrectTotal()
    {
        await using var context = CreateContext();
        var service = CreateService(context, retentionMonths: 12);

        for (int i = 0; i < 7; i++)
        {
            context.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                LogType = "StateChange",
                Details = $"entry {i}"
            });
        }
        await context.SaveChangesAsync();

        var count = await service.GetLogCountAsync();

        Assert.Equal(7, count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static LoggingService CreateService(AppDbContext context, int retentionMonths)
    {
        var settings = new Settings { LogRetentionMonths = retentionMonths };
        var settingsService = new TestSettingsService(settings);
        return new LoggingService(context, NullLogger<LoggingService>.Instance, settingsService);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
