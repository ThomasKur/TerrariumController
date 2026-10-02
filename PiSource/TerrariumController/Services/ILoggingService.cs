using TerrariumController.Models;

namespace TerrariumController.Services;

public interface ILoggingService
{
    Task LogRelayChangeAsync(RelayState relayState, CancellationToken cancellationToken = default);

    Task LogHourlySnapshotAsync(IReadOnlyList<LiveSensorState> sensors, CancellationToken cancellationToken = default);

    Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken cancellationToken = default);

    Task PruneRetentionAsync(int retentionMonths, CancellationToken cancellationToken = default);
}
