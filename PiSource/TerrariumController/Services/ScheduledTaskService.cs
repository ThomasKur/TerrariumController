using TerrariumController.Data;

namespace TerrariumController.Services
{
    /// <summary>
    /// Background service that periodically prunes old log entries and applies scheduled relay control.
    /// </summary>
    public class ScheduledTaskService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ScheduledTaskService> _logger;
        private DateTime _lastHourlySnapshot = DateTime.UtcNow;
        private DateTime _lastDailyPrune = DateTime.UtcNow;

        public ScheduledTaskService(IServiceProvider serviceProvider, ILogger<ScheduledTaskService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scheduled task service starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var loggingService = scope.ServiceProvider.GetRequiredService<ILoggingService>();
                        var sensorService = scope.ServiceProvider.GetRequiredService<ISensorService>();
                        var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();

                        var now = DateTime.UtcNow;

                        // Hourly snapshot (every 60 minutes)
                        if (now - _lastHourlySnapshot >= TimeSpan.FromHours(1))
                        {
                            try
                            {
                                var latestReadings = await sensorService.GetLatestReadingsAsync();
                                var readingDict = latestReadings.ToDictionary(
                                    r => r.SensorId,
                                    r => ((double?)r.Temperature, (double?)r.Humidity)
                                );

                                await loggingService.LogHourlySnapshotAsync(readingDict);
                                _lastHourlySnapshot = now;
                                _logger.LogInformation("Hourly snapshot logged");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error logging hourly snapshot");
                            }
                        }

                        // Daily pruning (once per day at midnight)
                        if (now - _lastDailyPrune >= TimeSpan.FromHours(24))
                        {
                            try
                            {
                                await loggingService.PruneOldEntriesAsync();
                                _lastDailyPrune = now;
                                _logger.LogInformation("Daily log pruning completed");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error pruning log entries");
                            }
                        }

                        // Check and apply daylight schedule (every 5 minutes)
                        try
                        {
                            await schedulerService.CheckAndApplyScheduleAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking schedule");
                        }
                    }

                    // Check every 5 minutes
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduled task service");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("Scheduled task service stopping");
        }
    }
}
