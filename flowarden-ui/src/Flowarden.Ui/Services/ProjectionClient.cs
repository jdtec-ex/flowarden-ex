using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Projection.V1;
using Flowarden.Ui.Models;
using Grpc.Core;
using Grpc.Net.Client;

namespace Flowarden.Ui.Services;

public sealed class ProjectionClient
{
    private readonly ProjectionService.ProjectionServiceClient _client;

    public ProjectionClient(GrpcChannel channel)
    {
        _client = new ProjectionService.ProjectionServiceClient(channel);
    }

    public async Task<OverviewSnapshotDto> GetLatestOverviewAsync(
        uint topN,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.GetLatestOverviewAsync(
            new GetLatestOverviewRequest { TopN = topN },
            cancellationToken: cancellationToken
        );

        return new OverviewSnapshotDto
        {
            CaptureId = response.CaptureId,
            Mode = MapProjectionMode(response.Mode),
            Sequence = response.Sequence,
            Timestamp = response.Timestamp is null
                ? new PacketTimestampDto()
                : new PacketTimestampDto
                {
                    Seconds = response.Timestamp.Seconds,
                    Microseconds = response.Timestamp.Microseconds,
                },
            Totals = response.Totals is null
                ? new AggregateTotalsDto()
                : new AggregateTotalsDto
                {
                    Packets = response.Totals.Packets,
                    Bytes = response.Totals.Bytes,
                    BytesIn = response.Totals.BytesIn,
                    BytesOut = response.Totals.BytesOut,
                },
            DroppedPackets = response.DroppedPackets,
            LastPacketTimestamp = response.LastPacketTimestamp is null
                ? null
                : new PacketTimestampDto
                {
                    Seconds = response.LastPacketTimestamp.Seconds,
                    Microseconds = response.LastPacketTimestamp.Microseconds,
                },
            TopConnections = response.TopConnections
                .Select(connection => new ConnectionRowDto
                {
                    SourceAddress = connection.SourceAddress,
                    SourcePort = connection.SourcePort == 0 ? null : (ushort?)connection.SourcePort,
                    DestinationAddress = connection.DestinationAddress,
                    DestinationPort = connection.DestinationPort == 0 ? null : (ushort?)connection.DestinationPort,
                    Protocol = connection.Protocol,
                    ServiceName = connection.ServiceName,
                    Direction = connection.Direction,
                    Packets = connection.Packets,
                    Bytes = connection.Bytes,
                })
                .ToArray(),
            TopHosts = response.TopHosts
                .Select(host => new HostRowDto
                {
                    Host = host.Host,
                    CountryLabel = host.CountryLabel,
                    Packets = host.Packets,
                    Bytes = host.Bytes,
                })
                .ToArray(),
            TopServices = response.TopServices
                .Select(service => new ServiceRowDto
                {
                    Name = service.Name,
                    Transport = service.Transport,
                    Packets = service.Packets,
                    Bytes = service.Bytes,
                })
                .ToArray(),
            DestinationMap = new DestinationMapPlaceholderDto
            {
                State = response.DestinationMap?.State ?? "reserved",
                Message = response.DestinationMap?.Message
                    ?? "Destination map is reserved for a future phase 2 enhancement.",
            },
            TopDestinations = response.TopDestinations
                .Select(destination => new DestinationSummaryDto
                {
                    Label = destination.Label,
                    CountryLabel = destination.CountryLabel,
                    Bytes = destination.Bytes,
                    Ratio = destination.Ratio,
                })
                .ToArray(),
            SourceLabel = string.IsNullOrWhiteSpace(response.SourceLabel)
                ? "Live source · unknown"
                : response.SourceLabel,
            FilterLabel = string.IsNullOrWhiteSpace(response.FilterLabel)
                ? "Filter · none"
                : response.FilterLabel,
            MetricMode = string.IsNullOrWhiteSpace(response.MetricMode)
                ? "bytes"
                : response.MetricMode,
            TimelinePoints = response.TimelinePoints
                .Select(point => new TimelinePointDto
                {
                    Timestamp = point.Timestamp is null
                        ? new PacketTimestampDto()
                        : new PacketTimestampDto
                        {
                            Seconds = point.Timestamp.Seconds,
                            Microseconds = point.Timestamp.Microseconds,
                        },
                    InboundBytes = point.InboundBytes,
                    OutboundBytes = point.OutboundBytes,
                })
                .ToArray(),
        };
    }

