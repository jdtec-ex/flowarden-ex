using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;
using Flowarden.Ui.State;

namespace Flowarden.Ui.ViewModels;

public sealed partial class OverviewPageViewModel : ViewModelBase
{
    private const string DataAccentBrush = "#D9A84E";
    private const string NeutralMetricBrush = "#CBC4D2";

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
        Snapshot = isDesignTime ? CreateSeedSnapshot() : CreateInitialRuntimeSnapshot();
        StatusCards = BuildStatusCards(Snapshot, ModeLabel);
        RebuildSnapshotDerivedState(Snapshot);
        if (!isDesignTime && _liveProjectionState is not null)
        {
            _liveProjectionState.OverviewUpdated += ApplySnapshot;
        }
    }

    public OverviewSnapshotDto Snapshot { get; private set; }

    public IReadOnlyList<OverviewStatusCardViewModel> StatusCards { get; private set; }

    public string ModeLabel => ResolveModeLabel(_modeOverride, Snapshot);

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

    public string PacketsLabel => FormatCount(Snapshot.Totals.Packets);

    public string BytesLabel => FormatBytes(Snapshot.Totals.Bytes);

    public string PacketsHealthLabel => ResolveMetricStateLabel(Snapshot.Totals.Packets > 0, Snapshot);

    public string BytesHealthLabel => ResolveMetricStateLabel(Snapshot.Totals.Bytes > 0, Snapshot);

    public string InboundSummary => FormatByteRate(Snapshot.Totals.BytesIn);

    public string OutboundSummary => FormatByteRate(Snapshot.Totals.BytesOut);

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

    public string YAxisTopLabel => FormatByteRate(MaxTimelineValue);

    public string YAxisUpperMidLabel => FormatByteRate(MaxTimelineValue * 3 / 4);

    public string YAxisMidLabel => FormatByteRate(MaxTimelineValue / 2);

    public string YAxisLowerMidLabel => FormatByteRate(MaxTimelineValue / 4);

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
        StatusCards = BuildStatusCards(Snapshot, ModeLabel);
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
        ThroughputHoverTimeLabel = FormatPacketTimestamp(point.Timestamp);
        ThroughputHoverInboundLabel = FormatByteRate(point.InboundBytes);
        ThroughputHoverOutboundLabel = FormatByteRate(point.OutboundBytes);
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

        var axisSeconds = Snapshot.TimelinePoints[timelineIndex].Timestamp.Seconds;

        if (axisSeconds <= 0)
        {
            return "--:--:--";
        }

        var localTime = DateTimeOffset
            .FromUnixTimeSeconds(axisSeconds)
            .ToLocalTime();

        return localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildTimelinePath(
        IReadOnlyList<TimelinePointDto> timelinePoints,
        ulong maxTimelineValue,
        bool selectOutbound
    )
    {
        if (timelinePoints.Count == 0)
        {
            return string.Empty;
        }

        const double width = 640;
        const double height = 84;
        var maxValue = (double)Math.Max(maxTimelineValue, 1);

        if (timelinePoints.Count == 1)
        {
            var point = timelinePoints[0];
            var value = selectOutbound ? point.OutboundBytes : point.InboundBytes;
            var y = height - ((double)value / maxValue * height);
            return FormattableString.Invariant($"M 0,{y:0.##} L {width:0.##},{y:0.##}");
        }

        var step = width / (timelinePoints.Count - 1);
        var coordinates = new List<(double X, double Y)>(timelinePoints.Count);

        for (var i = 0; i < timelinePoints.Count; i++)
        {
            var point = timelinePoints[i];
            var value = selectOutbound ? point.OutboundBytes : point.InboundBytes;
            var x = i * step;
            var y = height - ((double)value / maxValue * height);
            coordinates.Add((x, y));
        }

        return BuildSmoothPath(coordinates);
    }

    private static string BuildAreaPath(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        const double width = 640;
        const double height = 84;
        return $"{line} L {width.ToString(CultureInfo.InvariantCulture)},{height.ToString(CultureInfo.InvariantCulture)} L 0,{height.ToString(CultureInfo.InvariantCulture)} Z";
    }

    private static string FormatByteRate(ulong bytes)
    {
        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:0.##} MB/s";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.##} KB/s";
        }

        return $"{bytes} B/s";
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1_099_511_627_776)
        {
            return $"{bytes / 1_099_511_627_776.0:0.##} TB";
        }

        if (bytes >= 1_073_741_824)
        {
            return $"{bytes / 1_073_741_824.0:0.##} GB";
        }

        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:0.##} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.##} KB";
        }

        return $"{bytes} B";
    }

    private static IReadOnlyList<OverviewMetricRowViewModel> BuildTopHostRows(IReadOnlyList<HostRowDto> rows)
    {
        var maxPackets = rows.Count == 0 ? 0 : rows.Max(row => row.Packets);
        return rows
            .Select(row => new OverviewMetricRowViewModel(
                FormatAddressWithOwner(row.Host, row.CountryLabel),
                FormatCount(row.Packets),
                CalculateBarWidth(row.Packets, maxPackets),
                NeutralMetricBrush
            ))
            .ToArray();
    }

    private static IReadOnlyList<OverviewMetricRowViewModel> BuildTopServiceRows(IReadOnlyList<ServiceRowDto> rows)
    {
        var totalBytes = rows.Aggregate<ServiceRowDto, ulong>(0, (current, row) => current + row.Bytes);
        var maxBytes = rows.Count == 0 ? 0 : rows.Max(row => row.Bytes);
        return rows
            .Select(row => new OverviewMetricRowViewModel(
                row.Name.ToUpperInvariant(),
                totalBytes == 0 ? FormatBytes(row.Bytes) : $"{(double)row.Bytes / totalBytes:0%}",
                CalculateBarWidth(row.Bytes, maxBytes),
                DataAccentBrush
            ))
            .ToArray();
    }

    private static IReadOnlyList<OverviewRegionRowViewModel> BuildTopRegionRows(IReadOnlyList<DestinationSummaryDto> rows)
    {
        return rows
            .Select(row => new OverviewRegionRowViewModel(
                string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label,
                row.Ratio.ToString("P0", CultureInfo.InvariantCulture),
                DataAccentBrush
            ))
            .ToArray();
    }

    private static IReadOnlyList<OverviewRegionMarkerViewModel> BuildTopRegionMarkers(IReadOnlyList<DestinationSummaryDto> rows)
    {
        var maxBytes = rows.Count == 0 ? 0 : rows.Max(row => row.Bytes);
        return rows
            .Select(row => CreateRegionMarker(row, maxBytes))
            .Where(marker => marker is not null)
            .Cast<OverviewRegionMarkerViewModel>()
            .ToArray();
    }

    private static OverviewRegionMarkerViewModel? CreateRegionMarker(
        DestinationSummaryDto row,
        ulong maxBytes
    )
    {
        var countryCode = string.IsNullOrWhiteSpace(row.CountryCode)
            ? ExtractOwnerCode(row.CountryLabel)
            : row.CountryCode;
        if (!TryGetCountryCoordinate(countryCode, out var longitude, out var latitude))
        {
            return null;
        }

        var (x, y) = ProjectEqualEarth(longitude, latitude);
        var normalized = maxBytes == 0 ? 0.0 : Math.Sqrt(row.Bytes / (double)maxBytes);
        var size = 7.0 + normalized * 11.0;
        return new OverviewRegionMarkerViewModel(
            string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label,
            row.Ratio.ToString("P0", CultureInfo.InvariantCulture),
            FormatBytes(row.Bytes),
            x - size / 2,
            y - size / 2,
            size,
            DataAccentBrush
        );
    }

    private static IReadOnlyList<OverviewConnectionRowViewModel> BuildTopConnectionRows(
        IReadOnlyList<ConnectionRowDto> rows,
        IReadOnlyList<HostRowDto> hosts
    )
    {
        var countryByHost = hosts
            .Where(host => !string.IsNullOrWhiteSpace(host.CountryLabel))
            .GroupBy(host => host.Host, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ExtractOwnerCode(group.First().CountryLabel),
                StringComparer.OrdinalIgnoreCase
            );

        return rows
            .Select(row => new OverviewConnectionRowViewModel(
                FormatAddressWithOwner(row.SourceAddress, countryByHost.TryGetValue(row.SourceAddress, out var sourceOwner) ? sourceOwner : string.Empty),
                FormatAddressWithOwner(row.DestinationAddress, countryByHost.TryGetValue(row.DestinationAddress, out var destinationOwner) ? destinationOwner : string.Empty),
                FormatBytes(row.Bytes)
            ))
            .ToArray();
    }

    private static double CalculateBarWidth(ulong value, ulong maxValue)
    {
        if (value == 0 || maxValue == 0)
        {
            return 0;
        }

        return Math.Max(8, Math.Min(140, value / (double)maxValue * 140));
    }

    private static string FormatAddressWithOwner(string address, string ownerLabel)
    {
        var ownerCode = ExtractOwnerCode(ownerLabel);
        return string.IsNullOrWhiteSpace(ownerCode) ? address : $"{address}({ownerCode})";
    }

    private static string ExtractOwnerCode(string ownerLabel)
    {
        if (string.IsNullOrWhiteSpace(ownerLabel))
        {
            return string.Empty;
        }

        var normalized = ownerLabel.Trim();
        var separatorIndex = normalized.IndexOf('·');
        if (separatorIndex < 0)
        {
            separatorIndex = normalized.IndexOf(' ');
        }

        var code = separatorIndex > 0 ? normalized[..separatorIndex] : normalized;
        code = code.Trim();
        return code.Length is >= 2 and <= 6 ? code.ToUpperInvariant() : string.Empty;
    }

    private static string FormatCount(ulong value)
    {
        if (value >= 1_000_000_000)
        {
            return $"{value / 1_000_000_000.0:0.##}B";
        }

        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000.0:0.##}M";
        }

        if (value >= 1_000)
        {
            return $"{value / 1_000.0:0.##}K";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildSnapshotDerivedState(OverviewSnapshotDto snapshot)
    {
        _topHostRows = BuildTopHostRows(snapshot.TopHosts);
        _topServiceRows = BuildTopServiceRows(snapshot.TopServices);
        _topRegionRows = BuildTopRegionRows(snapshot.TopDestinations);
        _topRegionMarkers = BuildTopRegionMarkers(snapshot.TopDestinations);
        _primaryRegionMarker = _topRegionMarkers.Count > 0 ? _topRegionMarkers[0] : null;
        _secondaryRegionMarker = _topRegionMarkers.Count > 1 ? _topRegionMarkers[1] : null;
        _tertiaryRegionMarker = _topRegionMarkers.Count > 2 ? _topRegionMarkers[2] : null;
        _topConnectionRows = BuildTopConnectionRows(snapshot.TopConnections, snapshot.TopHosts);
        _maxTimelineValue = CalculateMaxTimelineValue(snapshot.TimelinePoints);
        _outboundPathData = BuildTimelinePath(
            snapshot.TimelinePoints,
            _maxTimelineValue,
            selectOutbound: true
        );
        _inboundPathData = BuildTimelinePath(
            snapshot.TimelinePoints,
            _maxTimelineValue,
            selectOutbound: false
        );
        _outboundAreaPathData = BuildAreaPath(_outboundPathData);
    }

    private static ulong CalculateMaxTimelineValue(IReadOnlyList<TimelinePointDto> timelinePoints)
    {
        var max = 0UL;
        for (var i = 0; i < timelinePoints.Count; i++)
        {
            var point = timelinePoints[i];
            if (point.InboundBytes > max)
            {
                max = point.InboundBytes;
            }

            if (point.OutboundBytes > max)
            {
                max = point.OutboundBytes;
            }
        }

        return max;
    }

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        StatusCards = BuildStatusCards(snapshot, ModeLabel);
        RebuildSnapshotDerivedState(snapshot);
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
        if (!HasTimeline)
        {
            ClearThroughputHover();
        }
    }

    private static string BuildSmoothPath(IReadOnlyList<(double X, double Y)> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return string.Empty;
        }

        if (coordinates.Count == 1)
        {
            return FormattableString.Invariant($"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##}");
        }

        if (coordinates.Count == 2)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##} L {coordinates[1].X:0.##},{coordinates[1].Y:0.##}"
            );
        }

        var builder = new StringBuilder();
        builder.Append(FormattableString.Invariant($"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##}"));

        for (var i = 0; i < coordinates.Count - 1; i++)
        {
            var previous = i == 0 ? coordinates[i] : coordinates[i - 1];
            var start = coordinates[i];
            var end = coordinates[i + 1];
            var next = i + 2 < coordinates.Count ? coordinates[i + 2] : coordinates[i + 1];

            var firstControlX = start.X + (end.X - previous.X) / 6d;
            var firstControlY = start.Y + (end.Y - previous.Y) / 6d;
            var secondControlX = end.X - (next.X - start.X) / 6d;
            var secondControlY = end.Y - (next.Y - start.Y) / 6d;

            builder.Append(
                FormattableString.Invariant(
                    $" C {firstControlX:0.##},{firstControlY:0.##} {secondControlX:0.##},{secondControlY:0.##} {end.X:0.##},{end.Y:0.##}"
                )
            );
        }

        return builder.ToString();
    }

    private string BuildHeroSummary()
    {
        var timestamp = Snapshot.LastPacketTimestamp;
        if (timestamp is null || timestamp.Seconds <= 0)
        {
            return $"Sequence {Snapshot.Sequence} · waiting for packets";
        }

        return $"Sequence {Snapshot.Sequence} · last packet {FormatPacketTimestamp(timestamp)}";
    }

    private static string FormatPacketTimestamp(PacketTimestampDto timestamp)
    {
        var localTime = DateTimeOffset
            .FromUnixTimeSeconds(timestamp.Seconds)
            .ToLocalTime();

        return localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static (double X, double Y) ProjectEqualEarth(double longitude, double latitude)
    {
        const double width = 360.0;
        const double height = 170.0;
        const double scale = 64.52285022416004;
        const double centerX = width / 2.0;
        const double centerY = height / 2.0;
        const double a1 = 1.340264;
        const double a2 = -0.081106;
        const double a3 = 0.000893;
        const double a4 = 0.003796;
        const double sqrt3 = 1.7320508075688772;
        const double sqrt3Over2 = 0.8660254037844386;

        var lambda = longitude * Math.PI / 180.0;
        var phi = latitude * Math.PI / 180.0;
        var theta = Math.Asin(sqrt3Over2 * Math.Sin(phi));
        var theta2 = theta * theta;
        var theta6 = theta2 * theta2 * theta2;
        var denominator = 3.0
            * (a1 + 3.0 * a2 * theta2 + theta6 * (7.0 * a3 + 9.0 * a4 * theta2));
        var x = (2.0 * sqrt3 * lambda * Math.Cos(theta)) / denominator;
        var y = a1 * theta + a2 * theta * theta2 + theta6 * theta * (a3 + a4 * theta2);

        return (centerX + x * scale, centerY - y * scale);
    }

    private static bool TryGetCountryCoordinate(
        string countryCode,
        out double longitude,
        out double latitude
    )
    {
        if (CountryCoordinates.TryGetValue(countryCode.Trim().ToUpperInvariant(), out var coordinate))
        {
            longitude = coordinate.Longitude;
            latitude = coordinate.Latitude;
            return true;
        }

        longitude = 0;
        latitude = 0;
        return false;
    }

    private static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> CountryCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["AE"] = (54.20, 23.87),
            ["AF"] = (66.00, 33.84),
            ["AL"] = (20.03, 41.13),
            ["AM"] = (45.01, 40.21),
            ["AO"] = (17.47, -12.23),
            ["AR"] = (-64.75, -34.74),
            ["AT"] = (14.06, 47.62),
            ["AU"] = (134.31, -25.76),
            ["AZ"] = (47.56, 40.22),
            ["BA"] = (17.82, 44.18),
            ["BD"] = (90.28, 23.83),
            ["BE"] = (4.59, 50.65),
            ["BF"] = (-1.78, 12.31),
            ["BG"] = (25.19, 42.76),
            ["BI"] = (29.91, -3.38),
            ["BJ"] = (2.34, 9.64),
            ["BN"] = (114.92, 4.69),
            ["BO"] = (-64.65, -16.70),
            ["BR"] = (-53.17, -10.66),
            ["BS"] = (-77.93, 25.51),
            ["BT"] = (90.47, 27.43),
            ["BW"] = (23.78, -22.08),
            ["BY"] = (27.96, 53.50),
            ["BZ"] = (-88.70, 17.19),
            ["CA"] = (-96.40, 60.48),
            ["CD"] = (23.58, -2.84),
            ["CF"] = (20.37, 6.55),
            ["CG"] = (15.14, -0.84),
            ["CH"] = (8.12, 46.79),
            ["CI"] = (-5.61, 7.55),
            ["CL"] = (-71.18, -37.31),
            ["CM"] = (12.61, 5.65),
            ["CN"] = (103.45, 36.68),
            ["CO"] = (-73.07, 3.92),
            ["CR"] = (-84.17, 9.97),
            ["CU"] = (-78.93, 21.65),
            ["CY"] = (33.04, 34.91),
            ["CZ"] = (15.34, 49.78),
            ["DE"] = (10.27, 51.08),
            ["DJ"] = (42.50, 11.77),
            ["DK"] = (9.89, 56.06),
            ["DO"] = (-70.46, 18.89),
            ["DZ"] = (2.61, 28.09),
            ["EC"] = (-78.38, -1.45),
            ["EE"] = (25.83, 58.64),
            ["EG"] = (29.86, 26.47),
            ["EH"] = (-12.19, 24.28),
            ["ER"] = (38.69, 15.43),
            ["ES"] = (-3.62, 40.32),
            ["ET"] = (39.56, 8.65),
            ["FI"] = (26.14, 64.26),
            ["FJ"] = (178.57, -17.32),
            ["FK"] = (-59.42, -51.72),
            ["FR"] = (-6.80, 43.14),
            ["GA"] = (11.69, -0.65),
            ["GB"] = (-2.76, 53.81),
            ["GE"] = (43.50, 42.17),
            ["GH"] = (-1.24, 7.92),
            ["GL"] = (-41.96, 73.15),
            ["GM"] = (-15.43, 13.48),
            ["GN"] = (-11.06, 10.45),
            ["GQ"] = (10.37, 1.65),
            ["GR"] = (22.72, 39.04),
            ["GT"] = (-90.37, 15.70),
            ["GW"] = (-15.11, 12.02),
            ["GY"] = (-58.97, 4.79),
            ["HN"] = (-86.59, 14.83),
            ["HR"] = (16.57, 45.01),
            ["HT"] = (-72.66, 18.90),
            ["HU"] = (19.34, 47.20),
            ["ID"] = (117.36, -2.27),
            ["IE"] = (-8.02, 53.17),
            ["IL"] = (35.00, 31.48),
            ["IN"] = (79.54, 22.82),
            ["IQ"] = (43.79, 33.01),
            ["IR"] = (54.45, 32.47),
            ["IS"] = (-18.77, 65.08),
            ["IT"] = (12.27, 42.67),
            ["JM"] = (-77.32, 18.14),
            ["JO"] = (36.77, 31.24),
            ["JP"] = (137.71, 37.54),
            ["KE"] = (37.79, 0.60),
            ["KG"] = (74.59, 41.52),
            ["KH"] = (104.87, 12.68),
            ["KP"] = (127.13, 40.13),
            ["KR"] = (127.82, 36.42),
            ["KW"] = (47.60, 29.31),
            ["KZ"] = (67.24, 48.41),
            ["LA"] = (103.79, 18.43),
            ["LB"] = (35.87, 33.91),
            ["LK"] = (80.67, 7.70),
            ["LR"] = (-9.41, 6.43),
            ["LS"] = (28.17, -29.62),
            ["LT"] = (23.89, 55.28),
            ["LU"] = (5.97, 49.76),
            ["LV"] = (24.84, 56.82),
            ["LY"] = (18.03, 26.99),
            ["MA"] = (-8.69, 29.82),
            ["MD"] = (28.42, 47.20),
            ["ME"] = (19.29, 42.79),
            ["MG"] = (46.73, -19.30),
            ["MK"] = (21.70, 41.61),
            ["ML"] = (-3.59, 17.24),
            ["MM"] = (96.51, 20.94),
            ["MN"] = (103.02, 46.95),
            ["MR"] = (-10.35, 20.18),
            ["MW"] = (34.19, -13.16),
            ["MX"] = (-102.22, 23.91),
            ["MY"] = (109.70, 3.75),
            ["MZ"] = (35.54, -17.15),
            ["NA"] = (17.14, -22.04),
            ["NC"] = (165.53, -21.26),
            ["NE"] = (9.27, 17.34),
            ["NG"] = (7.99, 9.54),
            ["NI"] = (-85.02, 12.85),
            ["NL"] = (5.50, 52.29),
            ["NO"] = (12.83, 66.65),
            ["NP"] = (84.04, 28.25),
            ["NZ"] = (172.95, -41.55),
            ["OM"] = (56.07, 20.59),
            ["PA"] = (-80.11, 8.53),
            ["PE"] = (-74.43, -9.15),
            ["PG"] = (145.31, -6.46),
            ["PH"] = (122.94, 11.72),
            ["PK"] = (69.23, 29.91),
            ["PL"] = (19.34, 52.13),
            ["PR"] = (-66.48, 18.24),
            ["PS"] = (35.27, 31.94),
            ["PT"] = (-8.06, 39.61),
            ["PY"] = (-58.43, -23.23),
            ["QA"] = (51.18, 25.32),
            ["RO"] = (24.95, 45.85),
            ["RS"] = (20.84, 44.22),
            ["RU"] = (95.79, 66.07),
            ["RW"] = (29.92, -2.01),
            ["SA"] = (44.64, 24.09),
            ["SB"] = (159.96, -8.85),
            ["SD"] = (29.83, 15.97),
            ["SE"] = (16.11, 62.42),
            ["SI"] = (14.93, 46.13),
            ["SK"] = (19.50, 48.73),
            ["SL"] = (-11.80, 8.53),
            ["SN"] = (-14.51, 14.35),
            ["SO"] = (46.23, 9.76),
            ["SR"] = (-55.91, 4.12),
            ["SS"] = (30.20, 7.29),
            ["SV"] = (-88.87, 13.73),
            ["SY"] = (38.52, 35.01),
            ["SZ"] = (31.40, -26.49),
            ["TD"] = (18.57, 15.28),
            ["TF"] = (69.53, -49.31),
            ["TG"] = (1.00, 8.43),
            ["TH"] = (101.00, 14.98),
            ["TJ"] = (71.05, 38.59),
            ["TL"] = (125.97, -8.77),
            ["TM"] = (59.35, 39.10),
            ["TN"] = (9.54, 34.14),
            ["TR"] = (35.12, 39.15),
            ["TT"] = (-61.33, 10.43),
            ["TW"] = (120.97, 23.74),
            ["TZ"] = (34.74, -6.25),
            ["UA"] = (31.29, 49.19),
            ["UG"] = (32.36, 1.30),
            ["US"] = (-103.57, 44.76),
            ["UY"] = (-56.01, -32.77),
            ["UZ"] = (63.37, 41.77),
            ["VE"] = (-66.15, 7.16),
            ["VN"] = (106.33, 16.56),
            ["VU"] = (167.07, -15.54),
            ["XK"] = (20.90, 42.58),
            ["YE"] = (47.52, 15.92),
            ["ZA"] = (25.16, -28.92),
            ["ZM"] = (27.76, -13.39),
            ["ZW"] = (29.79, -18.90),
        };

    private static IReadOnlyList<OverviewStatusCardViewModel> BuildStatusCards(
        OverviewSnapshotDto snapshot,
        string modeLabel
    )
    {
        return
        [
            new OverviewStatusCardViewModel("Packets", snapshot.Totals.Packets.ToString(), "Phase1 aggregate total"),
            new OverviewStatusCardViewModel("Bytes", snapshot.Totals.Bytes.ToString(), "Phase1 aggregate total"),
            new OverviewStatusCardViewModel("Dropped", snapshot.DroppedPackets.ToString(), "Capture drop metric"),
            new OverviewStatusCardViewModel("Mode", modeLabel, "Live / offline display"),
        ];
    }

    private static string ResolveModeLabel(string? modeOverride, OverviewSnapshotDto snapshot)
    {
        if (!string.IsNullOrWhiteSpace(modeOverride))
        {
            return string.Equals(modeOverride, "Replay", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeOverride, "Offline", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeOverride, "offline", System.StringComparison.OrdinalIgnoreCase)
                ? "Offline"
                : "Live";
        }

        return string.Equals(snapshot.Mode, "offline", System.StringComparison.OrdinalIgnoreCase)
            ? "Offline"
            : "Live";
    }

    private static string ResolveMetricStateLabel(bool hasValue, OverviewSnapshotDto snapshot)
    {
        if (!hasValue)
        {
            return "WAITING";
        }

        return string.Equals(snapshot.Mode, "offline", System.StringComparison.OrdinalIgnoreCase)
            ? "OFFLINE"
            : "LIVE";
    }

    private static OverviewSnapshotDto CreateSeedSnapshot()
    {
        return new OverviewSnapshotDto
        {
            CaptureId = "live-seed",
            Mode = "live",
            Sequence = 42,
            SourceLabel = "Live source · en0",
            FilterLabel = "Filter · tcp",
            MetricMode = "bytes",
            Timestamp = new PacketTimestampDto
            {
                Seconds = 1_714_587_200,
                Microseconds = 0,
            },
            Totals = new AggregateTotalsDto
            {
                Packets = 823,
                Bytes = 541_120,
                BytesIn = 158_640,
                BytesOut = 382_480,
            },
            DroppedPackets = 3,
            LastPacketTimestamp = new PacketTimestampDto
            {
                Seconds = 1_714_587_202,
                Microseconds = 250_000,
            },
            TopConnections =
            [
                new ConnectionRowDto
                {
                    SourceAddress = "192.168.50.21",
                    SourcePort = 52901,
                    DestinationAddress = "142.250.72.14",
                    DestinationPort = 443,
                    Protocol = "tcp",
                    ServiceName = "https",
                    Direction = "outbound",
                    Packets = 144,
                    Bytes = 212_540,
                },
                new ConnectionRowDto
                {
                    SourceAddress = "192.168.50.21",
                    SourcePort = 53112,
                    DestinationAddress = "151.101.1.140",
                    DestinationPort = 80,
                    Protocol = "tcp",
                    ServiceName = "http",
                    Direction = "outbound",
                    Packets = 88,
                    Bytes = 10_928,
                },
            ],
            TopHosts =
            [
                new HostRowDto
                {
                    Host = "142.250.72.14",
                    CountryLabel = "US · United States",
                    Packets = 144,
                    Bytes = 212_540,
                },
                new HostRowDto
                {
                    Host = "1.1.1.1",
                    CountryLabel = "AU · Australia",
                    Packets = 91,
                    Bytes = 145_000,
                },
            ],
            TopServices =
            [
                new ServiceRowDto
                {
                    Name = "https",
                    Transport = "tcp",
                    Packets = 188,
                    Bytes = 301_440,
                },
                new ServiceRowDto
                {
                    Name = "dns",
                    Transport = "udp",
                    Packets = 74,
                    Bytes = 22_800,
                },
            ],
            TopDestinations =
            [
                new DestinationSummaryDto
                {
                    Label = "US / Google",
                    CountryLabel = "US · United States",
                    Bytes = 212_540,
                    Ratio = 0.39,
                },
                new DestinationSummaryDto
                {
                    Label = "AU / Cloudflare",
                    CountryLabel = "AU · Australia",
                    Bytes = 145_000,
                    Ratio = 0.27,
                },
            ],
            DestinationMap = DestinationMapPlaceholderDto.CreateReserved(),
            TimelinePoints =
            [
                new TimelinePointDto
                {
                    Timestamp = new PacketTimestampDto { Seconds = 1_714_587_182, Microseconds = 0 },
                    InboundBytes = 4_096,
                    OutboundBytes = 8_192,
                },
                new TimelinePointDto
                {
                    Timestamp = new PacketTimestampDto { Seconds = 1_714_587_187, Microseconds = 0 },
                    InboundBytes = 10_240,
                    OutboundBytes = 14_336,
                },
                new TimelinePointDto
                {
                    Timestamp = new PacketTimestampDto { Seconds = 1_714_587_192, Microseconds = 0 },
                    InboundBytes = 6_144,
                    OutboundBytes = 8_704,
                },
                new TimelinePointDto
                {
                    Timestamp = new PacketTimestampDto { Seconds = 1_714_587_197, Microseconds = 0 },
                    InboundBytes = 12_288,
                    OutboundBytes = 15_360,
                },
                new TimelinePointDto
                {
                    Timestamp = new PacketTimestampDto { Seconds = 1_714_587_202, Microseconds = 0 },
                    InboundBytes = 7_168,
                    OutboundBytes = 16_384,
                },
            ],
        };
    }

    private static OverviewSnapshotDto CreateInitialRuntimeSnapshot()
    {
        return new OverviewSnapshotDto
        {
            CaptureId = "live:inactive",
            Mode = "live",
            Sequence = 0,
            SourceLabel = "Live source · not started",
            FilterLabel = "Filter · none",
            MetricMode = "bytes",
            Timestamp = new PacketTimestampDto(),
            Totals = new AggregateTotalsDto(),
            DroppedPackets = 0,
            LastPacketTimestamp = null,
            TopConnections = Array.Empty<ConnectionRowDto>(),
            TopHosts = Array.Empty<HostRowDto>(),
            TopServices = Array.Empty<ServiceRowDto>(),
            DestinationMap = DestinationMapPlaceholderDto.CreateReserved(),
            TopDestinations = Array.Empty<DestinationSummaryDto>(),
            TimelinePoints = Array.Empty<TimelinePointDto>(),
        };
    }
}

