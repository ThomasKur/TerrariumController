using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class RelayService : IRelayService
{
    private readonly ControlStateStore _stateStore;
    private readonly ILoggingService _loggingService;

    public RelayService(ControlStateStore stateStore, ILoggingService loggingService)
    {
        _stateStore = stateStore;
        _loggingService = loggingService;
    }

    public IReadOnlyList<LiveRelayState> GetCurrentStates() => _stateStore.GetRelays();

    public async Task<bool> SetRelayStateAsync(
        int relayId,
        bool isOn,
        string triggerSource,
        decimal? temperatureC,
        decimal? humidityPercent,
        CancellationToken cancellationToken = default)
    {
        if (relayId is < 1 or > 6)
        {
            return false;
        }

        var current = _stateStore.GetRelays().Single(item => item.RelayId == relayId);
        if (current.IsOn == isOn)
        {
            return false;
        }

        var next = new LiveRelayState
        {
            RelayId = relayId,
            IsOn = isOn,
            TimestampUtc = DateTime.UtcNow,
            TriggerSource = triggerSource
        };

        _stateStore.UpdateRelay(next);

        await _loggingService.LogRelayChangeAsync(new RelayState
        {
            RelayId = relayId,
            IsOn = isOn,
            TimestampUtc = next.TimestampUtc,
            TriggerSource = triggerSource,
            TemperatureC = temperatureC,
            HumidityPercent = humidityPercent
        }, cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task PulseRelayAsync(int relayId, TimeSpan duration, string triggerSource, CancellationToken cancellationToken = default)
    {
        var changed = await SetRelayStateAsync(relayId, true, triggerSource, null, null, cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            await SetRelayStateAsync(relayId, false, triggerSource, null, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
