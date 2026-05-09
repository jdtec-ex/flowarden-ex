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
            .Replace("Live source", "not started", StringComparison.OrdinalIgnoreCase);

    public string TopHostLabel => Snapshot.TopHosts.FirstOrDefault()?.Host ?? "-";

    public string TopServiceLabel => Snapshot.TopServices.FirstOrDefault()?.Name ?? "-";

    public string TopServiceDisplayLabel => TopServiceLabel.ToUpperInvariant();

    public string PacketsLabel => FormatCount(Snapshot.Totals.Packets);

    public string BytesLabel => FormatBytes(Snapshot.Totals.Bytes);

    public string PacketsHealthLabel => Snapshot.Totals.Packets > 0 ? "LIVE" : "WAITING";

    public string BytesHealthLabel => Snapshot.Totals.Bytes > 0 ? "LIVE" : "WAITING";

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

    public string OutboundPathData => BuildTimelinePath(selectOutbound: true);

    public string InboundPathData => BuildTimelinePath(selectOutbound: false);

    public string OutboundAreaPathData => BuildAreaPath(selectOutbound: true);

    public string DestinationPlaceholderMessage => Snapshot.DestinationMap.Message;

    public string DestinationPlaceholderTitle => "Destination Distribution Future Slot";

    public string DestinationPlaceholderState => Snapshot.DestinationMap.State;

    public string DestinationPlaceholderHint =>
        "This area is reserved for future geographic or destination-distribution visualization driven by destination projection data.";

    public IReadOnlyList<OverviewMetricRowViewModel> TopHostRows => BuildTopHostRows(Snapshot.TopHosts);

    public bool HasTopHostRows => TopHostRows.Count > 0;

    public bool HasNoTopHostRows => !HasTopHostRows;

    public IReadOnlyList<OverviewMetricRowViewModel> TopServiceRows => BuildTopServiceRows(Snapshot.TopServices);

    public bool HasTopServiceRows => TopServiceRows.Count > 0;

    public bool HasNoTopServiceRows => !HasTopServiceRows;

    public IReadOnlyList<OverviewRegionRowViewModel> TopRegionRows => BuildTopRegionRows(Snapshot.TopDestinations);

    public bool HasTopRegionRows => TopRegionRows.Count > 0;

    public bool HasNoTopRegionRows => !HasTopRegionRows;

    public IReadOnlyList<OverviewConnectionRowViewModel> TopConnectionRows => BuildTopConnectionRows(Snapshot.TopConnections, Snapshot.TopHosts);

    public bool HasTopConnectionRows => TopConnectionRows.Count > 0;

    public bool HasNoTopConnectionRows => !HasTopConnectionRows;

    private ulong MaxTimelineValue =>
        Snapshot.TimelinePoints.Count == 0
            ? 0
            : Snapshot.TimelinePoints
                .SelectMany(point => new[] { point.InboundBytes, point.OutboundBytes })
                .Max();

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

    private string BuildTimelinePath(bool selectOutbound)
    {
        if (Snapshot.TimelinePoints.Count == 0)
        {
            return string.Empty;
        }

        const double width = 640;
        const double height = 84;
        var maxValue = (double)Math.Max(MaxTimelineValue, 1);
        var step = Snapshot.TimelinePoints.Count == 1 ? 0 : width / (Snapshot.TimelinePoints.Count - 1);
        var coordinates = new List<(double X, double Y)>(Snapshot.TimelinePoints.Count);

        for (var i = 0; i < Snapshot.TimelinePoints.Count; i++)
        {
            var point = Snapshot.TimelinePoints[i];
            var value = selectOutbound ? point.OutboundBytes : point.InboundBytes;
            var x = i * step;
            var y = height - ((double)value / maxValue * height);
            coordinates.Add((x, y));
        }

        return BuildSmoothPath(coordinates);
    }

    private string BuildAreaPath(bool selectOutbound)
    {
        var line = BuildTimelinePath(selectOutbound);
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
                "#00F0FF"
            ))
            .ToArray();
    }

    private static IReadOnlyList<OverviewMetricRowViewModel> BuildTopServiceRows(IReadOnlyList<ServiceRowDto> rows)
    {
        var totalBytes = rows.Aggregate<ServiceRowDto, ulong>(0, (current, row) => current + row.Bytes);
        var maxBytes = rows.Count == 0 ? 0 : rows.Max(row => row.Bytes);
        return rows
            .Select((row, index) => new OverviewMetricRowViewModel(
                row.Name.ToUpperInvariant(),
                totalBytes == 0 ? FormatBytes(row.Bytes) : $"{(double)row.Bytes / totalBytes:0%}",
                CalculateBarWidth(row.Bytes, maxBytes),
                index == 0 ? "#B026FF" : "#948E9C"
            ))
            .ToArray();
    }

    private static IReadOnlyList<OverviewRegionRowViewModel> BuildTopRegionRows(IReadOnlyList<DestinationSummaryDto> rows)
    {
        return rows
            .Select((row, index) => new OverviewRegionRowViewModel(
                string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label,
                row.Ratio.ToString("P0", CultureInfo.InvariantCulture),
                index == 0 ? "#00F0FF" : index == 1 ? "#B026FF" : "#CBC4D2"
            ))
            .ToArray();
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

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        StatusCards = BuildStatusCards(snapshot, ModeLabel);
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
                ? "Offline"
                : "Live";
        }

        return snapshot.CaptureId.StartsWith("offline") ? "Offline" : "Live";
    }

    private static OverviewSnapshotDto CreateSeedSnapshot()
    {
        return new OverviewSnapshotDto
        {
            CaptureId = "live-seed",
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
