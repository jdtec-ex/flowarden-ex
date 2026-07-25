using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;
using Flowarden.Ui.State;
using Flowarden.Ui.ViewModels.Overview;

namespace Flowarden.Ui.ViewModels;

public sealed partial class OverviewPageViewModel : ViewModelBase
{
    private readonly ProjectionClient? _projectionClient;
    private readonly LiveProjectionState? _liveProjectionState;
    private readonly ProjectionSettingsState _projectionSettings;
    private readonly bool _isDesignTime;
    private string? _modeOverride;
    private bool _isThroughputHoverVisible;
    private double _throughputHoverMarkerLeft;
    private double _throughputHoverPanelLeft;
    private double _throughputHoverPanelTop;
    private double _throughputHoverPlotHeight;
    private string _throughputHoverTimeLabel = "--:--:--";
    private string _throughputHoverInboundLabel = "0 B/s";
    private string _throughputHoverOutboundLabel = "0 B/s";
    private IReadOnlyList<OverviewMetricRowViewModel> _topHostRows =
        Array.Empty<OverviewMetricRowViewModel>();
    private IReadOnlyList<OverviewMetricRowViewModel> _topServiceRows =
        Array.Empty<OverviewMetricRowViewModel>();
    private IReadOnlyList<OverviewRegionRowViewModel> _topRegionRows =
        Array.Empty<OverviewRegionRowViewModel>();
    private IReadOnlyList<OverviewRegionMarkerViewModel> _topRegionMarkers =
        Array.Empty<OverviewRegionMarkerViewModel>();
    private IReadOnlyList<OverviewConnectionRowViewModel> _topConnectionRows =
        Array.Empty<OverviewConnectionRowViewModel>();
    private OverviewRegionMarkerViewModel? _primaryRegionMarker;
    private OverviewRegionMarkerViewModel? _secondaryRegionMarker;
    private OverviewRegionMarkerViewModel? _tertiaryRegionMarker;
    private ulong _maxTimelineValue;
    private string _outboundPathData = string.Empty;
    private string _inboundPathData = string.Empty;
    private string _outboundAreaPathData = string.Empty;

    public OverviewPageViewModel()
        : this(
            projectionClient: null,
            liveProjectionState: null,
            projectionSettings: new ProjectionSettingsState(),
            isDesignTime: true
        )
    {
    }

