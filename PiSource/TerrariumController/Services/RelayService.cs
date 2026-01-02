using TerrariumController.Data;
using TerrariumController.Models;
using System.Device.Gpio;

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
        private Dictionary<int, int> _relayGpioPins = new();

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
                _relayGpioPins = new Dictionary<int, int>
                {
                    { 1, settings.Relay1GPIO },
                    { 2, settings.Relay2GPIO },
                    { 3, settings.Relay3GPIO },
                    { 4, settings.Relay4GPIO },
                    { 5, settings.Relay5GPIO },
                    { 6, settings.Relay6GPIO }
                };

                if (_gpioController == null)
                {
                    _gpioController = new GpioController();
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
                            _logger.LogInformation("Initialized GPIO pin {GpioPin} for Relay {RelayId}", gpioPin, relayId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialize GPIO pin {GpioPin} for Relay {RelayId}", gpioPin, relayId);
                    }
                }

                _logger.LogInformation("GPIO controller initialized for all relays");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing GPIO controller for relays");
            }
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up GPIO controller");
            }
        }

        public async Task<bool> GetRelayStateAsync(int relayId)
        {
            if (_relayStates.TryGetValue(relayId, out var state))
            {
                return state;
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

                _relayStates[relayId] = state;

                // Control GPIO pin if available
                if (_gpioController != null && _relayGpioPins.TryGetValue(relayId, out int gpioPin))
                {
                    try
                    {
                        if (_gpioController.IsPinOpen(gpioPin))
                        {
                            PinValue gpioValue = state ? PinValue.High : PinValue.Low;
                            _gpioController.Write(gpioPin, gpioValue);
                            _logger.LogInformation("GPIO pin {GpioPin} (Relay {RelayId}) set to {Value}",
                                gpioPin, relayId, state ? "HIGH" : "LOW");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set GPIO pin {GpioPin} for Relay {RelayId}", gpioPin, relayId);
                    }
                }

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

        public async Task<Dictionary<int, bool>> GetAllRelayStatesAsync()
        {
            return new Dictionary<int, bool>(_relayStates);
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
