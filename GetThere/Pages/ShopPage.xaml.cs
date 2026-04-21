using System;
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

    private bool? _lastIsMobile;

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
        _lastIsMobile = null; // force update
        UpdateResponsiveLayout();
        UpdateCountryBadge();
        await LoadOperatorsAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        var isMobile = Width < 700;
        if (_lastIsMobile != isMobile)
        {
            _lastIsMobile = isMobile;
            UpdateResponsiveLayout();
        }
    }

    private void OnMainScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        var scrollView = sender as ScrollView;
        if (scrollView == null) return;

        double scrollY = e.ScrollY;
        const double maxScroll = 120.0;
        
        // --- 1. Header Parallax & Scale ---
        double factor = Math.Clamp(scrollY / maxScroll, 0, 1);
        PageHeaderGrid.Scale = 1.0 - (factor * 0.15);
        PageHeaderGrid.Opacity = 1.0 - (factor * 0.5);
        PageHeaderGrid.TranslationY = -(factor * 10);
        
        // Parallax the badge too
        CountryBadge.Scale = 1.0 - (factor * 0.1);
        CountryBadge.TranslationY = -(factor * 5);
        
        // --- 2. Custom scrollbar logic ---
        double contentHeight = scrollView.ContentSize.Height;
        double viewHeight = scrollView.Height;
        
        if (contentHeight > viewHeight)
        {
            CustomScrollThumb.IsVisible = true;
            double usableHeight = viewHeight - CustomScrollThumb.HeightRequest - 60;
            double scrollPercent = Math.Clamp(scrollY / (contentHeight - viewHeight), 0, 1);
            CustomScrollThumb.TranslationY = (scrollPercent * usableHeight) + 30;
            CustomScrollThumb.Opacity = 0.5 + (scrollPercent * 0.5);
        }
        else
        {
            CustomScrollThumb.IsVisible = false;
        }
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

    private bool _isLoading;
    private CancellationTokenSource? _loadingCts;

    private async Task LoadOperatorsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        
        StartLoadingAnimations();
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
            StopLoadingAnimations();
            _isLoading = false;
        }
    }

    private async void StartLoadingAnimations()
    {
        LoadingState.IsVisible = true;
        _loadingCts = new CancellationTokenSource();
        var token = _loadingCts.Token;

        // 1. Shimmer sweep loop
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() => ShimmerBox.TranslationX = -500);
                await ShimmerBox.TranslateTo(1000, 0, 1500, Easing.CubicInOut);
                await Task.Delay(300, token);
            }
        }, token);
    }



    private void StopLoadingAnimations()
    {
        _loadingCts?.Cancel();
        LoadingState.IsVisible = false;
    }

    private Border BuildOperatorCard(TicketableOperatorDto op)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var nameInitial = op.Name.Length > 0 ? op.Name[0].ToString() : "?";

        // Brand colour badge
        var badge = new Border
        {
            BackgroundColor = Color.FromArgb(op.Color),
            StrokeThickness = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
        };
        badge.Content = new Label
        {
            Text = nameInitial,
            FontSize = 18,
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
            ColumnDefinitions = [new ColumnDefinition(44), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
            ColumnSpacing = 12,
        };

        Grid.SetColumn(badge, 0);
        Grid.SetRowSpan(badge, 2);
        grid.Children.Add(badge);

        var nameLabel = new Label
        {
            Text = op.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = isDark ? Colors.White : Colors.Black,
        };
        Grid.SetColumn(nameLabel, 1);
        Grid.SetRow(nameLabel, 0);
        grid.Children.Add(nameLabel);

        var descLabel = new Label
        {
            Text = op.Description,
            FontSize = 12,
            TextColor = isDark ? Color.FromArgb("#9CA3AF") : Colors.Gray,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        Grid.SetColumn(descLabel, 1);
        Grid.SetRow(descLabel, 1);
        grid.Children.Add(descLabel);

        Grid.SetColumn(typeBadge, 2);
        Grid.SetRowSpan(typeBadge, 2);
        grid.Children.Add(typeBadge);

        var card = new Border
        {
            Margin = new Thickness(0, 4),
            Padding = new Thickness(12, 10),
            BackgroundColor = isDark ? Color.FromArgb("#111827") : Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.1f, Radius = 6, Offset = new Point(0, 3) },
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
        var page = new TicketPurchasePage(_shopService, _mockStore, _prefs);
        page.Prepare(op);
        await Navigation.PushAsync(page);
    }
}

