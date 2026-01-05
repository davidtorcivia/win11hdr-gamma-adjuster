using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HDRGammaController.Core;
using HDRGammaController.Services;
using HDRGammaController.Interop;

namespace HDRGammaController.ViewModels
{
    public class TrayViewModel
    {
        private readonly MonitorManager _monitorManager;
        private readonly ProfileManager _profileManager;
        private readonly DispwinRunner _dispwinRunner;
        private readonly SettingsManager _settingsManager;
        private readonly HotkeyManager? _hotkeyManager;
        private readonly NightModeService _nightModeService;
        
        private readonly Dictionary<int, Action<MonitorInfo>> _hotkeyActions = new Dictionary<int, Action<MonitorInfo>>();
        private int _panicId = -1;
        private int _nightModeToggleId = -1;
        private bool _nightModeManuallyDisabled = false;

        private Dictionary<string, MonitorInfo> _savedConfigs = new Dictionary<string, MonitorInfo>();

        public ObservableCollection<object> TrayItems { get; } = new ObservableCollection<object>();

        public ICommand ExitCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand StartupCommand { get; }

        public TrayViewModel(HotkeyManager? hotkeyManager = null)
        {
            _monitorManager = new MonitorManager();
            _settingsManager = new SettingsManager();
            
            // Initialize Night Mode Service
            _nightModeService = new NightModeService(_settingsManager.NightMode);
            _nightModeService.BlendChanged += (blend) => 
            {
                // Dispatch to UI thread if needed (though ApplyAll primarily runs dispwin which is blocking/background)
                // WPF Observables need UI thread, but ApplyAll primarily affects hardware. 
                // However, TrayItems might update. Better invoke.
                Application.Current.Dispatcher.Invoke(() => ApplyAll());
            };
            
            _settingsManager.NightModeChanged += (newSettings) => _nightModeService.UpdateSettings(newSettings);
            
            // Start service
            _nightModeService.Start();

            // Assumes template is in the same directory (needs to be sourced by user)
            string profileTemplatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "srgb_to_gamma2p2_100_mhc2.icm");
            _profileManager = new ProfileManager(profileTemplatePath);
            _dispwinRunner = new DispwinRunner(); // Auto-detects
            _hotkeyManager = hotkeyManager;

            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            RefreshCommand = new RelayCommand(_ => RefreshMonitors());
            StartupCommand = new RelayCommand(_ => ToggleStartup());

            RefreshMonitors();
            
            // Apply saved profiles on startup
            ApplyAll();
            
            if (_hotkeyManager != null)
            {
                RegisterHotkeys();
                _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            }
        }
        
        private void RegisterHotkeys()
        {
            if (_hotkeyManager == null) return;

            // Win+Shift+F1 -> Gamma 2.2
            int id22 = _hotkeyManager.Register(Key.F1, ModifierKeys.Windows | ModifierKeys.Shift);
            if (id22 > 0) _hotkeyActions[id22] = m => ApplyProfile(m, GammaMode.Gamma22);
            
            // Win+Shift+F2 -> Gamma 2.4
            int id24 = _hotkeyManager.Register(Key.F2, ModifierKeys.Windows | ModifierKeys.Shift);
            if (id24 > 0) _hotkeyActions[id24] = m => ApplyProfile(m, GammaMode.Gamma24);
            
            // Win+Shift+F3 -> Default
            int idDef = _hotkeyManager.Register(Key.F3, ModifierKeys.Windows | ModifierKeys.Shift);
            if (idDef > 0) _hotkeyActions[idDef] = m => ApplyProfile(m, GammaMode.WindowsDefault);
            
            // Panic: Win+Shift+F4
            _panicId = _hotkeyManager.Register(Key.F4, ModifierKeys.Windows | ModifierKeys.Shift);
            
            // Night Mode Toggle: Win+Shift+N
            _nightModeToggleId = _hotkeyManager.Register(Key.N, ModifierKeys.Windows | ModifierKeys.Shift);
        }

