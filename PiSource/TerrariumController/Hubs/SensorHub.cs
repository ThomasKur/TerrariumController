using Microsoft.AspNetCore.SignalR;
using TerrariumController.Models;
using TerrariumController.Services;

namespace TerrariumController.Hubs
{
    public class SensorHub : Hub
    {
        private readonly ISensorService _sensorService;
        private readonly IRelayService _relayService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<SensorHub> _logger;

        public SensorHub(ISensorService sensorService, IRelayService relayService,
            ISettingsService settingsService, ILogger<SensorHub> logger)
        {
            _sensorService = sensorService;
            _relayService = relayService;
            _settingsService = settingsService;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client {Context.ConnectionId} connected");
            
            // Send initial state to the connecting client
            try
            {
                var readings = await _sensorService.GetLatestReadingsAsync();
                var relayStates = await _relayService.GetAllRelayStatesAsync();
                var settings = await _settingsService.GetSettingsAsync();

                await Clients.Caller.SendAsync("ReceiveInitialState", new
                {
                    Readings = readings,
                    RelayStates = relayStates,
                    Settings = new
                    {
                        settings.Threshold1Temperature,
                        settings.Threshold2Temperature,
                        settings.Threshold3Temperature,
                        settings.Sensor1HumidityThreshold,
                        settings.Relay4OnTime,
                        settings.Relay4OffTime
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial state");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client {Context.ConnectionId} disconnected");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task GetLatestReadings()
        {
            try
            {
                var readings = await _sensorService.GetLatestReadingsAsync();
                await Clients.Caller.SendAsync("ReceiveReadings", readings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest readings");
                await Clients.Caller.SendAsync("ReceiveError", "Failed to get readings");
            }
        }

        public async Task GetRelayStates()
        {
            try
            {
                var states = await _relayService.GetAllRelayStatesAsync();
                await Clients.Caller.SendAsync("ReceiveRelayStates", states);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting relay states");
                await Clients.Caller.SendAsync("ReceiveError", "Failed to get relay states");
            }
        }

        public async Task SetRelayManually(int relayId, bool state)
        {
            try
            {
                var relayService = Context.GetHttpContext()?.RequestServices.GetRequiredService<IRelayService>();
                if (relayService != null)
                {
                    await relayService.SetRelayStateAsync(relayId, state, "Manual Override");
                    var states = await relayService.GetAllRelayStatesAsync();
                    await Clients.All.SendAsync("ReceiveRelayStates", states);
                    _logger.LogInformation("Relay {RelayId} manually set to {State}", relayId, state);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting relay {RelayId}", relayId);
                await Clients.Caller.SendAsync("ReceiveError", $"Failed to set relay {relayId}");
            }
        }

        public async Task BroadcastSensorUpdate(SensorReading reading)
        {
            try
            {
                await Clients.All.SendAsync("ReceiveSensorUpdate", reading);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting sensor update");
            }
        }

        public async Task BroadcastRelayStateChange(int relayId, bool state, string triggerSource)
        {
            try
            {
                var states = await _relayService.GetAllRelayStatesAsync();
                await Clients.All.SendAsync("ReceiveRelayStateChange", new
                {
                    RelayId = relayId,
                    State = state,
                    TriggerSource = triggerSource,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting relay state change");
            }
        }
    }
}
