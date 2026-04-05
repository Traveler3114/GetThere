#nullable enable
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
    }

    /// <summary>Sets the operator context before the page appears.</summary>
    public TicketPurchasePage Prepare(TicketableOperatorDto op)
    {
        _operator = op;
        PageTitleLabel.Text = op.Name;
        Title = op.Name;
        return this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_operator is not null)
            await LoadOptionsAsync();
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
        var accentColor = Color.FromArgb(_operator?.Color ?? "#1264AB");

        // Quantity stepper
        var qtyLabel = new Label
        {
            Text = "1",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = AppInfo.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black,
            MinimumWidthRequest = 32,
        };

        var minusBtn = new Button
        {
            Text = "−",
            FontSize = 20,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = Thickness.Zero,
            BackgroundColor = Color.FromArgb("#E0E0E0"),
            TextColor = Colors.Black,
            CornerRadius = 18,
        };
        var plusBtn = new Button
        {
            Text = "+",
            FontSize = 20,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = Thickness.Zero,
            BackgroundColor = accentColor,
            TextColor = Colors.White,
            CornerRadius = 18,
        };

        int quantity = 1;
        minusBtn.Clicked += (_, _) => { quantity = Math.Max(1, quantity - 1); qtyLabel.Text = quantity.ToString(); };
        plusBtn.Clicked += (_, _) => { quantity = Math.Min(10, quantity + 1); qtyLabel.Text = quantity.ToString(); };

        var buyBtn = new Button
        {
            Text = $"Buy  •  €{option.Price:F2}",
            BackgroundColor = accentColor,
            TextColor = Colors.White,
            CornerRadius = 12,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HeightRequest = 48,
        };
        buyBtn.Clicked += async (_, _) => await OnBuyClicked(option, quantity, buyBtn);

        var card = new Border
        {
            Padding = new Thickness(16),
            BackgroundColor = AppInfo.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1C1C1E")
                : Colors.White,
            StrokeThickness = 2,
            Stroke = new SolidColorBrush(Color.FromArgb((_operator?.Color ?? "#1264AB") + "33")),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.05f, Radius = 8, Offset = new Point(0, 2) },
        };

        card.Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = option.Name,
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = AppInfo.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black,
                },
                new Label
                {
                    Text = option.Description,
                    FontSize = 13,
                    TextColor = Colors.Gray,
                },
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Border
                        {
                            Padding = new Thickness(8, 4),
                            BackgroundColor = Color.FromArgb((_operator?.Color ?? "#1264AB") + "22"),
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                            Content = new Label
                            {
                                Text = $"⏱ {option.Validity}",
                                FontSize = 12,
                                TextColor = accentColor,
                            },
                        },
                        new Border
                        {
                            Padding = new Thickness(8, 4),
                            BackgroundColor = Color.FromArgb((_operator?.Color ?? "#1264AB") + "22"),
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                            Content = new Label
                            {
                                Text = $"€{option.Price:F2}",
                                FontSize = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = accentColor,
                            },
                        },
                    },
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
                            Spacing = 8,
                            VerticalOptions = LayoutOptions.Center,
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
