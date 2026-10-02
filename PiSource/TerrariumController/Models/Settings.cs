namespace TerrariumController.Models;

public sealed class Settings
{
    public int Id { get; set; } = 1;

    public decimal Sensor1TemperatureThresholdC { get; set; } = 29;

    public decimal Sensor2TemperatureThresholdC { get; set; } = 29;

    public decimal Sensor3TemperatureThresholdC { get; set; } = 29;

    public decimal Sensor1HumidityThresholdPercent { get; set; } = 60;

    public int Sensor1GpioBcm { get; set; } = 23;

    public int Sensor2GpioBcm { get; set; } = 22;

    public int Sensor3GpioBcm { get; set; } = 25;

    public int Relay1GpioBcm { get; set; } = 5;

    public int Relay2GpioBcm { get; set; } = 6;

    public int Relay3GpioBcm { get; set; } = 13;

    public int Relay4GpioBcm { get; set; } = 19;

    public int Relay5GpioBcm { get; set; } = 26;

    public int Relay6GpioBcm { get; set; } = 21;

    public int DaylightOnMinutes { get; set; } = 8 * 60;

    public int DaylightOffMinutes { get; set; } = 20 * 60;

    public int RetentionMonths { get; set; } = 12;

    public int CameraWidth { get; set; } = 1920;

    public int CameraHeight { get; set; } = 1080;

    public int CameraFps { get; set; } = 15;

    public string CameraUrl { get; set; } = "http://localhost:8080";

    public int PollIntervalSeconds { get; set; } = 5;

    public bool UseMockSensors { get; set; } = true;

    public bool UseMockRelays { get; set; } = true;

    public decimal HysteresisC { get; set; } = 1;

    public int HumidityPulseSeconds { get; set; } = 1;

    public int HumidityLockoutHours { get; set; } = 6;

    public static Settings CreateDefault() => new();
}