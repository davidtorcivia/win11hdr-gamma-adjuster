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
        /// Start time for night mode (e.g., "21:00").
        /// </summary>
        public TimeSpan StartTime { get; set; } = new TimeSpan(21, 0, 0);
        
        /// <summary>
        /// End time for night mode (e.g., "07:00").
        /// </summary>
        public TimeSpan EndTime { get; set; } = new TimeSpan(7, 0, 0);
        
        /// <summary>
        /// Temperature shift during night mode (-50 to 0, negative = warmer).
        /// </summary>
        public double Temperature { get; set; } = -30.0;
        
        /// <summary>
        /// Fade duration in minutes (0 = instant, 60 = gradual).
        /// </summary>
        public int FadeMinutes { get; set; } = 30;
    }
    
    /// <summary>
    /// Service that manages automatic night mode scheduling with fade transitions.
    /// </summary>
    public class NightModeService : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly NightModeSettings _settings;
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
            // Handle overnight periods (e.g., 21:00 to 07:00)
            if (_settings.StartTime > _settings.EndTime)
            {
                // Night period spans midnight
                return now >= _settings.StartTime || now <= _settings.EndTime;
            }
            else
            {
                // Normal period within same day
                return now >= _settings.StartTime && now <= _settings.EndTime;
            }
        }
        
        private double CalculateFadeBlend(TimeSpan now)
        {
            var fadeDuration = TimeSpan.FromMinutes(_settings.FadeMinutes);
            
            // Fade in: StartTime - fadeMinutes to StartTime
            var fadeInStart = _settings.StartTime - fadeDuration;
            if (fadeInStart < TimeSpan.Zero) fadeInStart += TimeSpan.FromHours(24);
            
            // Fade out: EndTime to EndTime + fadeMinutes
            var fadeOutEnd = _settings.EndTime + fadeDuration;
            if (fadeOutEnd > TimeSpan.FromHours(24)) fadeOutEnd -= TimeSpan.FromHours(24);
            
            // Check if we're in fade-in period
            if (IsInFadeRange(now, fadeInStart, _settings.StartTime))
            {
                double progress = GetFadeProgress(now, fadeInStart, _settings.StartTime);
                return progress;
            }
            
            // Check if we're in full night mode
            if (IsInNightPeriod(now))
            {
                return 1.0;
            }
            
            // Check if we're in fade-out period
            if (IsInFadeRange(now, _settings.EndTime, fadeOutEnd))
            {
                double progress = GetFadeProgress(now, _settings.EndTime, fadeOutEnd);
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
                Temperature = _settings.Temperature * _currentBlend
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
