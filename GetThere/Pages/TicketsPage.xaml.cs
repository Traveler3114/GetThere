using GetThereShared.Dtos;
using GetThereShared.Enums;
using GetThere.Services;
using GetThere.Helpers;
using GetThere.State;
using Microsoft.Maui.Controls;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly TicketService _ticketService;
    private readonly MockTicketStore _mockStore;
    private List<TicketDto> _allTickets = [];
    private TicketStatus _activeFilter = TicketStatus.Active;

    public TicketsPage(TicketService ticketService, MockTicketStore mockStore)
    {
        InitializeComponent();
        _ticketService = ticketService;
        _mockStore = mockStore;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ShowMockTickets();
        await LoadTicketsAsync();
    }

    private void ShowMockTickets()
    {
        var tickets = _mockStore.Tickets;
        MockTicketsSection.IsVisible = tickets.Any();
        MockTicketsRows.Children.Clear();

        foreach (var t in tickets)
        {
            var validFrom = ParseAndFormatLocal(t.ValidFrom);
            var validUntil = ParseAndFormatLocal(t.ValidUntil);

            var row = new Grid
            {
                Padding = new Thickness(0, 10),
                ColumnDefinitions =
                [
                    new ColumnDefinition(48),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                ],
                ColumnSpacing = 12,
            };

            var icon = new Border
            {
                WidthRequest = 48,
                HeightRequest = 48,
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#FFF3CD"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(24) },
                Content = new Label
                {
                    Text = "🎫",
                    FontSize = 22,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            };
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var info = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = $"{t.OperatorName} — {t.TicketName}",
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = AppInfo.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black,
                        LineBreakMode = LineBreakMode.TailTruncation,
                    },
                    new Label
                    {
                        Text = $"{validFrom} → {validUntil}",
                        FontSize = 11,
                        TextColor = Colors.Gray,
                    },
                },
            };
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            var badge = new Border
            {
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(8, 4),
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#FFC107"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
                Content = new Label
                {
                    Text = "MOCK",
                    TextColor = Colors.Black,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                },
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);

            MockTicketsRows.Children.Add(row);
        }
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

    /// <summary>Parses an ISO 8601 datetime string and formats it as local time.</summary>
    private static string ParseAndFormatLocal(string? iso)
        => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("dd MMM HH:mm")
            : iso ?? string.Empty;
}
