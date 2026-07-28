using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class JourneyDetailPage : ContentPage
{
    public JourneyDetailPage(JourneyDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
