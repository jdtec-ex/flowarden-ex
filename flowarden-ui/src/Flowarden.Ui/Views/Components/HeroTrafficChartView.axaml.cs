using Avalonia.Controls;
using Avalonia.Input;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.Views.Components;

public partial class HeroTrafficChartView : UserControl
{
    public HeroTrafficChartView()
    {
        InitializeComponent();
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
    }

    private void OnPlotPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is OverviewPageViewModel viewModel)
        {
            viewModel.ClearThroughputHover();
        }
    }
}
