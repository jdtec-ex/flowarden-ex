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

public sealed class OverviewConnectionRowViewModel : INotifyPropertyChanged
{
    private IImage? _processIcon;

    public OverviewConnectionRowViewModel(
        string sourceAddress,
        string destinationAddress,
        string volumeLabel,
        string processLabel = "—",
        ProcessIconKey iconKey = default
    )
    {
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        VolumeLabel = volumeLabel;
        ProcessLabel = string.IsNullOrWhiteSpace(processLabel) ? "—" : processLabel;
        IconKey = iconKey;
    }

    public string SourceAddress { get; }

    public string DestinationAddress { get; }

    public string VolumeLabel { get; }

    public string ProcessLabel { get; }

    public ProcessIconKey IconKey { get; }

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
