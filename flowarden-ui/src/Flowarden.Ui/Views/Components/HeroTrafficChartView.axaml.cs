using Avalonia.Controls;
using Avalonia.Input;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.Views.Components;

public partial class HeroTrafficChartView : UserControl
{
    public HeroTrafficChartView()
    {
        InitializeComponent();
        ThroughputPlotHost.SizeChanged += (_, _) => LayoutForensicsMarker();
        DataContextChanged += (_, _) => LayoutForensicsMarker();
    }

    private void LayoutForensicsMarker()
    {
        if (DataContext is not OverviewPageViewModel viewModel)
        {
            return;
        }

        var width = ThroughputPlotHost.Bounds.Width;
        var height = ThroughputPlotHost.Bounds.Height;
        if (height > 0)
        {
            viewModel.SetForensicsPlotHeight(height);
        }

        if (width > 0)
        {
            viewModel.LayoutForensicsMarker(width);
        }
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not OverviewPageViewModel viewModel)
        {
            return;
        }

        var position = e.GetPosition(ThroughputPlotHost);
        viewModel.UpdateThroughputHover(
            position.X,
            position.Y,
            ThroughputPlotHost.Bounds.Width,
            ThroughputPlotHost.Bounds.Height
        );
        if (ThroughputPlotHost.Bounds.Height > 0)
        {
            viewModel.SetForensicsPlotHeight(ThroughputPlotHost.Bounds.Height);
        }
    }

    private void OnPlotPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is OverviewPageViewModel viewModel)
        {
            viewModel.ClearThroughputHover();
        }
    }
}
