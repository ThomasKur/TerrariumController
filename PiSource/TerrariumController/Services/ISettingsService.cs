using TerrariumController.Models;

namespace TerrariumController.Services;

public interface ISettingsService
{
    Task<Settings> GetAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Settings settings, CancellationToken cancellationToken = default);

    Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken = default);

    Task CompactDatabaseAsync(CancellationToken cancellationToken = default);
}