using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
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

        // Last successful readings for fallback
        private static readonly Dictionary<int, SensorReading> LastValidReadings = new();
        private static readonly Dictionary<int, SensorRuntime> SensorRuntimes = new();
        private static readonly object SensorRuntimeSync = new();
        private static GpioController? SensorGpioController;

        private sealed class SensorRuntime
        {
            public int GpioPin { get; }
            public Dht22 Sensor { get; }
            public SemaphoreSlim ReadLock { get; } = new(1, 1);

            public SensorRuntime(int gpioPin, GpioController gpioController)
            {
                GpioPin = gpioPin;
                Sensor = new Dht22(gpioPin, gpioController);
            }
        }

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
                _logger.LogInformation("Reading DHT22 Sensor {SensorId} on BCM GPIO {GpioPin}", sensorId, gpioPin);

                var runtime = GetOrCreateSensorRuntime(sensorId, gpioPin);
                await runtime.ReadLock.WaitAsync();

                try
                {
                    // Try up to 3 times since DHT22 can be finicky
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        if (runtime.Sensor.TryReadTemperature(out var temperature) && runtime.Sensor.TryReadHumidity(out var humidity))
                        {
                            double tempC = temperature.DegreesCelsius;
                            double humPct = humidity.Percent;
                            _logger.LogInformation("DHT22 Sensor {SensorId} on BCM GPIO {GpioPin}: T={Temperature:F1}°C, RH={Humidity:F1}% (attempt {Attempt})", sensorId, gpioPin, tempC, humPct, attempt);
                            return (tempC, humPct);
                        }

                        // Datasheet recommends at least 2 seconds between reads.
                        await Task.Delay(2000);
                    }
                }
                finally
                {
                    runtime.ReadLock.Release();
                }

                _logger.LogWarning("DHT22 sensor {SensorId} on BCM GPIO {GpioPin} read failed after retries", sensorId, gpioPin);
                return (null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error communicating with DHT22 Sensor {SensorId} on BCM GPIO {GpioPin}", sensorId, gpioPin);
                return (null, null);
            }
        }

        private SensorRuntime GetOrCreateSensorRuntime(int sensorId, int gpioPin)
        {
            lock (SensorRuntimeSync)
            {
                if (SensorRuntimes.TryGetValue(sensorId, out var existing))
                {
                    if (existing.GpioPin == gpioPin)
                    {
                        return existing;
                    }

                    existing.Sensor.Dispose();
                    existing.ReadLock.Dispose();
                    SensorRuntimes.Remove(sensorId);
                    _logger.LogInformation("Recreating DHT22 runtime for Sensor {SensorId} after GPIO change to BCM {GpioPin}", sensorId, gpioPin);
                }

                var created = new SensorRuntime(gpioPin, GetOrCreateSensorGpioController());
                SensorRuntimes[sensorId] = created;
                return created;
            }
        }

        private GpioController GetOrCreateSensorGpioController()
        {
            if (SensorGpioController != null)
            {
                return SensorGpioController;
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    const string gpioChipPath = "/dev/gpiochip0";
                    if (File.Exists(gpioChipPath))
                    {
                        _logger.LogInformation("Using libgpiod GPIO driver for DHT22 sensors on {GpioChipPath}", gpioChipPath);
                        SensorGpioController = new GpioController(new LibGpiodDriver(0));
                        return SensorGpioController;
                    }

                    _logger.LogWarning("{GpioChipPath} not found; falling back to default GPIO driver for sensors", gpioChipPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create libgpiod sensor GPIO driver; falling back to default GPIO driver");
                }
            }

            SensorGpioController = new GpioController();
            return SensorGpioController;
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

        private void AddConfiguredSensor(IDictionary<int, int> map, int sensorId, int boardPin)
        {
            if (boardPin <= 0)
            {
                _logger.LogWarning("Sensor {SensorId} BOARD pin not configured", sensorId);
                return;
            }

            if (!TryConvertBoardPinToBcm(boardPin, out var bcmPin))
            {
                _logger.LogError(
                    "Sensor {SensorId} uses unsupported BOARD pin {BoardPin}; skipping sensor",
                    sensorId,
                    boardPin);
                return;
            }

            map[sensorId] = bcmPin;
        }

        private static bool TryConvertBoardPinToBcm(int boardPin, out int bcmPin)
        {
            bcmPin = boardPin switch
            {
                3 => 2,
                5 => 3,
                7 => 4,
                8 => 14,
                10 => 15,
                11 => 17,
                12 => 18,
                13 => 27,
                15 => 22,
                16 => 23,
                18 => 24,
                19 => 10,
                21 => 9,
                22 => 25,
                23 => 11,
                24 => 8,
                26 => 7,
                27 => 0,
                28 => 1,
                29 => 5,
                31 => 6,
                32 => 12,
                33 => 13,
                35 => 19,
                36 => 16,
                37 => 26,
                38 => 20,
                40 => 21,
                _ => -1
            };

            return bcmPin >= 0;
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
