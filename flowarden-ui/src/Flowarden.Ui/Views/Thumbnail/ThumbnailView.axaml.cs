using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.Views.Thumbnail;

public partial class ThumbnailView : UserControl
{
    public ThumbnailView()
    {
        InitializeComponent();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ThumbnailViewModel vm)
        {
            vm.ExpandCommand.Execute(null);
        }
    }
}
