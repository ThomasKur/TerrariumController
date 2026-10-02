using TerrariumController.Models;

namespace TerrariumController.Services;

public interface IRelayService
{
    IReadOnlyList<LiveRelayState> GetCurrentStates();

    Task<bool> SetRelayStateAsync(
        int relayId,
        bool isOn,
        string triggerSource,
        decimal? temperatureC,
        decimal? humidityPercent,
        CancellationToken cancellationToken = default);

    Task PulseRelayAsync(int relayId, TimeSpan duration, string triggerSource, CancellationToken cancellationToken = default);
}
