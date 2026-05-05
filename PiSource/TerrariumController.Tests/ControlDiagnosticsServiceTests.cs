using TerrariumController.Services;
using Xunit;

namespace TerrariumController.Tests;

public class ControlDiagnosticsServiceTests
{
    [Fact]
    public void GetSnapshot_ReturnsEmptyCollections_ByDefault()
    {
        var service = new ControlDiagnosticsService();
        var snapshot = service.GetSnapshot();

        Assert.Empty(snapshot.Sensors);
        Assert.Empty(snapshot.Relays);
        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(0.0, snapshot.LastCommandLatencyMs);
    }

    [Fact]
    public void UpdateSensor_AppearsInSnapshot()
    {
        var service = new ControlDiagnosticsService();
        var timestamp = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        service.UpdateSensor(1, timestamp, isValid: true, temperature: 28.5, humidity: 65.0, status: "valid");

        var snapshot = service.GetSnapshot();
        Assert.Single(snapshot.Sensors);
        var sensor = snapshot.Sensors[1];
        Assert.Equal(1, sensor.SensorId);
        Assert.Equal(timestamp, sensor.LastTimestampUtc);
        Assert.True(sensor.IsValid);
        Assert.Equal(28.5, sensor.Temperature);
        Assert.Equal(65.0, sensor.Humidity);
        Assert.Equal("valid", sensor.Status);
    }

    [Fact]
    public void UpdateSensor_OverwritesPreviousEntryForSameSensor()
    {
        var service = new ControlDiagnosticsService();

        service.UpdateSensor(2, DateTime.UtcNow, true, 27.0, 60.0, "valid");
        service.UpdateSensor(2, DateTime.UtcNow, false, null, null, "stale-failsafe");

        var snapshot = service.GetSnapshot();
        Assert.Equal("stale-failsafe", snapshot.Sensors[2].Status);
        Assert.Null(snapshot.Sensors[2].Temperature);
    }

    [Fact]
    public void UpdateRelayDecision_AppearsInSnapshot()
    {
        var service = new ControlDiagnosticsService();
        var timestamp = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        service.UpdateRelayDecision(3, targetState: true, applied: true, reason: "Temperature Threshold", cycleId: "abc123", timestampUtc: timestamp);

        var snapshot = service.GetSnapshot();
        Assert.Single(snapshot.Relays);
        var relay = snapshot.Relays[3];
        Assert.Equal(3, relay.RelayId);
        Assert.True(relay.TargetState);
        Assert.True(relay.Applied);
        Assert.Equal("Temperature Threshold", relay.Reason);
        Assert.Equal("abc123", relay.CycleId);
        Assert.Equal(timestamp, relay.LastDecisionUtc);
    }

    [Fact]
    public void UpdateRelayDecision_OverwritesPreviousEntryForSameRelay()
    {
        var service = new ControlDiagnosticsService();

        service.UpdateRelayDecision(1, true, true, "Temperature Threshold", "cycle-1", DateTime.UtcNow);
        service.UpdateRelayDecision(1, false, true, "Scheduler", "cycle-2", DateTime.UtcNow);

        var snapshot = service.GetSnapshot();
        Assert.Equal("Scheduler", snapshot.Relays[1].Reason);
        Assert.False(snapshot.Relays[1].TargetState);
    }

    [Fact]
    public void UpdateQueueMetrics_ReflectedInSnapshot()
    {
        var service = new ControlDiagnosticsService();

        service.UpdateQueueMetrics(queueDepth: 5, lastCommandLatencyMs: 123.4);

        var snapshot = service.GetSnapshot();
        Assert.Equal(5, snapshot.QueueDepth);
        Assert.Equal(123.4, snapshot.LastCommandLatencyMs);
    }

    [Fact]
    public void UpdateQueueMetrics_OverwritesPreviousMetrics()
    {
        var service = new ControlDiagnosticsService();

        service.UpdateQueueMetrics(10, 50.0);
        service.UpdateQueueMetrics(2, 12.5);

        var snapshot = service.GetSnapshot();
        Assert.Equal(2, snapshot.QueueDepth);
        Assert.Equal(12.5, snapshot.LastCommandLatencyMs);
    }

    [Fact]
    public void GetSnapshot_IncludesMultipleSensorsAndRelays()
    {
        var service = new ControlDiagnosticsService();

        service.UpdateSensor(1, DateTime.UtcNow, true, 27.0, 65.0, "valid");
        service.UpdateSensor(2, DateTime.UtcNow, true, 29.0, 55.0, "valid");
        service.UpdateSensor(3, DateTime.UtcNow, false, null, null, "stale-failsafe");
        service.UpdateRelayDecision(1, true, true, "Temperature Threshold", "c1", DateTime.UtcNow);
        service.UpdateRelayDecision(4, false, false, "Scheduler", "c1", DateTime.UtcNow);

        var snapshot = service.GetSnapshot();
        Assert.Equal(3, snapshot.Sensors.Count);
        Assert.Equal(2, snapshot.Relays.Count);
    }
}
