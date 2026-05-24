using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;

namespace TerrariumController.Services
{
    public interface IRelayService
    {
        Task<bool> GetRelayStateAsync(int relayId);
        Task SetRelayStateAsync(int relayId, bool state, string triggerSource);
        Task<Dictionary<int, bool>> GetAllRelayStatesAsync();
        Task<bool> ShouldRelayBeOnAsync(int relayId, double? temperature, double? humidity);
        Task InitializeGpioAsync();
        Task CleanupGpioAsync();
    }

    public class RelayService : IRelayService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<RelayService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        private readonly Dictionary<int, bool> _relayStates = new();
        private readonly Dictionary<int, double?> _lastTemperatures = new();
        private static GpioController? _gpioController;
        private static int? _configuredLinuxGpioChipId;
        private Dictionary<int, int> _relayGpioPins = new();
        private Dictionary<int, int> _relayBoardPins = new();

        public RelayService(AppDbContext context, ILogger<RelayService> logger,
            ISettingsService settingsService, ILoggingService loggingService)
        {
            _context = context;
            _logger = logger;
            _settingsService = settingsService;
            _loggingService = loggingService;

            // Initialize relay states
            for (int i = 1; i <= 6; i++)
            {
                _relayStates[i] = false;
                _lastTemperatures[i] = null;
            }
        }

        public async Task InitializeGpioAsync()
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();

                // Map relay IDs to GPIO pins from settings
                _relayBoardPins = new Dictionary<int, int>
                {
                    { 1, settings.Relay1GPIO },
                    { 2, settings.Relay2GPIO },
                    { 3, settings.Relay3GPIO },
                    { 4, settings.Relay4GPIO },
                    { 5, settings.Relay5GPIO },
                    { 6, settings.Relay6GPIO }
                };

                _relayGpioPins = new Dictionary<int, int>();
                foreach (var (relayId, boardPin) in _relayBoardPins)
                {
                    if (TryConvertBoardPinToBcm(boardPin, out var bcmPin))
                    {
                        _relayGpioPins[relayId] = bcmPin;
                    }
                    else
                    {
                        _logger.LogError(
                            "Relay {RelayId} uses unsupported BOARD pin {BoardPin}; skipping GPIO initialization",
                            relayId,
                            boardPin);
                    }
                }

                int? configuredLinuxChipId = settings.LinuxGpioChip >= 0 ? settings.LinuxGpioChip : null;

                if (_gpioController == null || _configuredLinuxGpioChipId != configuredLinuxChipId)
                {
                    _gpioController?.Dispose();
                    // Relay GPIO values in Settings are BOARD pin numbers.
                    _gpioController = CreateGpioController(configuredLinuxChipId);
                    _configuredLinuxGpioChipId = configuredLinuxChipId;
                }

