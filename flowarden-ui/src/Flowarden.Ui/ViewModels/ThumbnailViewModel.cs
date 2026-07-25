using System;
using System.Collections.Generic;
using System.Linq;
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
    private string inboundLabel = "0 B/s";

    [ObservableProperty]
    private string outboundLabel = "0 B/s";

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

    private void OnOverviewUpdated(OverviewSnapshotDto snapshot) => ApplySnapshot(snapshot);

    private void ApplySnapshot(OverviewSnapshotDto snapshot)
    {
        PacketsLabel = OverviewFormatting.FormatCount(snapshot.Totals.Packets);
        BytesLabel = OverviewFormatting.FormatBytes(snapshot.Totals.Bytes);
        InboundLabel = OverviewFormatting.FormatByteRate(snapshot.Totals.BytesIn);
        OutboundLabel = OverviewFormatting.FormatByteRate(snapshot.Totals.BytesOut);
        CaptureStatusLabel = string.IsNullOrWhiteSpace(snapshot.CaptureStatus)
            ? "idle"
            : snapshot.CaptureStatus;
        SourceLabel = snapshot.SourceLabel
            .Replace("Live source · ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Offline source · ", string.Empty, StringComparison.OrdinalIgnoreCase);
        var max = OverviewChartPaths.CalculateMaxTimelineValue(snapshot.TimelinePoints);
        SparklinePath = OverviewChartPaths.BuildTimelinePath(
            snapshot.TimelinePoints,
            max,
            selectOutbound: true
        );
        UnreadSignalsLabel = _shell.SignalsPage.UnreadCount.ToString();
        HasUnreadSignals = _shell.SignalsPage.UnreadCount > 0;
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
    }
}
