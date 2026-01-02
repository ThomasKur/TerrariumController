using TerrariumController.Data;
using TerrariumController.Models;
using Microsoft.EntityFrameworkCore;

namespace TerrariumController.Services
{
    public interface ISettingsService
    {
        Task<Settings> GetSettingsAsync();
        Task UpdateSettingsAsync(Settings settings);
        Task<long> GetDatabaseSizeAsync();
        Task CompactDatabaseAsync();
    }

    public class SettingsService : ISettingsService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(AppDbContext context, ILogger<SettingsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Settings> GetSettingsAsync()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Settings();
                _context.Settings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        public async Task UpdateSettingsAsync(Settings settings)
        {
            settings.LastModified = DateTime.UtcNow;
            _context.Settings.Update(settings);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Settings updated at {Timestamp}", DateTime.UtcNow);
        }

        public async Task<long> GetDatabaseSizeAsync()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.DataSource != null && File.Exists(connection.DataSource))
                {
                    var fileInfo = new FileInfo(connection.DataSource);
                    return fileInfo.Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database size");
            }
            return 0;
        }

        public async Task CompactDatabaseAsync()
        {
            try
            {
                // SQLite VACUUM command compacts the database file
                await _context.Database.ExecuteSqlRawAsync("VACUUM;");
                _logger.LogInformation("Database compacted at {Timestamp}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compacting database");
            }
        }
    }
}
