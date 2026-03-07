using SkiaSharp;
using SkiaSharp.Views.Maui;

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

    public AnimatedBackground()
    {
        InitializeComponent();
    }

    private static void OnOffsetChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnimatedBackground background)
        {
            background.CanvasView.InvalidateSurface();
        }
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var surface = e.Surface;
        var canvas = surface.Canvas;
        var info = e.Info;

        canvas.Clear();

        var isDarkTheme = Application.Current.RequestedTheme == AppTheme.Dark;

        var blueAlpha = isDarkTheme ? 0.2f : 0.62f;
        var purpleAlpha = isDarkTheme ? 0.2f : 0.62f;

        var blueRadius = isDarkTheme ? 200f : 350f;
        var purpleRadius = isDarkTheme ? 180f : 300f;

        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(XOffset, YOffset),
                blueRadius,
                new SKColor[] { new SKColor(0, 0, 255, (byte)(255 * blueAlpha)), SKColors.Transparent },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(XOffset, YOffset, blueRadius, paint);

            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(info.Width - XOffset, info.Height - YOffset),
                purpleRadius,
                new SKColor[] { new SKColor(128, 0, 128, (byte)(255 * purpleAlpha)), SKColors.Transparent },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(info.Width - XOffset, info.Height - YOffset, purpleRadius, paint);
        }
    }
}
