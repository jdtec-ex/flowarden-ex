using System;
using System.Collections.Generic;

namespace Flowarden.Ui.Models;

public sealed class InspectResultDto
{
    public IReadOnlyList<ConnectionRowDto> Rows { get; init; } = Array.Empty<ConnectionRowDto>();

    public IReadOnlyList<TcpConnectionRowDto> TcpRows { get; init; } = Array.Empty<TcpConnectionRowDto>();

    public InspectResultSummaryDto Summary { get; init; } = new();

    public string State { get; init; } = "seed";
}

public sealed class InspectResultSummaryDto
{
    public ulong TotalRows { get; init; }

    public ulong VisibleRows { get; init; }

    public ulong TotalPackets { get; init; }

    public ulong TotalBytes { get; init; }

    public string SortBy { get; init; } = "bytes";

    public string SortDirection { get; init; } = "desc";
}
