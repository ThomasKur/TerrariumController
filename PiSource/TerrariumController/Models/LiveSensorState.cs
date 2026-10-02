namespace TerrariumController.Models;

public sealed class LiveSensorState
{
    public int SensorId { get; set; }

    public decimal? TemperatureC { get; set; }

    public decimal? HumidityPercent { get; set; }

    public bool IsValid { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Source { get; set; } = "mock";
}
