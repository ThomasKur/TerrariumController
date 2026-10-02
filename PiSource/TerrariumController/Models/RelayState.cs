namespace TerrariumController.Models;

public sealed class RelayState
{
    public int Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public int RelayId { get; set; }

    public bool IsOn { get; set; }

    public string? TriggerSource { get; set; }

    public decimal? TemperatureC { get; set; }

    public decimal? HumidityPercent { get; set; }
}