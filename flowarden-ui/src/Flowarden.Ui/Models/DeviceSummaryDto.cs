using System;
using System.Collections.Generic;

namespace Flowarden.Ui.Models;

public sealed class DeviceSummaryDto
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public IReadOnlyList<DeviceAddressDto> Addresses { get; init; } = Array.Empty<DeviceAddressDto>();
}

public sealed class DeviceAddressDto
{
    public string Address { get; init; } = string.Empty;

    public string? Netmask { get; init; }

    public string? BroadcastAddress { get; init; }

    public string? DestinationAddress { get; init; }
}
