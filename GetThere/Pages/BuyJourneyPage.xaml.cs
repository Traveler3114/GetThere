using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class BuyJourneyPage : ContentPage
{
    private readonly BuyJourneyViewModel _viewModel;

    public BuyJourneyPage(BuyJourneyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The itinerary arrives through the JourneyHandoff singleton before navigation, so the load
        // runs here rather than from a query parameter; the handoff is consumed on the first load.
        _viewModel.LoadCommand.Execute(null);
    }
}
