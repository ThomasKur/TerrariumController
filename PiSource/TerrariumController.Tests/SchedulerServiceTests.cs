using TerrariumController.Models;
using TerrariumController.Services;

namespace TerrariumController.Tests;

public sealed class SchedulerServiceTests
{
    private readonly SchedulerService _service = new();

    [Fact]
    public void ShouldDaylightRelayBeOn_HandlesSameTime_AsDisabled()
    {
        var settings = new Settings
        {
            DaylightOnMinutes = 480,
            DaylightOffMinutes = 480
        };

        var result = _service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 2, 9, 0, 0));

        Assert.False(result);
    }

    [Fact]
    public void ShouldDaylightRelayBeOn_UsesNormalWindow()
    {
        var settings = new Settings
        {
            DaylightOnMinutes = 480,
            DaylightOffMinutes = 1200
        };

        Assert.True(_service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 2, 10, 0, 0)));
        Assert.False(_service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 2, 21, 0, 0)));
    }

    [Fact]
    public void ShouldDaylightRelayBeOn_HandlesOvernightWindow()
    {
        var settings = new Settings
        {
            DaylightOnMinutes = 1200,
            DaylightOffMinutes = 360
        };

        Assert.True(_service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 2, 22, 0, 0)));
        Assert.True(_service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 3, 2, 0, 0)));
        Assert.False(_service.ShouldDaylightRelayBeOn(settings, new DateTime(2026, 10, 3, 10, 0, 0)));
    }
}
