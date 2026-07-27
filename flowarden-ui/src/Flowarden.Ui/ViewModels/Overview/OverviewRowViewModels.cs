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
        string tooltip = ""
    )
    {
        Label = label;
        ValueLabel = valueLabel;
        BarWidth = barWidth;
        AccentBrush = accentBrush;
        Tooltip = string.IsNullOrWhiteSpace(tooltip) ? label : tooltip;
    }

    public string Label { get; }

    public string Tooltip { get; }

    public string ValueLabel { get; }

    public double BarWidth { get; }

    public string AccentBrush { get; }
}

public sealed class OverviewRegionRowViewModel
{
    public OverviewRegionRowViewModel(
        string label,
        string ratioLabel,
        string accentBrush,
        string tooltip = ""
    )
    {
        Label = label;
        RatioLabel = ratioLabel;
        AccentBrush = accentBrush;
        Tooltip = string.IsNullOrWhiteSpace(tooltip) ? label : tooltip;
    }

    public string Label { get; }

    public string Tooltip { get; }

    public string RatioLabel { get; }

    public string AccentBrush { get; }
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
        string accentBrush
    )
    {
        Label = label;
        RatioLabel = ratioLabel;
        BytesLabel = bytesLabel;
        X = x;
        Y = y;
        Size = size;
        AccentBrush = accentBrush;
    }

    public string Label { get; }

    public string RatioLabel { get; }

    public string BytesLabel { get; }

    public double X { get; }

    public double Y { get; }

    public double Size { get; }

    public string AccentBrush { get; }
}

public sealed class OverviewConnectionRowViewModel
{
    public OverviewConnectionRowViewModel(
        string sourceAddress,
        string destinationAddress,
        string volumeLabel,
        string processLabel = "—"
    )
    {
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        VolumeLabel = volumeLabel;
        ProcessLabel = string.IsNullOrWhiteSpace(processLabel) ? "—" : processLabel;
    }

    public string SourceAddress { get; }

    public string DestinationAddress { get; }

    public string VolumeLabel { get; }

    public string ProcessLabel { get; }
}
