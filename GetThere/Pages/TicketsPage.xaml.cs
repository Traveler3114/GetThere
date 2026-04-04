using GetThereShared.Dtos;
using GetThereShared.Enums;
using GetThere.Services;
using GetThere.Helpers;
using Microsoft.Maui.Controls;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly TicketService _ticketService;
    private List<TicketDto> _allTickets = [];
    private TicketStatus _activeFilter = TicketStatus.Active;

    public TicketsPage(TicketService ticketService)
    {
        InitializeComponent();
        _ticketService = ticketService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadTicketsAsync();
    }

    private async Task LoadTicketsAsync()
    {
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        try
        {
            var result = await _ticketService.GetTicketsAsync();
            if (result.Success && result.Data != null)
            {
                _allTickets = result.Data.ToList();
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allTickets
            .Where(t => t.Status == _activeFilter)
            .OrderByDescending(t => t.ValidUntil)
            .ToList();

        BindableLayout.SetItemsSource(TicketsRows, filtered);
        BindableLayout.SetItemTemplate(TicketsRows, (DataTemplate)Resources["TicketRowTemplate"]);
        TicketsRows.IsVisible = filtered.Any();
        TicketsEmptyState.IsVisible = !filtered.Any();
        TicketsEmptyLabel.Text = $"No {_activeFilter.ToString().ToLower()} tickets";
        FilterBtn.Text = $"{_activeFilter} ▾";
    }

    private async void OnShowFilterOptions(object? sender, EventArgs e)
    {
        FilterBottomSheet.IsVisible = true;
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(1, 200),
            FilterContent.TranslateToAsync(0, 0, 300, Easing.CubicOut)
        );
    }

    private async void OnHideFilterBottomSheet(object? sender, EventArgs e)
    {
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(0, 200),
            FilterContent.TranslateToAsync(0, 600, 300, Easing.CubicIn)
        );
        FilterBottomSheet.IsVisible = false;
    }

    private async void OnFilterOptionClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string chosen)
        {
            if (Enum.TryParse<TicketStatus>(chosen, out var status))
            {
                _activeFilter = status;
                ApplyFilter();
            }
        }
        OnHideFilterBottomSheet(sender, e);
    }
}
