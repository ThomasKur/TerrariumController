namespace TerrariumController.Services
{
    public class RelayCommandCoordinatorOptions
    {
        public TimeSpan ManualOverrideDuration { get; set; } = TimeSpan.FromMinutes(10);
    }
}