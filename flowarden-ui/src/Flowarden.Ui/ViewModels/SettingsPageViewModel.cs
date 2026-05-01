using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed class SettingsPageViewModel : ViewModelBase
{
    public SettingsPageViewModel()
    {
        RuntimeState = new CaptureSessionStateDto
        {
            SourceKind = "live",
            SourceDisplayName = "en0",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = "tcp",
        };

        CoreHealth = new CoreHealthDto
        {
            Status = "ok",
            StartedAtUnixSeconds = 1_714_587_200,
        };

        Diagnostics = new ReadOnlyCollection<CoreErrorDto>(
            [
                new CoreErrorDto
                {
                    Source = "Capture",
                    Reason = "PermissionDenied",
                    Message = "Live capture on restricted interfaces may require elevated permissions.",
                },
                new CoreErrorDto
                {
                    Source = "Filter",
                    Reason = "NotApplied",
                    Message = "No active filter apply failures. Entry reserved for future runtime diagnostics.",
                },
            ]
        );
    }

    public CaptureSessionStateDto RuntimeState { get; }

    public CoreHealthDto CoreHealth { get; }

    public IReadOnlyList<CoreErrorDto> Diagnostics { get; }

    public string CoreEndpoint => "127.0.0.1:39091";

    public string ProcessState => "Running";

    public string CoreVersion => "0.1.0";

    public string UiVersion => "0.1.0-phase2";

    public string TickInterval => "1s";

    public string TopN => "20";

    public string ErrorLogEntry => "Settings diagnostics list";

    public string StartedAtLabel => DateTimeOffset.FromUnixTimeSeconds((long)CoreHealth.StartedAtUnixSeconds)
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss");
}