    public async IAsyncEnumerable<OverviewSnapshotDto> StreamOverviewAsync(
        uint topN,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        using var call = _client.StreamOverview(
            new StreamOverviewRequest { TopN = topN },
            cancellationToken: cancellationToken
        );

        await foreach (
            var response in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return new OverviewSnapshotDto
            {
                CaptureId = response.CaptureId,
                Mode = MapProjectionMode(response.Mode),
                Sequence = response.Sequence,
                Timestamp = response.Timestamp is null
                    ? new PacketTimestampDto()
                    : new PacketTimestampDto
                    {
                        Seconds = response.Timestamp.Seconds,
                        Microseconds = response.Timestamp.Microseconds,
                    },
                Totals = response.Totals is null
                    ? new AggregateTotalsDto()
                    : new AggregateTotalsDto
                    {
                        Packets = response.Totals.Packets,
                        Bytes = response.Totals.Bytes,
                        BytesIn = response.Totals.BytesIn,
                        BytesOut = response.Totals.BytesOut,
                    },
                DroppedPackets = response.DroppedPackets,
                LastPacketTimestamp = response.LastPacketTimestamp is null
                    ? null
                    : new PacketTimestampDto
                    {
                        Seconds = response.LastPacketTimestamp.Seconds,
                        Microseconds = response.LastPacketTimestamp.Microseconds,
                    },
                TopConnections = response.TopConnections
                    .Select(connection => new ConnectionRowDto
                    {
                        SourceAddress = connection.SourceAddress,
                        SourcePort = connection.SourcePort == 0 ? null : (ushort?)connection.SourcePort,
                        DestinationAddress = connection.DestinationAddress,
                        DestinationPort = connection.DestinationPort == 0
                            ? null
                            : (ushort?)connection.DestinationPort,
                        Protocol = connection.Protocol,
                        ServiceName = connection.ServiceName,
                        Direction = connection.Direction,
                        Packets = connection.Packets,
                        Bytes = connection.Bytes,
                    })
                    .ToArray(),
                TopHosts = response.TopHosts
                    .Select(host => new HostRowDto
                    {
                        Host = host.Host,
                        CountryLabel = host.CountryLabel,
                        Packets = host.Packets,
                        Bytes = host.Bytes,
                    })
                    .ToArray(),
                TopServices = response.TopServices
                    .Select(service => new ServiceRowDto
                    {
                        Name = service.Name,
                        Transport = service.Transport,
                        Packets = service.Packets,
                        Bytes = service.Bytes,
                    })
                    .ToArray(),
                DestinationMap = new DestinationMapPlaceholderDto
                {
                    State = response.DestinationMap?.State ?? "reserved",
                    Message = response.DestinationMap?.Message
                        ?? "Destination map is reserved for a future phase 2 enhancement.",
                },
                TopDestinations = response.TopDestinations
                    .Select(destination => new DestinationSummaryDto
                    {
                        Label = destination.Label,
                        CountryLabel = destination.CountryLabel,
                        Bytes = destination.Bytes,
                        Ratio = destination.Ratio,
                    })
                    .ToArray(),
                SourceLabel = string.IsNullOrWhiteSpace(response.SourceLabel)
                    ? "Live source · unknown"
                    : response.SourceLabel,
                FilterLabel = string.IsNullOrWhiteSpace(response.FilterLabel)
                    ? "Filter · none"
                    : response.FilterLabel,
                MetricMode = string.IsNullOrWhiteSpace(response.MetricMode)
                    ? "bytes"
                    : response.MetricMode,
                TimelinePoints = response.TimelinePoints
                    .Select(point => new TimelinePointDto
                    {
                        Timestamp = point.Timestamp is null
                            ? new PacketTimestampDto()
                            : new PacketTimestampDto
                            {
                                Seconds = point.Timestamp.Seconds,
                                Microseconds = point.Timestamp.Microseconds,
                            },
                        InboundBytes = point.InboundBytes,
                        OutboundBytes = point.OutboundBytes,
                    })
                    .ToArray(),
            };
        }
    }

