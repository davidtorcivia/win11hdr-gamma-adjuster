using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HDRGammaController.Core;

namespace HDRGammaController
{
    public partial class SettingsWindow : Window
    {
        private readonly List<MonitorInfo> _monitors;
        private readonly SettingsManager _settingsManager;
        private readonly Action<MonitorInfo, GammaMode, CalibrationSettings>? _applyCallback;
        
        private bool _isLoading = true;
        private MonitorInfo _currentMonitor;
        private MonitorProfileData _currentProfile;
        private MonitorProfileData? _savedProfile; // Last saved profile for compare
        private Dictionary<string, MonitorProfileData> _pendingChanges = new();
        
        // Debounce timer for live preview
        private DispatcherTimer? _previewTimer;
        private const int PreviewDebounceMs = 150;

        public SettingsWindow(
            MonitorInfo initialMonitor,
            List<MonitorInfo> allMonitors,
            SettingsManager settingsManager,
            Action<MonitorInfo, GammaMode, CalibrationSettings>? applyCallback = null)
        {
            InitializeComponent();
            
            _monitors = allMonitors;
            _currentMonitor = initialMonitor;
            _settingsManager = settingsManager;
            _applyCallback = applyCallback;
            
            // Populate monitor selector
            foreach (var m in _monitors)
            {
                string label = $"{m.FriendlyName} ({(m.IsHdrActive ? "HDR" : "SDR")})";
                MonitorSelector.Items.Add(new ComboBoxItem { Content = label, Tag = m });
                
                if (m.MonitorDevicePath == initialMonitor.MonitorDevicePath)
                {
                    MonitorSelector.SelectedIndex = MonitorSelector.Items.Count - 1;
                }
            }
            
            // Load profile for initial monitor
            LoadMonitorProfile(_currentMonitor);
            _isLoading = false;
        }
        
        // Simplified constructor for single monitor (backwards compatibility)
        public SettingsWindow(
            MonitorInfo monitor, 
            SettingsManager settingsManager,
            Action<MonitorInfo, GammaMode, CalibrationSettings>? applyCallback = null)
            : this(monitor, new List<MonitorInfo> { monitor }, settingsManager, applyCallback)
        {
        }
        
        private void MonitorSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            
            // Save pending changes for current monitor before switching
            SaveCurrentToPending();
            
