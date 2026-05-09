using System;

namespace Flowarden.Ui.Models;

public sealed class ConnectionRowDto
{
    public string SourceAddress { get; init; } = string.Empty;

    public ushort? SourcePort { get; init; }

    public string DestinationAddress { get; init; } = string.Empty;

    public ushort? DestinationPort { get; init; }

    public string PeerAddress =>
        string.Equals(Direction, "inbound", StringComparison.OrdinalIgnoreCase)
            ? SourceAddress
            : DestinationAddress;

    public ushort? PeerPort =>
        string.Equals(Direction, "inbound", StringComparison.OrdinalIgnoreCase)
            ? SourcePort
            : DestinationPort;

    public string Protocol { get; init; } = string.Empty;

    public string ServiceName { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }

    public string ProtocolLabel => Protocol.ToUpperInvariant();

    public string DirectionGlyph =>
        string.Equals(Direction, "inbound", StringComparison.OrdinalIgnoreCase) ? "<-" : "->";

    public string PacketsLabel => FormatCount(Packets);

    public string BytesLabel => FormatBytes(Bytes);

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000.0:0.#}G";
        }

        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000.0:0.#}M";
        }

        if (bytes >= 1_000)
        {
            return $"{bytes / 1_000.0:0.#}K";
        }

        return $"{bytes}B";
    }

    private static string FormatCount(ulong count)
    {
        if (count >= 1_000_000)
        {
            return $"{count / 1_000_000.0:0.#}M";
        }

        if (count >= 1_000)
        {
            return $"{count / 1_000.0:0.#}K";
        }

        return count.ToString();
    }
}
