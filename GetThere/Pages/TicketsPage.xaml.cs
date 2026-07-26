using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly TicketsViewModel _vm;

    public TicketsPage(TicketsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadTicketsCommand.Execute(null);
    }
}
