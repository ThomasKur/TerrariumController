using System.ComponentModel.DataAnnotations;

namespace TerrariumController.Models
{
    public class SensorReading
    {
        [Key]
        public int Id { get; set; }

        public int SensorId { get; set; } // 1, 2, or 3

        public DateTime Timestamp { get; set; }

        public double? Temperature { get; set; } // Celsius

        public double? Humidity { get; set; } // Percentage

        public bool IsValid { get; set; } // True if sensor data is valid

        public string? Label { get; set; } // "Nest 1", "Nest 2", "Arena"
    }
}
