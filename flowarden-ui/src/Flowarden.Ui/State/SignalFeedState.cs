using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.State;

public sealed class SignalFeedState
{
    private const int MaxSignals = 30;
    private readonly HashSet<string> _seenIds = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastThresholdAt = DateTimeOffset.MinValue;

    public ObservableCollection<SignalItemDto> Signals { get; } = new();

    public int UnreadCount { get; private set; }

    public event Action? Changed;

    /// <summary>Raised when a brand-new signal id is first observed (for desktop notify/sound).</summary>
    public event Action<SignalItemDto>? NewSignalRaised;

    public void ObserveOverview(OverviewSnapshotDto snapshot, UserPreferences preferences)
    {
        if (snapshot.Signals is { Count: > 0 })
        {
            foreach (var signal in snapshot.Signals.Reverse())
            {
                UpsertFromCore(signal);
            }

            Trim();
            RecountUnread();
            Changed?.Invoke();
            return;
        }

        // Fallback for older cores without signal projection.
        if (preferences.DataThresholdBytes > 0
            && snapshot.Totals.Bytes >= preferences.DataThresholdBytes
            && DateTimeOffset.Now - _lastThresholdAt > TimeSpan.FromSeconds(30))
        {
            _lastThresholdAt = DateTimeOffset.Now;
            UpsertFromCore(
                new SignalItemDto
                {
                    Id = $"local-threshold-{DateTimeOffset.Now.ToUnixTimeSeconds()}",
                    Kind = "DataThresholdExceeded",
                    Title = "Data threshold exceeded",
                    Detail =
                        $"Observed {snapshot.Totals.Bytes:N0} bytes (threshold {preferences.DataThresholdBytes:N0}).",
                    Subject = snapshot.SourceLabel,
                    Severity = "warning",
                    Mode = string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase)
                        ? "offline"
                        : "live",
                    Status = string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase)
                        ? "finding"
                        : "active",
                    Timestamp = DateTimeOffset.Now,
                    PivotKind = "none",
                }
            );
        }

        Trim();
        RecountUnread();
        Changed?.Invoke();
    }

    public void MarkAllRead()
    {
        foreach (var signal in Signals)
        {
            signal.IsUnread = false;
        }

        RecountUnread();
        Changed?.Invoke();
    }

    public void MarkRead(SignalItemDto signal)
    {
        signal.IsUnread = false;
        RecountUnread();
        Changed?.Invoke();
    }

    public void Clear()
    {
        Signals.Clear();
        _seenIds.Clear();
        UnreadCount = 0;
        Changed?.Invoke();
    }

    private void UpsertFromCore(SignalItemDto signal)
    {
        var key = string.IsNullOrWhiteSpace(signal.Id)
            ? $"{signal.Kind}|{signal.Subject}|{signal.Title}"
            : signal.Id;

        var existing = Signals.FirstOrDefault(s =>
            string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(s.Id)
                && string.Equals(s.Kind, signal.Kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Subject, signal.Subject, StringComparison.OrdinalIgnoreCase))
        );

        if (existing is not null)
        {
            // Keep position; refresh text only.
            return;
        }

        var isNew = _seenIds.Add(key);
        var item = new SignalItemDto
        {
            Id = key,
            Kind = signal.Kind,
            Title = signal.Title,
            Detail = signal.Detail,
            Subject = signal.Subject,
            Severity = signal.Severity,
            Mode = signal.Mode,
            Status = signal.Status,
            Timestamp = signal.Timestamp,
            PivotKind = signal.PivotKind,
            PivotValue = signal.PivotValue,
            IsUnread = true,
        };
        Signals.Insert(0, item);
        if (isNew)
        {
            NewSignalRaised?.Invoke(item);
        }
    }

    private void Trim()
    {
        while (Signals.Count > MaxSignals)
        {
            Signals.RemoveAt(Signals.Count - 1);
        }
    }

    private void RecountUnread()
    {
        UnreadCount = Signals.Count(s => s.IsUnread);
    }
}
