#nullable enable
using GetThere.Helpers;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Dtos;

namespace GetThere.Pages;

/// <summary>
/// Shop page — displays ticketable operators filtered by the user's selected country.
/// Tapping an operator card navigates to the ticket purchase view for that operator.
/// </summary>
public partial class ShopPage : ContentPage
{
    private readonly ShopService _shopService;
    private readonly CountryPreferenceService _prefs;
    private readonly MockTicketStore _mockStore;

    public ShopPage(ShopService shopService, CountryPreferenceService prefs, MockTicketStore mockStore)
    {
        InitializeComponent();
        _shopService = shopService;
        _prefs = prefs;
        _mockStore = mockStore;
        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        UpdateCountryBadge();
        await LoadOperatorsAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, ShopContent);
    }

    private void UpdateCountryBadge()
    {
        var name = _prefs.GetSelectedCountryName();
        CountryBadgeLabel.Text = string.IsNullOrEmpty(name) ? "All countries" : $"🌍 {name}";
    }

    private async Task LoadOperatorsAsync()
    {
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        OperatorCards.Children.Clear();
        EmptyState.IsVisible = false;

        try
        {
            int? countryId = _prefs.HasSelection ? _prefs.GetSelectedCountryId() : null;
            var operators = await _shopService.GetTicketableOperatorsAsync(countryId) ?? [];

            if (!operators.Any())
            {
                EmptyState.IsVisible = true;
                return;
            }

            foreach (var op in operators)
            {
                var card = BuildOperatorCard(op);
                OperatorCards.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not load operators: " + ex.Message, "OK");
        }
        finally
        {
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
        }
    }

    private Border BuildOperatorCard(TicketableOperatorDto op)
    {
        var nameInitial = op.Name.Length > 0 ? op.Name[0].ToString() : "?";

        // Brand colour badge
        var badge = new Border
        {
            BackgroundColor = Color.FromArgb(op.Color),
            StrokeThickness = 0,
            WidthRequest = 56,
            HeightRequest = 56,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
        };
        badge.Content = new Label
        {
            Text = nameInitial,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        // Type badge
        var typeBadge = new Border
        {
            BackgroundColor = Color.FromArgb(op.Color),
            StrokeThickness = 0,
            Padding = new Thickness(10, 5),
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
        };
        typeBadge.Content = new Label
        {
            Text = op.Type,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
        };

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(56), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
            ColumnSpacing = 14,
        };

        Grid.SetColumn(badge, 0);
        Grid.SetRowSpan(badge, 2);
        grid.Children.Add(badge);

        var nameLabel = new Label
        {
            Text = op.Name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black,
        };
        Grid.SetColumn(nameLabel, 1);
        Grid.SetRow(nameLabel, 0);
        grid.Children.Add(nameLabel);

        var descLabel = new Label
        {
            Text = op.Description,
            FontSize = 13,
            TextColor = Colors.Gray,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2,
        };
        Grid.SetColumn(descLabel, 1);
        Grid.SetRow(descLabel, 1);
        grid.Children.Add(descLabel);

        Grid.SetColumn(typeBadge, 2);
        Grid.SetRowSpan(typeBadge, 2);
        grid.Children.Add(typeBadge);

        var card = new Border
        {
            Margin = new Thickness(0, 6),
            Padding = new Thickness(16),
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.22f, Radius = 12, Offset = new Point(0, 6) },
        };
        card.Content = grid;
        card.BindingContext = op;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnOperatorTapped(op);
        card.GestureRecognizers.Add(tap);

        return card;
    }

    private async void OnOperatorTapped(TicketableOperatorDto op)
    {
        var page = new TicketPurchasePage(_shopService, _mockStore);
        page.Prepare(op);
        await Navigation.PushAsync(page);
    }
}

