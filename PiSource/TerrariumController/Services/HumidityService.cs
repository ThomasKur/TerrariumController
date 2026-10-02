using Microsoft.EntityFrameworkCore;
using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class HumidityService : IHumidityService
{
    private readonly AppDbContext _dbContext;
    private readonly IRelayService _relayService;

    public HumidityService(AppDbContext dbContext, IRelayService relayService)
    {
        _dbContext = dbContext;
        _relayService = relayService;
    }

    public async Task EvaluateAsync(Settings settings, LiveSensorState? sensor1, CancellationToken cancellationToken = default)
    {
        if (sensor1 is null || !sensor1.IsValid || !sensor1.HumidityPercent.HasValue)
        {
            return;
        }

        var state = await _dbContext.HumidityLockoutStates
            .SingleOrDefaultAsync(item => item.SensorId == 1, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new HumidityLockoutState { SensorId = 1 };
            _dbContext.HumidityLockoutStates.Add(state);
        }

        state.LastHumidityPercent = sensor1.HumidityPercent;

        var now = DateTime.UtcNow;
        if (state.LockoutUntilUtc.HasValue && state.LockoutUntilUtc.Value > now)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sensor1.HumidityPercent.Value < settings.Sensor1HumidityThresholdPercent)
        {
            state.TriggeredAtUtc = now;
            state.LockoutUntilUtc = now.AddHours(settings.HumidityLockoutHours);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _relayService.PulseRelayAsync(
                relayId: 5,
                duration: TimeSpan.FromSeconds(settings.HumidityPulseSeconds),
                triggerSource: "humidity-lockout",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HumidityLockoutState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _dbContext.HumidityLockoutStates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SensorId == 1, cancellationToken)
            .ConfigureAwait(false);

        return state ?? new HumidityLockoutState { SensorId = 1 };
    }
}
