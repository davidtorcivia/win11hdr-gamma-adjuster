using System;
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
        
        public double CurrentBlend => _currentBlend;
        public bool IsNightModeActive => _currentBlend > 0.01;
        
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
                UpdateBlend(); 
                ScheduleNextTick();
            }
        }
        
        public void Start()
        {
            if (!_settings.Enabled) return;
            UpdateBlend();
            ScheduleNextTick();
        }
        
        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            UpdateBlend();
            ScheduleNextTick();
        }

        private void ScheduleNextTick()
        {
            if (!_settings.Enabled) return;

            var now = DateTime.Now.TimeOfDay;
            bool isFading = IsInAnyFadeWindow(now);

            double intervalMs = 30000; // Default stable interval (30s)

            if (isFading)
            {
                // Dynamic Interval Logic:
                // We want to be "imperceptibly smooth".
                // Target 100 steps for the entire transition.
                // For 30 mins (1800s): 1800/100 = 18s interval.
                // Delta per step approx 38K (invisible).
                
                double fadeDurationSeconds = _settings.FadeMinutes * 60.0;
                double targetIntervalSeconds = fadeDurationSeconds / 100.0;
                
                // Clamp: Never faster than 4s (performance), never slower than 30s (responsiveness)
                intervalMs = Math.Clamp(targetIntervalSeconds * 1000, 4000, 30000);
            }
            
            _timer.Interval = intervalMs;
            _timer.Start();
        }
        
        private bool IsInAnyFadeWindow(TimeSpan now)
        {
            if (_settings.FadeMinutes <= 0) return false;

            var (startTime, endTime) = _settings.GetEffectiveTimes();
            var fadeDuration = TimeSpan.FromMinutes(_settings.FadeMinutes);

            // Fade In Window (Start - Fade to Start)
            var fadeInStart = startTime - fadeDuration;
            if (fadeInStart < TimeSpan.Zero) fadeInStart += TimeSpan.FromHours(24);
            if (IsInFadeRange(now, fadeInStart, startTime)) return true;

            // Fade Out Window (End to End + Fade)
            var fadeOutEnd = endTime + fadeDuration;
            if (fadeOutEnd > TimeSpan.FromHours(24)) fadeOutEnd -= TimeSpan.FromHours(24);
            if (IsInFadeRange(now, endTime, fadeOutEnd)) return true;

            return false;
        }
        
        private void UpdateBlend()
        {
            if (!_settings.Enabled)
            {
                if (_currentBlend > 0)
                {
                    _currentBlend = 0;
                    BlendChanged?.Invoke(_currentBlend);
                    ApplyCurrentAdjustments();
                }
                return;
            }
            
            var now = DateTime.Now.TimeOfDay;
            double targetBlend = IsInNightPeriod(now) ? 1.0 : 0.0;
            
            // Handle fade transitions
            if (_settings.FadeMinutes > 0)
            {
                targetBlend = CalculateFadeBlend(now);
            }
            
            // Only update if changed significantly
            if (Math.Abs(targetBlend - _currentBlend) > 0.01)
            {
                _currentBlend = targetBlend;
                BlendChanged?.Invoke(_currentBlend);
                ApplyCurrentAdjustments();
            }
        }
        
        private bool IsInNightPeriod(TimeSpan now)
        {
            // Get effective times (handles auto sunrise/sunset mode)
            var (startTime, endTime) = _settings.GetEffectiveTimes();
            
            // Handle overnight periods (e.g., sunset 17:00 to sunrise 07:00)
            if (startTime > endTime)
            {
                // Night period spans midnight
                return now >= startTime || now <= endTime;
            }
            else
            {
                // Normal period within same day
                return now >= startTime && now <= endTime;
            }
        }
        
        private double CalculateFadeBlend(TimeSpan now)
        {
            var (startTime, endTime) = _settings.GetEffectiveTimes();
            var fadeDuration = TimeSpan.FromMinutes(_settings.FadeMinutes);
            
            // Fade in: StartTime - fadeMinutes to StartTime
            var fadeInStart = startTime - fadeDuration;
            if (fadeInStart < TimeSpan.Zero) fadeInStart += TimeSpan.FromHours(24);
            
            // Fade out: EndTime to EndTime + fadeMinutes
            var fadeOutEnd = endTime + fadeDuration;
            if (fadeOutEnd > TimeSpan.FromHours(24)) fadeOutEnd -= TimeSpan.FromHours(24);
            
            // Check if we're in fade-in period
            if (IsInFadeRange(now, fadeInStart, startTime))
            {
                double progress = GetFadeProgress(now, fadeInStart, startTime);
                return progress;
            }
            
            // Check if we're in full night mode
            if (IsInNightPeriod(now))
            {
                return 1.0;
            }
            
            // Check if we're in fade-out period
            if (IsInFadeRange(now, endTime, fadeOutEnd))
            {
                double progress = GetFadeProgress(now, endTime, fadeOutEnd);
                return 1.0 - progress;
            }
            
            return 0.0;
        }
        
        private bool IsInFadeRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start > end)
            {
                return now >= start || now <= end;
            }
            return now >= start && now <= end;
        }
        
        private double GetFadeProgress(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            double totalMinutes = (end - start).TotalMinutes;
            if (totalMinutes <= 0) return 1.0;
            
            double elapsedMinutes = (now - start).TotalMinutes;
            if (elapsedMinutes < 0) elapsedMinutes += 24 * 60;
            
            return Math.Clamp(elapsedMinutes / totalMinutes, 0.0, 1.0);
        }
        
        private void ApplyCurrentAdjustments()
        {
            var calibration = new CalibrationSettings
            {
                Temperature = _settings.Temperature * _currentBlend,
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
