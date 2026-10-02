namespace TerrariumController.Services;

public sealed class CameraService : ICameraService
{
    private readonly ISettingsService _settingsService;

    public CameraService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> GetStreamUrlAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        return settings.CameraUrl;
    }
}
