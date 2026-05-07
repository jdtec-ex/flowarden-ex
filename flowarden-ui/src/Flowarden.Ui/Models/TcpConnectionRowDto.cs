using System;

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

    public string FirstSeenLabel => FormatTimestamp(FirstSeen);

    public string LastSeenLabel => FormatTimestamp(LastSeen);

    private static string FormatTimestamp(PacketTimestampDto timestamp)
    {
        if (timestamp.Seconds <= 0)
        {
            return "-";
        }

        var dateTime = DateTimeOffset
            .FromUnixTimeSeconds(timestamp.Seconds)
            .ToLocalTime()
            .DateTime;

        return timestamp.Microseconds == 0
            ? dateTime.ToString("HH:mm:ss")
            : $"{dateTime:HH:mm:ss}.{timestamp.Microseconds / 1000:000}";
    }
}
