using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Data;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class HumidityServiceTests
{
    [Fact]
    public async Task CheckAndApplyHumidityLockoutAsync_TriggersPulseAndLocksWhenBelowThreshold()
    {
        await using var context = CreateContext();
        var relayCoordinator = new RecordingRelayCommandCoordinator();
        var settingsService = new TestSettingsService(new Settings
        {
            Sensor1HumidityThreshold = 60.0,
            HumidityLockoutHours = 6
        });
        var service = new HumidityService(
            context,
            relayCoordinator,
            settingsService,
            new NoOpLoggingService(),
            NullLogger<HumidityService>.Instance);

        await service.CheckAndApplyHumidityLockoutAsync(1, 50.0);

        Assert.Equal(2, relayCoordinator.Calls.Count);
        Assert.Equal((5, true, "Humidity Threshold"), relayCoordinator.Calls[0]);
        Assert.Equal((5, false, "Humidity Pulse Complete"), relayCoordinator.Calls[1]);

        var state = await context.HumidityLockoutStates.FirstOrDefaultAsync(s => s.SensorId == 1);
        Assert.NotNull(state);
        Assert.True(state!.IsLocked);
        Assert.True(state.LockExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task CheckAndApplyHumidityLockoutAsync_DoesNotTriggerDuringActiveLockout()
    {
        await using var context = CreateContext();
        context.HumidityLockoutStates.Add(new HumidityLockoutState
        {
            SensorId = 1,
            IsLocked = true,
            LockExpiresAt = DateTime.UtcNow.AddHours(2)
        });
        await context.SaveChangesAsync();

        var relayCoordinator = new RecordingRelayCommandCoordinator();
        var settingsService = new TestSettingsService(new Settings
        {
            Sensor1HumidityThreshold = 60.0,
            HumidityLockoutHours = 6
        });
        var service = new HumidityService(
            context,
            relayCoordinator,
            settingsService,
            new NoOpLoggingService(),
            NullLogger<HumidityService>.Instance);

        await service.CheckAndApplyHumidityLockoutAsync(1, 40.0);

        Assert.Empty(relayCoordinator.Calls);
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