        private void OnHotkeyPressed(int id)
        {
            if (id == _panicId)
            {
                PanicAll();
                return;
            }
            
            if (id == _nightModeToggleId)
            {
                // Toggle night mode on/off
                _nightModeManuallyDisabled = !_nightModeManuallyDisabled;
                ApplyAll(); // Re-apply all calibrations with night mode toggled
                return;
            }

            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                var monitor = GetFocusedMonitor();
                if (monitor != null)
                {
                     action(monitor);
                }
            }
        }
        
        private MonitorInfo? GetFocusedMonitor()
        {
            IntPtr hwnd = User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            
            IntPtr hMonitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
            
            foreach(var item in TrayItems)
            {
                if (item is MonitorViewModel vm && vm.Model.HMonitor == hMonitor)
                {
                    return vm.Model;
                }
            }
            return null;
        }

        public async void HandleDisplayChange()
        {
            await Task.Delay(1500);
            RefreshMonitors();
            ApplyAll();
        }

        public async void HandleResume()
        {
            await Task.Delay(3000); 
            RefreshMonitors();
            ApplyAll();
        }
        
        private void ApplyProfile(MonitorInfo monitor, GammaMode mode)
        {
            RequestApply(monitor, mode);
            
            // Refresh to update checkmarks
            RefreshMonitors();
        }
        
        public void RequestApply(MonitorInfo monitor, GammaMode mode, CalibrationSettings? manualCalibration = null)
        {
             // Use service's blend factor
            double blend = _nightModeService.CurrentBlend;
            if (_nightModeManuallyDisabled) blend = 0.0;
            bool nightModeActive = blend > 0.001;
            
            try 
            { 
                 // If manual calibration is provided (from live preview), use it.
                 // Otherwise load from profile.
                 CalibrationSettings calibration;
                 double brightness = 100;
                 
                 if (manualCalibration != null)
                 {
                     calibration = manualCalibration;
                     brightness = calibration.Brightness; // Approx
                 }
                 else
                 {
                    var profile = _settingsManager.GetMonitorProfile(monitor.MonitorDevicePath) ?? new MonitorProfileData();
                    calibration = profile.ToCalibrationSettings();
                    brightness = profile.Brightness;
                 }

                // Apply night mode temperature if active
                if (nightModeActive)
                {
                    var nightMode = _settingsManager.NightMode;
                    
                    // Calculate night mode shift (-50 to +50 scale)
                    double nightShift = (nightMode.TemperatureKelvin - 6500) / 70.0;
                    
                    // Blend the shift
                    double blendedShift = nightShift * blend;
                    
                    // Combine with user's specific monitor calibration
                    calibration.Temperature += blendedShift;
                    calibration.Temperature = Math.Clamp(calibration.Temperature, -50.0, 50.0);
                    
                    if (blend > 0.5)
                    {
                        calibration.Algorithm = nightMode.Algorithm;
                    }
                }
                
                Console.WriteLine($"RequestApply: Applying {monitor.FriendlyName} - Gamma={mode}, Brightness={brightness}, Temp={calibration.Temperature:F1}");
                _dispwinRunner.ApplyGamma(monitor, mode, monitor.SdrWhiteLevel, calibration); 
                
                // Update persistent state if this wasn't a manual preview
                if (manualCalibration == null)
                {
                     monitor.CurrentGamma = mode;
                     if (!string.IsNullOrEmpty(monitor.MonitorDevicePath))
                     {
                         _settingsManager.SetProfileForMonitor(monitor.MonitorDevicePath, mode);
                     }
                }
            } catch (Exception ex) 
            {
                Console.WriteLine($"RequestApply error: {ex.Message}");
            }
        }

        private void ApplyAll()
        {
            foreach(var item in TrayItems)
            {
                if (item is MonitorViewModel vm)
                {
                    // Get saved mode
                    var profile = _settingsManager.GetMonitorProfile(vm.Model.MonitorDevicePath);
                    var mode = profile?.GammaMode ?? vm.Model.CurrentGamma;
                    
                    if (mode == GammaMode.WindowsDefault) continue;
                    
                    RequestApply(vm.Model, mode);
                }
            }
        }

        private void PanicAll()
        {
            foreach(var item in TrayItems)
            {
                if (item is MonitorViewModel vm)
                {
                     try { _dispwinRunner.ClearGamma(vm.Model); } catch {}
                }
            }
            MessageBox.Show("Panic Mode Activated: All gamma tables cleared.", "HDR Gamma Controller", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleStartup()
        {
            StartupManager.IsStartupEnabled = !StartupManager.IsStartupEnabled;
            RefreshMonitors(); // Refresh to update the checkmark
        }
        
        private void OnMonitorProfileChanged(MonitorInfo monitor, GammaMode mode)
        {
            // Persist to settings
            if (!string.IsNullOrEmpty(monitor.MonitorDevicePath))
            {
                _settingsManager.SetProfileForMonitor(monitor.MonitorDevicePath, mode);
            }
        }

        public void RefreshMonitors()
        {
            Console.WriteLine("TrayViewModel: Refreshing monitors...");
            TrayItems.Clear();
            var monitors = _monitorManager.EnumerateMonitors();
            Console.WriteLine($"TrayViewModel: Enumerated {monitors.Count} monitors.");
            
            if (monitors.Count == 0)
            {
                TrayItems.Add(new ActionViewModel("No Monitors Found", RefreshCommand)); 
            }
            else
            {
                int index = 1;
                foreach (var m in monitors)
                {
                    // Restore persistent state from settings file
                    var savedMode = _settingsManager.GetProfileForMonitor(m.MonitorDevicePath);
                    if (savedMode.HasValue)
                    {
                        m.CurrentGamma = savedMode.Value;
                    }
                    else if (!string.IsNullOrEmpty(m.MonitorDevicePath) && 
                        _savedConfigs.TryGetValue(m.MonitorDevicePath, out var saved))
                    {
                        // Fallback to in-memory cache
                        m.CurrentGamma = saved.CurrentGamma;
                        m.SdrWhiteLevel = saved.SdrWhiteLevel;
                    }
                    
                    // Update saved mapping
                    if (!string.IsNullOrEmpty(m.MonitorDevicePath))
                    {
                        _savedConfigs[m.MonitorDevicePath] = m;
                    }

                    var vm = new MonitorViewModel(m, _profileManager, _dispwinRunner, index, _settingsManager);
                    // Point MonitorViewModel to use our centralized RequestApply which handles Night Mode
                    vm.OnApplyWithCalibration = RequestApply;
                    vm.GetAllMonitors = () => monitors;
                    TrayItems.Add(vm);
                    index++;
                }
            }
            
            // Startup toggle with checkmark
            string startupLabel = StartupManager.IsStartupEnabled ? "✓ Start with Windows" : "Start with Windows";
            TrayItems.Add(new ActionViewModel(startupLabel, StartupCommand));
            TrayItems.Add(new ActionViewModel("Refresh", RefreshCommand));
            TrayItems.Add(new ActionViewModel("Exit", ExitCommand));
        }
    }
}
