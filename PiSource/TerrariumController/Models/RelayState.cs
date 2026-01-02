using System.ComponentModel.DataAnnotations;

namespace TerrariumController.Models
{
    public class RelayState
    {
        [Key]
        public int Id { get; set; }

        public int RelayId { get; set; } // 1-6

        public DateTime Timestamp { get; set; }

        public bool State { get; set; } // true = on, false = off

        public string? TriggerSource { get; set; } // e.g., "Sensor 1 Temperature", "Scheduler", "Manual"

        public int? SourceSensorId { get; set; } // Which sensor triggered this change

        public double? SensorTemperature { get; set; } // Temperature at trigger time

        public double? SensorHumidity { get; set; } // Humidity at trigger time
    }
}
