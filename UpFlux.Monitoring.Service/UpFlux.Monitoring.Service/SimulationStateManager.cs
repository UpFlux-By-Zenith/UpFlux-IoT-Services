using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Manages whether the device is in a Busy or Idle state, 
    /// automatically cycling at random intervals so each device 
    /// has a unique pattern.
    /// </summary>
    public class SimulationStateManager
    {
        private SimulationState _currentState;
        private DateTime _stateEndTime;
        private readonly Random _random;

        // These fields define the range for how long Busy or Idle will last (in seconds).
        private readonly int _minBusySeconds;
        private readonly int _maxBusySeconds;
        private readonly int _minIdleSeconds;
        private readonly int _maxIdleSeconds;

        /// <summary>
        /// Initializes the state manager by automatically picking 
        /// random Busy/Idle durations. Each device will have different intervals.
        /// </summary>
        public SimulationStateManager()
        {
            _random = new Random();

            _minBusySeconds = _random.Next(120, 181);   // 2-3 minutes
            _maxBusySeconds = _minBusySeconds + _random.Next(30, 61); // add 30-60s to min

            _minIdleSeconds = _random.Next(60, 121);    // 1-2 minutes
            _maxIdleSeconds = _minIdleSeconds + _random.Next(30, 61); // add 30-60s to min

            // Start in Busy state
            _currentState = SimulationState.Busy;

            // Decide how many seconds this first Busy period will last
            int initialBusyDuration = _random.Next(_minBusySeconds, _maxBusySeconds + 1);
            _stateEndTime = DateTime.UtcNow.AddSeconds(initialBusyDuration);
        }

        /// <summary>
        /// Returns the current simulation state (Busy or Idle). 
        /// If the current state's duration has expired, it transitions 
        /// to the next state automatically.
        /// </summary>
        public SimulationState GetCurrentState()
        {
            if (DateTime.UtcNow >= _stateEndTime)
            {
                TransitionToNextState();
            }

            return _currentState;
        }

        /// <summary>
        /// Switches from Busy→Idle or Idle→Busy 
        /// and picks a new random duration for the new state.
        /// </summary>
        private void TransitionToNextState()
        {
            if (_currentState == SimulationState.Busy)
            {
                // Switch to Idle
                _currentState = SimulationState.Idle;
                int idleDuration = _random.Next(_minIdleSeconds, _maxIdleSeconds + 1);
                _stateEndTime = DateTime.UtcNow.AddSeconds(idleDuration);
            }
            else
            {
                // Switch to Busy
                _currentState = SimulationState.Busy;
                int busyDuration = _random.Next(_minBusySeconds, _maxBusySeconds + 1);
                _stateEndTime = DateTime.UtcNow.AddSeconds(busyDuration);
            }
        }
    }
}
