using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class ImportTicketPage : ContentPage
{
    public ImportTicketPage(ImportTicketViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
