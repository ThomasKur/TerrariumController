namespace TerrariumController.Services
{
    public record RuntimeHealthSnapshot(
        bool DatabaseReady,
        bool GpioReady,
        bool ControlLoopStarted,
        DateTime? LastSuccessfulCycleUtc,
        string LastCycleStatus,
        DateTime SnapshotUtc)
    {
        public bool IsReady =>
            DatabaseReady &&
            GpioReady &&
            ControlLoopStarted &&
            LastSuccessfulCycleUtc.HasValue;
    }

    public interface IRuntimeHealthService
    {
        void MarkDatabaseReady();
        void MarkGpioReady();
        void MarkControlLoopStarted();
        void MarkSuccessfulCycle(DateTime completedAtUtc);
        void MarkCycleFailure(string status);
        RuntimeHealthSnapshot GetSnapshot();
    }

    public class RuntimeHealthService : IRuntimeHealthService
    {
        private readonly object _lock = new();
        private bool _databaseReady;
        private bool _gpioReady;
        private bool _controlLoopStarted;
        private DateTime? _lastSuccessfulCycleUtc;
        private string _lastCycleStatus = "starting";

        public void MarkDatabaseReady()
        {
            lock (_lock)
            {
                _databaseReady = true;
            }
        }

        public void MarkGpioReady()
        {
            lock (_lock)
            {
                _gpioReady = true;
            }
        }

        public void MarkControlLoopStarted()
        {
            lock (_lock)
            {
                _controlLoopStarted = true;
                if (_lastCycleStatus == "starting")
                {
                    _lastCycleStatus = "loop-started";
                }
            }
        }

        public void MarkSuccessfulCycle(DateTime completedAtUtc)
        {
            lock (_lock)
            {
                _lastSuccessfulCycleUtc = completedAtUtc;
                _lastCycleStatus = "ok";
            }
        }

        public void MarkCycleFailure(string status)
        {
            lock (_lock)
            {
                _lastCycleStatus = status;
            }
        }

        public RuntimeHealthSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new RuntimeHealthSnapshot(
                    _databaseReady,
                    _gpioReady,
                    _controlLoopStarted,
                    _lastSuccessfulCycleUtc,
                    _lastCycleStatus,
                    DateTime.UtcNow);
            }
        }
    }
}