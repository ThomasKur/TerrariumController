using System.ComponentModel.DataAnnotations;

namespace TerrariumController.Models
{
    public class LogEntry
    {
        [Key]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; }

        public string LogType { get; set; } = "StateChange"; // "StateChange" or "HourlySnapshot"

        public string Details { get; set; } = string.Empty;

        public int? RelayId { get; set; } // For state changes

        public bool? RelayState { get; set; } // For state changes

        // Sensor data snapshot
        public double? Sensor1Temperature { get; set; }
        public double? Sensor1Humidity { get; set; }
        public double? Sensor2Temperature { get; set; }
        public double? Sensor2Humidity { get; set; }
        public double? Sensor3Temperature { get; set; }
        public double? Sensor3Humidity { get; set; }
    }
}
