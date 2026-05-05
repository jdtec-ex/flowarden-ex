using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;

namespace Flowarden.Ui.ViewModels;

public sealed partial class OverviewPageViewModel : ViewModelBase
{
    private readonly ProjectionClient? _projectionClient;
    private readonly bool _isDesignTime;
    private string? _modeOverride;

    public OverviewPageViewModel()
        : this(projectionClient: null, isDesignTime: true)
    {
    }

    public OverviewPageViewModel(ProjectionClient? projectionClient)
        : this(projectionClient, isDesignTime: false)
    {
    }

    private OverviewPageViewModel(ProjectionClient? projectionClient, bool isDesignTime)
    {
        _projectionClient = projectionClient;
        _isDesignTime = isDesignTime;
        Snapshot = CreateSeedSnapshot();
        StatusCards = BuildStatusCards(Snapshot, ModeLabel);
    }

    public OverviewSnapshotDto Snapshot { get; private set; }

    public IReadOnlyList<OverviewStatusCardViewModel> StatusCards { get; private set; }

    public string ModeLabel => ResolveModeLabel(_modeOverride, Snapshot);

    public string HeroTitle => "Traffic Overview";

    public string HeroSummary =>
        $"Sequence {Snapshot.Sequence} · last packet {Snapshot.LastPacketTimestamp?.Seconds ?? 0}s";

    public string SourceSummary => Snapshot.SourceLabel;

    public string FilterSummary => Snapshot.FilterLabel;

    public string MetricModeSummary => $"Metric · {Snapshot.MetricMode}";

    public string InboundSummary => Snapshot.Totals.BytesIn.ToString();

    public string OutboundSummary => Snapshot.Totals.BytesOut.ToString();

    public string HeroLegendPrimary => "Outbound";

    public string HeroLegendSecondary => "Inbound";

    public string AxisLabelStart => FormatAxisTime(-20);

    public string AxisLabelMidLeft => FormatAxisTime(-15);

    public string AxisLabelCenter => FormatAxisTime(-10);

    public string AxisLabelMidRight => FormatAxisTime(-5);

    public string AxisLabelEnd => FormatAxisTime(0);

    public string DestinationPlaceholderMessage => Snapshot.DestinationMap.Message;

    public string DestinationPlaceholderTitle => "Destination Distribution Future Slot";

    public string DestinationPlaceholderState => Snapshot.DestinationMap.State;

    public string DestinationPlaceholderHint =>
        "This area is reserved for future geographic or destination-distribution visualization driven by destination projection data.";

    public string DestinationFutureStateLabel => "Future state: destination density, region hot spots, organization overlays";

    public async Task LoadAsync()
    {
        if (_projectionClient is null || _isDesignTime)
        {
            return;
        }

        var snapshot = await _projectionClient.GetLatestOverviewAsync();
        Snapshot = snapshot;
        StatusCards = BuildStatusCards(snapshot, ModeLabel);
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(HeroSummary));
        OnPropertyChanged(nameof(DestinationPlaceholderMessage));
        OnPropertyChanged(nameof(DestinationPlaceholderState));
        OnPropertyChanged(nameof(AxisLabelStart));
        OnPropertyChanged(nameof(AxisLabelMidLeft));
        OnPropertyChanged(nameof(AxisLabelCenter));
        OnPropertyChanged(nameof(AxisLabelMidRight));
        OnPropertyChanged(nameof(AxisLabelEnd));
    }

    public void SetMode(string mode)
    {
        _modeOverride = mode;
        StatusCards = BuildStatusCards(Snapshot, ModeLabel);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(StatusCards));
        OnPropertyChanged(nameof(HeroSummary));
    }

    private string FormatAxisTime(int secondsOffset)
    {
        var baseSeconds = Snapshot.LastPacketTimestamp?.Seconds ?? Snapshot.Timestamp.Seconds;
        var axisSeconds = baseSeconds + secondsOffset;

        if (axisSeconds <= 0)
        {
            return "--:--:--";
        }

        var localTime = DateTimeOffset
            .FromUnixTimeSeconds(axisSeconds)
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
                    SourceAddress = "127.0.0.1",
                    SourcePort = 50100,
                    DestinationAddress = "127.0.0.1",
                    DestinationPort = 39091,
                    Protocol = "tcp",
                    ServiceName = "grpc",
                    Direction = "loopback",
                    Packets = 88,
                    Bytes = 10_928,
                },
            ],
            TopHosts =
            [
                new HostRowDto
                {
                    Host = "142.250.72.14",
                    Packets = 144,
                    Bytes = 212_540,
                },
                new HostRowDto
                {
                    Host = "1.1.1.1",
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
                    Bytes = 212_540,
                    Ratio = 0.39,
                },
                new DestinationSummaryDto
                {
                    Label = "AU / Cloudflare",
                    Bytes = 145_000,
                    Ratio = 0.27,
                },
            ],
            DestinationMap = DestinationMapPlaceholderDto.CreateReserved(),
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
