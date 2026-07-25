using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flowarden.Ui.Models;

public sealed partial class SignalItemDto : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Severity { get; init; } = "info";

    /// live | offline
    public string Mode { get; init; } = "live";

    /// active | updated | finding | ...
    public string Status { get; init; } = "active";

    public string PivotKind { get; init; } = "none";

    public string PivotValue { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadMarker))]
    private bool isUnread = true;

    public string TimestampLabel => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string UnreadMarker => IsUnread ? "●" : " ";

    public string ModeStatusLabel =>
        string.Equals(Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? $"offline · {Status}"
            : $"live · {Status}";

    public bool CanPivot =>
        !string.IsNullOrWhiteSpace(PivotValue)
        && !string.Equals(PivotKind, "none", StringComparison.OrdinalIgnoreCase);
}
