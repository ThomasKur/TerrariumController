using Microsoft.EntityFrameworkCore;
using TerrariumController.Models;

namespace TerrariumController.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<SensorReading> SensorReadings { get; set; } = default!;
        public DbSet<RelayState> RelayStates { get; set; } = default!;
        public DbSet<LogEntry> LogEntries { get; set; } = default!;
        public DbSet<Settings> Settings { get; set; } = default!;
        public DbSet<HumidityLockoutState> HumidityLockoutStates { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure table properties
            modelBuilder.Entity<SensorReading>()
                .HasIndex(sr => sr.Timestamp)
                .IsDescending();

            modelBuilder.Entity<RelayState>()
                .HasIndex(rs => rs.Timestamp)
                .IsDescending();

            modelBuilder.Entity<LogEntry>()
                .HasIndex(le => le.Timestamp)
                .IsDescending();

            modelBuilder.Entity<Settings>()
                .HasData(new Settings
                {
                    Id = 1,
                    Threshold1Temperature = 29.0,
                    Threshold2Temperature = 29.0,
                    Threshold3Temperature = 29.0,
                    Sensor1HumidityThreshold = 60.0,
                    TemperatureHysteresis = 1.0,
                    Relay4OnTime = "08:00",
                    Relay4OffTime = "20:00",
                    Relay1GPIO = 29,
                    Relay2GPIO = 31,
                    Relay3GPIO = 33,
                    Relay4GPIO = 35,
                    Relay5GPIO = 37,
                    Relay6GPIO = 40,
                    Sensor1GPIO = 23,
                    Sensor2GPIO = 24,
                    Sensor3GPIO = 25,
                    CameraWidth = 1280,
                    CameraHeight = 720,
                    CameraFramerate = 15,
                    LogRetentionMonths = 12,
                    HumidityLockoutHours = 6,
                    LastModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
        }
    }
}
