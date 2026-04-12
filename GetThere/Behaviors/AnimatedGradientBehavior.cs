#nullable enable
#pragma warning disable CA1416
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;

namespace GetThere.Behaviors
{
    public class AnimatedGradientBehavior : Behavior<VisualElement>
    {
        public string CircleId { get; set; } = "DefaultCircle";
        public double Speed { get; set; } = 2.5; 
        public double StartXPercent { get; set; } = 0.5; 
        public double StartYPercent { get; set; } = 0.5; 
        public double MinYPercent { get; set; } = 0.0; 
        public double MaxYPercent { get; set; } = 1.0; 
        public double InitialAngle { get; set; } = 0;

        private CircleState? _state;
        private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(16);
        private bool _started;

        protected override void OnAttachedTo(VisualElement bindable)
        {
            base.OnAttachedTo(bindable);
            
            _state = BackgroundAnimationService.GetState(CircleId);

            // If this is the first time the circle is used (ever), set initial velocity
            if (_state.VX == 0 && _state.VY == 0)
            {
                _state.VX = Math.Cos(InitialAngle) * Speed;
                _state.VY = Math.Sin(InitialAngle) * Speed;
            }

            if (!_started)
            {
                _started = true;
                bindable.Dispatcher.StartTimer(_interval, () => OnTimerTick(bindable));
            }
        }

        private bool OnTimerTick(VisualElement bindable)
        {
            if (bindable == null || _state == null || bindable.Parent is not VisualElement parent) return false;
            if (parent.Width <= 0 || parent.Height <= 0) return true;

            // Initialize position once across any page if not yet initialized
            if (!_state.Initialized)
            {
                _state.PosX = (StartXPercent - 0.5) * parent.Width;
                _state.PosY = (StartYPercent - 0.5) * parent.Height;
                _state.Initialized = true;
            }

            // Move
            _state.PosX += _state.VX;
            _state.PosY += _state.VY;

            double halfParentW = parent.Width / 2;

            // Horizontal Bounce
            if (_state.PosX > halfParentW) { _state.PosX = halfParentW; _state.VX *= -1; }
            else if (_state.PosX < -halfParentW) { _state.PosX = -halfParentW; _state.VX *= -1; }

            // Vertical Bounce (Restricted)
            double limitTop = (MinYPercent - 0.5) * parent.Height;
            double limitBottom = (MaxYPercent - 0.5) * parent.Height;

            if (_state.PosY > limitBottom) { _state.PosY = limitBottom; _state.VY *= -1; }
            else if (_state.PosY < limitTop) { _state.PosY = limitTop; _state.VY *= -1; }

            // Update UI
            bindable.TranslationX = _state.PosX;
            bindable.TranslationY = _state.PosY;

            return _started;
        }

        protected override void OnDetachingFrom(VisualElement bindable)
        {
            base.OnDetachingFrom(bindable);
            _started = false;
        }
    }

    public static class BackgroundAnimationService
    {
        private static readonly System.Collections.Generic.Dictionary<string, CircleState> _states = new();
        public static CircleState GetState(string id)
        {
            if (!_states.ContainsKey(id)) _states[id] = new CircleState();
            return _states[id];
        }
    }

    public class CircleState
    {
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double VX { get; set; }
        public double VY { get; set; }
        public bool Initialized { get; set; }
    }
}