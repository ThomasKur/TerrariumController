using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Data;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class RelayServiceTests
{
    [Fact]
    public async Task ShouldRelayBeOnAsync_TurnsOnWhenBelowThreshold()
    {
        await using var context = CreateContext();
        var settingsService = new TestSettingsService(new Settings
        {
            Threshold1Temperature = 29.0,
            TemperatureHysteresis = 1.0
        });
        var relayService = CreateRelayService(context, settingsService);

        var shouldBeOn = await relayService.ShouldRelayBeOnAsync(1, 28.5, null);

        Assert.True(shouldBeOn);
    }

    [Fact]
    public async Task ShouldRelayBeOnAsync_StaysOnWithinHysteresisBand()
    {
        await using var context = CreateContext();
        var settingsService = new TestSettingsService(new Settings
        {
            Threshold1Temperature = 29.0,
            TemperatureHysteresis = 1.0
        });
        var relayService = CreateRelayService(context, settingsService);
        await relayService.SetRelayStateAsync(1, true, "test");

        var shouldBeOn = await relayService.ShouldRelayBeOnAsync(1, 29.8, null);

        Assert.True(shouldBeOn);
    }

    [Fact]
    public async Task ShouldRelayBeOnAsync_TurnsOffAtThresholdPlusHysteresis()
    {
        await using var context = CreateContext();
        var settingsService = new TestSettingsService(new Settings
        {
            Threshold1Temperature = 29.0,
            TemperatureHysteresis = 1.0
        });
        var relayService = CreateRelayService(context, settingsService);
        await relayService.SetRelayStateAsync(1, true, "test");

        var shouldBeOn = await relayService.ShouldRelayBeOnAsync(1, 30.0, null);

        Assert.False(shouldBeOn);
    }

    private static RelayService CreateRelayService(AppDbContext context, ISettingsService settingsService)
    {
        return new RelayService(
            context,
            NullLogger<RelayService>.Instance,
            settingsService,
            new NoOpLoggingService());
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
