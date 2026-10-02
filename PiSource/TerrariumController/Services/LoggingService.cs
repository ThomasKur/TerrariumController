using Microsoft.EntityFrameworkCore;
using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly AppDbContext _dbContext;

    public LoggingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogRelayChangeAsync(RelayState relayState, CancellationToken cancellationToken = default)
    {
        _dbContext.RelayStates.Add(relayState);
        _dbContext.LogEntries.Add(new LogEntry
        {
            TimestampUtc = relayState.TimestampUtc,
            EntryType = "relay-change",
            RelayId = relayState.RelayId,
            Details = $"Relay {relayState.RelayId} => {(relayState.IsOn ? "On" : "Off")} ({relayState.TriggerSource})",
            SensorId = null
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LogHourlySnapshotAsync(IReadOnlyList<LiveSensorState> sensors, CancellationToken cancellationToken = default)
    {
        var snapshot = string.Join(", ",
            sensors.OrderBy(item => item.SensorId)
                .Select(item => $"S{item.SensorId}:T={item.TemperatureC?.ToString("0.0") ?? "n/a"}C RH={item.HumidityPercent?.ToString("0.0") ?? "n/a"}%"));

        _dbContext.LogEntries.Add(new LogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            EntryType = "hourly-snapshot",
            Details = snapshot,
            SensorId = null,
            RelayId = null
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(1, query.Page);

        var q = _dbContext.LogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EntryType))
        {
            q = q.Where(item => item.EntryType == query.EntryType);
        }

        if (query.RelayId.HasValue)
        {
            q = q.Where(item => item.RelayId == query.RelayId.Value);
        }

        if (query.SensorId.HasValue)
        {
            q = q.Where(item => item.SensorId == query.SensorId.Value);
        }

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);
        var entries = await q
            .OrderByDescending(item => item.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LogQueryResult
        {
            Entries = entries,
            TotalCount = total
        };
    }

    public async Task PruneRetentionAsync(int retentionMonths, CancellationToken cancellationToken = default)
    {
        retentionMonths = Math.Clamp(retentionMonths, 1, 24);
        var cutoff = DateTime.UtcNow.AddMonths(-retentionMonths);

        var staleLogs = await _dbContext.LogEntries
            .Where(item => item.TimestampUtc < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (staleLogs.Count == 0)
        {
            return;
        }

        _dbContext.LogEntries.RemoveRange(staleLogs);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
