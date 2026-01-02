using System.ComponentModel.DataAnnotations;

namespace TerrariumController.Models
{
    public class Settings
    {
        [Key]
        public int Id { get; set; }

        // Temperature Thresholds (per sensor/relay)
        public double Threshold1Temperature { get; set; } = 29.0; // Relay 1
        public double Threshold2Temperature { get; set; } = 29.0; // Relay 2
        public double Threshold3Temperature { get; set; } = 29.0; // Relay 3

        // Humidity threshold for Sensor 1 (triggers Relay 5)
        public double Sensor1HumidityThreshold { get; set; } = 60.0;

        // Hysteresis (1°C)
        public double TemperatureHysteresis { get; set; } = 1.0;

        // Relay 4 daylight scheduler
        public string Relay4OnTime { get; set; } = "08:00"; // HH:mm format
        public string Relay4OffTime { get; set; } = "20:00";

        // GPIO Configuration (BOARD numbering)
        public int Relay1GPIO { get; set; } = 29;
        public int Relay2GPIO { get; set; } = 31;
        public int Relay3GPIO { get; set; } = 33;
        public int Relay4GPIO { get; set; } = 35;
        public int Relay5GPIO { get; set; } = 37;
        public int Relay6GPIO { get; set; } = 40;

        // Sensor GPIO Configuration (BCM numbering)
        public int Sensor1GPIO { get; set; } = 23;
        public int Sensor2GPIO { get; set; } = 24;
        public int Sensor3GPIO { get; set; } = 25;

        // Camera settings
        public int CameraWidth { get; set; } = 1280;
        public int CameraHeight { get; set; } = 720;
        public int CameraFramerate { get; set; } = 15;

        // Log retention in months
        public int LogRetentionMonths { get; set; } = 12;

        // Humidity lockout duration (6 hours)
        public int HumidityLockoutHours { get; set; } = 6;

        // Last modified timestamp
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}
