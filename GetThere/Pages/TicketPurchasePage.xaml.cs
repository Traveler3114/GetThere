using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class TicketPurchasePage : ContentPage
{
    public TicketPurchasePage(TicketPurchaseViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
