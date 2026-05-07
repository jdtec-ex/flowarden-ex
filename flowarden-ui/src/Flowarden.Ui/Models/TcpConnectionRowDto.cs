namespace Flowarden.Ui.Models;

public sealed class TcpConnectionRowDto
{
    public string EndpointAAddress { get; init; } = string.Empty;

    public ushort EndpointAPort { get; init; }

    public string EndpointBAddress { get; init; } = string.Empty;

    public ushort EndpointBPort { get; init; }

    public string State { get; init; } = string.Empty;

    public ulong SynCount { get; init; }

    public ulong FinCount { get; init; }

    public ulong RstCount { get; init; }

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }

    public PacketTimestampDto FirstSeen { get; init; } = new();

    public PacketTimestampDto LastSeen { get; init; } = new();

    public string ConnectionLabel => $"{EndpointAAddress}:{EndpointAPort} ↔ {EndpointBAddress}:{EndpointBPort}";
}
