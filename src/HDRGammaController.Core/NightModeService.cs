using System;
using System.Collections.Generic;
using System.Timers;

namespace HDRGammaController.Core
{
    /// <summary>
    /// Night mode settings for automatic temperature/dimming scheduling.
    /// </summary>
    public class NightModeSettings
    {
        /// <summary>
        /// Whether night mode is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;
        
        /// <summary>
        /// Use automatic sunrise/sunset calculation based on location.
        /// </summary>
        public bool UseAutoSchedule { get; set; } = false;
        
        /// <summary>
        /// Latitude for sunrise/sunset calculation (-90 to 90).
        /// </summary>
        public double? Latitude { get; set; } = null;
        
        /// <summary>
        /// Longitude for sunrise/sunset calculation (-180 to 180).
        /// </summary>
        public double? Longitude { get; set; } = null;
        
        /// <summary>
        /// Start time for night mode (e.g., "21:00"). Used when UseAutoSchedule is false.
        /// </summary>
        public TimeSpan StartTime { get; set; } = new TimeSpan(21, 0, 0);
        
        /// <summary>
        /// End time for night mode (e.g., "07:00"). Used when UseAutoSchedule is false.
        /// </summary>
        public TimeSpan EndTime { get; set; } = new TimeSpan(7, 0, 0);
        
        /// <summary>
        /// Color temperature in Kelvin during night mode (1900-6500K, lower = warmer).
        /// Default 2700K matches warm incandescent lighting.
        /// </summary>
        public int TemperatureKelvin { get; set; } = 2700;
        
        /// <summary>
        /// Algorithm to use for color temperature transformation.
        /// </summary>
        public NightModeAlgorithm Algorithm { get; set; } = NightModeAlgorithm.Standard;
        
        /// <summary>
        /// Legacy temperature as -50 to +50 scale. Converts to Kelvin internally.
        /// </summary>
        public double Temperature
        {
            get => (TemperatureKelvin - 6500) / 70.0;
            set => TemperatureKelvin = (int)(6500 + value * 70);
        }
        
        /// <summary>
        /// Fade duration in minutes (0 = instant, 60 = gradual).
        /// </summary>
        public int FadeMinutes { get; set; } = 30;
        
        public List<NightModeSchedulePoint> Schedule { get; set; } = new List<NightModeSchedulePoint>();

        public void EnsureSchedule(double? lat, double? lon)
        {
            if (Schedule != null && Schedule.Count > 0) return;
            
            // Migrate legacy settings to schedule
            Schedule = new List<NightModeSchedulePoint>();
            
            // Point 1: At StartTime (or Sunset), fade to Night Temp
            var startPoint = new NightModeSchedulePoint
            {
                TriggerType = UseAutoSchedule ? ScheduleTriggerType.Sunset : ScheduleTriggerType.FixedTime,
                Time = StartTime,
                OffsetMinutes = 0,
                TargetKelvin = TemperatureKelvin,
                FadeMinutes = FadeMinutes
            };
            
            // Point 2: At EndTime (or Sunrise), fade to Daylight (6500K)
            var endPoint = new NightModeSchedulePoint
            {
                TriggerType = UseAutoSchedule ? ScheduleTriggerType.Sunrise : ScheduleTriggerType.FixedTime,
                Time = EndTime,
                OffsetMinutes = 0,
                TargetKelvin = 6500, // Daylight
                FadeMinutes = FadeMinutes
            };
            
            Schedule.Add(startPoint);
            Schedule.Add(endPoint);
        }
        
        /// <summary>
        /// Gets effective start/end times, using sunrise/sunset if auto mode enabled.
        /// </summary>
        public (TimeSpan start, TimeSpan end) GetEffectiveTimes()
        {
            if (UseAutoSchedule && Latitude.HasValue && Longitude.HasValue)
            {
                var (sunrise, sunset) = SunCalculator.CalculateToday(Latitude.Value, Longitude.Value);
                // Night mode starts at sunset, ends at sunrise
                return (sunset, sunrise);
            }
            return (StartTime, EndTime);
        }
    }
    
    /// <summary>
    /// Service that manages automatic night mode scheduling with fade transitions.
    /// </summary>
    public class NightModeService : IDisposable
    {
        private System.Timers.Timer _timer;
        private NightModeSettings _settings;
        private double _currentBlend = 0.0; // 0 = day mode, 1 = full night mode
        private bool _isTransitioning = false;
        
        /// <summary>
        /// Fired when the night mode blend factor changes (for real-time UI updates).
        /// </summary>
        public event Action<double>? BlendChanged;
        
        /// <summary>
        /// Fired when calibration should be reapplied with new night mode adjustments.
        /// </summary>
        public event Action<CalibrationSettings>? ApplyAdjustments;
        
        public int CurrentNightKelvin => _currentNightKelvin;
        public bool IsNightModeActive => _currentNightKelvin < 6450;
        
        private int _currentNightKelvin = 6500;
        
        public NightModeService(NightModeSettings settings)
        {
            _settings = settings;
            
            // One-shot timer, we restart it manually with dynamic intervals
            _timer = new System.Timers.Timer(1000); 
            _timer.AutoReset = false;
            _timer.Elapsed += OnTimerElapsed;
        }
        
        public void UpdateSettings(NightModeSettings newSettings)
        {
            // If specific settings changed (like toggle/times), force immediate re-eval
            bool wasEnabled = _settings.Enabled;
            _settings = newSettings;
            
            if (_settings.Enabled && !wasEnabled)
            {
                Start();
            }
            else
            {
                // Force an update to catch new times/durations immediately
                UpdateState(); 
                ScheduleNextTick();
            }
        }
        