public sealed class OverviewStatusCardViewModel
{
    public OverviewStatusCardViewModel(string label, string value, string hint)
    {
        Label = label;
        Value = value;
        Hint = hint;
    }

    public string Label { get; }

    public string Value { get; }

    public string Hint { get; }
}

public sealed class OverviewMetricRowViewModel
{
    public OverviewMetricRowViewModel(string label, string valueLabel, double barWidth, string accentBrush)
    {
        Label = label;
        ValueLabel = valueLabel;
        BarWidth = barWidth;
        AccentBrush = accentBrush;
    }

    public string Label { get; }

    public string ValueLabel { get; }

    public double BarWidth { get; }

    public string AccentBrush { get; }
}

public sealed class OverviewRegionRowViewModel
{
    public OverviewRegionRowViewModel(string label, string ratioLabel, string accentBrush)
    {
        Label = label;
        RatioLabel = ratioLabel;
        AccentBrush = accentBrush;
    }

    public string Label { get; }

    public string RatioLabel { get; }

    public string AccentBrush { get; }
}

public sealed class OverviewRegionMarkerViewModel
{
    public OverviewRegionMarkerViewModel(
        string label,
        string ratioLabel,
        string bytesLabel,
        double x,
        double y,
        double size,
        string accentBrush
    )
    {
        Label = label;
        RatioLabel = ratioLabel;
        BytesLabel = bytesLabel;
        X = x;
        Y = y;
        Size = size;
        AccentBrush = accentBrush;
    }

    public string Label { get; }

    public string RatioLabel { get; }

    public string BytesLabel { get; }

    public double X { get; }

    public double Y { get; }

    public double Size { get; }

    public string AccentBrush { get; }
}

public sealed class OverviewConnectionRowViewModel
{
    public OverviewConnectionRowViewModel(string sourceAddress, string destinationAddress, string volumeLabel)
    {
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        VolumeLabel = volumeLabel;
    }

    public string SourceAddress { get; }

    public string DestinationAddress { get; }

    public string VolumeLabel { get; }
}
