using Microsoft.Maui.Controls.Shapes;
using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly TicketService _ticketService;
    private readonly AuthService _authService;
    private bool _loaded;

    public TicketsPage(TicketService ticketService, AuthService authService)
    {
        InitializeComponent();
        _ticketService = ticketService;
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        // Reload always to keep wallet up to date
        await LoadTicketsAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        await LoadTicketsAsync();
    }

    private async Task LoadTicketsAsync()
    {
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || AuthService.IsGuest())
        {
            ShowAccountRequired();
            return;
        }

        ShowLoading();

        var result = await _ticketService.GetMyTicketsAsync();
        if (!result.Success)
        {
            ShowError(result.Message ?? "Could not load your tickets.");
            return;
        }

        _loaded = true;
        var tickets = result.Data ?? [];
        if (tickets.Count == 0)
        {
            ShowEmpty();
            return;
        }

        TicketsList.Clear();
        foreach (var ticket in tickets)
            TicketsList.Add(BuildTicketCard(ticket));

        LoadingState.IsVisible = false;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = false;
        TicketsScrollView.IsVisible = true;
    }

    private View BuildTicketCard(TicketResponse ticket)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var cardColor = isDark ? Color.FromArgb("#1E293B") : Colors.White;
        var borderColor = isDark ? Color.FromArgb("#334155") : Color.FromArgb("#E2E8F0");
        var textColor = isDark ? Colors.White : Color.FromArgb("#0F172A");
        var mutedColor = isDark ? Color.FromArgb("#94A3B8") : Color.FromArgb("#64748B");

        // Status pill color
        var statusColor = ticket.Status switch
        {
            TicketStatus.Active => Color.FromArgb("#10B981"), // Green
            TicketStatus.Used => Color.FromArgb("#6B7280"),   // Gray
            TicketStatus.Expired => Color.FromArgb("#EF4444"),// Red
            _ => Color.FromArgb("#F59E0B")                    // Yellow
        };

        var titleLabel = new Label
        {
            Text = ticket.OptionName,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = textColor
        };

        var adapterLabel = new Label
        {
            Text = ticket.AdapterName,
            FontSize = 12,
            FontAttributes = FontAttributes.Italic,
            TextColor = mutedColor
        };

        var statusPill = new Border
        {
            Padding = new Thickness(10, 4),
            BackgroundColor = statusColor.WithAlpha(0.15f),
            Stroke = statusColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = ticket.Status.ToString().ToUpper(),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = statusColor
            }
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ]
        };
        var textStack = new VerticalStackLayout { Spacing = 2 };
        textStack.Add(titleLabel);
        textStack.Add(adapterLabel);
        headerGrid.Add(textStack);
        headerGrid.Add(statusPill, 1);

        var divider = new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = borderColor,
            Margin = new Thickness(0, 12)
        };

        var detailsLayout = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            ],
            RowSpacing = 8
        };

        detailsLayout.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "VALID FROM", FontSize = 10, TextColor = mutedColor, FontAttributes = FontAttributes.Bold },
                new Label { Text = ticket.ValidFrom?.ToString("g") ?? "N/A", FontSize = 12, TextColor = textColor }
            }
        }, 0, 0);

        detailsLayout.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "VALID UNTIL", FontSize = 10, TextColor = mutedColor, FontAttributes = FontAttributes.Bold },
                new Label { Text = ticket.ValidUntil?.ToString("g") ?? "N/A", FontSize = 12, TextColor = textColor }
            }
        }, 1, 0);

        var actionHintLabel = new Label
        {
            Text = "➔ Tap to view QR Code Pass",
            FontSize = 12,
            TextColor = Color.FromArgb("#0D9488"),
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var mainStack = new VerticalStackLayout { Spacing = 4 };
        mainStack.Add(headerGrid);
        mainStack.Add(divider);
        mainStack.Add(detailsLayout);
        mainStack.Add(actionHintLabel);

        var border = new Border
        {
            Padding = 18,
            BackgroundColor = cardColor,
            Stroke = borderColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Opacity = 0.08f,
                Radius = 10,
                Offset = new Point(0, 4)
            },
            Content = mainStack
        };

        // Tap to display QR Code dialog
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += async (s, e) =>
        {
            await DisplayAlert("Digital Pass", $"Ticket: {ticket.OptionName}\nID: {ticket.TicketId}\n\n[QR CODE SIMULATED]\nScan at the validator.", "Close");
        };
        border.GestureRecognizers.Add(tapGesture);

        return border;
    }

    private void ShowLoading()
    {
        LoadingState.IsVisible = true;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = false;
        TicketsScrollView.IsVisible = false;
    }

    private void ShowEmpty()
    {
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = true;
        TicketsScrollView.IsVisible = false;
    }

    private void ShowError(string message)
    {
        ErrorTitleLabel.Text = "Could not load tickets";
        ErrorMessageLabel.Text = message;
        RetryButton.IsVisible = true;
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = true;
        EmptyState.IsVisible = false;
        TicketsScrollView.IsVisible = false;
    }

    private void ShowAccountRequired()
    {
        ErrorTitleLabel.Text = LocalizationService.Instance["Tickets_AccountRequired"];
        ErrorMessageLabel.Text = LocalizationService.Instance["Tickets_AccountRequiredDesc"];
        RetryButton.IsVisible = false;
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = true;
        EmptyState.IsVisible = false;
        TicketsScrollView.IsVisible = false;
    }
}
