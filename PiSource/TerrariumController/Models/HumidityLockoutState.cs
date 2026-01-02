using System.ComponentModel.DataAnnotations;

namespace TerrariumController.Models
{
    public class HumidityLockoutState
    {
        [Key]
        public int Id { get; set; }

        public int SensorId { get; set; } = 1; // Only for Sensor 1

        public DateTime LastTriggeredTime { get; set; } = DateTime.MinValue;

        public bool IsLocked { get; set; } = false;

        public DateTime LockExpiresAt { get; set; } = DateTime.MinValue;
    }
}
