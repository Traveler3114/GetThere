namespace GetThere.Components;

public partial class ModernLoader : ContentView
{
    private bool _isAnimating;

    public static readonly BindableProperty IsRunningProperty =
        BindableProperty.Create(nameof(IsRunning), typeof(bool), typeof(ModernLoader), false, propertyChanged: OnIsRunningChanged);

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public ModernLoader()
    {
        InitializeComponent();
        IsVisible = false;
    }

    private static void OnIsRunningChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var loader = (ModernLoader)bindable;
        bool isRunning = (bool)newValue;
        loader.IsVisible = isRunning;
        if (isRunning)
            loader.StartAnimation();
        else
            loader.StopAnimation();
    }

    private async void StartAnimation()
    {
        if (_isAnimating) return;
        _isAnimating = true;

        // Beautiful XAML animation
        while (_isAnimating)
        {
            var rotate = OuterRing.RelRotateToAsync(360, 1000, Easing.Linear);
            var pulseScale = InnerPulse.ScaleToAsync(2.0, 500, Easing.CubicOut);
            var pulseFade = InnerPulse.FadeToAsync(0, 500, Easing.CubicOut);

            await Task.WhenAll(rotate, pulseScale, pulseFade);
            
            if (!_isAnimating) break;
            
            InnerPulse.Scale = 1.0;
            InnerPulse.Opacity = 0.3;
        }
    }

    private void StopAnimation()
    {
        _isAnimating = false;
        OuterRing.CancelAnimations();
        InnerPulse.CancelAnimations();
    }
}
