using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flowarden.Ui.Models;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewRankingsBuilder
{
    // Match TfcDataAccent / TfcMutedText tokens (string form for brush bindings).
    public const string DataAccentBrush = "#D9A84E";
    public const string NeutralMetricBrush = "#CBC4D2";

    public static IReadOnlyList<OverviewMetricRowViewModel> BuildTopHostRows(
        IReadOnlyList<HostRowDto> rows
    )
    {
        var maxPackets = rows.Count == 0 ? 0 : rows.Max(row => row.Packets);
        return rows
            .Select(row =>
            {
                var label = OverviewFormatting.FormatAddressWithOwner(
                    row.Host,
                    row.CountryLabel,
                    row.Hostname,
                    row.Sni
                );
                var rawHost = row.Host?.Trim() ?? string.Empty;

                return new OverviewMetricRowViewModel(
                    label,
                    OverviewFormatting.FormatCount(row.Packets),
                    CalculateBarWidth(row.Packets, maxPackets),
                    NeutralMetricBrush,
                    tooltip: string.IsNullOrWhiteSpace(rawHost)
                        ? label
                        : $"{label}\nClick to filter Inspect · host={rawHost}",
                    pivotKind: "host",
                    pivotValue: rawHost
                );
            })
            .ToArray();
    }

    public static IReadOnlyList<OverviewMetricRowViewModel> BuildTopServiceRows(
        IReadOnlyList<ServiceRowDto> rows
    )
    {
        var totalBytes = rows.Aggregate<ServiceRowDto, ulong>(0, (current, row) => current + row.Bytes);
        var maxBytes = rows.Count == 0 ? 0 : rows.Max(row => row.Bytes);
        return rows
            .Select(row =>
            {
                var rawName = row.Name?.Trim() ?? string.Empty;
                var displayName = string.IsNullOrWhiteSpace(rawName)
                    ? "(unknown)"
                    : rawName.ToUpperInvariant();
                return new OverviewMetricRowViewModel(
                    displayName,
                    totalBytes == 0
                        ? OverviewFormatting.FormatBytes(row.Bytes)
                        : $"{(double)row.Bytes / totalBytes:0%}",
                    CalculateBarWidth(row.Bytes, maxBytes),
                    DataAccentBrush,
                    tooltip: string.IsNullOrWhiteSpace(rawName)
                        ? displayName
                        : $"Click to filter Inspect · service={rawName}",
                    pivotKind: "service",
                    pivotValue: rawName
                );
            })
            .ToArray();
    }

    public static IReadOnlyList<OverviewRegionRowViewModel> BuildTopRegionRows(
        IReadOnlyList<DestinationSummaryDto> rows
    )
    {
        var maxBytes = rows.Count == 0 ? 0UL : rows.Max(row => row.Bytes);
        return rows
            .Select(row =>
            {
                var label = string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label;
                var code = string.IsNullOrWhiteSpace(row.CountryCode)
                    ? OverviewFormatting.ExtractOwnerCode(row.CountryLabel)
                    : row.CountryCode.Trim();
                var pivot = !string.IsNullOrWhiteSpace(code) ? code : label;
                var ratio = row.Ratio.ToString("P0", CultureInfo.InvariantCulture);
                return new OverviewRegionRowViewModel(
                    label,
                    ratio,
                    DataAccentBrush,
                    tooltip: $"{label} · {ratio} · {OverviewFormatting.FormatBytes(row.Bytes)}\nClick to filter Inspect · country={pivot}",
                    pivotValue: pivot,
                    barWidth: CalculateBarWidth(row.Bytes, maxBytes),
                    bytesLabel: OverviewFormatting.FormatBytes(row.Bytes)
                );
            })
            .ToArray();
    }

    public static IReadOnlyList<OverviewConnectionRowViewModel> BuildTopConnectionRows(
        IReadOnlyList<ConnectionRowDto> rows,
        IReadOnlyList<HostRowDto> hosts
    )
    {
        var countryByHost = hosts
            .Where(host => !string.IsNullOrWhiteSpace(host.CountryLabel))
            .GroupBy(host => host.Host, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => OverviewFormatting.ExtractOwnerCode(group.First().CountryLabel),
                StringComparer.OrdinalIgnoreCase
            );

        return rows
            .Select(row =>
            {
                var source = row.SourceAddress ?? string.Empty;
                var destination = row.DestinationAddress ?? string.Empty;
                // Prefer remote peer for pivot Search (local-oriented sessions).
                var peerRaw = !string.IsNullOrWhiteSpace(destination)
                    ? destination.Trim()
                    : source.Trim();

                return new OverviewConnectionRowViewModel(
                    OverviewFormatting.FormatAddressWithOwner(
                        source,
                        countryByHost.TryGetValue(source, out var sourceOwner)
                            ? sourceOwner
                            : string.Empty
                    ),
                    OverviewFormatting.FormatAddressWithOwner(
                        destination,
                        countryByHost.TryGetValue(destination, out var destinationOwner)
                            ? destinationOwner
                            : string.Empty
                    ),
                    OverviewFormatting.FormatBytes(row.Bytes),
                    row.ProcessLabel,
                    row.IconKey,
                    peerAddressRaw: peerRaw,
                    processNameRaw: row.ProcessName?.Trim() ?? string.Empty
                );
            })
            .ToArray();
    }

    public static IReadOnlyList<OverviewStatusCardViewModel> BuildStatusCards(
        OverviewSnapshotDto snapshot,
        string modeLabel
    )
    {
        return
        [
            new OverviewStatusCardViewModel(
                "Packets",
                snapshot.Totals.Packets.ToString(),
                "Phase1 aggregate total"
            ),
            new OverviewStatusCardViewModel(
                "Bytes",
                snapshot.Totals.Bytes.ToString(),
                "Phase1 aggregate total"
            ),
            new OverviewStatusCardViewModel(
                "Dropped",
                snapshot.DroppedPackets.ToString(),
                "Capture drop metric"
            ),
            new OverviewStatusCardViewModel("Mode", modeLabel, "Live / offline display"),
        ];
    }

    public static string ResolveModeLabel(string? modeOverride, OverviewSnapshotDto snapshot)
    {
        if (!string.IsNullOrWhiteSpace(modeOverride))
        {
            return string.Equals(modeOverride, "Replay", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeOverride, "Offline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeOverride, "offline", StringComparison.OrdinalIgnoreCase)
                ? "Offline"
                : "Live";
        }

        return string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? "Offline"
            : "Live";
    }

    public static string ResolveMetricStateLabel(bool hasValue, OverviewSnapshotDto snapshot)
    {
        if (!hasValue)
        {
            return "WAITING";
        }

        return string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? "OFFLINE"
            : "LIVE";
    }

    private static double CalculateBarWidth(ulong value, ulong maxValue)
    {
        if (value == 0 || maxValue == 0)
        {
            return 0;
        }

        return Math.Max(8, Math.Min(140, value / (double)maxValue * 140));
    }
}
