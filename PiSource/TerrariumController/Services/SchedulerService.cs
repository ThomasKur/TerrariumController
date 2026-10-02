using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class SchedulerService : ISchedulerService
{
    public bool ShouldDaylightRelayBeOn(Settings settings, DateTime nowLocal)
    {
        var nowMinutes = nowLocal.Hour * 60 + nowLocal.Minute;
        var onMinutes = settings.DaylightOnMinutes;
        var offMinutes = settings.DaylightOffMinutes;

        if (onMinutes == offMinutes)
        {
            return false;
        }

        if (onMinutes < offMinutes)
        {
            return nowMinutes >= onMinutes && nowMinutes < offMinutes;
        }

        return nowMinutes >= onMinutes || nowMinutes < offMinutes;
    }
}
