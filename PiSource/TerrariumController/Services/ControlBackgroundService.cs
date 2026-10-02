using Microsoft.Extensions.Hosting;
using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class ControlBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private DateTime _lastHourlySnapshotUtc = DateTime.MinValue;
    private DateTime _lastRetentionPruneUtc = DateTime.MinValue;

    public ControlBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var waitSeconds = 5;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var sensorService = scope.ServiceProvider.GetRequiredService<ISensorService>();
                var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
                var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();
                var humidityService = scope.ServiceProvider.GetRequiredService<IHumidityService>();
                var loggingService = scope.ServiceProvider.GetRequiredService<ILoggingService>();

                var settings = await settingsService.GetAsync(stoppingToken).ConfigureAwait(false);
                waitSeconds = settings.PollIntervalSeconds;

                var sensors = await sensorService.PollAsync(settings, stoppingToken).ConfigureAwait(false);
                await ApplyTemperatureControlAsync(settings, sensors, relayService, stoppingToken).ConfigureAwait(false);

                var relay4On = schedulerService.ShouldDaylightRelayBeOn(settings, DateTime.Now);
                await relayService.SetRelayStateAsync(4, relay4On, "daylight-scheduler", null, null, stoppingToken).ConfigureAwait(false);

                var sensor1 = sensors.SingleOrDefault(item => item.SensorId == 1);
                await humidityService.EvaluateAsync(settings, sensor1, stoppingToken).ConfigureAwait(false);

                var currentHour = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc);
                if (_lastHourlySnapshotUtc != currentHour)
                {
                    await loggingService.LogHourlySnapshotAsync(sensors, stoppingToken).ConfigureAwait(false);
                    _lastHourlySnapshotUtc = currentHour;
                }

                var pruneMarker = DateTime.UtcNow.Date;
                if (_lastRetentionPruneUtc != pruneMarker)
                {
                    await loggingService.PruneRetentionAsync(settings.RetentionMonths, stoppingToken).ConfigureAwait(false);
                    _lastRetentionPruneUtc = pruneMarker;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                waitSeconds = Math.Max(waitSeconds, 5);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 60)), stoppingToken).ConfigureAwait(false);
        }
    }

    private static async Task ApplyTemperatureControlAsync(
        Settings settings,
        IReadOnlyList<LiveSensorState> sensors,
        IRelayService relayService,
        CancellationToken cancellationToken)
    {
        var relayStates = relayService.GetCurrentStates().ToDictionary(item => item.RelayId, item => item);

        foreach (var sensor in sensors)
        {
            if (!sensor.IsValid || !sensor.TemperatureC.HasValue)
            {
                continue;
            }

            var relayId = sensor.SensorId;
            if (relayId is < 1 or > 3)
            {
                continue;
            }

            var currentState = relayStates[relayId].IsOn;
            var threshold = GetTemperatureThreshold(settings, sensor.SensorId);
            var targetState = TemperatureControlPolicy.DetermineRelayState(
                currentState,
                sensor.TemperatureC.Value,
                threshold,
                settings.HysteresisC);

            if (targetState == true)
            {
                await relayService.SetRelayStateAsync(relayId, true, "temperature-below-threshold", sensor.TemperatureC, sensor.HumidityPercent, cancellationToken).ConfigureAwait(false);
            }
            else if (targetState == false)
            {
                await relayService.SetRelayStateAsync(relayId, false, "temperature-above-threshold-hysteresis", sensor.TemperatureC, sensor.HumidityPercent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static decimal GetTemperatureThreshold(Settings settings, int sensorId)
    {
        return sensorId switch
        {
            1 => settings.Sensor1TemperatureThresholdC,
            2 => settings.Sensor2TemperatureThresholdC,
            3 => settings.Sensor3TemperatureThresholdC,
            _ => settings.Sensor1TemperatureThresholdC
        };
    }
}