            // Switch to new monitor
            var selectedItem = MonitorSelector.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is MonitorInfo newMonitor)
            {
                _currentMonitor = newMonitor;
                _isLoading = true;
                LoadMonitorProfile(_currentMonitor);
                _isLoading = false;
            }
        }
        
        private void SaveCurrentToPending()
        {
            UpdateProfileFromUI();
            if (!string.IsNullOrEmpty(_currentMonitor.MonitorDevicePath))
            {
                // Clone to avoid reference issues
                _pendingChanges[_currentMonitor.MonitorDevicePath] = _currentProfile.Clone();
            }
        }
        
        private void LoadMonitorProfile(MonitorInfo monitor)
        {
            Console.WriteLine($"LoadMonitorProfile: Loading for {monitor.MonitorDevicePath?.Substring(0, Math.Min(30, monitor.MonitorDevicePath?.Length ?? 0))}...");
            
            // Load saved profile from settings (for compare feature)
            _savedProfile = _settingsManager.GetMonitorProfile(monitor.MonitorDevicePath)?.Clone();
            if (_savedProfile == null)
            {
                Console.WriteLine($"LoadMonitorProfile: No saved profile found, using defaults");
                // Default to monitor's current state
                _savedProfile = new MonitorProfileData { GammaMode = monitor.CurrentGamma };
            }
            else
            {
                Console.WriteLine($"LoadMonitorProfile: Saved profile found - Brightness={_savedProfile.Brightness}");
            }
            
            // Check pending changes first, then settings file, then monitor's current state
            if (!string.IsNullOrEmpty(monitor.MonitorDevicePath) && 
                _pendingChanges.TryGetValue(monitor.MonitorDevicePath, out var pending))
            {
                _currentProfile = pending;
                Console.WriteLine($"LoadMonitorProfile: Using pending changes - Brightness={pending.Brightness}");
            }
            else
            {
                var saved = _settingsManager.GetMonitorProfile(monitor.MonitorDevicePath);
                if (saved != null)
                {
                    _currentProfile = saved;
                    Console.WriteLine($"LoadMonitorProfile: Using saved profile - Brightness={saved.Brightness}");
                }
                else
                {
                    Console.WriteLine($"LoadMonitorProfile: No saved, using defaults");
                    // Use monitor's current gamma mode as default
                    _currentProfile = new MonitorProfileData { GammaMode = monitor.CurrentGamma };
                }
            }
            
            LoadSettingsUI();
        }
        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void LoadSettingsUI()
        {
            // Gamma mode
            GammaModeCombo.SelectedIndex = _currentProfile.GammaMode switch
            {
                GammaMode.Gamma22 => 0,
                GammaMode.Gamma24 => 1,
                GammaMode.WindowsDefault => 2,
                _ => 0
            };
            
            // Brightness
            BrightnessSlider.Value = _currentProfile.Brightness;
            BrightnessValue.Text = $"{_currentProfile.Brightness:F0}%";
            EnhanceShadowsCheck.IsChecked = !_currentProfile.UseLinearBrightness;
            
            // Temperature/Tint
            TemperatureSlider.Value = _currentProfile.Temperature;
            TemperatureValue.Text = $"{_currentProfile.Temperature:F0}";
            TempOffsetSlider.Value = _currentProfile.TemperatureOffset;
            TempOffsetValue.Text = $"{_currentProfile.TemperatureOffset:F0}";
            TintSlider.Value = _currentProfile.Tint;
            TintValue.Text = $"{_currentProfile.Tint:F0}";
            
            // RGB Gains
            RedGainSlider.Value = _currentProfile.RedGain;
            RedGainValue.Text = $"{_currentProfile.RedGain:F2}";
            GreenGainSlider.Value = _currentProfile.GreenGain;
            GreenGainValue.Text = $"{_currentProfile.GreenGain:F2}";
            BlueGainSlider.Value = _currentProfile.BlueGain;
            BlueGainValue.Text = $"{_currentProfile.BlueGain:F2}";
            
            // Night Mode (global, not per-monitor)
            var nightMode = _settingsManager.NightMode;
            Console.WriteLine($"LoadSettingsUI: NightMode loaded - Enabled={nightMode.Enabled}, UseAuto={nightMode.UseAutoSchedule}, Kelvin={nightMode.TemperatureKelvin}");
            NightModeEnabled.IsChecked = nightMode.Enabled;
            UseAutoSchedule.IsChecked = nightMode.UseAutoSchedule;
            NightAlgorithmCombo.SelectedIndex = (int)nightMode.Algorithm;
            
            if (nightMode.Latitude.HasValue)
                LatitudeBox.Text = nightMode.Latitude.Value.ToString("F4");
            if (nightMode.Longitude.HasValue)
                LongitudeBox.Text = nightMode.Longitude.Value.ToString("F4");
                
            NightStartTime.Text = nightMode.StartTime.ToString(@"hh\:mm");
            NightEndTime.Text = nightMode.EndTime.ToString(@"hh\:mm");
            NightTempSlider.Value = nightMode.TemperatureKelvin;
            NightTempValue.Text = $"{nightMode.TemperatureKelvin}K";
            FadeSlider.Value = nightMode.FadeMinutes;
            FadeValue.Text = $"{nightMode.FadeMinutes}";
            
            UpdateNightModeOptionsVisibility();
            UpdateSunTimesDisplay();
        }
        
        private void UpdateNightModeOptionsVisibility()
        {
            if (NightModeOptions == null) return;
            NightModeOptions.Opacity = NightModeEnabled.IsChecked == true ? 1.0 : 0.4;
            NightModeOptions.IsEnabled = NightModeEnabled.IsChecked == true;
            
            // Show/hide location vs manual time panels
            if (LocationPanel != null && ManualTimesPanel != null)
            {
                bool autoMode = UseAutoSchedule?.IsChecked == true;
                LocationPanel.Visibility = autoMode ? Visibility.Visible : Visibility.Collapsed;
                ManualTimesPanel.Visibility = autoMode ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        
        private void UpdateSunTimesDisplay()
        {
            if (SunTimesDisplay == null) return;
            
            if (double.TryParse(LatitudeBox?.Text, out double lat) && 
                double.TryParse(LongitudeBox?.Text, out double lon))
            {
                try
                {
                    var (sunrise, sunset) = Core.SunCalculator.CalculateToday(lat, lon);
                    SunTimesDisplay.Text = $"Sunrise: {sunrise:hh\\:mm}, Sunset: {sunset:hh\\:mm}";
                }
                catch
                {
                    SunTimesDisplay.Text = "Invalid coordinates";
                }
            }
            else
            {
                SunTimesDisplay.Text = "";
            }
        }
        
        private void ScheduleLivePreview()
        {
            if (_isLoading) return;
            if (LivePreviewToggle?.IsChecked != true) return;
            
            _previewTimer?.Stop();
            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PreviewDebounceMs)
            };
            _previewTimer.Tick += (s, _) =>
            {
                _previewTimer?.Stop();
                ApplyCurrentPreview();
            };
            _previewTimer.Start();
        }
        
        private void ApplyCurrentPreview()
        {
            UpdateProfileFromUI();
            _applyCallback?.Invoke(_currentMonitor, _currentProfile.GammaMode, _currentProfile.ToCalibrationSettings());
        }
        
        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            BrightnessValue.Text = $"{e.NewValue:F0}%";
            _currentProfile.Brightness = e.NewValue;
            ScheduleLivePreview();
        }
        
        private void TemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            TemperatureValue.Text = $"{e.NewValue:F0}";
            _currentProfile.Temperature = e.NewValue;
            ScheduleLivePreview();
        }

        private void TempOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            TempOffsetValue.Text = $"{e.NewValue:F0}";
            _currentProfile.TemperatureOffset = e.NewValue;
            ScheduleLivePreview();
        }
        
        private void TintSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            TintValue.Text = $"{e.NewValue:F0}";
            _currentProfile.Tint = e.NewValue;
            ScheduleLivePreview();
        }
        
        private void EnhanceShadows_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _currentProfile.UseLinearBrightness = EnhanceShadowsCheck.IsChecked != true;
            ScheduleLivePreview();
        }
        
        private void NightTempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || NightTempValue == null) return;
            NightTempValue.Text = $"{(int)e.NewValue}K";
            
            // Live preview - apply night temperature to all monitors
            if (NightModeEnabled.IsChecked == true)
            {
                PreviewNightTemperature((int)e.NewValue);
            }
        }
        
        private void NightAlgorithmCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || NightModeEnabled == null) return; // NightModeEnabled might be null during init
            if (NightModeEnabled.IsChecked == true)
            {
                PreviewNightTemperature((int)NightTempSlider.Value);
            }
        }
        
        private void PreviewNightTemperature(int kelvin)
        {
            // Convert Kelvin to temperature shift for preview
            double tempShift = (kelvin - 6500) / 70.0;
            var algorithm = (NightModeAlgorithm)NightAlgorithmCombo.SelectedIndex;
            
            foreach (var monitor in _monitors)
            {
                var profile = _settingsManager.GetMonitorProfile(monitor.MonitorDevicePath);
                var calibration = profile?.ToCalibrationSettings() ?? new CalibrationSettings();
                calibration.Temperature = tempShift;
                calibration.Algorithm = algorithm;
                
                _applyCallback?.Invoke(monitor, 
                    profile?.GammaMode ?? monitor.CurrentGamma, 
                    calibration);
            }
        }
        
        private void UseAutoSchedule_Changed(object sender, RoutedEventArgs e)
        {
            UpdateNightModeOptionsVisibility();
            UpdateSunTimesDisplay();
        }
        
        private async void DetectLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try IP-based geolocation as a simple fallback
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon");
                
                // Parse simple JSON response
                var latMatch = System.Text.RegularExpressions.Regex.Match(response, "\"lat\":([\\d.-]+)");
                var lonMatch = System.Text.RegularExpressions.Regex.Match(response, "\"lon\":([\\d.-]+)");
                
                if (latMatch.Success && lonMatch.Success)
                {
                    LatitudeBox.Text = latMatch.Groups[1].Value;
                    LongitudeBox.Text = lonMatch.Groups[1].Value;
                    UpdateSunTimesDisplay();
                }
            }
            catch
            {
                SunTimesDisplay.Text = "Location detection failed";
            }
        }
        
        private void FadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || FadeValue == null) return;
            FadeValue.Text = $"{e.NewValue:F0}";
        }
        
        private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            
            RedGainValue.Text = $"{RedGainSlider.Value:F2}";
            GreenGainValue.Text = $"{GreenGainSlider.Value:F2}";
            BlueGainValue.Text = $"{BlueGainSlider.Value:F2}";
            
            _currentProfile.RedGain = RedGainSlider.Value;
            _currentProfile.GreenGain = GreenGainSlider.Value;
            _currentProfile.BlueGain = BlueGainSlider.Value;
            ScheduleLivePreview();
        }
        
        private void NightModeEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdateNightModeOptionsVisibility();
        }
        
        private void GammaModeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            _currentProfile.GammaMode = GetSelectedGammaMode();
            ScheduleLivePreview();
        }
        
        private void LivePreviewToggle_Changed(object sender, RoutedEventArgs e)
        {
            // Show Apply button when live preview is off
            if (ApplyButton == null) return;
            ApplyButton.Visibility = LivePreviewToggle.IsChecked == true 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }
        
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Manual apply when live preview is off
            ApplyCurrentPreview();
        }
        
        private void Brightness100_Click(object sender, RoutedEventArgs e) => BrightnessSlider.Value = 100;
        private void Brightness75_Click(object sender, RoutedEventArgs e) => BrightnessSlider.Value = 75;
        private void Brightness50_Click(object sender, RoutedEventArgs e) => BrightnessSlider.Value = 50;
        
        private void ResetRgb_Click(object sender, RoutedEventArgs e)
        {
            RedGainSlider.Value = 1.0;
            GreenGainSlider.Value = 1.0;
            BlueGainSlider.Value = 1.0;
        }
        
        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            _currentProfile = new MonitorProfileData();
            _isLoading = true;
            LoadSettingsUI();
            _isLoading = false;
            ScheduleLivePreview();
        }
        
        private void Compare_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Apply last saved settings while holding
            if (_savedProfile != null)
            {
                _applyCallback?.Invoke(_currentMonitor, _savedProfile.GammaMode, _savedProfile.ToCalibrationSettings());
            }
            e.Handled = true;
        }
        
        private void Compare_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Return to current edits when released
            ApplyCurrentPreview();
            e.Handled = true;
        }
        
        private GammaMode GetSelectedGammaMode()
        {
            return GammaModeCombo.SelectedIndex switch
            {
                0 => GammaMode.Gamma22,
                1 => GammaMode.Gamma24,
                2 => GammaMode.WindowsDefault,
                _ => GammaMode.Gamma22
            };
        }
        
        private void UpdateProfileFromUI()
        {
            _currentProfile.GammaMode = GetSelectedGammaMode();
            _currentProfile.Brightness = BrightnessSlider.Value;
            _currentProfile.UseLinearBrightness = EnhanceShadowsCheck.IsChecked != true;
            _currentProfile.Temperature = TemperatureSlider.Value;
            _currentProfile.TemperatureOffset = TempOffsetSlider.Value;
            _currentProfile.Tint = TintSlider.Value;
            _currentProfile.RedGain = RedGainSlider.Value;
            _currentProfile.GreenGain = GreenGainSlider.Value;
            _currentProfile.BlueGain = BlueGainSlider.Value;
            
            Console.WriteLine($"UpdateProfileFromUI: Brightness={_currentProfile.Brightness}, Temp={_currentProfile.Temperature}, Tint={_currentProfile.Tint}");
        }
        
        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            // Save current monitor to pending
            SaveCurrentToPending();
            
            // Save all pending changes to settings file
            foreach (var kvp in _pendingChanges)
            {
                _settingsManager.SetMonitorProfile(kvp.Key, kvp.Value);
            }
            
            // Save night mode settings (global)
            var nightMode = new NightModeSettings
            {
                Enabled = NightModeEnabled.IsChecked == true,
                UseAutoSchedule = UseAutoSchedule.IsChecked == true,
                Latitude = double.TryParse(LatitudeBox.Text, out var lat) ? lat : null,
                Longitude = double.TryParse(LongitudeBox.Text, out var lon) ? lon : null,
                StartTime = TimeSpan.TryParse(NightStartTime.Text, out var start) ? start : new TimeSpan(21, 0, 0),
                EndTime = TimeSpan.TryParse(NightEndTime.Text, out var end) ? end : new TimeSpan(7, 0, 0),
                TemperatureKelvin = (int)NightTempSlider.Value,
                FadeMinutes = (int)FadeSlider.Value,
                Algorithm = (NightModeAlgorithm)NightAlgorithmCombo.SelectedIndex
            };
            _settingsManager.SetNightMode(nightMode);
            
            // Apply all monitors that have pending changes
            foreach (var kvp in _pendingChanges)
            {
                var monitor = _monitors.Find(m => m.MonitorDevicePath == kvp.Key);
                if (monitor != null)
                {
                    _applyCallback?.Invoke(monitor, kvp.Value.GammaMode, kvp.Value.ToCalibrationSettings());
                }
            }
            
            Close();
        }
    }
}
