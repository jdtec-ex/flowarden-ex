using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flowarden.Ui.State;

public sealed class UserPreferences
{
    public uint TopN { get; set; } = 10;

    /// <summary>comfortable (default) or compact — UI density preference.</summary>
    public string UiDensity { get; set; } = "comfortable";

    public ulong DataThresholdBytes { get; set; } = 50_000_000;

    public List<string> WatchedHosts { get; set; } = new();

    public List<string> KnownBadHosts { get; set; } = new();

    public bool ShutdownCoreWhenUiCloses { get; set; } = true;

    public bool DesktopNotificationsEnabled { get; set; } = true;

    public bool SignalSoundEnabled { get; set; } = false;

    /// <summary>When true, restore the main window into thumbnail mode on next launch.</summary>
    public bool StartInThumbnail { get; set; }

    /// <summary>Null means "no saved thumbnail position yet".</summary>
    public double? ThumbnailX { get; set; }

    /// <summary>Null means "no saved thumbnail position yet".</summary>
    public double? ThumbnailY { get; set; }

    public double ThumbnailWidth { get; set; } = 360;

    public double ThumbnailHeight { get; set; } = 220;

    public double NormalWidth { get; set; } = 1440;

    public double NormalHeight { get; set; } = 900;

    /// <summary>Replace NaN/Infinity with JSON-safe defaults before persistence.</summary>
    public void SanitizeGeometry()
    {
        ThumbnailX = FiniteOrNull(ThumbnailX);
        ThumbnailY = FiniteOrNull(ThumbnailY);
        ThumbnailWidth = FiniteOr(ThumbnailWidth, 360, 320, 520);
        ThumbnailHeight = FiniteOr(ThumbnailHeight, 220, 180, 360);
        NormalWidth = FiniteOr(NormalWidth, 1440, 960, 3840);
        NormalHeight = FiniteOr(NormalHeight, 900, 640, 2160);
    }

    private static double? FiniteOrNull(double? value)
    {
        if (value is not { } number || double.IsNaN(number) || double.IsInfinity(number))
        {
            return null;
        }

        return number;
    }

    private static double FiniteOr(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            return fallback;
        }

        return value;
    }
}

/// <summary>Simple JSON preferences under the user app-data directory.</summary>
public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Defensive: never crash the UI process if a non-finite slips through.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions)
                ?? new UserPreferences();
            preferences.SanitizeGeometry();
            return preferences;
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public string FilePath => _path;

    public void Save(UserPreferences preferences)
    {
        try
        {
            preferences.SanitizeGeometry();

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Preferences are non-critical; never take down the desktop shell.
        }
    }
}
