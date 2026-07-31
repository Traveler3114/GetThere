using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class ImportedTicketDetailPage : ContentPage
{
    public ImportedTicketDetailPage(ImportedTicketDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..");
}
