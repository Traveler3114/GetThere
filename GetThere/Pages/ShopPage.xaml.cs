using Microsoft.Maui.Controls.Shapes;

using GetThere.Services;
using GetThereShared.Contracts;

namespace GetThere.Pages;

public partial class ShopPage : ContentPage
{
    private readonly TicketService _ticketService;
    private bool _loaded;

    public ShopPage(TicketService ticketService)
    {
        InitializeComponent();
        _ticketService = ticketService;
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
        var cardColor = isDark ? Color.FromArgb("#111827") : Colors.White;
        var borderColor = isDark ? Color.FromArgb("#374151") : Color.FromArgb("#E5E7EB");
        var textColor = isDark ? Colors.White : Colors.Black;
        var mutedColor = isDark ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#6B7280");

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
            FontSize = 13,
            TextColor = mutedColor
        };

        var description = new Label
        {
            Text = option.Description ?? FormatDuration(option.DurationMinutes),
            FontSize = 13,
            TextColor = mutedColor,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var price = new Label
        {
            Text = $"{option.Price:N2} {option.Currency}",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#009688"),
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

        var textStack = new VerticalStackLayout { Spacing = 4 };
        textStack.Add(title);
        textStack.Add(adapter);
        if (!string.IsNullOrWhiteSpace(description.Text))
            textStack.Add(description);

        grid.Add(textStack);
        grid.Add(price, 1);

        return new Border
        {
            Padding = 16,
            BackgroundColor = cardColor,
            Stroke = borderColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Opacity = 0.10f,
                Radius = 8,
                Offset = new Point(0, 4)
            },
            Content = grid
        };
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
        ErrorMessageLabel.Text = message;
        LoadingState.IsVisible = false;
        ErrorState.IsVisible = true;
        EmptyState.IsVisible = false;
        OptionsScrollView.IsVisible = false;
    }
}
