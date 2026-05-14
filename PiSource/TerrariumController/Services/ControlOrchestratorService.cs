using TerrariumController.Models;

namespace TerrariumController.Services
{
    public class ControlOrchestratorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ControlOrchestratorService> _logger;
        private readonly IControlLoopSignal _controlLoopSignal;
        private readonly IControlDiagnosticsService _diagnostics;
        private readonly IRuntimeHealthService _runtimeHealth;
        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _sensorStaleFailsafeWindow = TimeSpan.FromMinutes(5);
        private readonly Dictionary<int, DateTime> _lastValidSensorTimestamps = new();
        private DateTime _lastHourlySnapshot = DateTime.UtcNow;
        private DateTime _lastDailyPrune = DateTime.UtcNow;

        public ControlOrchestratorService(
            IServiceProvider serviceProvider,
            ILogger<ControlOrchestratorService> logger,
            IControlLoopSignal controlLoopSignal,
            IControlDiagnosticsService diagnostics,
            IRuntimeHealthService runtimeHealth)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _controlLoopSignal = controlLoopSignal;
            _diagnostics = diagnostics;
            _runtimeHealth = runtimeHealth;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Control orchestrator starting");
            _runtimeHealth.MarkControlLoopStarted();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch
            {
            }

            await ReconcileStartupAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunSingleCycleAsync(stoppingToken, "periodic");

