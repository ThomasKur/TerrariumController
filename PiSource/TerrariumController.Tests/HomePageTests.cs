using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Components.Pages;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class HomePageTests
{
    [Fact]
    public void Home_RefreshesClockAndSensorReadings()
    {
        using var context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupVoid("startCameraSnapshot", _ => true);

        var sensorService = new PollingSensorService();
        var settingsService = new TestSettingsService(new Settings
        {
            Relay4OnTime = "08:00",
            Relay4OffTime = "20:00",
            TemperatureHysteresis = 1.0
        });

        context.Services.AddSingleton<ISensorService>(sensorService);
        context.Services.AddSingleton<ISettingsService>(settingsService);
        context.Services.AddSingleton(typeof(ILogger<Home>), NullLogger<Home>.Instance);

        var cut = context.RenderComponent<Home>(parameters => parameters
            .Add(p => p.ClockRefreshInterval, TimeSpan.FromMilliseconds(200))
            .Add(p => p.SensorRefreshInterval, TimeSpan.FromMilliseconds(200)));

        var initialClock = cut.Find(".hero-meta .meta-item .meta-value").TextContent;

        cut.WaitForAssertion(
            () => Assert.True(sensorService.GetLatestReadingsCallCount >= 2),
            TimeSpan.FromSeconds(3));

        cut.WaitForAssertion(
            () => Assert.NotEqual(initialClock, cut.Find(".hero-meta .meta-item .meta-value").TextContent),
            TimeSpan.FromSeconds(3));
    }
}