    public async Task<InspectResultDto> GetInspectPageAsync(
        InspectFilterDto filter,
        uint topN,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.GetInspectPageAsync(
            new GetInspectPageRequest
            {
                SourceAddress = filter.SourceAddress ?? string.Empty,
                DestinationAddress = filter.DestinationAddress ?? string.Empty,
                ServiceName = filter.ServiceName ?? string.Empty,
                Protocol = filter.Protocol ?? string.Empty,
                Direction = filter.Direction ?? string.Empty,
                Bpf = filter.Bpf ?? string.Empty,
                TopN = topN,
            },
            cancellationToken: cancellationToken
        );

        return new InspectResultDto
        {
            State = response.State,
            Rows = response.Rows
                .Select(row => new ConnectionRowDto
                {
                    SourceAddress = row.SourceAddress,
                    SourcePort = row.SourcePort == 0 ? null : (ushort?)row.SourcePort,
                    DestinationAddress = row.DestinationAddress,
                    DestinationPort = row.DestinationPort == 0 ? null : (ushort?)row.DestinationPort,
                    Protocol = row.Protocol,
                    ServiceName = row.ServiceName,
                    Direction = row.Direction,
                    Packets = row.Packets,
                    Bytes = row.Bytes,
                })
                .ToArray(),
            Summary = new InspectResultSummaryDto
            {
                TotalRows = (ulong)response.Rows.Count,
                VisibleRows = (ulong)response.Rows.Count,
                TotalPackets = response.Rows.Aggregate(0UL, (acc, row) => acc + row.Packets),
                TotalBytes = response.Rows.Aggregate(0UL, (acc, row) => acc + row.Bytes),
                SortBy = "bytes",
                SortDirection = "desc",
            },
        };
    }

    private static string MapProjectionMode(ProjectionMode mode)
    {
        return mode switch
        {
            ProjectionMode.Offline => "offline",
            ProjectionMode.Live => "live",
            _ => "live",
        };
    }

    public async Task<InspectResultDto> GetTcpConnectionsPageAsync(
        InspectFilterDto filter,
        uint topN,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.GetTcpConnectionsPageAsync(
            new GetTcpConnectionsPageRequest
            {
                Address = filter.Address ?? string.Empty,
                Port = filter.Port ?? string.Empty,
                State = filter.State ?? string.Empty,
                TopN = topN,
            },
            cancellationToken: cancellationToken
        );

        return new InspectResultDto
        {
            State = response.State,
            TcpRows = response.Rows
                .Select(row => new TcpConnectionRowDto
                {
                    EndpointAAddress = row.EndpointAAddress,
                    EndpointAPort = (ushort)row.EndpointAPort,
                    EndpointBAddress = row.EndpointBAddress,
                    EndpointBPort = (ushort)row.EndpointBPort,
                    State = row.State,
                    SynCount = row.SynCount,
                    FinCount = row.FinCount,
                    RstCount = row.RstCount,
                    Packets = row.Packets,
                    Bytes = row.Bytes,
                    FirstSeen = row.FirstSeen is null
                        ? new PacketTimestampDto()
                        : new PacketTimestampDto
                        {
                            Seconds = row.FirstSeen.Seconds,
                            Microseconds = row.FirstSeen.Microseconds,
                        },
                    LastSeen = row.LastSeen is null
                        ? new PacketTimestampDto()
                        : new PacketTimestampDto
                        {
                            Seconds = row.LastSeen.Seconds,
                            Microseconds = row.LastSeen.Microseconds,
                        },
                })
                .ToArray(),
            Summary = new InspectResultSummaryDto
            {
                TotalRows = (ulong)response.Rows.Count,
                VisibleRows = (ulong)response.Rows.Count,
                TotalPackets = response.Rows.Aggregate(0UL, (acc, row) => acc + row.Packets),
                TotalBytes = response.Rows.Aggregate(0UL, (acc, row) => acc + row.Bytes),
                SortBy = "bytes",
                SortDirection = "desc",
            },
        };
    }
}
