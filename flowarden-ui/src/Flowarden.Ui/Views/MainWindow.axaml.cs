using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.Views;

public partial class MainWindow : Window
{
    private bool _applyingGeometry;
    private PixelPoint? _normalPosition;
    private Size? _normalSize;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PositionChanged += OnPositionChanged;
        PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AppShellViewModel shell)
        {
            shell.ThumbnailModeChanged += OnThumbnailModeChanged;
            if (shell.Preferences.StartInThumbnail)
            {
                shell.EnterThumbnailCommand.Execute(null);
            }
        }
    }

    private void OnThumbnailModeChanged()
    {
        if (DataContext is not AppShellViewModel shell || _applyingGeometry)
        {
            return;
        }

        _applyingGeometry = true;
        try
        {
            if (shell.IsThumbnailMode)
            {
                _normalPosition = Position;
                _normalSize = new Size(Width, Height);
                shell.Preferences.NormalWidth = Width;
                shell.Preferences.NormalHeight = Height;

                Topmost = true;
                CanResize = true;
                var width = Sanitize(shell.Preferences.ThumbnailWidth, 360, 320, 520);
                var height = Sanitize(shell.Preferences.ThumbnailHeight, 220, 180, 360);
                Width = width;
                Height = height;
                if (
                    !double.IsNaN(shell.Preferences.ThumbnailX)
                    && !double.IsNaN(shell.Preferences.ThumbnailY)
                )
                {
                    Position = new PixelPoint(
                        (int)shell.Preferences.ThumbnailX,
                        (int)shell.Preferences.ThumbnailY
                    );
                }
            }
            else
            {
                Topmost = false;
                CanResize = true;
                Width = Sanitize(shell.Preferences.NormalWidth, 1440, 960, 3840);
                Height = Sanitize(shell.Preferences.NormalHeight, 900, 640, 2160);
                if (_normalPosition is { } pos)
                {
                    Position = pos;
                }
            }

            shell.PreferencesStore.Save(shell.Preferences);
        }
        finally
        {
            _applyingGeometry = false;
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        PersistGeometry();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty || e.Property == HeightProperty)
        {
            PersistGeometry();
        }
    }

    private void PersistGeometry()
    {
        if (_applyingGeometry || DataContext is not AppShellViewModel shell)
        {
            return;
        }

        if (shell.IsThumbnailMode)
        {
            shell.Preferences.ThumbnailX = Position.X;
            shell.Preferences.ThumbnailY = Position.Y;
            shell.Preferences.ThumbnailWidth = Width;
            shell.Preferences.ThumbnailHeight = Height;
        }
        else
        {
            shell.Preferences.NormalWidth = Width;
            shell.Preferences.NormalHeight = Height;
        }

        shell.PreferencesStore.Save(shell.Preferences);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AppShellViewModel shell)
        {
            return;
        }

        var toggle =
            e.Key == Key.T
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (OperatingSystem.IsMacOS())
        {
            toggle =
                e.Key == Key.T
                && e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        }

        if (toggle)
        {
            shell.ToggleThumbnailCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static double Sanitize(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            return fallback;
        }

        return value;
    }
}
