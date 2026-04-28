#nullable enable
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Diagnostics;

namespace GetThere.Components;

public partial class AnimatedBackground : ContentView
{
    public static readonly BindableProperty XOffsetProperty =
        BindableProperty.Create(nameof(XOffset), typeof(float), typeof(AnimatedBackground), 0f, propertyChanged: OnOffsetChanged);

    public static readonly BindableProperty YOffsetProperty =
        BindableProperty.Create(nameof(YOffset), typeof(float), typeof(AnimatedBackground), 0f, propertyChanged: OnOffsetChanged);

    public float XOffset
    {
        get => (float)GetValue(XOffsetProperty);
        set => SetValue(XOffsetProperty, value);
    }

    public float YOffset
    {
        get => (float)GetValue(YOffsetProperty);
        set => SetValue(YOffsetProperty, value);
    }

    private static float _blueX, _blueY;
    private static float _purpleX, _purpleY;
    private static float _blueVx, _blueVy;
    private static float _purpleVx, _purpleVy;
    
    private readonly Stopwatch _stopwatch = new();
    private bool _isAnimating = false;

    private static bool _initialized = false;

    public AnimatedBackground()
    {
        InitializeComponent();
        
        if (!_initialized)
        {
            _blueVx = 54f;
            _blueVy = 54f;
            _purpleVx = -54f; // Start moving LEFT
            _purpleVy = -54f; // Start moving UP
        }

        Loaded += (s, e) => StartAnimation();
        Unloaded += (s, e) => _isAnimating = false;
    }

    private async void StartAnimation()
    {
        if (_isAnimating) return;
        _isAnimating = true;
        _stopwatch.Restart();

        while (_isAnimating)
        {
            UpdatePositions();
            CanvasView.InvalidateSurface();
            await Task.Delay(16); // ~60 FPS
        }
    }

    private void UpdatePositions()
    {
        float dt = 0.016f; // delta time approx
        
        var width = (float)CanvasView.Width;
        var height = (float)CanvasView.Height;

        if (width <= 0 || height <= 0) return;

        // One-time initialization of positions at corners
        if (!_initialized)
        {
            _blueX = 0;
            _blueY = 0;
            _purpleX = width;
            _purpleY = height;
            _initialized = true;
        }

        // Update blue
        _blueX += _blueVx * dt;
        _blueY += _blueVy * dt;

        if (_blueX < 0 || _blueX > width) _blueVx *= -1;
        if (_blueY < 0 || _blueY > height) _blueVy *= -1;

        // Update purple
        _purpleX += _purpleVx * dt;
        _purpleY += _purpleVy * dt;

        if (_purpleX < 0 || _purpleX > width) _purpleVx *= -1;
        if (_purpleY < 0 || _purpleY > height) _purpleVy *= -1;
        
        // Keep within bounds
        _blueX = Math.Clamp(_blueX, 0, width);
        _blueY = Math.Clamp(_blueY, 0, height);
        _purpleX = Math.Clamp(_purpleX, 0, width);
        _purpleY = Math.Clamp(_purpleY, 0, height);
    }

    private static void OnOffsetChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnimatedBackground background)
        {
            background.CanvasView.InvalidateSurface();
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var surface = e.Surface;
        var canvas = surface.Canvas;
        var info = e.Info;

        canvas.Clear();

        var isDarkTheme = Application.Current?.RequestedTheme == AppTheme.Dark;

        var blueAlpha = isDarkTheme ? 0.42f : 0.45f;
        var purpleAlpha = isDarkTheme ? 0.40f : 0.45f;

        var blueRadius = isDarkTheme ? 800f : 750f;
        var purpleRadius = isDarkTheme ? 800f : 750f;

        // Combine autonomous position with external XOffset/YOffset (pan)
        // We use XOffset/10 to make the pan subtler
        float finalBlueX = _blueX + (XOffset * 0.15f);
        float finalBlueY = _blueY + (YOffset * 0.15f);
        
        float finalPurpleX = _purpleX - (XOffset * 0.15f);
        float finalPurpleY = _purpleY - (YOffset * 0.15f);

        using (var paint = new SKPaint())
        {
            // Blue circle
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(finalBlueX, finalBlueY),
                blueRadius,
                new SKColor[] 
                {
                    new SKColor(74, 132, 255, (byte)(255 * blueAlpha)),
                    isDarkTheme ? new SKColor(74, 132, 255, (byte)(255 * (blueAlpha * 0.22f))) : SKColors.Transparent,
                    SKColors.Transparent
                },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(finalBlueX, finalBlueY, blueRadius, paint);

            // Purple circle
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(finalPurpleX, finalPurpleY),
                purpleRadius,
                new SKColor[] 
                {
                    new SKColor(123, 63, 255, (byte)(255 * purpleAlpha)),
                    isDarkTheme ? new SKColor(123, 63, 255, (byte)(255 * (purpleAlpha * 0.22f))) : SKColors.Transparent,
                    SKColors.Transparent
                },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(finalPurpleX, finalPurpleY, purpleRadius, paint);
        }
    }
}
