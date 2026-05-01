namespace Flowarden.Ui.ViewModels;

public sealed partial class AppShellViewModel : ViewModelBase
{
    public string Title { get; } = "Flowarden";

    public string Subtitle { get; } = "Phase 2 UI Shell";

    public string CurrentPageTitle { get; } = "Overview";

    public string CurrentMode { get; } = "Live";

    public string CoreStatus { get; } = "Core: disconnected";

    public string CaptureStatus { get; } = "Capture: idle";
}
