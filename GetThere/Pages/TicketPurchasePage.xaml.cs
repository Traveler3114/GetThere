#nullable enable
using GetThere.Helpers;
using GetThere.Services;
using GetThere.State;
using GetThereShared.Dtos;

namespace GetThere.Pages;

/// <summary>
/// Displays available ticket options for a specific operator and handles purchase.
/// Navigated to from <see cref="ShopPage"/> when the user taps an operator card.
/// </summary>
public partial class TicketPurchasePage : ContentPage
{
    private readonly ShopService _shopService;
    private readonly MockTicketStore _store;
    private TicketableOperatorDto? _operator;

    public TicketPurchasePage(ShopService shopService, MockTicketStore store)
    {
        InitializeComponent();
        _shopService = shopService;
        _store = store;
        SizeChanged += OnPageSizeChanged;
    }

    /// <summary>Sets the operator context before the page appears.</summary>
    public TicketPurchasePage Prepare(TicketableOperatorDto op)
    {
        _operator = op;
        PageTitleLabel.Text = $"{op.Name} Shop";
        PageSubtitleLabel.Text = op.Name switch
        {
            "ZET" => "Buy tickets for Zagreb transit",
            "HZPP" => "Buy tickets for Croatian Railways",
            "Bajs" => "Buy tickets for city bikes",
            _ => op.Description,
        };
        Title = op.Name;
        return this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        if (_operator is not null)
            await LoadOptionsAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, PurchaseSurface);
    }

    private async Task LoadOptionsAsync()
    {
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        OptionsContainer.Children.Clear();
        EmptyState.IsVisible = false;

        try
        {
            var options = await _shopService.GetTicketOptionsAsync(_operator!.Id) ?? [];

            if (!options.Any())
            {
                EmptyState.IsVisible = true;
                return;
            }

            foreach (var option in options)
                OptionsContainer.Children.Add(BuildOptionCard(option));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not load options: " + ex.Message, "OK");
        }
        finally
        {
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
        }
    }

    private View BuildOptionCard(MockTicketOptionDto option)
    {
        var accentColor = Color.FromArgb("#009688");
        var detailColor = _operator?.Name switch
        {
            "ZET" => Color.FromArgb("#13A89E"),
            "HZPP" => Color.FromArgb("#0F9ED5"),
            "Bajs" => Color.FromArgb("#17B26A"),
            _ => accentColor,
        };

        // Quantity stepper
        var qtyLabel = new Label
        {
            Text = "1",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.Black,
            MinimumWidthRequest = 22,
        };

        var minusBtn = new Button
        {
            Text = "−",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 34,
            HeightRequest = 34,
            Padding = Thickness.Zero,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#A0A0A0"),
            BorderColor = Color.FromArgb("#EBEBEC"),
            BorderWidth = 1,
            CornerRadius = 17,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.08f, Radius = 4, Offset = new Point(1, 1) },
        };
        var plusBtn = new Button
        {
            Text = "+",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 34,
            HeightRequest = 34,
            Padding = Thickness.Zero,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#A0A0A0"),
            BorderColor = Color.FromArgb("#EBEBEC"),
            BorderWidth = 1,
            CornerRadius = 17,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.08f, Radius = 4, Offset = new Point(1, 1) },
        };

        int quantity = 1;
        minusBtn.Clicked += (_, _) => { quantity = Math.Max(1, quantity - 1); qtyLabel.Text = quantity.ToString(); };
        plusBtn.Clicked += (_, _) => { quantity = Math.Min(10, quantity + 1); qtyLabel.Text = quantity.ToString(); };

        var buyBtn = new Button
        {
            Text = $"Buy  •  €{option.Price:F2}",
            BackgroundColor = accentColor,
            TextColor = Colors.White,
            CornerRadius = 22,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HeightRequest = 44,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.14f, Radius = 6, Offset = new Point(2, 2) },
        };
        buyBtn.Clicked += async (_, _) => await OnBuyClicked(option, quantity, buyBtn);

        var card = new Border
        {
            Padding = new Thickness(16),
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb("#EBEBEC")),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.16f, Radius = 8, Offset = new Point(4, 4) },
        };

        card.Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = option.Name,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Black,
                },
                new Label
                {
                    Text = option.Description,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#A0A0A0"),
                },
                new HorizontalStackLayout
                {
                    Spacing = 12, // Gap between Validity and Price
                    Children =
                    {
                                new Grid
                                {
                                    ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
                                    ColumnSpacing = 4,
                                    VerticalOptions = LayoutOptions.Center,
                                    Children =
                                    {
                                        new Image
                                        {
                                            Source = ImageSource.FromFile("stopwatch.png"),
                                            WidthRequest = 20,
                                            HeightRequest = 20,
                                            Aspect = Aspect.AspectFit,
                                            BackgroundColor = Colors.Yellow, // TEMPORARY DEBUG COLOR
                                            ZIndex = 10,
                                            VerticalOptions = LayoutOptions.Center
                                        }.WithColumn(0),
                                        new Label
                                        {
                                            Text = option.Validity,
                                            FontSize = 12,
                                            FontAttributes = FontAttributes.Bold,
                                            TextColor = detailColor,
                                            VerticalOptions = LayoutOptions.Center
                                        }.WithColumn(1)
                                    }
                                },
                        new HorizontalStackLayout
                        {
                            Spacing = 4,
                            VerticalOptions = LayoutOptions.Center,
                            Children =
                            {
                                new Label
                                {
                                    Text = $"€{option.Price:F2}",
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold,
                                    TextColor = Colors.Black,
                                    VerticalOptions = LayoutOptions.Center
                                }
                            }
                        }
                    }
                },
                // Quantity row
                new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                    Children =
                    {
                        buyBtn.WithColumn(0),
                        new HorizontalStackLayout
                        {
                            Spacing = 10,
                            VerticalOptions = LayoutOptions.Center,
                            Margin = new Thickness(12, 0, 0, 0), // Push away from Buy button
                            Children = { minusBtn, qtyLabel, plusBtn },
                        }.WithColumn(1),
                    },
                },
            },
        };

        return card;
    }

    private async Task OnBuyClicked(MockTicketOptionDto option, int quantity, Button buyBtn)
    {
        buyBtn.IsEnabled = false;
        try
        {
            var result = await _shopService.PurchaseTicketAsync(_operator!.Id, option.OptionId, quantity);
            if (result?.Success == true && result.Data is not null)
            {
                _store.Add(result.Data);
                var confirmPage = new MockTicketConfirmationPage(result.Data);
                await Navigation.PushAsync(confirmPage);
            }
            else
            {
                await DisplayAlertAsync("Error", result?.Message ?? "Purchase failed.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            buyBtn.IsEnabled = true;
        }
    }

    private void OnBackClicked(object? sender, EventArgs e)
        => Navigation.PopAsync();
}

/// <summary>Extension helpers for setting Grid attached properties inline.</summary>
internal static class GridExtensions
{
    public static T WithColumn<T>(this T view, int col) where T : View
    {
        Grid.SetColumn(view, col);
        return view;
    }
}
