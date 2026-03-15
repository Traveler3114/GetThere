#nullable enable
using Microsoft.Maui.Controls.Shapes;

namespace GetThere.Components;

public partial class BreathingBackground : ContentView
{
    private readonly List<Microsoft.Maui.Controls.Shapes.Path> _circles;
    private readonly Random _random = new();

    public BreathingBackground()
    {
        InitializeComponent();
        _circles = new List<Microsoft.Maui.Controls.Shapes.Path> { Circle1, Circle2, Circle3, Circle4, Circle5 };
        SizeChanged += OnBreathingBackgroundSizeChanged;
    }

    private void OnBreathingBackgroundSizeChanged(object? sender, EventArgs e)
    {
        UpdateCircles();
    }

    private void UpdateCircles()
    {
        var center = new Point(Width / 2, Height / 2);
        foreach (var circle in _circles)
        {
            circle.Data = new EllipseGeometry(center, 0, 0);
        }
        StartAnimation();
    }

    private async void StartAnimation()
    {
        while (true)
        {
            await AnimateCircle(_circles[_random.Next(_circles.Count)]);
            await Task.Delay(_random.Next(200, 500));
        }
    }

    private async Task AnimateCircle(Microsoft.Maui.Controls.Shapes.Path circle)
    {
        var center = new Point(Width / 2, Height / 2);
        var maxRadius = Math.Max(Width, Height) / 2;

        circle.Opacity = 0.5;

        var animation = new Animation(v =>
        {
            circle.Data = new EllipseGeometry(center, v * maxRadius, v * maxRadius);
        });

        var opacityAnimation = new Animation(v => circle.Opacity = v, 0.5, 0);

        var storyboard = new Animation();
        storyboard.Add(0, 1, animation);
        storyboard.Add(0.5, 1, opacityAnimation); // Fade out in the second half of the animation

        storyboard.Commit(this, "BreathingCircleAnimation", 16, 4000, Easing.SinInOut, (v, c) =>
        {
            circle.Data = new EllipseGeometry(center, 0, 0);
            circle.Opacity = 0;
        });
    }
}
