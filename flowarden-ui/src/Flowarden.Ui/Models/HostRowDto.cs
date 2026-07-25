namespace Flowarden.Ui.Models;

public sealed class HostRowDto
{
    public string Host { get; init; } = string.Empty;

    public string CountryLabel { get; init; } = string.Empty;

    public string Hostname { get; init; } = string.Empty;

    /// TLS SNI when observed for traffic involving this host.
    public string Sni { get; init; } = string.Empty;

    public uint AsnNumber { get; init; }

    public string AsnOrganization { get; init; } = string.Empty;

    /// Preformatted "AS15169 · Google LLC"; empty if unknown.
    public string AsnLabel { get; init; } = string.Empty;

    /// Best display name: SNI > rDNS > empty.
    public string PreferredName =>
        !string.IsNullOrWhiteSpace(Sni)
            ? Sni.Trim()
            : !string.IsNullOrWhiteSpace(Hostname)
                ? Hostname.Trim()
                : string.Empty;

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }
}
