using System;
using System.Collections.Generic;

namespace GetThere.Behaviors
{
    /// <summary>
    /// Global state to keep track of background circles across pages.
    /// </summary>
    public static class BackgroundAnimationService
    {
        private static readonly Dictionary<string, CircleState> _states = new();

        public static CircleState GetState(string id)
        {
            if (!_states.ContainsKey(id))
            {
                _states[id] = new CircleState();
            }
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
