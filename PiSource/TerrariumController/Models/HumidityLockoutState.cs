namespace TerrariumController.Models;

public sealed class HumidityLockoutState
{
    public int Id { get; set; }

    public int SensorId { get; set; }

    public DateTime? TriggeredAtUtc { get; set; }

    public DateTime? LockoutUntilUtc { get; set; }

    public decimal? LastHumidityPercent { get; set; }
}