using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.Views;

public partial class MainWindow : Window
{
    private bool _applyingGeometry;
    private bool _modeTransitionArmed;
    private PixelPoint? _normalPosition;

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
            shell.ThumbnailModeChanged -= OnThumbnailModeChanged;
            shell.ThumbnailModeChanged += OnThumbnailModeChanged;
            if (shell.Preferences.StartInThumbnail)
            {
                // Defer so the first layout pass completes before resizing.
                Dispatcher.UIThread.Post(() => shell.EnterThumbnailCommand.Execute(null));
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
        _modeTransitionArmed = true;
        try
        {
            if (shell.IsThumbnailMode)
            {
                _normalPosition = Position;
                shell.Preferences.NormalWidth = FiniteOr(Width, 1440, 960, 3840);
                shell.Preferences.NormalHeight = FiniteOr(Height, 900, 640, 2160);

                Topmost = true;
                CanResize = true;
                Width = FiniteOr(shell.Preferences.ThumbnailWidth, 360, 320, 520);
                Height = FiniteOr(shell.Preferences.ThumbnailHeight, 220, 180, 360);
                if (shell.Preferences.ThumbnailX is { } x && shell.Preferences.ThumbnailY is { } y)
                {
                    Position = new PixelPoint((int)x, (int)y);
                }

                // Ensure metrics bind against the latest projection after chrome swap.
                shell.ThumbnailPage.RefreshFromCurrentProjection();
            }
            else
            {
                Topmost = false;
                CanResize = true;
                Width = FiniteOr(shell.Preferences.NormalWidth, 1440, 960, 3840);
                Height = FiniteOr(shell.Preferences.NormalHeight, 900, 640, 2160);
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
            // Ignore geometry events that fire as a side-effect of the transition for a short window.
            Dispatcher.UIThread.Post(
                () =>
                {
                    _modeTransitionArmed = false;
                },
                DispatcherPriority.Background
            );
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_applyingGeometry || _modeTransitionArmed)
        {
            return;
        }

        PersistGeometry();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WidthProperty && e.Property != HeightProperty)
        {
            return;
        }

        if (_applyingGeometry || _modeTransitionArmed)
        {
            return;
        }

        PersistGeometry();
    }

    private void PersistGeometry()
    {
        if (DataContext is not AppShellViewModel shell)
        {
            return;
        }

        if (shell.IsThumbnailMode)
        {
            shell.Preferences.ThumbnailX = Position.X;
            shell.Preferences.ThumbnailY = Position.Y;
            shell.Preferences.ThumbnailWidth = FiniteOr(Width, 360, 320, 520);
            shell.Preferences.ThumbnailHeight = FiniteOr(Height, 220, 180, 360);
        }
        else
        {
            shell.Preferences.NormalWidth = FiniteOr(Width, 1440, 960, 3840);
            shell.Preferences.NormalHeight = FiniteOr(Height, 900, 640, 2160);
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

    private static double FiniteOr(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            return fallback;
        }

        return value;
    }
}
