using TerrariumController.Services;

namespace TerrariumController.Tests;

public sealed class TemperatureControlPolicyTests
{
    [Fact]
    public void DetermineRelayState_TurnsOn_WhenBelowThreshold()
    {
        var result = TemperatureControlPolicy.DetermineRelayState(
            currentRelayOn: false,
            currentTemperatureC: 28.9m,
            thresholdC: 29m,
            hysteresisC: 1m);

        Assert.True(result);
    }

    [Fact]
    public void DetermineRelayState_HoldsState_InHysteresisBand()
    {
        var result = TemperatureControlPolicy.DetermineRelayState(
            currentRelayOn: true,
            currentTemperatureC: 29.5m,
            thresholdC: 29m,
            hysteresisC: 1m);

        Assert.Null(result);
    }

    [Fact]
    public void DetermineRelayState_TurnsOff_AtUpperBound()
    {
        var result = TemperatureControlPolicy.DetermineRelayState(
            currentRelayOn: true,
            currentTemperatureC: 30m,
            thresholdC: 29m,
            hysteresisC: 1m);

        Assert.False(result);
    }
}
