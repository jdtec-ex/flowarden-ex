using Avalonia;
using Avalonia.Controls;

namespace Flowarden.Ui.Views.Components;

public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Title), "No data");

    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Detail), string.Empty);

    public EmptyStateView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}
