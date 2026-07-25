using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.State;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SignalsPageViewModel : ViewModelBase
{
    private readonly SignalFeedState _feed;

    public SignalsPageViewModel(SignalFeedState feed)
    {
        _feed = feed;
        _feed.Changed += OnFeedChanged;
        OnFeedChanged();
    }

    public ObservableCollection<SignalItemDto> Signals => _feed.Signals;

    public event Action<SignalItemDto>? PivotRequested;

    [ObservableProperty]
    private string summaryLabel = "No signals yet";

    [ObservableProperty]
    private int unreadCount;

    [RelayCommand]
    private void MarkAllRead()
    {
        _feed.MarkAllRead();
    }

    [RelayCommand]
    private void ClearSignals()
    {
        _feed.Clear();
    }

    [RelayCommand]
    private void OpenSignal(SignalItemDto? signal)
    {
        if (signal is null)
        {
            return;
        }

        _feed.MarkRead(signal);
        PivotRequested?.Invoke(signal);
    }

    private void OnFeedChanged()
    {
        UnreadCount = _feed.UnreadCount;
        if (Signals.Count == 0)
        {
            SummaryLabel =
                "No behavior signals yet. Threshold and watchlist rules will appear here.";
            return;
        }

        var offline = 0;
        var live = 0;
        foreach (var signal in Signals)
        {
            if (string.Equals(signal.Mode, "offline", StringComparison.OrdinalIgnoreCase))
            {
                offline++;
            }
            else
            {
                live++;
            }
        }

        SummaryLabel = offline > 0 && live == 0
            ? $"{offline} offline finding(s) · {UnreadCount} unread · click to pivot Overview + Inspect"
            : offline > 0
                ? $"{Signals.Count} signal(s) ({live} live / {offline} offline) · {UnreadCount} unread"
                : $"{Signals.Count} signal(s) · {UnreadCount} unread · click a row to inspect";
    }
}
