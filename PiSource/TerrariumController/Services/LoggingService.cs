using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;

namespace TerrariumController.Services
{
    public interface ILoggingService
    {
        Task LogRelayStateChangeAsync(int relayId, bool newState, string triggerSource, 
            int? sensorId = null, double? temperature = null, double? humidity = null);
        Task LogHourlySnapshotAsync(Dictionary<int, (double? temp, double? humidity)> sensorReadings);
        Task PruneOldEntriesAsync();
        Task<List<LogEntry>> GetLogEntriesAsync(int pageNumber, int pageSize);
        Task<int> GetLogCountAsync();
    }

    public class LoggingService : ILoggingService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LoggingService> _logger;
        private readonly ISettingsService _settingsService;

        public LoggingService(AppDbContext context, ILogger<LoggingService> logger, ISettingsService settingsService)
        {
            _context = context;
            _logger = logger;
            _settingsService = settingsService;
        }

        public async Task LogRelayStateChangeAsync(int relayId, bool newState, string triggerSource,
            int? sensorId = null, double? temperature = null, double? humidity = null)
        {
            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogType = "StateChange",
                    Details = $"Relay {relayId} turned {(newState ? "ON" : "OFF")} - Trigger: {triggerSource}",
                    RelayId = relayId,
                    RelayState = newState
                };

                // Store sensor values if provided
                if (sensorId == 1)
                {
                    logEntry.Sensor1Temperature = temperature;
                    logEntry.Sensor1Humidity = humidity;
                }
                else if (sensorId == 2)
                {
                    logEntry.Sensor2Temperature = temperature;
                    logEntry.Sensor2Humidity = humidity;
                }
                else if (sensorId == 3)
                {
                    logEntry.Sensor3Temperature = temperature;
                    logEntry.Sensor3Humidity = humidity;
                }

                _context.LogEntries.Add(logEntry);
                await _context.SaveChangesAsync();
                _logger.LogInformation(logEntry.Details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging relay state change for Relay {RelayId}", relayId);
            }
        }

        public async Task LogHourlySnapshotAsync(Dictionary<int, (double? temp, double? humidity)> sensorReadings)
        {
            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogType = "HourlySnapshot",
                    Details = "Hourly sensor reading snapshot"
                };

                // Populate sensor data
                if (sensorReadings.TryGetValue(1, out var sensor1))
                {
                    logEntry.Sensor1Temperature = sensor1.temp;
                    logEntry.Sensor1Humidity = sensor1.humidity;
                }
                if (sensorReadings.TryGetValue(2, out var sensor2))
                {
                    logEntry.Sensor2Temperature = sensor2.temp;
                    logEntry.Sensor2Humidity = sensor2.humidity;
                }
                if (sensorReadings.TryGetValue(3, out var sensor3))
                {
                    logEntry.Sensor3Temperature = sensor3.temp;
                    logEntry.Sensor3Humidity = sensor3.humidity;
                }

                _context.LogEntries.Add(logEntry);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Hourly sensor snapshot logged");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging hourly snapshot");
            }
        }

        public async Task PruneOldEntriesAsync()
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var cutoffDate = DateTime.UtcNow.AddMonths(-settings.LogRetentionMonths);

                var entriesToDelete = await _context.LogEntries
                    .Where(le => le.Timestamp < cutoffDate)
                    .ToListAsync();

                if (entriesToDelete.Count > 0)
                {
                    _context.LogEntries.RemoveRange(entriesToDelete);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Pruned {Count} log entries older than {CutoffDate}", 
                        entriesToDelete.Count, cutoffDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pruning old log entries");
            }
        }

        public async Task<List<LogEntry>> GetLogEntriesAsync(int pageNumber, int pageSize)
        {
            return await _context.LogEntries
                .OrderByDescending(le => le.Timestamp)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .AsAsyncEnumerable()
                .ToListAsync();
        }

        public async Task<int> GetLogCountAsync()
        {
            return await _context.LogEntries.AsAsyncEnumerable().CountAsync();
        }
    }
}
