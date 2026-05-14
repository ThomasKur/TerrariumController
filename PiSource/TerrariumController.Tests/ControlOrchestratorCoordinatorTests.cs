using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class ControlOrchestratorCoordinatorTests
{
    [Fact]
    public async Task ControlCycle_UsesCoordinatorAndAppliesRelayState()
    {
        var services = new ServiceCollection();
        var relayService = new RuleBasedRelayService();

        services.AddLogging();

        services.AddSingleton<ISettingsService>(new TestSettingsService(new Settings
        {
            Threshold1Temperature = 29.0,
            Threshold2Temperature = 29.0,
            Threshold3Temperature = 29.0,
            TemperatureHysteresis = 1.0,
            Relay4OnTime = "08:00",
            Relay4OffTime = "20:00"
        }));
        services.AddSingleton<ILoggingService>(new NoOpLoggingService());
        services.AddSingleton<ISensorService>(new DeterministicSensorService(new Dictionary<int, SensorReading>
        {
            [1] = new SensorReading { SensorId = 1, Temperature = 28.0, Humidity = 50.0, IsValid = true, Timestamp = DateTime.UtcNow },
            [2] = new SensorReading { SensorId = 2, Temperature = 35.0, Humidity = 50.0, IsValid = true, Timestamp = DateTime.UtcNow },
            [3] = new SensorReading { SensorId = 3, Temperature = 35.0, Humidity = 50.0, IsValid = true, Timestamp = DateTime.UtcNow }
        }));
        services.AddSingleton<IHumidityService, NoOpHumidityService>();
        services.AddSingleton<IControlLoopSignal, ControlLoopSignal>();
        services.AddSingleton<IControlDiagnosticsService, ControlDiagnosticsService>();
        services.AddSingleton<IRuntimeHealthService, RuntimeHealthService>();
        services.AddSingleton(new RelayCommandCoordinatorOptions { ManualOverrideDuration = TimeSpan.FromMinutes(15) });
        services.AddSingleton(relayService);
        services.AddScoped<IRelayService>(_ => relayService);
        services.AddSingleton<RelayCommandCoordinator>();
        services.AddSingleton<IRelayCommandCoordinator>(sp => sp.GetRequiredService<RelayCommandCoordinator>());

        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<RelayCommandCoordinator>();
        await coordinator.StartAsync(CancellationToken.None);

        var orchestrator = new ControlOrchestratorService(
            provider,
            NullLogger<ControlOrchestratorService>.Instance,
            provider.GetRequiredService<IControlLoopSignal>(),
            provider.GetRequiredService<IControlDiagnosticsService>(),
            provider.GetRequiredService<IRuntimeHealthService>());

        await orchestrator.RunSingleCycleAsync(CancellationToken.None, "test");

        var relay1State = await relayService.GetRelayStateAsync(1);
        Assert.True(relay1State);
        Assert.Contains(relayService.AppliedCommands, c => c.RelayId == 1 && c.State);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Coordinator_ProcessesCommandsSerially_WhenCalledConcurrently()
    {
        var services = new ServiceCollection();
        var relayService = new ConcurrencyTrackingRelayService();

        services.AddLogging();
        services.AddSingleton<IControlDiagnosticsService, ControlDiagnosticsService>();
        services.AddSingleton(new RelayCommandCoordinatorOptions { ManualOverrideDuration = TimeSpan.FromMinutes(15) });
        services.AddSingleton(relayService);
        services.AddScoped<IRelayService>(_ => relayService);
        services.AddSingleton<RelayCommandCoordinator>();

        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<RelayCommandCoordinator>();
        await coordinator.StartAsync(CancellationToken.None);

        var tasks = Enumerable.Range(0, 40)
            .Select(i => coordinator.RequestRelayStateAsync(1, i % 2 == 0, $"Temperature Threshold {i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(40, relayService.AppliedCommands.Count);
        Assert.Equal(1, relayService.MaxConcurrency);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Coordinator_BlocksLowerPriorityDuringManualOverride_ThenAllowsAfterExpiry()
    {
        var services = new ServiceCollection();
        var relayService = new ConcurrencyTrackingRelayService();

        services.AddLogging();
        services.AddSingleton<IControlDiagnosticsService, ControlDiagnosticsService>();
        services.AddSingleton(new RelayCommandCoordinatorOptions { ManualOverrideDuration = TimeSpan.FromMilliseconds(150) });
        services.AddSingleton(relayService);
        services.AddScoped<IRelayService>(_ => relayService);
        services.AddSingleton<RelayCommandCoordinator>();

        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<RelayCommandCoordinator>();
        await coordinator.StartAsync(CancellationToken.None);

        var manualApplied = await coordinator.RequestRelayStateAsync(1, true, "Manual Override");
        Assert.True(manualApplied);

        var schedulerAppliedDuringOverride = await coordinator.RequestRelayStateAsync(1, false, "Scheduler");
        Assert.False(schedulerAppliedDuringOverride);

        await Task.Delay(250);

        var schedulerAppliedAfterExpiry = await coordinator.RequestRelayStateAsync(1, false, "Scheduler");
        Assert.True(schedulerAppliedAfterExpiry);

        var appliedCount = relayService.AppliedCommands.Count(c => c.RelayId == 1);
        Assert.Equal(2, appliedCount);

        await coordinator.StopAsync(CancellationToken.None);
    }

    private sealed class DeterministicSensorService : ISensorService
    {
        private readonly Dictionary<int, SensorReading> _readings;

        public DeterministicSensorService(Dictionary<int, SensorReading> readings)
        {
            _readings = readings;
        }

        public Task<SensorReading?> ReadSensorAsync(int sensorId)
        {
            _readings.TryGetValue(sensorId, out var reading);
            return Task.FromResult(reading);
        }

        public Task<List<SensorReading>> GetLatestReadingsAsync()
        {
            return Task.FromResult(_readings.Values.ToList());
        }

        public Task StoreSensorReadingAsync(SensorReading reading)
        {
            _readings[reading.SensorId] = reading;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpHumidityService : IHumidityService
    {
        public Task CheckAndApplyHumidityLockoutAsync(int sensorId, double? humidity)
        {
            return Task.CompletedTask;
        }

        public Task<bool> IsHumidityLocked(int sensorId)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class ConcurrencyTrackingRelayService : IRelayService
    {
        private int _activeCalls;

        public List<(int RelayId, bool State, string Trigger)> AppliedCommands { get; } = new();
        public int MaxConcurrency { get; private set; }

        public Task<bool> GetRelayStateAsync(int relayId)
        {
            return Task.FromResult(false);
        }

        public async Task SetRelayStateAsync(int relayId, bool state, string triggerSource)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            MaxConcurrency = Math.Max(MaxConcurrency, active);

            await Task.Delay(15);

            lock (AppliedCommands)
            {
                AppliedCommands.Add((relayId, state, triggerSource));
            }

            Interlocked.Decrement(ref _activeCalls);
        }

        public Task<Dictionary<int, bool>> GetAllRelayStatesAsync()
        {
            return Task.FromResult(new Dictionary<int, bool>());
        }

        public Task<bool> ShouldRelayBeOnAsync(int relayId, double? temperature, double? humidity)
        {
            return Task.FromResult(false);
        }

        public Task InitializeGpioAsync()
        {
            return Task.CompletedTask;
        }

        public Task CleanupGpioAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RuleBasedRelayService : IRelayService
    {
        private readonly Dictionary<int, bool> _states = new();

        public List<(int RelayId, bool State, string Trigger)> AppliedCommands { get; } = new();

        public Task<bool> GetRelayStateAsync(int relayId)
        {
            return Task.FromResult(_states.TryGetValue(relayId, out var state) && state);
        }

        public Task SetRelayStateAsync(int relayId, bool state, string triggerSource)
        {
            _states[relayId] = state;
            AppliedCommands.Add((relayId, state, triggerSource));
            return Task.CompletedTask;
        }

        public Task<Dictionary<int, bool>> GetAllRelayStatesAsync()
        {
            return Task.FromResult(new Dictionary<int, bool>(_states));
        }

        public Task<bool> ShouldRelayBeOnAsync(int relayId, double? temperature, double? humidity)
        {
            if (relayId is < 1 or > 3 || temperature == null)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(temperature.Value < 29.0);
        }

        public Task InitializeGpioAsync()
        {
            return Task.CompletedTask;
        }

        public Task CleanupGpioAsync()
        {
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Stale-sensor failsafe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ControlCycle_AppliesStaleFailsafe_WhenSensorNeverReturnedValidReading()
    {
        var services = new ServiceCollection();
        var relayService = new RuleBasedRelayService();

        // Pre-seed relay 1 as ON so we can verify the failsafe turns it OFF.
        await relayService.SetRelayStateAsync(1, true, "pre-seed");
        relayService.AppliedCommands.Clear();

        services.AddLogging();
        services.AddSingleton<ISettingsService>(new TestSettingsService(new Settings
        {
            Threshold1Temperature = 29.0,
            Threshold2Temperature = 29.0,
            Threshold3Temperature = 29.0,
            TemperatureHysteresis = 1.0,
            Relay4OnTime = "08:00",
            Relay4OffTime = "20:00"
        }));
        services.AddSingleton<ILoggingService>(new NoOpLoggingService());
        // Sensor always returns invalid
        services.AddSingleton<ISensorService>(new InvalidSensorService());
        services.AddSingleton<IHumidityService, NoOpHumidityService>();
        services.AddSingleton<IControlLoopSignal, ControlLoopSignal>();
        services.AddSingleton<IControlDiagnosticsService, ControlDiagnosticsService>();
        services.AddSingleton<IRuntimeHealthService, RuntimeHealthService>();
        services.AddSingleton(new RelayCommandCoordinatorOptions { ManualOverrideDuration = TimeSpan.FromMinutes(15) });
        services.AddSingleton(relayService);
        services.AddScoped<IRelayService>(_ => relayService);
        services.AddSingleton<RelayCommandCoordinator>();
        services.AddSingleton<IRelayCommandCoordinator>(sp => sp.GetRequiredService<RelayCommandCoordinator>());

        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<RelayCommandCoordinator>();
        await coordinator.StartAsync(CancellationToken.None);

        var orchestrator = new ControlOrchestratorService(
            provider,
            NullLogger<ControlOrchestratorService>.Instance,
            provider.GetRequiredService<IControlLoopSignal>(),
            provider.GetRequiredService<IControlDiagnosticsService>(),
            provider.GetRequiredService<IRuntimeHealthService>());

        await orchestrator.RunSingleCycleAsync(CancellationToken.None, "test");

        // Relay 1 should have been commanded OFF via stale failsafe
        Assert.Contains(relayService.AppliedCommands,
            c => c.RelayId == 1 && !c.State && c.Trigger == "Sensor Stale Failsafe");

        await coordinator.StopAsync(CancellationToken.None);
    }

    private sealed class InvalidSensorService : ISensorService
    {
        public Task<SensorReading?> ReadSensorAsync(int sensorId)
        {
            return Task.FromResult<SensorReading?>(new SensorReading
            {
                SensorId = sensorId,
                IsValid = false,
                Timestamp = DateTime.UtcNow
            });
        }

        public Task<List<SensorReading>> GetLatestReadingsAsync()
        {
            return Task.FromResult(new List<SensorReading>());
        }

        public Task StoreSensorReadingAsync(SensorReading reading)
        {
            return Task.CompletedTask;
        }
    }
}
