using GetThereShared.Dtos;
using GetThereShared.Enums;
using GetThere.Services;
#pragma warning disable CA1416
using System.Diagnostics;
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

    private async void OnFilterOptionClicked(object? sender, EventArgs e)
    {
        try
        {
            string? chosen = null;

            // Search for CommandParameter in the sender's recognizers
            if (sender is View view && view.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is { } tap)
            {
                chosen = tap.CommandParameter as string;
            }

            if (!string.IsNullOrEmpty(chosen) && Enum.TryParse<TicketStatus>(chosen, out var status))
            {
                _activeFilter = status;
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TicketsPage] Filter error: {ex.Message}");
        }
        finally
        {
            OnHideFilterBottomSheet(sender, e);
        }
    }

    private async void OnShowFilterOptions(object? sender, EventArgs e)
    {
        FilterBottomSheet.IsVisible = true;
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(1, 150),
            FilterContent.TranslateToAsync(0, 0, 200, Easing.CubicOut)
        );
    }

    private async void OnHideFilterBottomSheet(object? sender, EventArgs e)
    {
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(0, 150),
            FilterContent.TranslateToAsync(0, -20, 200, Easing.CubicIn)
        );
        FilterBottomSheet.IsVisible = false;
    }

    private void ApplyFilter()
    {
        var filtered = _allTickets
            .Where(t => t.Status == _activeFilter)
            .OrderByDescending(t => t.ValidUntil)
            .ToList();

        // Optimized: Only set items source, template is usually already there
        BindableLayout.SetItemsSource(TicketsRows, filtered);
        
        TicketsRows.IsVisible = filtered.Count > 0;
        TicketsEmptyState.IsVisible = filtered.Count == 0;
        TicketsEmptyLabel.Text = $"No {_activeFilter.ToString().ToLower()} tickets";
        CurrentFilterSectionLabel.Text = $"{_activeFilter} Tickets";
        FilterBtnLabel.Text = $"{_activeFilter} ▾";

        // Update Popup UI
        UpdatePopupUI();
    }

    private void UpdatePopupUI()
    {
        // Reset all to default
        ActiveOptionRow.BackgroundColor = Colors.Transparent;
        ActiveText.TextColor = Color.FromArgb("#374151");
        ActiveText.FontAttributes = FontAttributes.None;
        ActiveCheckmark.IsVisible = false;

        ExpiredOptionRow.BackgroundColor = Colors.Transparent;
        ExpiredText.TextColor = Color.FromArgb("#374151");
        ExpiredText.FontAttributes = FontAttributes.None;
        ExpiredCheckmark.IsVisible = false;

        UsedOptionRow.BackgroundColor = Colors.Transparent;
        UsedText.TextColor = Color.FromArgb("#374151");
        UsedText.FontAttributes = FontAttributes.None;
        UsedCheckmark.IsVisible = false;

        // Highlight selected
        var highlightColor = Color.FromArgb("#E6FFFA");
        var activeGreen = Color.FromArgb("#059669");

        switch (_activeFilter)
        {
            case TicketStatus.Active:
                ActiveOptionRow.BackgroundColor = highlightColor;
                ActiveText.TextColor = activeGreen;
                ActiveText.FontAttributes = FontAttributes.Bold;
                ActiveCheckmark.IsVisible = true;
                break;
            case TicketStatus.Expired:
                ExpiredOptionRow.BackgroundColor = highlightColor;
                ExpiredText.TextColor = activeGreen;
                ExpiredText.FontAttributes = FontAttributes.Bold;
                ExpiredCheckmark.IsVisible = true;
                break;
            case TicketStatus.Used:
                UsedOptionRow.BackgroundColor = highlightColor;
                UsedText.TextColor = activeGreen;
                UsedText.FontAttributes = FontAttributes.Bold;
                UsedCheckmark.IsVisible = true;
                break;
        }
    }

    /// <summary>Parses an ISO 8601 datetime string and formats it as local time.</summary>
    private static string ParseAndFormatLocal(string? iso)
        => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("dd MMM HH:mm")
            : iso ?? string.Empty;
}