                // Initialize all relay pins as outputs (inactive/low)
                foreach (var (relayId, gpioPin) in _relayGpioPins)
                {
                    try
                    {
                        if (!_gpioController.IsPinOpen(gpioPin))
                        {
                            _gpioController.OpenPin(gpioPin, PinMode.Output);
                            _gpioController.Write(gpioPin, PinValue.Low); // Relay off
                            _logger.LogInformation(
                                "Initialized BCM GPIO pin {GpioPin} (BOARD {BoardPin}) for Relay {RelayId}",
                                gpioPin,
                                _relayBoardPins.GetValueOrDefault(relayId),
                                relayId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to initialize BCM GPIO pin {GpioPin} (BOARD {BoardPin}) for Relay {RelayId}",
                            gpioPin,
                            _relayBoardPins.GetValueOrDefault(relayId),
                            relayId);
                    }
                }

                _logger.LogInformation("GPIO controller initialized for all relays");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing GPIO controller for relays");
            }
        }

        private GpioController CreateGpioController(int? configuredLinuxChipId)
        {
            if (OperatingSystem.IsLinux())
            {
                var candidateChipIds = LinuxGpioChipSelector.GetCandidateChipIds(_logger, configuredLinuxChipId);
                foreach (var chipId in candidateChipIds)
                {
                    var gpioChipPath = $"/dev/gpiochip{chipId}";
                    if (!File.Exists(gpioChipPath))
                    {
                        continue;
                    }

                    try
                    {
                        _logger.LogInformation("Trying libgpiod GPIO driver on {GpioChipPath}", gpioChipPath);
                        var controller = new GpioController(new LibGpiodDriver(chipId));
                        _logger.LogInformation("Using libgpiod GPIO driver on {GpioChipPath}", gpioChipPath);
                        return controller;
                    }
                    catch (PlatformNotSupportedException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "libgpiod is not installed. Install it on Raspberry Pi OS with: sudo apt update && sudo apt install -y libgpiod3 gpiod || sudo apt install -y libgpiod2 gpiod || sudo apt install -y libgpiod gpiod");
                        break;
                    }
                    catch (EntryPointNotFoundException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "libgpiod ABI mismatch detected on {GpioChipPath}. Remove any manual libgpiod soname symlinks and use distro-provided libgpiod files.",
                            gpioChipPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create libgpiod GPIO driver on {GpioChipPath}", gpioChipPath);
                    }
                }

                _logger.LogWarning("No usable libgpiod gpiochip device found; falling back to default GPIO driver");
            }

            _logger.LogInformation("Using default GPIO driver");
            return new GpioController();
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

        public async Task CleanupGpioAsync()
        {
            try
            {
                if (_gpioController != null)
                {
                    foreach (var (relayId, gpioPin) in _relayGpioPins)
                    {
                        try
                        {
                            if (_gpioController.IsPinOpen(gpioPin))
                            {
                                _gpioController.Write(gpioPin, PinValue.Low); // Ensure relay is off
                                _gpioController.ClosePin(gpioPin);
                                _logger.LogInformation("Closed GPIO pin {GpioPin} for Relay {RelayId}", gpioPin, relayId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing GPIO pin {GpioPin}", gpioPin);
                        }
                    }

                    _gpioController.Dispose();
                    _gpioController = null;
                    _configuredLinuxGpioChipId = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up GPIO controller");
            }
        }

        public async Task<bool> GetRelayStateAsync(int relayId)
        {
            var latestState = await _context.RelayStates
                .Where(r => r.RelayId == relayId)
                .OrderByDescending(r => r.Timestamp)
                .ThenByDescending(r => r.Id)
                .Select(r => (bool?)r.State)
                .FirstOrDefaultAsync();

            if (latestState.HasValue)
            {
                _relayStates[relayId] = latestState.Value;
                return latestState.Value;
            }

            if (_relayStates.TryGetValue(relayId, out var inMemoryState))
            {
                return inMemoryState;
            }

            return false;
        }

        public async Task SetRelayStateAsync(int relayId, bool state, string triggerSource)
        {
            try
            {
                bool oldState = await GetRelayStateAsync(relayId);
                if (oldState == state)
                    return; // No change

                var gpioWriteSucceeded = true;

                // Control GPIO pin if available
                if (_gpioController != null && _relayGpioPins.TryGetValue(relayId, out int gpioPin))
                {
                    gpioWriteSucceeded = await TryWriteRelayGpioWithRetryAsync(relayId, gpioPin, state);
                    if (!gpioWriteSucceeded)
                    {
                        _logger.LogError("Aborting state persistence because GPIO write failed for relay {RelayId}", relayId);
                        return;
                    }
                }

                _relayStates[relayId] = state;

                var relayLog = new RelayState
                {
                    RelayId = relayId,
                    Timestamp = DateTime.UtcNow,
                    State = state,
                    TriggerSource = triggerSource
                };

                _context.RelayStates.Add(relayLog);
                await _context.SaveChangesAsync();

                await _loggingService.LogRelayStateChangeAsync(relayId, state, triggerSource);

                _logger.LogInformation("Relay {RelayId} set to {State} - Trigger: {Trigger}",
                    relayId, state ? "ON" : "OFF", triggerSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting relay {RelayId}", relayId);
            }
        }

        private async Task<bool> TryWriteRelayGpioWithRetryAsync(int relayId, int gpioPin, bool state)
        {
            const int maxAttempts = 3;
            var gpioValue = state ? PinValue.High : PinValue.Low;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (_gpioController == null)
                    {
                        return false;
                    }

                    if (!_gpioController.IsPinOpen(gpioPin))
                    {
                        _logger.LogWarning("GPIO pin {GpioPin} for relay {RelayId} is not open", gpioPin, relayId);
                        return false;
                    }

                    _gpioController.Write(gpioPin, gpioValue);
                    _logger.LogInformation(
                        "GPIO pin {GpioPin} (Relay {RelayId}) set to {Value} on attempt {Attempt}",
                        gpioPin,
                        relayId,
                        state ? "HIGH" : "LOW",
                        attempt);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "GPIO write failed for relay {RelayId} on pin {GpioPin} attempt {Attempt}/{MaxAttempts}",
                        relayId,
                        gpioPin,
                        attempt,
                        maxAttempts);

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
                    }
                }
            }

            return false;
        }

        public async Task<Dictionary<int, bool>> GetAllRelayStatesAsync()
        {
            var states = new Dictionary<int, bool>();

            for (int relayId = 1; relayId <= 6; relayId++)
            {
                states[relayId] = await GetRelayStateAsync(relayId);
            }

            return states;
        }

        public async Task<bool> ShouldRelayBeOnAsync(int relayId, double? temperature, double? humidity)
        {
            if (temperature == null && humidity == null)
                return false; // Sensor data invalid

            var settings = await _settingsService.GetSettingsAsync();

            // Determine threshold based on relay
            double? threshold = relayId switch
            {
                1 => settings.Threshold1Temperature,
                2 => settings.Threshold2Temperature,
                3 => settings.Threshold3Temperature,
                _ => null
            };

            if (threshold == null || temperature == null)
                return false;

            // Apply hysteresis logic
            bool currentState = await GetRelayStateAsync(relayId);
            double hysteresis = settings.TemperatureHysteresis;

            if (currentState)
            {
                // Relay is on, turn off if temp is above threshold + hysteresis
                return temperature < (threshold + hysteresis);
            }
            else
            {
                // Relay is off, turn on if temp is below threshold
                return temperature < threshold;
            }
        }
    }
}
