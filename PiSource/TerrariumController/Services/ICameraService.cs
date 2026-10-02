namespace TerrariumController.Services;

public interface ICameraService
{
    Task<string> GetStreamUrlAsync(CancellationToken cancellationToken = default);
}
