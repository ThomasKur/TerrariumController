using Microsoft.EntityFrameworkCore;
using TerrariumController.Models;

namespace TerrariumController.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();

    public DbSet<RelayState> RelayStates => Set<RelayState>();

    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    public DbSet<Settings> Settings => Set<Settings>();

    public DbSet<HumidityLockoutState> HumidityLockoutStates => Set<HumidityLockoutState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensorReading>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.SensorId, item.TimestampUtc });
        });

        modelBuilder.Entity<RelayState>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RelayId, item.TimestampUtc });
        });

        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.TimestampUtc);
        });

        modelBuilder.Entity<Settings>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CameraUrl).HasMaxLength(256);
            entity.HasData(new Settings
            {
                Id = 1
            });
        });

        modelBuilder.Entity<HumidityLockoutState>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.SensorId).IsUnique();
        });
    }
}