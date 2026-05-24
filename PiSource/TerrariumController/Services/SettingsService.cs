using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;

namespace TerrariumController.Services
{
    public interface ISettingsService
    {
        Task<Settings> GetSettingsAsync();
        Task UpdateSettingsAsync(Settings settings);
        Task<long> GetDatabaseSizeAsync();
        Task CompactDatabaseAsync();
    }

    public class SettingsService : ISettingsService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SettingsService> _logger;
        private readonly IControlLoopSignal _controlLoopSignal;

        public SettingsService(AppDbContext context, ILogger<SettingsService> logger, IControlLoopSignal controlLoopSignal)
        {
            _context = context;
            _logger = logger;
            _controlLoopSignal = controlLoopSignal;
        }

        public async Task<Settings> GetSettingsAsync()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Settings();
                _context.Settings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        public async Task UpdateSettingsAsync(Settings settings)
        {
            ValidateSettings(settings);

            settings.LastModified = DateTime.UtcNow;
            _context.Settings.Update(settings);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Settings updated at {Timestamp}", DateTime.UtcNow);
            _logger.LogInformation(
                "Effective settings snapshot: T1={T1:F1}, T2={T2:F1}, T3={T3:F1}, Hysteresis={Hysteresis:F1}, RH1={RH1:F1}, Relay4On={Relay4On}, Relay4Off={Relay4Off}, LockoutHours={LockoutHours}, RelayPins={RelayPins}, SensorPins={SensorPins}, LinuxGpioChip={LinuxGpioChip}",
                settings.Threshold1Temperature,
                settings.Threshold2Temperature,
                settings.Threshold3Temperature,
                settings.TemperatureHysteresis,
                settings.Sensor1HumidityThreshold,
                settings.Relay4OnTime,
                settings.Relay4OffTime,
                settings.HumidityLockoutHours,
                string.Join(",", new[] { settings.Relay1GPIO, settings.Relay2GPIO, settings.Relay3GPIO, settings.Relay4GPIO, settings.Relay5GPIO, settings.Relay6GPIO }),
                string.Join(",", new[] { settings.Sensor1GPIO, settings.Sensor2GPIO, settings.Sensor3GPIO }),
                settings.LinuxGpioChip);
            _controlLoopSignal.RequestImmediateEvaluation("Settings updated");
        }

        private static void ValidateSettings(Settings settings)
        {
            if (settings.TemperatureHysteresis <= 0 || settings.TemperatureHysteresis > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.TemperatureHysteresis), "Temperature hysteresis must be between 0 and 5 C.");
            }

            ValidateThreshold(settings.Threshold1Temperature, nameof(settings.Threshold1Temperature));
            ValidateThreshold(settings.Threshold2Temperature, nameof(settings.Threshold2Temperature));
            ValidateThreshold(settings.Threshold3Temperature, nameof(settings.Threshold3Temperature));

            if (settings.Sensor1HumidityThreshold < 20 || settings.Sensor1HumidityThreshold > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Sensor1HumidityThreshold), "Humidity threshold must be between 20 and 100.");
            }

            if (!TimeSpan.TryParse(settings.Relay4OnTime, out _) || !TimeSpan.TryParse(settings.Relay4OffTime, out _))
            {
                throw new ArgumentException("Relay 4 schedule must use HH:mm time format.");
            }

            var relayPins = new[]
            {
                settings.Relay1GPIO,
                settings.Relay2GPIO,
                settings.Relay3GPIO,
                settings.Relay4GPIO,
                settings.Relay5GPIO,
                settings.Relay6GPIO
            };

            var sensorPins = new[]
            {
                settings.Sensor1GPIO,
                settings.Sensor2GPIO,
                settings.Sensor3GPIO
            };

            if (relayPins.Any(pin => pin <= 0) || sensorPins.Any(pin => pin <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "GPIO pins must be greater than zero.");
            }

            if (relayPins.Distinct().Count() != relayPins.Length)
            {
                throw new ArgumentException("Relay GPIO pins must be unique.");
            }

            if (sensorPins.Distinct().Count() != sensorPins.Length)
            {
                throw new ArgumentException("Sensor GPIO pins must be unique.");
            }

            if (settings.LinuxGpioChip < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.LinuxGpioChip), "Linux GPIO chip must be -1 (auto) or a non-negative chip id.");
            }
        }

        private static void ValidateThreshold(double threshold, string fieldName)
        {
            if (threshold < 5 || threshold > 60)
            {
                throw new ArgumentOutOfRangeException(fieldName, "Temperature thresholds must be between 5 and 60 C.");
            }
        }

        public async Task<long> GetDatabaseSizeAsync()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.DataSource != null && File.Exists(connection.DataSource))
                {
                    var fileInfo = new FileInfo(connection.DataSource);
                    return fileInfo.Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database size");
            }
            return 0;
        }

        public async Task CompactDatabaseAsync()
        {
            try
            {
                // SQLite VACUUM command compacts the database file
                await _context.Database.ExecuteSqlRawAsync("VACUUM;");
                _logger.LogInformation("Database compacted at {Timestamp}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compacting database");
            }
        }
    }
}
