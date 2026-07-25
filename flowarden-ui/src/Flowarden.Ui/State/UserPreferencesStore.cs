using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Flowarden.Ui.State;

public sealed class UserPreferences
{
    public uint TopN { get; set; } = 10;

    public ulong DataThresholdBytes { get; set; } = 50_000_000;

    public List<string> WatchedHosts { get; set; } = new();

    public List<string> KnownBadHosts { get; set; } = new();

    public bool ShutdownCoreWhenUiCloses { get; set; } = true;

    public bool DesktopNotificationsEnabled { get; set; } = true;

    public bool SignalSoundEnabled { get; set; } = false;

    /// <summary>When true, restore the main window into thumbnail mode on next launch.</summary>
    public bool StartInThumbnail { get; set; }

    public double ThumbnailX { get; set; } = double.NaN;

    public double ThumbnailY { get; set; } = double.NaN;

    public double ThumbnailWidth { get; set; } = 360;

    public double ThumbnailHeight { get; set; } = 220;

    public double NormalWidth { get; set; } = 1440;

    public double NormalHeight { get; set; } = 900;
}

/// <summary>Simple JSON preferences under the user app-data directory.</summary>
public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public UserPreferencesStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Flowarden"
        );
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "preferences.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UserPreferences();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions)
                ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public string FilePath => _path;

    public void Save(UserPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
