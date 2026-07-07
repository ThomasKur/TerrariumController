using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Data;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class SettingsServiceTests
{
    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateSettingsAsync_PersistsValidSettings()
    {
        await using var context = CreateContext();
        var signal = new ControlLoopSignal();
        var service = CreateService(context, signal);

        var settings = await service.GetSettingsAsync();
        settings.Threshold1Temperature = 27.0;
        await service.UpdateSettingsAsync(settings);

        var persisted = await context.Settings.FirstAsync();
        Assert.Equal(27.0, persisted.Threshold1Temperature);
    }

    [Fact]
    public async Task UpdateSettingsAsync_SignalsControlLoop()
    {
        await using var context = CreateContext();
        var signal = new ControlLoopSignal();
        var service = CreateService(context, signal);

        var settings = await service.GetSettingsAsync();
        await service.UpdateSettingsAsync(settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var reason = await signal.WaitForSignalAsync(cts.Token);
        Assert.Equal("Settings updated", reason);
    }

    // -------------------------------------------------------------------------
    // Validation: hysteresis
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(5.5)]
    public async Task UpdateSettingsAsync_ThrowsOnInvalidHysteresis(double hysteresis)
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.TemperatureHysteresis = hysteresis;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Validation: temperature thresholds
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(4.9)]
    [InlineData(60.1)]
    public async Task UpdateSettingsAsync_ThrowsOnOutOfRangeThreshold1(double threshold)
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.Threshold1Temperature = threshold;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Validation: humidity threshold
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(19.9)]
    [InlineData(100.1)]
    public async Task UpdateSettingsAsync_ThrowsOnInvalidHumidityThreshold(double humidity)
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.Sensor1HumidityThreshold = humidity;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Validation: schedule time format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateSettingsAsync_ThrowsOnInvalidScheduleTime()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.Relay4OnTime = "not-a-time";

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Validation: duplicate relay GPIO pins
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateSettingsAsync_ThrowsOnDuplicateRelayGpioPins()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.Relay1GPIO = 29;
        settings.Relay2GPIO = 29; // duplicate

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Validation: zero GPIO pins
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateSettingsAsync_ThrowsOnZeroRelayGpioPin()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new ControlLoopSignal());
        var settings = await service.GetSettingsAsync();
        settings.Relay1GPIO = 0;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateSettingsAsync(settings));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SettingsService CreateService(AppDbContext context, IControlLoopSignal signal)
    {
        return new SettingsService(context, NullLogger<SettingsService>.Instance, signal);
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
