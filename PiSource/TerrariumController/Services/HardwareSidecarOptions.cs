namespace TerrariumController.Services
{
    public sealed class HardwareSidecarOptions
    {
        public const string SectionName = "HardwareSidecar";

        // Supported values: Embedded, PythonSidecar
        public string Mode { get; set; } = "Embedded";
        public string BaseUrl { get; set; } = "http://127.0.0.1:5580/";
        public int TimeoutSeconds { get; set; } = 3;

        public bool UsePythonSidecar => string.Equals(Mode, "PythonSidecar", StringComparison.OrdinalIgnoreCase);
    }
}
