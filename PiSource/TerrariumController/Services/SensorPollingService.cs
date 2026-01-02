using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services
{
    /// <summary>
    /// Background service that periodically polls sensors and applies relay control logic.
    /// </summary>
    public class SensorPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SensorPollingService> _logger;
        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

        public SensorPollingService(IServiceProvider serviceProvider, ILogger<SensorPollingService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Sensor polling service starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var sensorService = scope.ServiceProvider.GetRequiredService<ISensorService>();
                        var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
                        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                        var humidityService = scope.ServiceProvider.GetRequiredService<IHumidityService>();
                        var loggingService = scope.ServiceProvider.GetRequiredService<ILoggingService>();

                        var settings = await settingsService.GetSettingsAsync();

                        // Poll each sensor
                        for (int sensorId = 1; sensorId <= 3; sensorId++)
                        {
                            var reading = await sensorService.ReadSensorAsync(sensorId);

                            if (reading?.IsValid == true)
                            {
                                // Apply temperature threshold for relays 1-3
                                if (sensorId <= 3)
                                {
                                    bool shouldBeOn = await relayService.ShouldRelayBeOnAsync(sensorId, reading.Temperature, null);
                                    await relayService.SetRelayStateAsync(sensorId, shouldBeOn, "Temperature Threshold");
                                }

                                // Check humidity for sensor 1
                                if (sensorId == 1)
                                {
                                    await humidityService.CheckAndApplyHumidityLockoutAsync(sensorId, reading.Humidity);
                                }

                                _logger.LogDebug("Sensor {SensorId}: {Temp:F1}°C, {Humidity:F0}%", 
                                    sensorId, reading.Temperature, reading.Humidity);
                            }
                            else
                            {
                                _logger.LogWarning("Sensor {SensorId} reading invalid or missing", sensorId);
                                // Keep relay off if sensor is invalid
                                await relayService.SetRelayStateAsync(sensorId, false, "Sensor Invalid");
                            }
                        }
                    }

                    await Task.Delay(_pollInterval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in sensor polling service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("Sensor polling service stopping");
        }
    }
}
