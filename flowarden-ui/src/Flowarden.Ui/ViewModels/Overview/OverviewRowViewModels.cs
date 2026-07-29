using System.ComponentModel;
using Avalonia.Media;
using Flowarden.Ui.Services;

namespace Flowarden.Ui.ViewModels;

public sealed class OverviewStatusCardViewModel
{
    public OverviewStatusCardViewModel(string label, string value, string hint)
    {
        Label = label;
        Value = value;
        Hint = hint;
    }

    public string Label { get; }

    public string Value { get; }

    public string Hint { get; }
}

public sealed class OverviewMetricRowViewModel
{
    public OverviewMetricRowViewModel(
        string label,
        string valueLabel,
        double barWidth,
        string accentBrush,
        string tooltip = "",
        string pivotKind = "",
        string pivotValue = ""
    )
    {
        Label = label;
        ValueLabel = valueLabel;
        BarWidth = barWidth;
        AccentBrush = accentBrush;
        Tooltip = string.IsNullOrWhiteSpace(tooltip) ? label : tooltip;
        PivotKind = pivotKind;
        PivotValue = pivotValue;
    }

    public string Label { get; }

    public string Tooltip { get; }

    public string ValueLabel { get; }

    public double BarWidth { get; }

    public string AccentBrush { get; }

    /// <summary>Raw projection key kind for Inspect pivot (never display Label).</summary>
    public string PivotKind { get; }

    /// <summary>Raw projection key value (IP, service name, etc.).</summary>
    public string PivotValue { get; }

    public bool CanPivot =>
        !string.IsNullOrWhiteSpace(PivotKind) && !string.IsNullOrWhiteSpace(PivotValue);
}

public sealed class OverviewRegionRowViewModel
{
    public OverviewRegionRowViewModel(
        string label,
        string ratioLabel,
        string accentBrush,
        string tooltip = "",
        string pivotValue = "",
        double barWidth = 0,
        string bytesLabel = ""
    )
    {
        Label = label;
        RatioLabel = ratioLabel;
        AccentBrush = accentBrush;
        Tooltip = string.IsNullOrWhiteSpace(tooltip) ? label : tooltip;
        PivotValue = pivotValue;
        BarWidth = barWidth;
        BytesLabel = bytesLabel;
    }

    public string Label { get; }

    public string Tooltip { get; }

    public string RatioLabel { get; }

    public string AccentBrush { get; }

    public double BarWidth { get; }

    public string BytesLabel { get; }

    /// <summary>Raw country code (preferred) or country label for Inspect pivot.</summary>
    public string PivotValue { get; }

    public string PivotKind => "country";

    public bool CanPivot => !string.IsNullOrWhiteSpace(PivotValue);

    public string PivotTooltip =>
        CanPivot ? $"Filter Inspect by country={PivotValue}" : string.Empty;
}

public sealed class OverviewRegionMarkerViewModel
{
    public OverviewRegionMarkerViewModel(
        string label,
        string ratioLabel,
        string bytesLabel,
        double x,
        double y,
        double size,
        string accentBrush,
        string pivotValue = "",
        string shortLabel = ""
    )
    {
        Label = label;
        RatioLabel = ratioLabel;
        BytesLabel = bytesLabel;
        X = x;
        Y = y;
        Size = size;
        AccentBrush = accentBrush;
        PivotValue = pivotValue;
        ShortLabel = string.IsNullOrWhiteSpace(shortLabel) ? label : shortLabel;
    }

    public string Label { get; }

    public string RatioLabel { get; }

    public string BytesLabel { get; }

    public double X { get; }

    public double Y { get; }

    public double Size { get; }

    public string AccentBrush { get; }

    public string PivotValue { get; }

    public string ShortLabel { get; }

    public string PivotKind => "country";

    public bool CanPivot => !string.IsNullOrWhiteSpace(PivotValue);

    public string MapTooltip =>
        $"{Label} · {RatioLabel} · {BytesLabel}"
        + (CanPivot ? "\nClick to filter Inspect" : string.Empty);
}

public sealed class OverviewConnectionRowViewModel : INotifyPropertyChanged
{
    private IImage? _processIcon;

    public OverviewConnectionRowViewModel(
        string sourceAddress,
        string destinationAddress,
        string volumeLabel,
        string processLabel = "—",
        ProcessIconKey iconKey = default,
        string peerAddressRaw = "",
        string processNameRaw = ""
    )
    {
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        VolumeLabel = volumeLabel;
        ProcessLabel = string.IsNullOrWhiteSpace(processLabel) ? "—" : processLabel;
        IconKey = iconKey;
        PeerAddressRaw = peerAddressRaw;
        ProcessNameRaw = processNameRaw;
    }

    public string SourceAddress { get; }

    public string DestinationAddress { get; }

    public string VolumeLabel { get; }

    public string ProcessLabel { get; }

    public ProcessIconKey IconKey { get; }

    /// <summary>Raw remote/peer IP for Inspect pivot (not display Label).</summary>
    public string PeerAddressRaw { get; }

    public string ProcessNameRaw { get; }

    // KD16: connection peer → host SearchText (OR src+dst), not process-only.
    public string PivotKind => "host";

    public string PivotValue => PeerAddressRaw;

    public bool CanPivot => !string.IsNullOrWhiteSpace(PivotValue);

    public string PivotTooltip =>
        CanPivot
            ? $"Filter Inspect by host={PivotValue}"
                + (string.IsNullOrWhiteSpace(ProcessNameRaw) ? string.Empty : $" · process {ProcessNameRaw}")
            : string.Empty;

    public string ProcessMonogram => IconKey.IsEmpty ? "·" : IconKey.Monogram;

    public IBrush ProcessMonogramBrush => IconKey.MonogramBrush;

    public bool HasProcessIcon => ProcessIcon is not null;

    public bool ShowProcessMonogram => !HasProcessIcon;

    public IImage? ProcessIcon
    {
        get => _processIcon;
        set
        {
            if (ReferenceEquals(_processIcon, value))
            {
                return;
            }

            _processIcon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProcessIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProcessMonogram)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
