using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HDRGammaController.Core;

namespace HDRGammaController
{
    public partial class DashboardWindow : Window
    {
        private readonly MonitorManager _monitorManager;
        private readonly SettingsManager _settingsManager;
        private readonly NightModeService _nightModeService;
        private readonly Action<MonitorInfo, GammaMode, CalibrationSettings> _applyCallback;
        
        public DashboardWindow(
            MonitorManager monitorManager, 
            SettingsManager settingsManager,
            NightModeService nightModeService,
            Action<MonitorInfo, GammaMode, CalibrationSettings> applyCallback)
        {
            InitializeComponent();
            _monitorManager = monitorManager;
            _settingsManager = settingsManager;
            _nightModeService = nightModeService;
            _applyCallback = applyCallback;
            
            // Re-refresh when simple blend changes (for live update)
            _nightModeService.BlendChanged += (val) => Dispatcher.Invoke(RefreshMonitors);

            RefreshMonitors();
        }
        
        private void RefreshMonitors()
        {
            var monitors = _monitorManager.EnumerateMonitors();
            var items = new List<DashboardItem>();
            
            // Night mode data
            double blend = _nightModeService.CurrentBlend;
            var nmSettings = _settingsManager.NightMode;
            double nightShift = (nmSettings.TemperatureKelvin - 6500) / 70.0;
            double blendedShift = nightShift * blend;
            
            foreach (var m in monitors)
            {
                // Load current state
                var profile = _settingsManager.GetMonitorProfile(m.MonitorDevicePath);
                
                // Determine display properties
                bool isHdr = m.IsHdrActive;
                string badgeText = isHdr ? "HDR" : "SDR";
                Brush badgeColor = isHdr 
                    ? new SolidColorBrush(Color.FromRgb(0, 120, 212)) // Blue
                    : new SolidColorBrush(Color.FromRgb(100, 100, 100)); // Grey
                
                double brightness = profile?.Brightness ?? 100;
                GammaMode gamma = profile?.GammaMode ?? m.CurrentGamma;
                
                // Calculate Effective Temperature
                double baseTemp = profile?.Temperature ?? 0;
                double offset = profile?.TemperatureOffset ?? 0;
                double effectiveTemp = baseTemp + offset + blendedShift;
                int kelvin = (int)(6500 + effectiveTemp * 70);

                string tempText = $"{kelvin}K";
                if (blend > 0.01) tempText += " (Night)";
                
                items.Add(new DashboardItem
                {
                    Model = m,
                    FriendlyName = m.FriendlyName,
                    BadgeText = badgeText,
                    BadgeColor = badgeColor,
                    CurrentGamma = gamma,
                    CurrentBrightness = brightness,
                    CurrentTemperatureText = tempText
                });
            }
            
            MonitorList.ItemsSource = items;
        }
        
        public class DashboardItem
        {
            public MonitorInfo Model { get; set; } = new MonitorInfo();
            public string FriendlyName { get; set; } = "";
            public string BadgeText { get; set; } = "";
            public Brush BadgeColor { get; set; } = Brushes.Gray;
            public GammaMode CurrentGamma { get; set; }
            public double CurrentBrightness { get; set; }
            public string CurrentTemperatureText { get; set; } = "";
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
        }

        private void Configure_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DashboardItem item)
            {
                // Open SettingsWindow for this monitor
                // We need to pass all monitors to it so the selector works
                var allMonitors = (MonitorList.ItemsSource as List<DashboardItem>)?.Select(i => i.Model).ToList();
                
                var settingsWindow = new SettingsWindow(
                    item.Model, 
                    allMonitors ?? new List<MonitorInfo> { item.Model }, 
                    _settingsManager, 
                    (mon, mode, cal) => 
                    {
                        // Callback updates app state
                        _applyCallback(mon, mode, cal);
                        // Refresh dashboard to show new values
                        RefreshMonitors();
                    });
                    
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
                
                // Refresh after close in case they changed things
                RefreshMonitors();
            }
        }
    }
}
