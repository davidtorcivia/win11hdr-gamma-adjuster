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
            
            // Check every 30 seconds
            _timer = new System.Timers.Timer(30000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }
        
        public void UpdateSettings(NightModeSettings newSettings)
        {
            _settings = newSettings;
            UpdateBlend();
            
            // If the service is running but disabled in new settings, stop it? 
            // Or just rely on UpdateBlend checking Enabled.
            // If enabled went from false to true, we might need to ensure timer is running.
            if (_settings.Enabled && !_timer.Enabled)
            {
                Start();
            }
        }
        
        public void Start()
        {
            if (!_settings.Enabled) return;
            
            // Initial check
            UpdateBlend();
            _timer.Start();
        }
        
        public void Stop()
        {
            _timer.Stop();
        }
        
        /// <summary>
        /// Immediately enables night mode (for quick toggle).
        /// </summary>
        public void EnableNow()
        {
            _currentBlend = 1.0;
            BlendChanged?.Invoke(_currentBlend);
            ApplyCurrentAdjustments();
        }
        
        /// <summary>
        /// Immediately disables night mode (for quick toggle).
        /// </summary>
        public void DisableNow()
        {
            _currentBlend = 0.0;
            BlendChanged?.Invoke(_currentBlend);
            ApplyCurrentAdjustments();
        }
        
        /// <summary>
        /// Gets the calibration settings with night mode adjustments blended in.
        /// </summary>
        public CalibrationSettings GetNightModeCalibration(CalibrationSettings baseCalibration)
        {
            if (_currentBlend < 0.01) return baseCalibration;
            
            var result = baseCalibration.Clone();
            
            // Blend in night mode temperature
            result.Temperature += _settings.Temperature * _currentBlend;
            result.Temperature = Math.Clamp(result.Temperature, -50.0, 50.0);
            
            // Apply night mode algorithm
            result.Algorithm = _settings.Algorithm;
            
            return result;
        }
        
        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            UpdateBlend();
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