    public OverviewPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState,
        ProjectionSettingsState projectionSettings
    )
        : this(projectionClient, liveProjectionState, projectionSettings, isDesignTime: false)
    {
    }

    private OverviewPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState,
        ProjectionSettingsState projectionSettings,
        bool isDesignTime
    )
    {
        _projectionClient = projectionClient;
        _liveProjectionState = liveProjectionState;
        _projectionSettings = projectionSettings;
        _isDesignTime = isDesignTime;
        Snapshot = isDesignTime
            ? OverviewSeedData.CreateSeedSnapshot()
            : OverviewSeedData.CreateInitialRuntimeSnapshot();
        StatusCards = OverviewRankingsBuilder.BuildStatusCards(Snapshot, ModeLabel);
        RebuildSnapshotDerivedState(Snapshot);
        if (!isDesignTime && _liveProjectionState is not null)
        {
            _liveProjectionState.OverviewUpdated += ApplySnapshot;
        }
    }

    public OverviewSnapshotDto Snapshot { get; private set; }

    public IReadOnlyList<OverviewStatusCardViewModel> StatusCards { get; private set; }

    public string ModeLabel => OverviewRankingsBuilder.ResolveModeLabel(_modeOverride, Snapshot);

    public string HeroTitle => "Traffic Overview";

    public string HeroSummary => BuildHeroSummary();

    public string SourceSummary => Snapshot.SourceLabel;

    public string FilterSummary => Snapshot.FilterLabel;

    public string MetricModeSummary => $"Metric · {Snapshot.MetricMode}";

    public string ActiveSourceLabel =>
        Snapshot.SourceLabel
            .Replace("Live source · ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Offline source · ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Live source", "not started", StringComparison.OrdinalIgnoreCase)
            .Replace("Offline source", "not started", StringComparison.OrdinalIgnoreCase);

    public string TopHostLabel => Snapshot.TopHosts.FirstOrDefault()?.Host ?? "-";

    public string TopServiceLabel => Snapshot.TopServices.FirstOrDefault()?.Name ?? "-";

    public string TopServiceDisplayLabel => TopServiceLabel.ToUpperInvariant();

    public string PacketsLabel => OverviewFormatting.FormatCount(Snapshot.Totals.Packets);

    public string BytesLabel => OverviewFormatting.FormatBytes(Snapshot.Totals.Bytes);

    public string PacketsHealthLabel =>
        OverviewRankingsBuilder.ResolveMetricStateLabel(Snapshot.Totals.Packets > 0, Snapshot);

    public string BytesHealthLabel =>
        OverviewRankingsBuilder.ResolveMetricStateLabel(Snapshot.Totals.Bytes > 0, Snapshot);

    public string InboundSummary => OverviewFormatting.FormatByteRate(Snapshot.Totals.BytesIn);

    public string OutboundSummary => OverviewFormatting.FormatByteRate(Snapshot.Totals.BytesOut);

    public bool IsThroughputHoverVisible
    {
        get => _isThroughputHoverVisible;
        private set => SetProperty(ref _isThroughputHoverVisible, value);
    }

    public double ThroughputHoverMarkerLeft
    {
        get => _throughputHoverMarkerLeft;
        private set => SetProperty(ref _throughputHoverMarkerLeft, value);
    }

    public double ThroughputHoverPanelLeft
    {
        get => _throughputHoverPanelLeft;
        private set => SetProperty(ref _throughputHoverPanelLeft, value);
    }

    public double ThroughputHoverPanelTop
    {
        get => _throughputHoverPanelTop;
        private set => SetProperty(ref _throughputHoverPanelTop, value);
    }

    public double ThroughputHoverPlotHeight
    {
        get => _throughputHoverPlotHeight;
        private set => SetProperty(ref _throughputHoverPlotHeight, value);
    }

    public string ThroughputHoverTimeLabel
    {
        get => _throughputHoverTimeLabel;
        private set => SetProperty(ref _throughputHoverTimeLabel, value);
    }

    public string ThroughputHoverInboundLabel
    {
        get => _throughputHoverInboundLabel;
        private set => SetProperty(ref _throughputHoverInboundLabel, value);
    }

    public string ThroughputHoverOutboundLabel
    {
        get => _throughputHoverOutboundLabel;
        private set => SetProperty(ref _throughputHoverOutboundLabel, value);
    }

    public string HeroLegendPrimary => "Outbound";

    public string HeroLegendSecondary => "Inbound";

    public string WorldMapPathData => MapAssetProvider.WorldMapPathData;

    public bool HasTimeline => Snapshot.TimelinePoints.Count >= 1;

    public bool HasNoTimeline => !HasTimeline;

    public string TimelineEmptyTitle => "Timeline unavailable";

    public string TimelineEmptyMessage =>
        "Resident core has not produced enough tick history yet. Start a capture session to render a real timeline.";

    public string AxisLabelStart => FormatAxisTimeAt(0);

    public string AxisLabelMidLeft => FormatAxisTimeAt(1);

    public string AxisLabelCenter => FormatAxisTimeAt(2);

    public string AxisLabelMidRight => FormatAxisTimeAt(3);

    public string AxisLabelEnd => FormatAxisTimeAt(4);

    public string YAxisTopLabel => OverviewFormatting.FormatByteRate(MaxTimelineValue);

    public string YAxisUpperMidLabel => OverviewFormatting.FormatByteRate(MaxTimelineValue * 3 / 4);

    public string YAxisMidLabel => OverviewFormatting.FormatByteRate(MaxTimelineValue / 2);

    public string YAxisLowerMidLabel => OverviewFormatting.FormatByteRate(MaxTimelineValue / 4);

    public string YAxisZeroLabel => "0 B/s";

    public string OutboundPathData => _outboundPathData;

    public string InboundPathData => _inboundPathData;

    public string OutboundAreaPathData => _outboundAreaPathData;

    public string DestinationPlaceholderMessage => Snapshot.DestinationMap.Message;

    public string DestinationPlaceholderTitle => "Destination Distribution Future Slot";

    public string DestinationPlaceholderState => Snapshot.DestinationMap.State;

    public string DestinationPlaceholderHint =>
        "This area is reserved for future geographic or destination-distribution visualization driven by destination projection data.";

    public IReadOnlyList<OverviewMetricRowViewModel> TopHostRows => _topHostRows;

    public bool HasTopHostRows => TopHostRows.Count > 0;

    public bool HasNoTopHostRows => !HasTopHostRows;

    public IReadOnlyList<OverviewMetricRowViewModel> TopServiceRows => _topServiceRows;

    public bool HasTopServiceRows => TopServiceRows.Count > 0;

    public bool HasNoTopServiceRows => !HasTopServiceRows;

    public IReadOnlyList<OverviewRegionRowViewModel> TopRegionRows => _topRegionRows;

    public bool HasTopRegionRows => TopRegionRows.Count > 0;

    public bool HasNoTopRegionRows => !HasTopRegionRows;

    public IReadOnlyList<OverviewRegionMarkerViewModel> TopRegionMarkers => _topRegionMarkers;

    public bool HasTopRegionMarkers => TopRegionMarkers.Count > 0;

    public OverviewRegionMarkerViewModel? PrimaryRegionMarker => _primaryRegionMarker;

    public OverviewRegionMarkerViewModel? SecondaryRegionMarker => _secondaryRegionMarker;

    public OverviewRegionMarkerViewModel? TertiaryRegionMarker => _tertiaryRegionMarker;

    public bool HasPrimaryRegionMarker => PrimaryRegionMarker is not null;

    public bool HasSecondaryRegionMarker => SecondaryRegionMarker is not null;

    public bool HasTertiaryRegionMarker => TertiaryRegionMarker is not null;

    public IReadOnlyList<OverviewConnectionRowViewModel> TopConnectionRows => _topConnectionRows;

    public bool HasTopConnectionRows => TopConnectionRows.Count > 0;

    public bool HasNoTopConnectionRows => !HasTopConnectionRows;

    private ulong MaxTimelineValue => _maxTimelineValue;

    public async Task LoadAsync()
    {
        if (_projectionClient is null || _isDesignTime)
        {
            return;
        }

        ApplySnapshot(await _projectionClient.GetLatestOverviewAsync(_projectionSettings.TopN));
    }

    public void SetMode(string mode)
    {
        _modeOverride = mode;
        StatusCards = OverviewRankingsBuilder.BuildStatusCards(Snapshot, ModeLabel);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(HeroSummary));
    }

    public void UpdateThroughputHover(double x, double y, double plotWidth, double plotHeight)
    {
        if (!HasTimeline || Snapshot.TimelinePoints.Count == 0 || plotWidth <= 0 || plotHeight <= 0)
        {
            ClearThroughputHover();
            return;
        }

        var normalizedX = Math.Clamp(x, 0, plotWidth);
        var index = Snapshot.TimelinePoints.Count == 1
            ? 0
            : (int)Math.Round(normalizedX / plotWidth * (Snapshot.TimelinePoints.Count - 1));
        index = Math.Clamp(index, 0, Snapshot.TimelinePoints.Count - 1);

        var markerLeft = Snapshot.TimelinePoints.Count == 1
            ? 0
            : index / (double)(Snapshot.TimelinePoints.Count - 1) * plotWidth;
        var tooltipWidth = 172d;
        var tooltipHeight = 88d;
        var panelLeft = markerLeft + 12d;
        if (panelLeft + tooltipWidth > plotWidth)
        {
            panelLeft = markerLeft - tooltipWidth - 12d;
        }

        var point = Snapshot.TimelinePoints[index];
        ThroughputHoverMarkerLeft = markerLeft;
        ThroughputHoverPanelLeft = Math.Clamp(panelLeft, 0, Math.Max(0, plotWidth - tooltipWidth));
        ThroughputHoverPanelTop = Math.Clamp(y + 10d, 0, Math.Max(0, plotHeight - tooltipHeight));
        ThroughputHoverPlotHeight = plotHeight;
        ThroughputHoverTimeLabel = OverviewFormatting.FormatPacketTimestamp(point.Timestamp);
        ThroughputHoverInboundLabel = OverviewFormatting.FormatByteRate(point.InboundBytes);
        ThroughputHoverOutboundLabel = OverviewFormatting.FormatByteRate(point.OutboundBytes);
        IsThroughputHoverVisible = true;
    }

    public void ClearThroughputHover()
    {
        IsThroughputHoverVisible = false;
    }

    private string FormatAxisTimeAt(int index)
    {
        if (Snapshot.TimelinePoints.Count == 0)
        {
            return "--:--:--";
        }

        var timelineIndex = index switch
        {
            0 => 0,
            1 => Snapshot.TimelinePoints.Count / 4,
            2 => Snapshot.TimelinePoints.Count / 2,
            3 => (Snapshot.TimelinePoints.Count * 3) / 4,
            _ => Snapshot.TimelinePoints.Count - 1,
        };

        return OverviewFormatting.FormatAxisTime(
            Snapshot.TimelinePoints[timelineIndex].Timestamp.Seconds
        );
    }

    private void RebuildSnapshotDerivedState(OverviewSnapshotDto snapshot)
    {
        _topHostRows = OverviewRankingsBuilder.BuildTopHostRows(snapshot.TopHosts);
        _topServiceRows = OverviewRankingsBuilder.BuildTopServiceRows(snapshot.TopServices);
        _topRegionRows = OverviewRankingsBuilder.BuildTopRegionRows(snapshot.TopDestinations);
        _topRegionMarkers = OverviewMapProjection.BuildTopRegionMarkers(snapshot.TopDestinations);
        _primaryRegionMarker = _topRegionMarkers.Count > 0 ? _topRegionMarkers[0] : null;
        _secondaryRegionMarker = _topRegionMarkers.Count > 1 ? _topRegionMarkers[1] : null;
        _tertiaryRegionMarker = _topRegionMarkers.Count > 2 ? _topRegionMarkers[2] : null;
        _topConnectionRows = OverviewRankingsBuilder.BuildTopConnectionRows(
            snapshot.TopConnections,
            snapshot.TopHosts
        );
        _maxTimelineValue = OverviewChartPaths.CalculateMaxTimelineValue(snapshot.TimelinePoints);
        _outboundPathData = OverviewChartPaths.BuildTimelinePath(
            snapshot.TimelinePoints,
            _maxTimelineValue,
            selectOutbound: true
        );
        _inboundPathData = OverviewChartPaths.BuildTimelinePath(
            snapshot.TimelinePoints,
            _maxTimelineValue,
            selectOutbound: false
        );
        _outboundAreaPathData = OverviewChartPaths.BuildAreaPath(_outboundPathData);
    }

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        StatusCards = OverviewRankingsBuilder.BuildStatusCards(snapshot, ModeLabel);
        RebuildSnapshotDerivedState(snapshot);
        NotifySnapshotPropertiesChanged();
        if (!HasTimeline)
        {
            ClearThroughputHover();
        }
    }

    private void NotifySnapshotPropertiesChanged()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(HeroSummary));
        OnPropertyChanged(nameof(ActiveSourceLabel));
        OnPropertyChanged(nameof(TopHostLabel));
        OnPropertyChanged(nameof(TopServiceLabel));
        OnPropertyChanged(nameof(TopServiceDisplayLabel));
        OnPropertyChanged(nameof(PacketsLabel));
        OnPropertyChanged(nameof(BytesLabel));
        OnPropertyChanged(nameof(PacketsHealthLabel));
        OnPropertyChanged(nameof(BytesHealthLabel));
        OnPropertyChanged(nameof(DestinationPlaceholderMessage));
        OnPropertyChanged(nameof(DestinationPlaceholderState));
        OnPropertyChanged(nameof(TopHostRows));
        OnPropertyChanged(nameof(HasTopHostRows));
        OnPropertyChanged(nameof(HasNoTopHostRows));
        OnPropertyChanged(nameof(TopServiceRows));
        OnPropertyChanged(nameof(HasTopServiceRows));
        OnPropertyChanged(nameof(HasNoTopServiceRows));
        OnPropertyChanged(nameof(TopRegionRows));
        OnPropertyChanged(nameof(HasTopRegionRows));
        OnPropertyChanged(nameof(HasNoTopRegionRows));
        OnPropertyChanged(nameof(TopRegionMarkers));
        OnPropertyChanged(nameof(HasTopRegionMarkers));
        OnPropertyChanged(nameof(PrimaryRegionMarker));
        OnPropertyChanged(nameof(SecondaryRegionMarker));
        OnPropertyChanged(nameof(TertiaryRegionMarker));
        OnPropertyChanged(nameof(HasPrimaryRegionMarker));
        OnPropertyChanged(nameof(HasSecondaryRegionMarker));
        OnPropertyChanged(nameof(HasTertiaryRegionMarker));
        OnPropertyChanged(nameof(TopConnectionRows));
        OnPropertyChanged(nameof(HasTopConnectionRows));
        OnPropertyChanged(nameof(HasNoTopConnectionRows));
        OnPropertyChanged(nameof(HasTimeline));
        OnPropertyChanged(nameof(HasNoTimeline));
        OnPropertyChanged(nameof(TimelineEmptyTitle));
        OnPropertyChanged(nameof(TimelineEmptyMessage));
        OnPropertyChanged(nameof(AxisLabelStart));
        OnPropertyChanged(nameof(AxisLabelMidLeft));
        OnPropertyChanged(nameof(AxisLabelCenter));
        OnPropertyChanged(nameof(AxisLabelMidRight));
        OnPropertyChanged(nameof(AxisLabelEnd));
        OnPropertyChanged(nameof(YAxisTopLabel));
        OnPropertyChanged(nameof(YAxisUpperMidLabel));
        OnPropertyChanged(nameof(YAxisMidLabel));
        OnPropertyChanged(nameof(YAxisLowerMidLabel));
        OnPropertyChanged(nameof(YAxisZeroLabel));
        OnPropertyChanged(nameof(OutboundPathData));
        OnPropertyChanged(nameof(InboundPathData));
        OnPropertyChanged(nameof(OutboundAreaPathData));
    }

    private string BuildHeroSummary()
    {
        var timestamp = Snapshot.LastPacketTimestamp;
        if (timestamp is null || timestamp.Seconds <= 0)
        {
            return $"Sequence {Snapshot.Sequence} · waiting for packets";
        }

        return $"Sequence {Snapshot.Sequence} · last packet {OverviewFormatting.FormatPacketTimestamp(timestamp)}";
    }
}
