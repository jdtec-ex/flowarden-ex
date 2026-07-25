using System;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewSeedData
{
    public static OverviewSnapshotDto CreateSeedSnapshot()
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

    public static OverviewSnapshotDto CreateInitialRuntimeSnapshot()
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
