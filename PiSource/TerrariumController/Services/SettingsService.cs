using Microsoft.EntityFrameworkCore;
using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class SettingsService : ISettingsService
{
    private const int SettingsRowId = 1;
    private readonly AppDbContext _dbContext;

    public SettingsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Settings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == SettingsRowId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is not null)
        {
            return settings;
        }

        settings = new Settings { Id = SettingsRowId };
        _dbContext.Settings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return settings;
    }

    public async Task UpdateAsync(Settings settings, CancellationToken cancellationToken = default)
    {
        settings.Id = SettingsRowId;
        settings.RetentionMonths = Math.Clamp(settings.RetentionMonths, 1, 24);
        settings.HysteresisC = Math.Clamp(settings.HysteresisC, 0.1m, 5m);
        settings.Sensor1TemperatureThresholdC = Math.Clamp(settings.Sensor1TemperatureThresholdC, 10m, 45m);
        settings.Sensor2TemperatureThresholdC = Math.Clamp(settings.Sensor2TemperatureThresholdC, 10m, 45m);
        settings.Sensor3TemperatureThresholdC = Math.Clamp(settings.Sensor3TemperatureThresholdC, 10m, 45m);
        settings.Sensor1HumidityThresholdPercent = Math.Clamp(settings.Sensor1HumidityThresholdPercent, 1m, 100m);
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 1, 60);
        settings.HumidityPulseSeconds = Math.Clamp(settings.HumidityPulseSeconds, 1, 30);
        settings.HumidityLockoutHours = Math.Clamp(settings.HumidityLockoutHours, 1, 24);
        settings.DaylightOnMinutes = Math.Clamp(settings.DaylightOnMinutes, 0, 1439);
        settings.DaylightOffMinutes = Math.Clamp(settings.DaylightOffMinutes, 0, 1439);
        settings.CameraWidth = Math.Clamp(settings.CameraWidth, 320, 3840);
        settings.CameraHeight = Math.Clamp(settings.CameraHeight, 240, 2160);
        settings.CameraFps = Math.Clamp(settings.CameraFps, 1, 60);

        var existingSettings = await _dbContext.Settings
            .SingleOrDefaultAsync(item => item.Id == SettingsRowId, cancellationToken)
            .ConfigureAwait(false);

        if (existingSettings is null)
        {
            _dbContext.Settings.Add(settings);
        }
        else
        {
            _dbContext.Entry(existingSettings).CurrentValues.SetValues(settings);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dbPath = _dbContext.Database.GetDbConnection().DataSource;
        var file = new FileInfo(dbPath);
        return Task.FromResult(file.Exists ? file.Length : 0L);
    }

    public async Task CompactDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken).ConfigureAwait(false);
    }
}