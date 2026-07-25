using System;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.State;

public sealed class LiveProjectionState
{
    private OverviewSnapshotDto _currentOverview = CreateEmpty();

    public event Action<OverviewSnapshotDto>? OverviewUpdated;

    public OverviewSnapshotDto CurrentOverview => _currentOverview;

    public void UpdateOverview(OverviewSnapshotDto snapshot)
    {
        _currentOverview = snapshot;
        OverviewUpdated?.Invoke(snapshot);
    }

    public void ResetOverview(OverviewSnapshotDto snapshot)
    {
        _currentOverview = snapshot;
        OverviewUpdated?.Invoke(snapshot);
    }

    private static OverviewSnapshotDto CreateEmpty()
    {
        return new OverviewSnapshotDto
        {
            CaptureId = "live:inactive",
            Mode = "live",
            SourceLabel = "Live source · not started",
            FilterLabel = "Filter · none",
            MetricMode = "bytes",
            CaptureStatus = "idle",
        };
    }
}
