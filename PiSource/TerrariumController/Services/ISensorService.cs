using TerrariumController.Models;

namespace TerrariumController.Services;

public interface ISensorService
{
    Task<IReadOnlyList<LiveSensorState>> PollAsync(Settings settings, CancellationToken cancellationToken = default);

    IReadOnlyList<LiveSensorState> GetLatest();
}
