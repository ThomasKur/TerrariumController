namespace TerrariumController.Services;

public static class TemperatureControlPolicy
{
    public static bool? DetermineRelayState(
        bool currentRelayOn,
        decimal currentTemperatureC,
        decimal thresholdC,
        decimal hysteresisC)
    {
        if (!currentRelayOn && currentTemperatureC <= thresholdC)
        {
            return true;
        }

        if (currentRelayOn && currentTemperatureC >= thresholdC + hysteresisC)
        {
            return false;
        }

        return null;
    }
}