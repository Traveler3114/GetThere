using GetThere.Helpers;
using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class RegistrationPage : ContentPage
{
    /// <summary>
    /// Below this the phone layout (3j) is used; at or above it, the split layout that matches
    /// Login's 3k. Deliberately the same number as <c>LoginPage.SplitLayoutMinWidth</c> — the two
    /// pages navigate to each other, and two different breakpoints would let a window sit at a width
    /// where crossing between them rearranged the screen.
    /// </summary>
    private const double SplitLayoutMinWidth = 900;

    /// <summary>Form width once the layout splits. Wider than Login's 340: this form has four fields.</summary>
    private const double SplitCardWidth = 400;

    private bool? _isSplit;

    public RegistrationPage(RegistrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
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
        LogoImage.Source = isDark ? "logo_white.png" : "logo.png";
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0)
            return;

        ApplyLayout(Width >= SplitLayoutMinWidth);
    }

    /// <summary>
    /// Switches between the stacked phone card and the side-by-side desktop arrangement.
    /// <para>
    /// This page had no desktop arrangement at all: it ran
    /// <see cref="PageUtility.ApplyTicketsStyleResponsive"/> at every width, setting the card's
    /// <c>WidthRequest</c> to 80% of the window while the markup capped it with
    /// <c>MaximumWidthRequest="400"</c>. The cap won, so the helper achieved nothing and a desktop
    /// window showed a phone-sized card stranded near the top of empty space.
    /// </para>
    /// </summary>
    private void ApplyLayout(bool split)
    {
        if (_isSplit == split)
            return;

        _isSplit = split;

        RegistrationRoot.RowDefinitions.Clear();
        RegistrationRoot.ColumnDefinitions.Clear();
        RegistrationRoot.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RegistrationRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        if (split)
        {
            // Gradient panel and form share the window, as on Login.
            RegistrationRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            BrandPanel.IsVisible = true;
            Grid.SetRow(BrandPanel, 0);
            Grid.SetColumn(BrandPanel, 0);

            Grid.SetRow(FormHost, 0);
            Grid.SetColumn(FormHost, 1);

            // Centred on the ScrollView rather than on the card: a ScrollView lays its content out
            // at the content's own height starting from the top, so centring the card inside it has
            // no effect. Centring the ScrollView keeps scrolling when the form is taller than the
            // window.
            FormHost.VerticalOptions = LayoutOptions.Center;

            RegistrationCard.WidthRequest = SplitCardWidth;
            RegistrationCard.HorizontalOptions = LayoutOptions.Center;
            RegistrationCard.VerticalOptions = LayoutOptions.Center;
            RegistrationCard.Margin = new Thickness(40);
            return;
        }

        // 3j: one card on the animated background, unchanged from before.
        BrandPanel.IsVisible = false;

        Grid.SetRow(FormHost, 0);
        Grid.SetColumn(FormHost, 0);
        FormHost.VerticalOptions = LayoutOptions.Fill;

        RegistrationCard.VerticalOptions = LayoutOptions.Start;
        RegistrationCard.Margin = new Thickness(24, 80, 24, 24);

        // Phones fill the width inside that margin; tablets get the same 400 the old
        // MaximumWidthRequest was effectively pinning them to, so nothing below the split
        // breakpoint changes appearance.
        if (Width < PageUtility.MobileBreakpoint)
        {
            RegistrationCard.WidthRequest = -1;
            RegistrationCard.HorizontalOptions = LayoutOptions.Fill;
        }
        else
        {
            RegistrationCard.WidthRequest = SplitCardWidth;
            RegistrationCard.HorizontalOptions = LayoutOptions.Center;
        }
    }
}
