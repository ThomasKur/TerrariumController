using System.Threading.Channels;

namespace TerrariumController.Services
{
    public interface IRelayCommandCoordinator
    {
        Task<bool> RequestRelayStateAsync(int relayId, bool state, string triggerSource, CancellationToken cancellationToken = default);
    }

    internal enum RelayCommandPriority
    {
        Temperature = 10,
        Scheduler = 20,
        Humidity = 30,
        ManualOverride = 40,
        Failsafe = 50,
        Other = 5
    }

    internal sealed class RelayCommand
    {
        public int RelayId { get; init; }
        public bool State { get; init; }
        public string TriggerSource { get; init; } = string.Empty;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTime EnqueuedAtUtc { get; init; } = DateTime.UtcNow;
        public RelayCommandPriority Priority { get; init; }
    }

    public class RelayCommandCoordinator : BackgroundService, IRelayCommandCoordinator
    {
        private readonly Channel<RelayCommand> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RelayCommandCoordinator> _logger;
        private readonly IControlDiagnosticsService _diagnostics;
        private readonly Dictionary<int, (DateTime ExpiresAtUtc, string Source)> _manualOverrides = new();
        private readonly RelayCommandCoordinatorOptions _options;
        private int _pendingCount;

        public RelayCommandCoordinator(
            IServiceScopeFactory scopeFactory,
            ILogger<RelayCommandCoordinator> logger,
            IControlDiagnosticsService diagnostics,
            RelayCommandCoordinatorOptions options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _diagnostics = diagnostics;
            _options = options;

            _channel = Channel.CreateUnbounded<RelayCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public async Task<bool> RequestRelayStateAsync(int relayId, bool state, string triggerSource, CancellationToken cancellationToken = default)
        {
            var command = new RelayCommand
            {
                RelayId = relayId,
                State = state,
                TriggerSource = triggerSource,
                Priority = MapPriority(triggerSource)
            };

            Interlocked.Increment(ref _pendingCount);
            _diagnostics.UpdateQueueMetrics(Volatile.Read(ref _pendingCount), 0);
            await _channel.Writer.WriteAsync(command, cancellationToken);

            using var registration = cancellationToken.Register(() => command.Completion.TrySetCanceled(cancellationToken));
            return await command.Completion.Task;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Relay command coordinator started");

            await foreach (var command in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var pendingAfterDequeue = Interlocked.Decrement(ref _pendingCount);

                    if (ShouldBlockByManualOverride(command))
                    {
                        _logger.LogInformation(
                            "Skipping relay command for relay {RelayId} due to active manual override (trigger {TriggerSource})",
                            command.RelayId,
                            command.TriggerSource);
                        command.Completion.TrySetResult(false);
                        _diagnostics.UpdateQueueMetrics(Math.Max(pendingAfterDequeue, 0), 0);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
                    await relayService.SetRelayStateAsync(command.RelayId, command.State, command.TriggerSource);

                    if (command.Priority == RelayCommandPriority.ManualOverride)
                    {
                        _manualOverrides[command.RelayId] = (DateTime.UtcNow.Add(_options.ManualOverrideDuration), command.TriggerSource);
                    }

                    var latencyMs = (DateTime.UtcNow - command.EnqueuedAtUtc).TotalMilliseconds;
                    _diagnostics.UpdateQueueMetrics(Math.Max(pendingAfterDequeue, 0), latencyMs);

                    command.Completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _pendingCount, Math.Max(Volatile.Read(ref _pendingCount), 0));
                    _logger.LogError(ex,
                        "Failed processing relay command for relay {RelayId} (state {State}, trigger {TriggerSource})",
                        command.RelayId,
                        command.State,
                        command.TriggerSource);
                    command.Completion.TrySetException(ex);
                }
            }

            _logger.LogInformation("Relay command coordinator stopping");
        }

        private bool ShouldBlockByManualOverride(RelayCommand command)
        {
            if (!_manualOverrides.TryGetValue(command.RelayId, out var manualOverride))
            {
                return false;
            }

            if (DateTime.UtcNow >= manualOverride.ExpiresAtUtc)
            {
                _manualOverrides.Remove(command.RelayId);
                return false;
            }

            return command.Priority < RelayCommandPriority.ManualOverride;
        }

        private static RelayCommandPriority MapPriority(string triggerSource)
        {
            if (triggerSource.Contains("Manual Override", StringComparison.OrdinalIgnoreCase))
            {
                return RelayCommandPriority.ManualOverride;
            }

            if (triggerSource.Contains("Humidity", StringComparison.OrdinalIgnoreCase))
            {
                return RelayCommandPriority.Humidity;
            }

            if (triggerSource.Contains("Scheduler", StringComparison.OrdinalIgnoreCase))
            {
                return RelayCommandPriority.Scheduler;
            }

            if (triggerSource.Contains("Failsafe", StringComparison.OrdinalIgnoreCase))
            {
                return RelayCommandPriority.Failsafe;
            }

            if (triggerSource.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
            {
                return RelayCommandPriority.Temperature;
            }

            return RelayCommandPriority.Other;
        }
    }
}