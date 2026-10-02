namespace TerrariumController.Models;

public sealed class LiveRelayState
{
    public int RelayId { get; set; }

    public bool IsOn { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string TriggerSource { get; set; } = "startup";
}
