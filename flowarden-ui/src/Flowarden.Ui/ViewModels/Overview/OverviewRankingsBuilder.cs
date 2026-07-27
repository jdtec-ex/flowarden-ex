using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flowarden.Ui.Models;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewRankingsBuilder
{
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

                return new OverviewMetricRowViewModel(
                    label,
                    OverviewFormatting.FormatCount(row.Packets),
                    CalculateBarWidth(row.Packets, maxPackets),
                    NeutralMetricBrush
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
            .Select(row => new OverviewMetricRowViewModel(
                row.Name.ToUpperInvariant(),
                totalBytes == 0
                    ? OverviewFormatting.FormatBytes(row.Bytes)
                    : $"{(double)row.Bytes / totalBytes:0%}",
                CalculateBarWidth(row.Bytes, maxBytes),
                DataAccentBrush
            ))
            .ToArray();
    }

    public static IReadOnlyList<OverviewRegionRowViewModel> BuildTopRegionRows(
        IReadOnlyList<DestinationSummaryDto> rows
    )
    {
        return rows
            .Select(row =>
            {
                var label = string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label;
                return new OverviewRegionRowViewModel(
                    label,
                    row.Ratio.ToString("P0", CultureInfo.InvariantCulture),
                    DataAccentBrush
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
            .Select(row => new OverviewConnectionRowViewModel(
                OverviewFormatting.FormatAddressWithOwner(
                    row.SourceAddress,
                    countryByHost.TryGetValue(row.SourceAddress, out var sourceOwner)
                        ? sourceOwner
                        : string.Empty
                ),
                OverviewFormatting.FormatAddressWithOwner(
                    row.DestinationAddress,
                    countryByHost.TryGetValue(row.DestinationAddress, out var destinationOwner)
                        ? destinationOwner
                        : string.Empty
                ),
                OverviewFormatting.FormatBytes(row.Bytes),
                row.ProcessLabel,
                row.IconKey
            ))
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
