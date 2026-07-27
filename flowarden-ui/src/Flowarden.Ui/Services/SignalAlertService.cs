using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flowarden.Ui.Models;
using Flowarden.Ui.State;

namespace Flowarden.Ui.Services;

/// <summary>In-app toasts + optional OS beep for new signals.</summary>
public sealed class SignalAlertService
{
    private WindowNotificationManager? _manager;
    private readonly UserPreferences _preferences;

    public SignalAlertService(UserPreferences preferences)
    {
        _preferences = preferences;
    }

    public void Attach(Window window)
    {
        _manager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3,
        };
    }

    public void OnNewSignal(SignalItemDto signal)
    {
        if (!_preferences.DesktopNotificationsEnabled && !_preferences.SignalSoundEnabled)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_preferences.DesktopNotificationsEnabled)
            {
                _manager?.Show(
                    new Notification(
                        signal.Title,
                        string.IsNullOrWhiteSpace(signal.Detail) ? signal.Subject : signal.Detail,
                        MapType(signal.Severity)
                    )
                );
            }

            if (_preferences.SignalSoundEnabled)
            {
                _ = PlayAlertSoundAsync();
            }
        });
    }

    private static NotificationType MapType(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "error" or "high" => NotificationType.Error,
            "warning" or "medium" => NotificationType.Warning,
            _ => NotificationType.Information,
        };
    }

    private static async Task PlayAlertSoundAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var process = Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "/usr/bin/afplay",
                        ArgumentList = { "/System/Library/Sounds/Purr.aiff" },
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                );
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }

                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Beep(880, 120);
                return;
            }

            // Linux best-effort
            using var paplay = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "paplay",
                    ArgumentList = { "/usr/share/sounds/freedesktop/stereo/message.oga" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            if (paplay is not null)
            {
                await paplay.WaitForExitAsync();
            }
        }
        catch
        {
            // Sound is optional.
        }
    }

    public static Window? TryGetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
