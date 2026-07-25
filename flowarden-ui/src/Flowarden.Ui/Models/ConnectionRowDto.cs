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

    /// Local process name from heuristic port→pid mapping (may be empty).
    public string ProcessName { get; init; } = string.Empty;

    public uint ProcessPid { get; init; }

    public bool ProcessInferred { get; init; }

    public string Sni { get; init; } = string.Empty;

    public string ProcessPath { get; init; } = string.Empty;

    public string ProcessBundleId { get; init; } = string.Empty;

    public string RemoteAsnLabel { get; init; } = string.Empty;

    public string ProcessLabel =>
        string.IsNullOrWhiteSpace(ProcessName)
            ? "—"
            : ProcessPid == 0
                ? ProcessName
                : $"{ProcessName} · {ProcessPid}";

    public string ProcessMonogram =>
        string.IsNullOrWhiteSpace(ProcessName)
            ? "·"
            : char.ToUpperInvariant(ProcessName.Trim()[0]).ToString();

    public string ProcessTooltip
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProcessName))
            {
                return "No process attribution";
            }

            var path = string.IsNullOrWhiteSpace(ProcessPath) ? "path unknown" : ProcessPath;
            var asn = string.IsNullOrWhiteSpace(RemoteAsnLabel) ? string.Empty : $"\nRemote ASN: {RemoteAsnLabel}";
            return $"{ProcessLabel}\n{path}{asn}";
        }
    }

    public string SniLabel => string.IsNullOrWhiteSpace(Sni) ? "—" : Sni.Trim();

    public string RemoteAsnDisplay =>
        string.IsNullOrWhiteSpace(RemoteAsnLabel) ? "—" : RemoteAsnLabel.Trim();

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
