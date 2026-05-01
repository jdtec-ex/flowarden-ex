using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Discovery.V1;
using Flowarden.Ui.Models;
using Grpc.Net.Client;

namespace Flowarden.Ui.Services;

public sealed class DiscoveryClient
{
    private readonly DiscoveryService.DiscoveryServiceClient _client;

    public DiscoveryClient(GrpcChannel channel)
    {
        _client = new DiscoveryService.DiscoveryServiceClient(channel);
    }

    public async Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.ListDevicesAsync(new ListDevicesRequest(), cancellationToken: cancellationToken);

        return response.Devices
            .Select(device => new DeviceSummaryDto
            {
                Name = device.Name,
                Description = string.IsNullOrWhiteSpace(device.Description) ? null : device.Description,
                Addresses = device.Addresses
                    .Select(address => new DeviceAddressDto
                    {
                        Address = address.Addr,
                    })
                    .ToArray(),
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<DevicePreviewDto>> GetDevicePreviewsAsync(
        ulong previewSeconds,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.ListDevicePreviewsAsync(
            new ListDevicePreviewsRequest
            {
                PreviewSeconds = previewSeconds,
            },
            cancellationToken: cancellationToken
        );

        return response.Previews
            .Select(preview => new DevicePreviewDto
            {
                Name = preview.Name,
                PacketsSeen = preview.PacketsSeen,
                BytesSeen = preview.BytesSeen,
                Unsupported = preview.Unsupported,
                Error = string.IsNullOrWhiteSpace(preview.Error) ? null : preview.Error,
            })
            .ToArray();
    }
}
