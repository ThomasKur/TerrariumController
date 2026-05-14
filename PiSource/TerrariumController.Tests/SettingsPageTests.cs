using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TerrariumController.Components.Pages;
using TerrariumController.Models;
using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class SettingsPageTests
{
    [Fact]
    public void SettingsPage_ShowsValidationSummary_WhenInputIsInvalid()
    {
        using var context = new TestContext();

        var settingsService = new TestSettingsService(new Settings
        {
            Relay1GPIO = 29,
            Relay2GPIO = 29,
            Relay3GPIO = 33,
            Relay4GPIO = 35,
            Relay5GPIO = 37,
            Relay6GPIO = 40,
            Sensor1GPIO = 23,
            Sensor2GPIO = 22,
            Sensor3GPIO = 25,
            Relay4OnTime = "08:00",
            Relay4OffTime = "20:00"
        });

        context.Services.AddSingleton<ISettingsService>(settingsService);
        context.Services.AddSingleton<ILoggingService>(new NoOpLoggingService());
        context.Services.AddSingleton(typeof(ILogger<SettingsPage>), NullLogger<SettingsPage>.Instance);

        var cut = context.RenderComponent<SettingsPage>();

        cut.Find(".btn-save").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Please fix these settings issues:", cut.Markup);
            Assert.Contains("Relay GPIO pins must be unique.", cut.Markup);
            Assert.Contains("Settings were not saved.", cut.Markup);
        });
    }

    [Fact]
    public void SettingsPage_ShowsDiagnosticsShortcut()
    {
        using var context = new TestContext();

        context.Services.AddSingleton<ISettingsService>(new TestSettingsService());
        context.Services.AddSingleton<ILoggingService>(new NoOpLoggingService());
        context.Services.AddSingleton(typeof(ILogger<SettingsPage>), NullLogger<SettingsPage>.Instance);

        var cut = context.RenderComponent<SettingsPage>();

        cut.WaitForAssertion(() =>
        {
            var diagnosticsLink = cut.Find("a.btn-diagnostics");
            Assert.Equal("/diagnostics", diagnosticsLink.GetAttribute("href"));
        });
    }
}
