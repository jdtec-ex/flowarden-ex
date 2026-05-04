using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Projection.V1;
using Flowarden.Ui.Models;
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
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.GetLatestOverviewAsync(
            new GetLatestOverviewRequest(),
            cancellationToken: cancellationToken
        );

        return new OverviewSnapshotDto
        {
            CaptureId = response.CaptureId,
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
                    Bytes = destination.Bytes,
                    Ratio = destination.Ratio,
                })
                .ToArray(),
        };
    }

    public Task<InspectResultDto> GetPlaceholderInspectResultAsync(
        InspectFilterDto filter,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = filter;
        return Task.FromResult(new InspectResultDto());
    }
}
