namespace TerrariumController.Models;

public sealed class LogEntry
{
    public int Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string EntryType { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public int? SensorId { get; set; }

    public int? RelayId { get; set; }
}