                    var delayTask = Task.Delay(_pollInterval, stoppingToken);
                    var signalTask = _controlLoopSignal.WaitForSignalAsync(stoppingToken);
                    var completedTask = await Task.WhenAny(delayTask, signalTask);
                    if (completedTask == signalTask)
                    {
                        var signalReason = await signalTask;
                        _logger.LogInformation("Immediate control reevaluation triggered: {Reason}", signalReason);
                    }
                }
                catch (Exception ex)
                {
                    _runtimeHealth.MarkCycleFailure("control-loop-error");
                    _logger.LogError(ex, "Error in control orchestrator loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("Control orchestrator stopping");
        }

        public async Task RunSingleCycleAsync(CancellationToken cancellationToken, string trigger)
        {
            using var scope = _serviceProvider.CreateScope();
            var sensorService = scope.ServiceProvider.GetRequiredService<ISensorService>();
            var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
            var relayCoordinator = scope.ServiceProvider.GetRequiredService<IRelayCommandCoordinator>();
            var humidityService = scope.ServiceProvider.GetRequiredService<IHumidityService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var loggingService = scope.ServiceProvider.GetRequiredService<ILoggingService>();

            var nowUtc = DateTime.UtcNow;
            var cycleId = Guid.NewGuid().ToString("N");
            var settings = await settingsService.GetSettingsAsync();

            _logger.LogInformation("Control cycle start {CycleId} trigger={Trigger}", cycleId, trigger);

            for (int sensorId = 1; sensorId <= 3; sensorId++)
            {
                var reading = await sensorService.ReadSensorAsync(sensorId);

                if (reading?.IsValid == true)
                {
                    _lastValidSensorTimestamps[sensorId] = nowUtc;
                    _diagnostics.UpdateSensor(sensorId, reading.Timestamp, true, reading.Temperature, reading.Humidity, "valid");

                    var shouldBeOn = await relayService.ShouldRelayBeOnAsync(sensorId, reading.Temperature, null);
                    var applied = await relayCoordinator.RequestRelayStateAsync(sensorId, shouldBeOn, "Temperature Threshold", cancellationToken);

                    _diagnostics.UpdateRelayDecision(
                        sensorId,
                        shouldBeOn,
                        applied,
                        "Temperature Threshold",
                        cycleId,
                        nowUtc);

                    _logger.LogInformation(
                        "Relay decision {CycleId}: relay={RelayId}, source=Temperature, temp={Temperature:F1}, target={Target}, applied={Applied}",
                        cycleId,
                        sensorId,
                        reading.Temperature,
                        shouldBeOn,
                        applied);

                    if (sensorId == 1)
                    {
                        await humidityService.CheckAndApplyHumidityLockoutAsync(sensorId, reading.Humidity);
                    }
                }
                else
                {
                    var lastValid = _lastValidSensorTimestamps.TryGetValue(sensorId, out var timestampUtc)
                        ? timestampUtc
                        : DateTime.MinValue;
                    var isStale = lastValid == DateTime.MinValue || (nowUtc - lastValid) >= _sensorStaleFailsafeWindow;
                    var status = isStale ? "stale-failsafe" : "invalid-preserve";

                    _diagnostics.UpdateSensor(sensorId, nowUtc, false, reading?.Temperature, reading?.Humidity, status);

                    if (isStale)
                    {
                        var applied = await relayCoordinator.RequestRelayStateAsync(sensorId, false, "Sensor Stale Failsafe", cancellationToken);
                        _diagnostics.UpdateRelayDecision(sensorId, false, applied, "Sensor Stale Failsafe", cycleId, nowUtc);
                        _logger.LogWarning(
                            "Applying stale sensor failsafe {CycleId}: relay={RelayId}, lastValidUtc={LastValidUtc}",
                            cycleId,
                            sensorId,
                            lastValid == DateTime.MinValue ? "none" : lastValid.ToString("O"));
                    }
                    else
                    {
                        _logger.LogWarning("Sensor invalid but preserving relay state {CycleId}: relay={RelayId}", cycleId, sensorId);
                    }
                }
            }

            await ApplySchedulerAsync(settings, relayService, relayCoordinator, cycleId, nowUtc, cancellationToken);

            if (nowUtc - _lastHourlySnapshot >= TimeSpan.FromHours(1))
            {
                var latestReadings = await sensorService.GetLatestReadingsAsync();
                var readingDict = latestReadings.ToDictionary(
                    r => r.SensorId,
                    r => ((double?)r.Temperature, (double?)r.Humidity));
                await loggingService.LogHourlySnapshotAsync(readingDict);
                _lastHourlySnapshot = nowUtc;
            }

            if (nowUtc - _lastDailyPrune >= TimeSpan.FromHours(24))
            {
                await loggingService.PruneOldEntriesAsync();
                _lastDailyPrune = nowUtc;
            }

            _logger.LogInformation("Control cycle complete {CycleId}", cycleId);
            _runtimeHealth.MarkSuccessfulCycle(nowUtc);
        }

        private async Task ReconcileStartupAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RunSingleCycleAsync(cancellationToken, "startup-reconcile");
                _logger.LogInformation("Startup relay reconciliation completed");
            }
            catch (Exception ex)
            {
                _runtimeHealth.MarkCycleFailure("startup-reconcile-failed");
                _logger.LogError(ex, "Startup relay reconciliation failed");
            }
        }

        private async Task ApplySchedulerAsync(
            Settings settings,
            IRelayService relayService,
            IRelayCommandCoordinator relayCoordinator,
            string cycleId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            if (!TimeSpan.TryParse(settings.Relay4OnTime, out var onTime) ||
                !TimeSpan.TryParse(settings.Relay4OffTime, out var offTime))
            {
                _logger.LogWarning("Invalid schedule times for relay 4: on={OnTime}, off={OffTime}", settings.Relay4OnTime, settings.Relay4OffTime);
                return;
            }

            var currentTime = DateTime.Now.TimeOfDay;
            bool shouldBeOn;
            if (onTime < offTime)
            {
                shouldBeOn = currentTime >= onTime && currentTime < offTime;
            }
            else
            {
                shouldBeOn = currentTime >= onTime || currentTime < offTime;
            }

            var currentState = await relayService.GetRelayStateAsync(4);
            if (currentState == shouldBeOn)
            {
                _diagnostics.UpdateRelayDecision(4, shouldBeOn, true, "Scheduler (no-op)", cycleId, nowUtc);
                return;
            }

            var applied = await relayCoordinator.RequestRelayStateAsync(4, shouldBeOn, "Scheduler", cancellationToken);
            _diagnostics.UpdateRelayDecision(4, shouldBeOn, applied, "Scheduler", cycleId, nowUtc);

            _logger.LogInformation(
                "Relay decision {CycleId}: relay=4, source=Scheduler, target={Target}, applied={Applied}",
                cycleId,
                shouldBeOn,
                applied);
        }
    }
}