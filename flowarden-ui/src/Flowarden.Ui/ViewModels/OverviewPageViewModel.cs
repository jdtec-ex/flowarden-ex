using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed class OverviewPageViewModel : ViewModelBase
{
    public OverviewPageViewModel()
    {
        Snapshot = CreateSeedSnapshot();

        StatusCards = new ReadOnlyCollection<OverviewStatusCardViewModel>(
            [
                new OverviewStatusCardViewModel("Packets", Snapshot.Totals.Packets.ToString(), "Phase1 aggregate total"),
                new OverviewStatusCardViewModel("Bytes", Snapshot.Totals.Bytes.ToString(), "Phase1 aggregate total"),
                new OverviewStatusCardViewModel("Dropped", Snapshot.DroppedPackets.ToString(), "Capture drop metric"),
                new OverviewStatusCardViewModel("Mode", ModeLabel, "Live / offline display"),
            ]
        );
    }

    public OverviewSnapshotDto Snapshot { get; }

    public IReadOnlyList<OverviewStatusCardViewModel> StatusCards { get; }

    public string ModeLabel => Snapshot.CaptureId.StartsWith("offline") ? "Offline" : "Live";

    public string HeroTitle => "Traffic Overview";

    public string HeroSummary =>
        $"Sequence {Snapshot.Sequence} · last packet {Snapshot.LastPacketTimestamp?.Seconds ?? 0}s";

    public string DestinationPlaceholderMessage => Snapshot.DestinationMap.Message;

    public string DestinationPlaceholderTitle => "Destination Distribution Future Slot";

    public string DestinationPlaceholderState => Snapshot.DestinationMap.State;

    public string DestinationPlaceholderHint =>
        "This area is reserved for future geographic or destination-distribution visualization driven by destination projection data.";

    public string DestinationFutureStateLabel => "Future state: destination density, region hot spots, organization overlays";

    private static OverviewSnapshotDto CreateSeedSnapshot()
    {
        return new OverviewSnapshotDto
        {
            CaptureId = "live-seed",
            Sequence = 42,
            Timestamp = new PacketTimestampDto
            {
                Seconds = 1_714_587_200,
                Microseconds = 0,
            },
            Totals = new AggregateTotalsDto
            {
                Packets = 823,
                Bytes = 541_120,
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
                    Confidence = "high",
                    Packets = 188,
                    Bytes = 301_440,
                },
                new ServiceRowDto
                {
                    Name = "dns",
                    Transport = "udp",
                    Confidence = "medium",
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
