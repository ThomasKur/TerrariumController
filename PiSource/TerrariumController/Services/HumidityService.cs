using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;

namespace TerrariumController.Services
{
    public interface IHumidityService
    {
        Task CheckAndApplyHumidityLockoutAsync(int sensorId, double? humidity);
        Task<bool> IsHumidityLocked(int sensorId);
    }

    public class HumidityService : IHumidityService
    {
        private readonly AppDbContext _context;
        private readonly IRelayService _relayService;
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        private readonly ILogger<HumidityService> _logger;

        public HumidityService(AppDbContext context, IRelayService relayService,
            ISettingsService settingsService, ILoggingService loggingService,
            ILogger<HumidityService> logger)
        {
            _context = context;
            _relayService = relayService;
            _settingsService = settingsService;
            _loggingService = loggingService;
            _logger = logger;
        }

        public async Task CheckAndApplyHumidityLockoutAsync(int sensorId, double? humidity)
        {
            if (sensorId != 1 || humidity == null)
                return;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var lockoutState = await _context.HumidityLockoutStates
                    .FirstOrDefaultAsync(hl => hl.SensorId == sensorId);

                if (lockoutState == null)
                {
                    lockoutState = new HumidityLockoutState { SensorId = sensorId };
                    _context.HumidityLockoutStates.Add(lockoutState);
                }

                // Check if lockout has expired
                if (lockoutState.IsLocked && DateTime.UtcNow >= lockoutState.LockExpiresAt)
                {
                    lockoutState.IsLocked = false;
                    _logger.LogInformation("Humidity lockout for Sensor {SensorId} expired", sensorId);
                }

                // Check if we should trigger
                if (!lockoutState.IsLocked && humidity < settings.Sensor1HumidityThreshold)
                {
                    // Trigger Relay 5 for 1 second
                    await _relayService.SetRelayStateAsync(5, true, "Humidity Threshold");
                    await Task.Delay(1000);
                    await _relayService.SetRelayStateAsync(5, false, "Humidity Pulse Complete");

                    // Apply lockout
                    lockoutState.LastTriggeredTime = DateTime.UtcNow;
                    lockoutState.IsLocked = true;
                    lockoutState.LockExpiresAt = DateTime.UtcNow.AddHours(settings.HumidityLockoutHours);

                    _logger.LogInformation("Humidity threshold triggered for Sensor {SensorId}. Lockout until {LockExpiresAt}",
                        sensorId, lockoutState.LockExpiresAt);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking humidity lockout for sensor {SensorId}", sensorId);
            }
        }

        public async Task<bool> IsHumidityLocked(int sensorId)
        {
            var lockoutState = await _context.HumidityLockoutStates
                .FirstOrDefaultAsync(hl => hl.SensorId == sensorId);

            return lockoutState?.IsLocked ?? false;
        }
    }
}
