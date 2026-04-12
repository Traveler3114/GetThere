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

    private bool? _lastIsMobile;

    public TicketsPage(TicketService ticketService, MockTicketStore mockStore)
    {
        InitializeComponent();
        _ticketService = ticketService;
        _mockStore = mockStore;
        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _lastIsMobile = null; // force update
        UpdateResponsiveLayout();
        ShowMockTickets();
        await LoadTicketsAsync();
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
        
        // Scale the main label and the filter badge
        HeaderRow.Scale = 1.0 - (factor * 0.15); // Shrink slightly
        HeaderRow.Opacity = 1.0 - (factor * 0.5); // Fade slightly
        HeaderRow.TranslationY = -(factor * 10);
        
        // --- 2. Premium Manual Scrollbar Logic ---
        double contentHeight = scrollView.ContentSize.Height;
        double viewHeight = scrollView.Height;
        
        if (contentHeight > viewHeight)
        {
            CustomScrollThumb.IsVisible = true;
            // Calculate thumb range and position
            double usableHeight = viewHeight - CustomScrollThumb.HeightRequest - 80; // Margin adjustment
            double scrollPercent = Math.Clamp(scrollY / (contentHeight - viewHeight), 0, 1);
            CustomScrollThumb.TranslationY = (scrollPercent * usableHeight) + 40; // Offset for cards
            
            // Subtle fade for the thumb
            CustomScrollThumb.Opacity = 0.5 + (scrollPercent * 0.5);
        }
        else
        {
            CustomScrollThumb.IsVisible = false;
        }
    }

    private void UpdateResponsiveLayout()
    {
        var isMobile = Width < 700;
        TicketsContent.Padding = isMobile ? new Thickness(20, 10, 20, 80) : new Thickness(24, 20, 24, 80);
        HeaderRow.Margin = isMobile ? new Thickness(20, 35, 20, 5) : new Thickness(24, 40, 24, 10);
        TopHandle.IsVisible = true;
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
                BackgroundColor = Color.FromArgb("#FFFBEB"),
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

    private bool _isLoading;
    private CancellationTokenSource? _loadingCts;

    private async Task LoadTicketsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        
        StartLoadingAnimations();
        
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
            StopLoadingAnimations();
            _isLoading = false;
        }
    }

    private async void StartLoadingAnimations()
    {
        PremiumLoadingState.IsVisible = true;
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
        PremiumLoadingState.IsVisible = false;
    }

    private async void OnFilterOptionClicked(object? sender, EventArgs e)
    {
        try
        {
            string? chosen = null;
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

        BindableLayout.SetItemsSource(TicketsRows, filtered);
        
        TicketsRows.IsVisible = filtered.Count > 0;
        TicketsEmptyState.IsVisible = filtered.Count == 0;
        TicketsEmptyLabel.Text = $"No {_activeFilter.ToString().ToLower()} tickets";
        CurrentFilterSectionLabel.Text = $"{_activeFilter} Tickets";
        FilterBtnLabel.Text = $"{_activeFilter} ▾";

        // Update Header Badge Color
        var (bg, fg) = _activeFilter switch
        {
            TicketStatus.Active => (Color.FromArgb("#D1FAE5"), Color.FromArgb("#065F46")),
            TicketStatus.Expired => (Color.FromArgb("#FEE2E2"), Color.FromArgb("#991B1B")),
            TicketStatus.Used => (Color.FromArgb("#FEF3C7"), Color.FromArgb("#92400E")),
            _ => (Color.FromArgb("#E5E7EB"), Color.FromArgb("#374151"))
        };
        FilterBadge.BackgroundColor = bg;
        FilterBtnLabel.TextColor = fg;

        UpdatePopupUI();
    }

    private void UpdatePopupUI()
    {
        // Reset all
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

        // Highlight based on status
        switch (_activeFilter)
        {
            case TicketStatus.Active:
                ActiveOptionRow.BackgroundColor = Color.FromArgb("#E6FFFA");
                ActiveText.TextColor = Color.FromArgb("#059669");
                ActiveText.FontAttributes = FontAttributes.Bold;
                ActiveCheckmark.IsVisible = true;
                ActiveCheckmark.TextColor = Color.FromArgb("#059669");
                break;
            case TicketStatus.Expired:
                ExpiredOptionRow.BackgroundColor = Color.FromArgb("#FEF2F2");
                ExpiredText.TextColor = Color.FromArgb("#DC2626");
                ExpiredText.FontAttributes = FontAttributes.Bold;
                ExpiredCheckmark.IsVisible = true;
                ExpiredCheckmark.TextColor = Color.FromArgb("#DC2626");
                break;
            case TicketStatus.Used:
                UsedOptionRow.BackgroundColor = Color.FromArgb("#FFFBEB");
                UsedText.TextColor = Color.FromArgb("#D97706");
                UsedText.FontAttributes = FontAttributes.Bold;
                UsedCheckmark.IsVisible = true;
                UsedCheckmark.TextColor = Color.FromArgb("#D97706");
                break;
        }
    }

    private static string ParseAndFormatLocal(string? iso)
        => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("dd MMM HH:mm")
            : iso ?? string.Empty;
}
