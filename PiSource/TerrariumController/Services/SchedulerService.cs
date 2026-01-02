using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services
{
    public interface ISchedulerService
    {
        Task UpdateScheduleAsync(string onTime, string offTime);
        Task CheckAndApplyScheduleAsync();
    }

    public class SchedulerService : ISchedulerService
    {
        private readonly IRelayService _relayService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<SchedulerService> _logger;

        public SchedulerService(IRelayService relayService, ISettingsService settingsService, 
            ILogger<SchedulerService> logger)
        {
            _relayService = relayService;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task UpdateScheduleAsync(string onTime, string offTime)
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.Relay4OnTime = onTime;
            settings.Relay4OffTime = offTime;
            await _settingsService.UpdateSettingsAsync(settings);
            _logger.LogInformation("Schedule updated: ON at {OnTime}, OFF at {OffTime}", onTime, offTime);
        }

        public async Task CheckAndApplyScheduleAsync()
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var now = DateTime.Now;
                var currentTime = now.TimeOfDay;

                // Parse on/off times
                if (!TimeSpan.TryParse(settings.Relay4OnTime, out var onTime) ||
                    !TimeSpan.TryParse(settings.Relay4OffTime, out var offTime))
                {
                    _logger.LogWarning("Invalid schedule times: {OnTime}, {OffTime}", 
                        settings.Relay4OnTime, settings.Relay4OffTime);
                    return;
                }

                bool shouldBeOn = false;

                if (onTime < offTime)
                {
                    // Normal case: on time is before off time on same day
                    shouldBeOn = currentTime >= onTime && currentTime < offTime;
                }
                else
                {
                    // Wrapped case: on time is after off time (e.g., on at 20:00, off at 06:00)
                    shouldBeOn = currentTime >= onTime || currentTime < offTime;
                }

                var currentState = await _relayService.GetRelayStateAsync(4);
                if (shouldBeOn != currentState)
                {
                    await _relayService.SetRelayStateAsync(4, shouldBeOn, "Scheduler");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking and applying schedule");
            }
        }
    }
}
