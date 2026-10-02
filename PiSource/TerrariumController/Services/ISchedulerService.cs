using TerrariumController.Models;

namespace TerrariumController.Services;

public interface ISchedulerService
{
    bool ShouldDaylightRelayBeOn(Settings settings, DateTime nowLocal);
}
