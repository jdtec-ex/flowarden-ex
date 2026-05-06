namespace Flowarden.Ui.Models;

public sealed class ConnectionRowDto
{
    public string SourceAddress { get; init; } = string.Empty;

    public ushort? SourcePort { get; init; }

    public string DestinationAddress { get; init; } = string.Empty;

    public ushort? DestinationPort { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string ServiceName { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }

    public string TcpState { get; init; } = string.Empty;

    public ulong SynCount { get; init; }

    public ulong FinCount { get; init; }

    public ulong RstCount { get; init; }
}
