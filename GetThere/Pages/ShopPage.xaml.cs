using Microsoft.Maui.Controls.Shapes;
using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThereShared.Contracts;

namespace GetThere.Pages;

public partial class ShopPage : ContentPage
{
    private readonly TicketService _ticketService;
    private readonly AuthService _authService;
    private bool _loaded;

    public ShopPage(TicketService ticketService, AuthService authService)
    {
        InitializeComponent();
        _ticketService = ticketService;
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
            return;

        await LoadTicketOptionsAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        await LoadTicketOptionsAsync();
    }

    private async Task LoadTicketOptionsAsync()
    {
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || AuthService.IsGuest())
        {
            ShowAccountRequired();
            return;
        }

        ShowLoading();

        var result = await _ticketService.GetTicketOptionsAsync();
        if (!result.Success)
        {
            ShowError(result.Message ?? "Could not load ticket options.");
            return;
        }

        _loaded = true;
        var options = result.Data ?? [];
        if (options.Count == 0)
        {
            ShowEmpty();
            return;
        }

        OptionsList.Clear();
        foreach (var option in options)
            OptionsList.Add(BuildOptionCard(option));

        LoadingState.IsVisible = false;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = false;
        OptionsScrollView.IsVisible = true;
    }

    private View BuildOptionCard(TicketOptionResponse option)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var cardColor = isDark ? Color.FromArgb("#1E293B") : Colors.White;
        var borderColor = isDark ? Color.FromArgb("#334155") : Color.FromArgb("#E2E8F0");
        var textColor = isDark ? Colors.White : Color.FromArgb("#0F172A");
        var mutedColor = isDark ? Color.FromArgb("#94A3B8") : Color.FromArgb("#64748B");

        var title = new Label
        {
            Text = option.Name,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = textColor,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var adapter = new Label
        {
            Text = option.AdapterName,
            FontSize = 12,
            FontAttributes = FontAttributes.Italic,
            TextColor = mutedColor
        };

        var description = new Label
        {
            Text = option.Description ?? FormatDuration(option.DurationMinutes),
            FontSize = 13,
            TextColor = mutedColor,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var price = new Label
        {
            Text = $"{option.Price:N2} {option.Currency}",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0D9488"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 16
        };

        var textStack = new VerticalStackLayout { Spacing = 2 };
        textStack.Add(title);
        textStack.Add(adapter);
        if (!string.IsNullOrWhiteSpace(description.Text))
            textStack.Add(description);

        grid.Add(textStack);
        grid.Add(price, 1);

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
            Content = grid
        };

        // Purchase tap gesture
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += async (s, e) =>
        {
            var confirm = await DisplayAlert("Confirm Purchase", $"Do you want to buy {option.Name} for {option.Price:N2} {option.Currency}?", "Yes", "No");
            if (confirm)
            {
                ShowLoading();
                var result = await _ticketService.PurchaseTicketAsync(new PurchaseTicketRequest
                {
                    AdapterId = option.TicketingAdapterId,
                    OptionId = option.Id
                });
                if (result.Success)
                {
                    await DisplayAlert("Success", "Ticket purchased successfully!", "OK");
                    // Reload ticket options
                    _loaded = false;
                    await LoadTicketOptionsAsync();
                }
                else
                {
                    ShowError(result.Message ?? "Purchase failed.");
                }
            }
        };
        border.GestureRecognizers.Add(tapGesture);

        return border;
    }

    private static string FormatDuration(int? minutes)
    {
        if (minutes is null)
            return string.Empty;

        if (minutes < 60)
            return $"{minutes} min";

        var hours = minutes.Value / 60;
        var remainingMinutes = minutes.Value % 60;
        return remainingMinutes == 0 ? $"{hours} h" : $"{hours} h {remainingMinutes} min";
    }

    private void ShowLoading()
    {
        LoadingState.IsVisible = true;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = false;
        OptionsScrollView.IsVisible = false;
    }

    private void ShowEmpty()
    {
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = false;
        EmptyState.IsVisible = true;
        OptionsScrollView.IsVisible = false;
    }

    private void ShowError(string message)
    {
        ErrorTitleLabel.Text = "Could not load shop";
        ErrorMessageLabel.Text = message;
        RetryButton.IsVisible = true;
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = true;
        EmptyState.IsVisible = false;
        OptionsScrollView.IsVisible = false;
    }

    private void ShowAccountRequired()
    {
        ErrorTitleLabel.Text = LocalizationService.Instance["Shop_AccountRequired"];
        ErrorMessageLabel.Text = LocalizationService.Instance["Shop_AccountRequiredDesc"];
        RetryButton.IsVisible = false;
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = true;
        EmptyState.IsVisible = false;
        OptionsScrollView.IsVisible = false;
    }
}
