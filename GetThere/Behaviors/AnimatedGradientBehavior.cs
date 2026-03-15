#nullable enable
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;

namespace GetThere.Behaviors
{
    /// <summary>
    /// Behavior that applies a two‑colour moving gradient to a Button's background.
    /// The colours are taken from the application resources ("Primary" and "PrimaryDark").
    /// The gradient stops are shifted on a timer, giving the impression of motion.
    /// </summary>
    public class AnimatedGradientBehavior : Behavior<Button>
    {
        private LinearGradientBrush? _brush;
        private double _progress;
        private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(30);
        private bool _started;

        protected override void OnAttachedTo(Button bindable)
        {
            base.OnAttachedTo(bindable);

            // create brush once per button instance
            if (Application.Current?.Resources == null) return;

            var primary = (Color)Application.Current.Resources["Primary"];
            var primaryDark = (Color)Application.Current.Resources["PrimaryDark"];

            _brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = primary, Offset = 0 },
                    new GradientStop { Color = primaryDark, Offset = 1 }
                }
            };

            bindable.Background = _brush;

            // start timer once
            if (!_started)
            {
                _started = true;
                // Device.StartTimer is obsolete; use the dispatcher on a bindable object instead
                Dispatcher.StartTimer(_interval, OnTimerTick);
            }
        }

        private bool OnTimerTick()
        {
            if (_brush == null)
                return false;

            // shift offsets
            _progress += 0.01;
            if (_progress >= 1)
                _progress -= 1;

            // change positions of the gradient stops
            _brush.GradientStops[0].Offset = (float)_progress;
            _brush.GradientStops[1].Offset = (float)((_progress + 0.6) % 1); // keep them apart

            return true; // repeat
        }

        protected override void OnDetachingFrom(Button bindable)
        {
            base.OnDetachingFrom(bindable);
            // nothing to clean up, timer will stop when app suspends
        }
    }
}