using System.Net.Http.Json;

namespace TerrariumController.Services
{
    public interface IHardwareSidecarClient
    {
        Task<SidecarSensorReadResponse?> ReadSensorAsync(int sensorId, int bcmGpioPin, CancellationToken cancellationToken = default);
        Task<bool> SetRelayStateAsync(int relayId, int bcmGpioPin, bool state, CancellationToken cancellationToken = default);
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    }

    public sealed class HardwareSidecarClient : IHardwareSidecarClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HardwareSidecarClient> _logger;

        public HardwareSidecarClient(HttpClient httpClient, ILogger<HardwareSidecarClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<SidecarSensorReadResponse?> ReadSensorAsync(int sensorId, int bcmGpioPin, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new SidecarSensorReadRequest
                {
                    SensorId = sensorId,
                    BcmGpioPin = bcmGpioPin
                };

                using var response = await _httpClient.PostAsJsonAsync("api/sensors/read", request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Sidecar sensor read failed with HTTP {StatusCode} for sensor {SensorId}", (int)response.StatusCode, sensorId);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<SidecarSensorReadResponse>(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sidecar sensor read request failed for sensor {SensorId}", sensorId);
                return null;
            }
        }

        public async Task<bool> SetRelayStateAsync(int relayId, int bcmGpioPin, bool state, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new SidecarRelaySetRequest
                {
                    RelayId = relayId,
                    BcmGpioPin = bcmGpioPin,
                    State = state
                };

                using var response = await _httpClient.PostAsJsonAsync("api/relays/set", request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Sidecar relay write failed with HTTP {StatusCode} for relay {RelayId}", (int)response.StatusCode, relayId);
                    return false;
                }

                var payload = await response.Content.ReadFromJsonAsync<SidecarRelaySetResponse>(cancellationToken: cancellationToken);
                return payload?.Success == true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sidecar relay write request failed for relay {RelayId}", relayId);
                return false;
            }
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync("health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class SidecarSensorReadRequest
    {
        public int SensorId { get; set; }
        public int BcmGpioPin { get; set; }
    }

    public sealed class SidecarSensorReadResponse
    {
        public bool Success { get; set; }
        public int SensorId { get; set; }
        public int BcmGpioPin { get; set; }
        public double? TemperatureC { get; set; }
        public double? HumidityPercent { get; set; }
        public string? Error { get; set; }
    }

    public sealed class SidecarRelaySetRequest
    {
        public int RelayId { get; set; }
        public int BcmGpioPin { get; set; }
        public bool State { get; set; }
    }

    public sealed class SidecarRelaySetResponse
    {
        public bool Success { get; set; }
        public int RelayId { get; set; }
        public int BcmGpioPin { get; set; }
        public bool State { get; set; }
        public string? Error { get; set; }
    }
}
