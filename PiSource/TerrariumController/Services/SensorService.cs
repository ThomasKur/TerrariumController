using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;
using System.Device.Gpio;
using Iot.Device.DHTxx;
using UnitsNet;

namespace TerrariumController.Services
{
    public interface ISensorService
    {
        Task<SensorReading?> ReadSensorAsync(int sensorId);
        Task<List<SensorReading>> GetLatestReadingsAsync();
        Task StoreSensorReadingAsync(SensorReading reading);
    }

    public class SensorService : ISensorService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SensorService> _logger;
        private readonly ISettingsService _settingsService;
        private static GpioController? _gpioController;

        // Last successful readings for fallback
        private static readonly Dictionary<int, SensorReading> LastValidReadings = new();

        public SensorService(
            AppDbContext context,
            ILogger<SensorService> logger,
            ISettingsService settingsService)
        {
            _context = context;
            _logger = logger;
            _settingsService = settingsService;
        }

        public async Task<SensorReading?> ReadSensorAsync(int sensorId)
        {
            try
            {
                var sensorGpioMap = await GetSensorGpioMapAsync();

                if (!sensorGpioMap.TryGetValue(sensorId, out int gpioPin))
                {
                    _logger.LogWarning("Sensor {SensorId} not configured in GPIO map", sensorId);
                    return null;
                }

                // Try to read the DHT22 sensor
                var (temperature, humidity) = await ReadDHT22Async(gpioPin, sensorId);

                // If reading failed and we have a last valid reading, use it with reduced validity
                if (temperature == null || humidity == null)
                {
                    if (LastValidReadings.TryGetValue(sensorId, out var lastReading))
                    {
                        _logger.LogWarning("DHT22 read failed for Sensor {SensorId}. Using last cached reading", sensorId);
                        var cachedReading = new SensorReading
                        {
                            SensorId = sensorId,
                            Timestamp = DateTime.UtcNow,
                            Temperature = lastReading.Temperature,
                            Humidity = lastReading.Humidity,
                            IsValid = false, // Mark as invalid since it's cached
                            Label = GetSensorLabel(sensorId)
                        };
                        return cachedReading;
                    }

                    _logger.LogError("DHT22 read failed for Sensor {SensorId} and no cached reading available", sensorId);
                    return null;
                }

                var reading = new SensorReading
                {
                    SensorId = sensorId,
                    Timestamp = DateTime.UtcNow,
                    Temperature = temperature,
                    Humidity = humidity,
                    IsValid = true,
                    Label = GetSensorLabel(sensorId)
                };

                // Cache this valid reading for fallback
                LastValidReadings[sensorId] = reading;

                await StoreSensorReadingAsync(reading);
                return reading;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading sensor {SensorId}", sensorId);
                
                // Return last valid reading as fallback without storing
                if (LastValidReadings.TryGetValue(sensorId, out var lastReading))
                {
                    var fallbackReading = new SensorReading
                    {
                        SensorId = sensorId,
                        Timestamp = DateTime.UtcNow,
                        Temperature = lastReading.Temperature,
                        Humidity = lastReading.Humidity,
                        IsValid = false,
                        Label = GetSensorLabel(sensorId)
                    };
                    return fallbackReading;
                }

                return null;
            }
        }

        private async Task<(double? Temperature, double? Humidity)> ReadDHT22Async(int gpioPin, int sensorId)
        {
            try
            {
                // Use BCM (logical) numbering; Settings use BCM pin numbers
                using var dht = new Dht22(gpioPin, PinNumberingScheme.Logical);

                // Try up to 3 times since DHT22 can be finicky
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    if (dht.TryReadTemperature(out var temperature) && dht.TryReadHumidity(out var humidity))
                    {
                        double tempC = temperature.DegreesCelsius;
                        double humPct = humidity.Percent;
                        _logger.LogInformation("DHT22 Sensor {SensorId}: T={Temperature:F1}°C, RH={Humidity:F1}% (attempt {Attempt})", sensorId, tempC, humPct, attempt);
                        return (tempC, humPct);
                    }

                    await Task.Delay(500);
                }

                _logger.LogWarning("DHT22 sensor {SensorId} read failed after retries on GPIO {GpioPin}", sensorId, gpioPin);
                return (null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error communicating with DHT22 on GPIO {GpioPin}", gpioPin);
                return (null, null);
            }
        }

        // Legacy manual bit-banging implementation removed in favor of Iot.Device.DHTxx

        private async Task<Dictionary<int, int>> GetSensorGpioMapAsync()
        {
            var settings = await _settingsService.GetSettingsAsync();

            var map = new Dictionary<int, int>();
            AddConfiguredSensor(map, 1, settings.Sensor1GPIO);
            AddConfiguredSensor(map, 2, settings.Sensor2GPIO);
            AddConfiguredSensor(map, 3, settings.Sensor3GPIO);

            return map;
        }

        private void AddConfiguredSensor(IDictionary<int, int> map, int sensorId, int gpioPin)
        {
            if (gpioPin <= 0)
            {
                _logger.LogWarning("Sensor {SensorId} GPIO pin not configured", sensorId);
                return;
            }

            map[sensorId] = gpioPin;
        }

        public async Task<List<SensorReading>> GetLatestReadingsAsync()
        {
            var grouped = await _context.SensorReadings
                .GroupBy(sr => sr.SensorId)
                .Select(g => g.OrderByDescending(sr => sr.Timestamp).FirstOrDefault())
                .ToListAsync();
            return grouped.Where(r => r != null).Cast<SensorReading>().ToList();
        }

        public async Task StoreSensorReadingAsync(SensorReading reading)
        {
            _context.SensorReadings.Add(reading);
            await _context.SaveChangesAsync();
        }

        private string GetSensorLabel(int sensorId)
        {
            return sensorId switch
            {
                1 => "Nest 1",
                2 => "Nest 2",
                3 => "Arena",
                _ => $"Sensor {sensorId}"
            };
        }
    }
}
