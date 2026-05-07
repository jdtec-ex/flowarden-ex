using System;
using System.Collections.Generic;

namespace Flowarden.Ui.Models;

public sealed class OverviewSnapshotDto
{
    public string CaptureId { get; init; } = string.Empty;

    public ulong Sequence { get; init; }

    public PacketTimestampDto Timestamp { get; init; } = new();

    public AggregateTotalsDto Totals { get; init; } = new();

    public ulong DroppedPackets { get; init; }

    public PacketTimestampDto? LastPacketTimestamp { get; init; }

    public IReadOnlyList<ConnectionRowDto> TopConnections { get; init; } = Array.Empty<ConnectionRowDto>();

    public IReadOnlyList<HostRowDto> TopHosts { get; init; } = Array.Empty<HostRowDto>();

    public IReadOnlyList<ServiceRowDto> TopServices { get; init; } = Array.Empty<ServiceRowDto>();

    public DestinationMapPlaceholderDto DestinationMap { get; init; } = DestinationMapPlaceholderDto.CreateReserved();

    public IReadOnlyList<DestinationSummaryDto> TopDestinations { get; init; } = Array.Empty<DestinationSummaryDto>();

    public string SourceLabel { get; init; } = "Live source · unknown";

    public string FilterLabel { get; init; } = "Filter · none";

    public string MetricMode { get; init; } = "bytes";

    public IReadOnlyList<TimelinePointDto> TimelinePoints { get; init; } = Array.Empty<TimelinePointDto>();
}

public sealed class PacketTimestampDto
{
    public long Seconds { get; init; }

    public uint Microseconds { get; init; }
}

public sealed class AggregateTotalsDto
{
    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }

    public ulong BytesIn { get; init; }

    public ulong BytesOut { get; init; }
}

public sealed class TimelinePointDto
{
    public PacketTimestampDto Timestamp { get; init; } = new();

    public ulong InboundBytes { get; init; }

    public ulong OutboundBytes { get; init; }
}

public sealed class DestinationMapPlaceholderDto
{
    public string State { get; init; } = "reserved";

    public string Message { get; init; } = "Destination map is reserved for a future phase 2 enhancement.";

    public static DestinationMapPlaceholderDto CreateReserved()
    {
        return new DestinationMapPlaceholderDto();
    }
}

public sealed class DestinationSummaryDto
{
    public string Label { get; init; } = string.Empty;

    public string CountryLabel { get; init; } = string.Empty;

    public ulong Bytes { get; init; }

    public double Ratio { get; init; }
}
