using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class TicketDetailPage : ContentPage
{
    public TicketDetailPage(TicketDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
