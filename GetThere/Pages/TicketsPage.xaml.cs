using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly TicketsViewModel _vm;

    /// <summary>
    /// Frame 3h lays the ticket cards out two-up on the desktop window; 3e keeps them in a single
    /// column on the phone. This is the width at which the second column earns its place.
    /// </summary>
    private const double TwoColumnMinWidth = 900;

    private int _span = 1;

    public TicketsPage(TicketsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;
        SizeChanged += OnPageSizeChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadTicketsCommand.Execute(null);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0)
            return;

        var span = Width >= TwoColumnMinWidth ? 2 : 1;
        if (span == _span)
            return;

        _span = span;
        TicketList.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = span > 1 ? 12 : 0
        };
    }
}
