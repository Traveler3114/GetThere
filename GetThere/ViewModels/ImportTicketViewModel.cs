using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Services;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

public partial class ImportTicketViewModel : BaseViewModel
{
    private readonly ImportedTicketService _importedService;
    private readonly IAnalyticsService _analytics;

    [ObservableProperty]
    private string _ticketName = string.Empty;

    [ObservableProperty]
    private string _routeDescription = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private DateTime _validFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime _validTo = DateTime.Today.AddDays(1);

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public ImportTicketViewModel(ImportedTicketService importedService, IAnalyticsService analytics)
    {
        _importedService = importedService;
        _analytics = analytics;
    }

    [RelayCommand]
    private async Task Save()
    {
        HasError = false;

        if (string.IsNullOrWhiteSpace(TicketName))
        {
            ErrorText = "Ticket name is required.";
            HasError = true;
            return;
        }

        if (ValidTo <= ValidFrom)
        {
            ErrorText = "Valid To must be after Valid From.";
            HasError = true;
            return;
        }

        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(PriceText))
        {
            if (!decimal.TryParse(PriceText.Replace(',', '.'), out var p) || p < 0)
            {
                ErrorText = "Invalid price.";
                HasError = true;
                return;
            }
            price = p;
        }

        IsBusy = true;

        try
        {
            var request = new CreateImportedTicketRequest
            {
                Source = ImportSource.Manual,
                TicketName = TicketName.Trim(),
                RouteDescription = string.IsNullOrWhiteSpace(RouteDescription) ? null : RouteDescription.Trim(),
                Price = price,
                Currency = price.HasValue ? "EUR" : null,
                ValidFrom = ValidFrom,
                ValidTo = ValidTo,
                OperatorNameSnapshot = string.IsNullOrWhiteSpace(OperatorName) ? null : OperatorName.Trim()
            };

            var result = await _importedService.CreateAsync(request);

            if (result.Success)
            {
                _analytics.TrackEvent("ticket_imported", new() { ["source"] = "manual" });
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                ErrorText = result.Message ?? "Could not save ticket.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
