using GetThere.Helpers;
using GetThere.ViewModels;

using Microsoft.Maui.Controls.Shapes;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    /// <summary>Below this the phone layout (3i) is used; at or above it, the split layout (3k).</summary>
    private const double SplitLayoutMinWidth = 900;

    private bool? _isSplit;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        ApplyThemeImages();
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
        SizeChanged += OnPageSizeChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
        SizeChanged -= OnPageSizeChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => ApplyThemeImages();

    private void ApplyThemeImages()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        LogoImage.Source = isDark ? "logo_white.svg" : "logo.svg";
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0)
            return;

        var split = Width >= SplitLayoutMinWidth;
        ApplyLayout(split);

        // In the split layout the card lives in a half-width column and is sized by the design
        // (3k draws a 340px form), so the proportional phone/tablet sizing must not run.
        if (!split)
            PageUtility.ApplyTicketsStyleResponsive(Width, LoginCard);
    }

    /// <summary>
    /// Moves the brand panel and the form between the stacked and side-by-side arrangements.
    /// Keeping one copy of the form means the two layouts cannot drift apart.
    /// </summary>
    private void ApplyLayout(bool split)
    {
        if (_isSplit == split)
            return;

        _isSplit = split;

        LoginRoot.RowDefinitions.Clear();
        LoginRoot.ColumnDefinitions.Clear();

        if (split)
        {
            // 3k: gradient panel and form share the window, each half its width.
            LoginRoot.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            LoginRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LoginRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            Grid.SetRow(BrandPanel, 0);
            Grid.SetColumn(BrandPanel, 0);
            BrandPanel.HeightRequest = -1;
            BrandStack.VerticalOptions = LayoutOptions.Center;
            BrandStack.Margin = new Thickness(0);
            BrandTagline.IsVisible = true;

            Grid.SetRow(FormHost, 0);
            Grid.SetColumn(FormHost, 1);
            FormHost.Margin = new Thickness(0);

            LoginCard.VerticalOptions = LayoutOptions.Center;
            LoginCard.StrokeShape = new RoundRectangle { CornerRadius = 20 };
            LoginCard.Margin = new Thickness(40);
            LoginCard.WidthRequest = 340;
            SheetHandle.IsVisible = false;
            return;
        }

        // 3i: gradient band across the top, form riding up over its lower edge.
        LoginRoot.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        LoginRoot.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        LoginRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Grid.SetRow(BrandPanel, 0);
        Grid.SetColumn(BrandPanel, 0);
        BrandPanel.HeightRequest = 320;
        BrandStack.VerticalOptions = LayoutOptions.Start;
        BrandStack.Margin = new Thickness(0, 30, 0, 0);
        BrandTagline.IsVisible = false;

        Grid.SetRow(FormHost, 1);
        Grid.SetColumn(FormHost, 0);
        FormHost.Margin = new Thickness(0, -60, 0, 0);

        LoginCard.VerticalOptions = LayoutOptions.Start;
        LoginCard.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22, 22, 0, 0) };
        LoginCard.Margin = new Thickness(1);
        SheetHandle.IsVisible = true;
    }
}
