using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed class SourceDeviceItemViewModel
{
    public required DeviceSummaryDto Device { get; init; }

    public required DevicePreviewDto Preview { get; init; }

    public string DisplayName => Device.Name;

    public string Description => string.IsNullOrWhiteSpace(Device.Description) ? "No description" : Device.Description;

    public string PreviewSummary => $"Preview only: {Preview.PacketsSeen} packets / {Preview.BytesSeen} bytes";

    public string StatusLabel =>
        Preview.Unsupported ? "Unsupported" :
        string.IsNullOrWhiteSpace(Preview.Error) ? "Ready for formal capture" :
        "Permission or capture error";
}
