using TerrariumController.Models;

namespace TerrariumController.Services;

public interface IHumidityService
{
    Task EvaluateAsync(Settings settings, LiveSensorState? sensor1, CancellationToken cancellationToken = default);

    Task<HumidityLockoutState> GetStateAsync(CancellationToken cancellationToken = default);
}
