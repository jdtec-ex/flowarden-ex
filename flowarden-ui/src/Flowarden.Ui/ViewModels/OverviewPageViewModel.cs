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
    private readonly bool _isDesignTime;
    private string? _modeOverride;

    public OverviewPageViewModel()
        : this(projectionClient: null, liveProjectionState: null, isDesignTime: true)
    {
    }

    public OverviewPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState = null
    )
        : this(projectionClient, liveProjectionState, isDesignTime: false)
    {
    }

    private OverviewPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState,
        bool isDesignTime
    )
    {
        _projectionClient = projectionClient;
        _liveProjectionState = liveProjectionState;
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

    public string InboundSummary => FormatByteRate(Snapshot.Totals.BytesIn);

    public string OutboundSummary => FormatByteRate(Snapshot.Totals.BytesOut);

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

        ApplySnapshot(await _projectionClient.GetLatestOverviewAsync());
    }

    public void SetMode(string mode)
    {
        _modeOverride = mode;
        StatusCards = BuildStatusCards(Snapshot, ModeLabel);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(HeroSummary));
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

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        StatusCards = BuildStatusCards(snapshot, ModeLabel);
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(HeroSummary));
        OnPropertyChanged(nameof(DestinationPlaceholderMessage));
        OnPropertyChanged(nameof(DestinationPlaceholderState));
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
