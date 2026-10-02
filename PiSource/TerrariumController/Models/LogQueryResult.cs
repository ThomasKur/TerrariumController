namespace TerrariumController.Models;

public sealed class LogQueryResult
{
    public required IReadOnlyList<LogEntry> Entries { get; init; }

    public int TotalCount { get; init; }
}