        public void Start()
        {
            if (!_settings.Enabled) return;
            UpdateState();
            ScheduleNextTick();
        }
        
        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            UpdateState();
            ScheduleNextTick();
        }

        private void ScheduleNextTick()
        {
            if (!_settings.Enabled) return;

            // Simple logic: update frequently (4s) to ensure smooth transitions
            // Optimization for the future: calculate time to next transition event
            // For now, consistent updates ensure responsiveness to schedule changes
            _timer.Interval = 4000;
            _timer.Start();
        }
        
        private void UpdateState()
        {
            if (!_settings.Enabled)
            {
                if (_currentNightKelvin != 6500)
                {
                    _currentNightKelvin = 6500;
                    BlendChanged?.Invoke(0); // Legacy blend support (0=Day)
                    ApplyCurrentAdjustments();
                }
                return;
            }
            
            _settings.EnsureSchedule(_settings.Latitude, _settings.Longitude);
            
            int targetKelvin = CalculateCurrentKelvin();
            
            // Only update if changed significantly
            if (Math.Abs(targetKelvin - _currentNightKelvin) > 5)
            {
                _currentNightKelvin = targetKelvin;
                BlendChanged?.Invoke(1.0); // Signal update
                ApplyCurrentAdjustments();
            }
        }
        
        private int CalculateCurrentKelvin()
        {
            var now = DateTime.Now;
            var timeOfDay = now.TimeOfDay;
            
            // 1. Resolve all points to absolute TimeSpans for today
            var points = _settings.Schedule;
            if (points == null || points.Count == 0) return 6500;

            // Sort points by resolved time
            var resolvedPoints = new List<(TimeSpan Time, NightModeSchedulePoint Point)>();
            foreach (var p in points)
            {
                resolvedPoints.Add((p.GetTimeOfDay(_settings.Latitude, _settings.Longitude), p));
            }
            resolvedPoints.Sort((a, b) => a.Time.CompareTo(b.Time));
            
            // 2. Find the last point that occurred (Time <= Now)
            int currentIndex = -1;
            for (int i = 0; i < resolvedPoints.Count; i++)
            {
                if (resolvedPoints[i].Time <= timeOfDay)
                {
                    currentIndex = i;
                }
            }
            
            // 3. Identify Current Point and Previous Point
            // If current is -1, we are in the early morning before the first point of the day.
            // Our "current state" is determined by the LAST point of Yesterday.
            
            (TimeSpan Time, NightModeSchedulePoint Point) currentContext;
            (TimeSpan Time, NightModeSchedulePoint Point) previousContext;
            
            if (currentIndex == -1)
            {
                // Current context is the last point of the list (acting as yesterday's end)
                // Its trigger time was yesterday.
                var last = resolvedPoints[resolvedPoints.Count - 1];
                currentContext = (last.Time - TimeSpan.FromHours(24), last.Point);
                
                // Prev would be the one before that
                var prev = resolvedPoints.Count > 1 ? resolvedPoints[resolvedPoints.Count - 2] : last;
                if (resolvedPoints.Count > 1) 
                     previousContext = (prev.Time - TimeSpan.FromHours(24), prev.Point);
                else previousContext = (prev.Time - TimeSpan.FromHours(48), prev.Point); // Edge case 1 point
            }
            else
            {
                currentContext = resolvedPoints[currentIndex];
                
                // Previous is index - 1. If index is 0, it's the last point of Yesterday.
                if (currentIndex > 0)
                {
                    previousContext = resolvedPoints[currentIndex - 1];
                }
                else
                {
                    var last = resolvedPoints[resolvedPoints.Count - 1];
                    previousContext = (last.Time - TimeSpan.FromHours(24), last.Point);
                }
            }
            
            // 4. Calculate Interpolation
            // Transition is: From PreviousTarget -> CurrentTarget
            // Triggered at: CurrentContext.Time
            // Duration: CurrentContext.Point.FadeMinutes
            
            // Wait. My Logic earlier:
            // "At 18:00 (Point A), we start fading TO Point A's target."  <- This effectively means Point A defines the transition start.
            // Start Value = Target of (A-1). End Value = Target of A.
            
            var targetPoint = currentContext.Point;
            var startKelvin = previousContext.Point.TargetKelvin;
            var endKelvin = targetPoint.TargetKelvin;
            
            // Check if we are inside the fade window
            // Window starts at currentContext.Time
            var timeSinceTrigger = timeOfDay - currentContext.Time;
            if (timeSinceTrigger < TimeSpan.Zero) timeSinceTrigger += TimeSpan.FromHours(24); // Handle wrapping if needed logic mismatch
            
            // Only fade if we are within the duration
            double fadeMinutes = targetPoint.FadeMinutes;
            if (timeSinceTrigger.TotalMinutes < fadeMinutes && fadeMinutes > 0)
            {
                double progress = timeSinceTrigger.TotalMinutes / fadeMinutes;
                progress = Math.Clamp(progress, 0.0, 1.0);
                
                // Lerp
                return (int)(startKelvin + (endKelvin - startKelvin) * progress);
            }
            
            // Otherwise we have arrived
            return endKelvin;
        }

        private void ApplyCurrentAdjustments()
        {
            // Convert Absolute Kelvin to relative Shift
            // 6500K = 0 shift
            // Base logic: Temp = (Kelvin - 6500) / 70
            double tempShift = (_currentNightKelvin - 6500) / 70.0;
            
            var calibration = new CalibrationSettings
            {
                Temperature = tempShift,
                Algorithm = _settings.Algorithm
            };
            ApplyAdjustments?.Invoke(calibration);
        }
        
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
