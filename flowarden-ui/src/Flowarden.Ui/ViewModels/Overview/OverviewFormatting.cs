using System;
using System.Globalization;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewFormatting
{
    public static string FormatByteRate(ulong bytes)
    {
        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:0.##} MB/s";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.##} KB/s";
        }

        return $"{bytes} B/s";
    }

    public static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1_099_511_627_776)
        {
            return $"{bytes / 1_099_511_627_776.0:0.##} TB";
        }

        if (bytes >= 1_073_741_824)
        {
            return $"{bytes / 1_073_741_824.0:0.##} GB";
        }

        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:0.##} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.##} KB";
        }

        return $"{bytes} B";
    }

    public static string FormatCount(ulong value)
    {
        if (value >= 1_000_000_000)
        {
            return $"{value / 1_000_000_000.0:0.##}B";
        }

        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000.0:0.##}M";
        }

        if (value >= 1_000)
        {
            return $"{value / 1_000.0:0.##}K";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatPacketTimestamp(PacketTimestampDto timestamp)
    {
        var localTime = DateTimeOffset
            .FromUnixTimeSeconds(timestamp.Seconds)
            .ToLocalTime();

        return localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatAxisTime(long axisSeconds)
    {
        if (axisSeconds <= 0)
        {
            return "--:--:--";
        }

        var localTime = DateTimeOffset
            .FromUnixTimeSeconds(axisSeconds)
            .ToLocalTime();

        return localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatAddressWithOwner(
        string address,
        string ownerLabel,
        string? hostname = null,
        string? sni = null
    )
    {
        var host = address?.Trim() ?? string.Empty;
        // Product order: SNI (business domain) > rDNS PTR > Country+IP.
        var name = !string.IsNullOrWhiteSpace(sni)
            ? sni.Trim()
            : hostname?.Trim() ?? string.Empty;
        var ownerCode = ExtractOwnerCode(ownerLabel);

        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(ownerCode)
                ? name
                : $"{name} · {ownerCode}";
        }

        return string.IsNullOrWhiteSpace(ownerCode) ? host : $"{host} · {ownerCode}";
    }

    public static string ExtractOwnerCode(string ownerLabel)
    {
        if (string.IsNullOrWhiteSpace(ownerLabel))
        {
            return string.Empty;
        }

        var normalized = ownerLabel.Trim();
        var separatorIndex = normalized.IndexOf('·');
        if (separatorIndex < 0)
        {
            separatorIndex = normalized.IndexOf(' ');
        }

        var code = separatorIndex > 0 ? normalized[..separatorIndex] : normalized;
        code = code.Trim();
        return code.Length is >= 2 and <= 6 ? code.ToUpperInvariant() : string.Empty;
    }

}
