using System;

using GetThere.Helpers;
using GetThere.Localization;
using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

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
        PageUtility.ApplyTicketsStyleResponsive(Width, LoginCard);
    }
}
