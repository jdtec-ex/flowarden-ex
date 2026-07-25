using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.State;
using Flowarden.Ui.ViewModels.Overview;

namespace Flowarden.Ui.ViewModels;

public sealed partial class ThumbnailViewModel : ViewModelBase
{
    private readonly LiveProjectionState _liveProjectionState;
    private readonly AppShellViewModel _shell;

    public ThumbnailViewModel(LiveProjectionState liveProjectionState, AppShellViewModel shell)
    {
        _liveProjectionState = liveProjectionState;
        _shell = shell;
        _liveProjectionState.OverviewUpdated += OnOverviewUpdated;
        ApplySnapshot(_liveProjectionState.CurrentOverview);
    }

    [ObservableProperty]
    private string packetsLabel = "0";

    [ObservableProperty]
    private string bytesLabel = "0 B";

    [ObservableProperty]
    private string inboundLabel = "0 B";

    [ObservableProperty]
    private string outboundLabel = "0 B";

    [ObservableProperty]
    private string captureStatusLabel = "Idle";

    [ObservableProperty]
    private string sourceLabel = "not started";

    [ObservableProperty]
    private string sparklinePath = string.Empty;

    [ObservableProperty]
    private string unreadSignalsLabel = "0";

    [ObservableProperty]
    private bool hasUnreadSignals;

    public IRelayCommand ExpandCommand => _shell.ExitThumbnailCommand;

    public IAsyncRelayCommand PauseCommand => _shell.SourcePage.PauseFormalCaptureCommand;

    public IAsyncRelayCommand ResumeCommand => _shell.SourcePage.ResumeFormalCaptureCommand;

    public IAsyncRelayCommand StopCommand => _shell.SourcePage.StopFormalCaptureCommand;

    public bool CanPause => _shell.SourcePage.CanPauseFormalCapture;

    public bool CanResume => _shell.SourcePage.CanResumeFormalCapture;

    public bool CanStop => _shell.SourcePage.CanStopFormalCapture;

    public void RefreshFromCurrentProjection()
    {
        ApplySnapshot(_liveProjectionState.CurrentOverview);
    }

    private void OnOverviewUpdated(OverviewSnapshotDto snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySnapshot(snapshot);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        // Totals are cumulative session counters (not per-second rates).
        PacketsLabel = OverviewFormatting.FormatCount(snapshot.Totals.Packets);
        BytesLabel = OverviewFormatting.FormatBytes(snapshot.Totals.Bytes);
        InboundLabel = OverviewFormatting.FormatBytes(snapshot.Totals.BytesIn);
        OutboundLabel = OverviewFormatting.FormatBytes(snapshot.Totals.BytesOut);
        CaptureStatusLabel = string.IsNullOrWhiteSpace(snapshot.CaptureStatus)
            ? "idle"
            : snapshot.CaptureStatus;
        SourceLabel = snapshot.SourceLabel
            .Replace("Live source · ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Offline source · ", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(SourceLabel))
        {
            SourceLabel = "not started";
        }

        var max = OverviewChartPaths.CalculateMaxTimelineValue(snapshot.TimelinePoints);
        SparklinePath = OverviewChartPaths.BuildTimelinePath(
            snapshot.TimelinePoints,
            max,
            selectOutbound: true
        );
        UnreadSignalsLabel = _shell.SignalUnreadCount.ToString();
        HasUnreadSignals = _shell.HasSignalUnread;
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
    }
}
