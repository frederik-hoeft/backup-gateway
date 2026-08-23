using System.Collections.Concurrent;

namespace BackupGateway.Web.Services.Observability;

public sealed class LifecycleMetrics
{
    private readonly ConcurrentDictionary<LifecycleMetricKey, LifecycleMetricValue> _metrics = new();

    internal void Record(string targetId, string operation, string outcome, TimeSpan duration)
    {
        LifecycleMetricKey key = new(targetId, operation, outcome);
        LifecycleMetricValue value = _metrics.GetOrAdd(key, static _ => new LifecycleMetricValue());
        value.Record(duration);
    }

    internal IReadOnlyList<LifecycleMetricSnapshot> Snapshot() =>
        [.. _metrics
            .Select(pair => pair.Value.Snapshot(pair.Key))
            .OrderBy(snapshot => snapshot.TargetId, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Operation, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Outcome, StringComparer.Ordinal)];

    private sealed class LifecycleMetricValue
    {
        private long _count;
        private long _durationTicks;

        internal void Record(TimeSpan duration)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _durationTicks, duration.Ticks);
        }

        public LifecycleMetricSnapshot Snapshot(LifecycleMetricKey key) => new(
            key.TargetId,
            key.Operation,
            key.Outcome,
            Interlocked.Read(ref _count),
            TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks)).TotalSeconds);
    }

    private readonly record struct LifecycleMetricKey(string TargetId, string Operation, string Outcome);
}

internal sealed record LifecycleMetricSnapshot(
    string TargetId,
    string Operation,
    string Outcome,
    long Count,
    double DurationSeconds);
