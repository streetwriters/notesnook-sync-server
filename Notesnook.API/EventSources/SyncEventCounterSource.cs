using System;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;

[EventSource(Name = "Notesnook.API.EventCounter.Sync")]
public sealed class SyncEventCounterSource : EventSource
{
    public static readonly SyncEventCounterSource Log = new();

    private readonly Meter meter = new("Notesnook.API.Metrics.Sync", "1.0.0");
    private readonly Counter<int> fetchCounter;
    private readonly Counter<int> pushCounter;
    private readonly Counter<int> pushV2Counter;
    private readonly Counter<int> fetchV2Counter;
    private readonly Histogram<long> fetchV2Duration;
    private readonly Histogram<long> pushV2Duration;
    private SyncEventCounterSource()
    {
        fetchCounter = meter.CreateCounter<int>("sync.fetches", "fetches", "Total fetches");
        pushCounter = meter.CreateCounter<int>("sync.pushes", "pushes", "Total pushes");
        fetchV2Counter = meter.CreateCounter<int>("sync.v2.fetches", "fetches", "Total v2 fetches");
        pushV2Counter = meter.CreateCounter<int>("sync.v2.pushes", "pushes", "Total v2 pushes");
        fetchV2Duration = meter.CreateHistogram<long>("sync.v2.fetch_duration");
        pushV2Duration = meter.CreateHistogram<long>("sync.v2.push_duration");
    }

    public void Fetch() => fetchCounter.Add(1);
    public void FetchV2() => fetchV2Counter.Add(1);
    public void PushV2() => pushV2Counter.Add(1);
    public void Push() => pushCounter.Add(1);
    public void RecordFetchDuration(long durationMs) => fetchV2Duration.Record(durationMs);
    public void RecordPushDuration(long durationMs) => pushV2Duration.Record(durationMs);

    protected override void Dispose(bool disposing)
    {
        meter.Dispose();
        base.Dispose(disposing);
    }
}