namespace TerrariumController.Models;

public sealed class LogQuery
{
    public string? EntryType { get; set; }

    public int? RelayId { get; set; }

    public int? SensorId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 100;
}
