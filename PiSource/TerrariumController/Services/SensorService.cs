using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;
using System.Device.Gpio;

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
                // Initialize GPIO controller if not already done
                if (_gpioController == null)
                {
                    _gpioController = new GpioController();
                }

                // Open the pin for DHT22 communication
                _gpioController.OpenPin(gpioPin, PinMode.Output);

                try
                {
                    // DHT22 communication protocol:
                    // 1. Pull low for 1-10ms to signal wake-up
                    _gpioController.Write(gpioPin, PinValue.Low);
                    await Task.Delay(5);

                    // 2. Pull high and wait for DHT22 response
                    _gpioController.Write(gpioPin, PinValue.High);
                    _gpioController.SetPinMode(gpioPin, PinMode.Input);

                    // Wait for DHT22 to respond (should pull low)
                    var timeout = DateTime.UtcNow.AddMilliseconds(100);
                    while (_gpioController.Read(gpioPin) == PinValue.High && DateTime.UtcNow < timeout)
                    {
                        await Task.Delay(1);
                    }

                    if (DateTime.UtcNow >= timeout)
                    {
                        _logger.LogWarning("DHT22 sensor {SensorId} did not respond on GPIO {GpioPin}", sensorId, gpioPin);
                        return (null, null);
                    }

                    // Read 40 bits of data (humidity high, humidity low, temperature high, temperature low, checksum)
                    byte[] data = new byte[5];
                    bool dataValid = await ReadDHT22BitsAsync(gpioPin, data);

                    if (!dataValid)
                    {
                        _logger.LogWarning("DHT22 sensor {SensorId} checksum validation failed", sensorId);
                        return (null, null);
                    }

                    // Parse the data
                    int humidity = (data[0] << 8) | data[1];
                    int temperature = ((data[2] & 0x7F) << 8) | data[3];
                    bool isFahrenheit = (data[2] & 0x80) != 0;

                    double temperatureCelsius = temperature / 10.0;
                    if (isFahrenheit)
                    {
                        temperatureCelsius = (temperatureCelsius - 32) * 5.0 / 9.0;
                    }

                    double humidityPercent = humidity / 10.0;

                    _logger.LogInformation(
                        "DHT22 Sensor {SensorId}: T={Temperature:F1}°C, RH={Humidity:F1}%",
                        sensorId, temperatureCelsius, humidityPercent);

                    return (temperatureCelsius, humidityPercent);
                }
                finally
                {
                    _gpioController.ClosePin(gpioPin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error communicating with DHT22 on GPIO {GpioPin}", gpioPin);
                return (null, null);
            }
        }

        private async Task<bool> ReadDHT22BitsAsync(int gpioPin, byte[] data)
        {
            try
            {
                Array.Clear(data, 0, data.Length);

                // Wait for sensor to pull low
                var lowTimeout = DateTime.UtcNow.AddMilliseconds(100);
                while (_gpioController!.Read(gpioPin) == PinValue.Low && DateTime.UtcNow < lowTimeout)
                {
                    await Task.Delay(1);
                }

                if (DateTime.UtcNow >= lowTimeout)
                    return false;

                // Read 40 bits (5 bytes)
                for (int byteIndex = 0; byteIndex < 5; byteIndex++)
                {
                    for (int bitIndex = 0; bitIndex < 8; bitIndex++)
                    {
                        // Wait for pin to go low
                        var bitTimeout = DateTime.UtcNow.AddMilliseconds(200);
                        while (_gpioController.Read(gpioPin) == PinValue.High && DateTime.UtcNow < bitTimeout)
                        {
                            await Task.Delay(1);
                        }

                        if (DateTime.UtcNow >= bitTimeout)
                            return false;

                        // Measure high pulse duration to determine bit value
                        var highStart = DateTime.UtcNow;
                        while (_gpioController.Read(gpioPin) == PinValue.Low && DateTime.UtcNow < bitTimeout)
                        {
                            await Task.Delay(1);
                        }

                        var highDuration = (DateTime.UtcNow - highStart).TotalMilliseconds;

                        // If high pulse > ~70µs, it's a 1; otherwise it's a 0
                        if (highDuration > 0.07)
                        {
                            data[byteIndex] |= (byte)(1 << (7 - bitIndex));
                        }
                    }
                }

                // Validate checksum
                byte checksum = (byte)((data[0] + data[1] + data[2] + data[3]) & 0xFF);
                return checksum == data[4];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading DHT22 bits");
                return false;
            }
        }

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
