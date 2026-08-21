using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class JourneyTicketPickerPage : ContentPage
{
    public JourneyTicketPickerPage(JourneyTicketPickerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
