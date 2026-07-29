using System;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

/// <summary>
/// Pure Inspect filter matching (Search OR ∩ structured AND). Used by live/cold paths.
/// </summary>
public static class InspectFilterMatcher
{
    public static bool Matches(ConnectionRowDto row, InspectFilterDto filter)
    {
        if (!MatchesSearchOr(row, filter.SearchText))
        {
            return false;
        }

        return MatchesText(filter.SourceAddress, row.SourceAddress)
            && MatchesText(filter.DestinationAddress, row.DestinationAddress)
            && MatchesText(filter.ServiceName, row.ServiceName)
            && MatchesText(filter.Protocol, row.Protocol)
            && MatchesText(filter.Direction, row.Direction)
            && MatchesText(filter.ProcessName, row.ProcessName)
            && MatchesText(filter.Sni, row.Sni);
    }

    public static bool MatchesSearchAndLocalOnly(ConnectionRowDto row, InspectFilterDto filter)
    {
        // After server structured filter: still re-apply Search + process/sni locally (KD14).
        if (!MatchesSearchOr(row, filter.SearchText))
        {
            return false;
        }

        return MatchesText(filter.ProcessName, row.ProcessName)
            && MatchesText(filter.Sni, row.Sni);
    }

    public static bool MatchesTcp(TcpConnectionRowDto row, InspectFilterDto filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var q = filter.SearchText.Trim();
            if (!row.ConnectionLabel.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !row.State.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return MatchesText(filter.Address, row.ConnectionLabel)
            && MatchesText(filter.Port, row.ConnectionLabel)
            && MatchesText(filter.State, row.State);
    }

    public static bool MatchesSearchOr(ConnectionRowDto row, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var q = searchText.Trim();
        return row.SourceAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.DestinationAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.ServiceName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Protocol.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Sni.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesText(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter)
            || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
