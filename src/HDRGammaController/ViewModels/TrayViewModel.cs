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
        private readonly HotkeyManager? _hotkeyManager;
        
        private readonly Dictionary<int, Action<MonitorInfo>> _hotkeyActions = new Dictionary<int, Action<MonitorInfo>>();
        private int _panicId = -1;

        private Dictionary<string, MonitorInfo> _savedConfigs = new Dictionary<string, MonitorInfo>();

        public ObservableCollection<object> TrayItems { get; } = new ObservableCollection<object>();

        public ICommand ExitCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand StartupCommand { get; }

        public TrayViewModel(HotkeyManager? hotkeyManager = null)
        {
            _monitorManager = new MonitorManager();
            // Assuems template is in the same directory (needs to be sourced by user)
            string profileTemplatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "srgb_to_gamma2p2_100_mhc2.icm");
            _profileManager = new ProfileManager(profileTemplatePath);
            _dispwinRunner = new DispwinRunner(); // Auto-detects
            _hotkeyManager = hotkeyManager;

            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            RefreshCommand = new RelayCommand(_ => RefreshMonitors());
            StartupCommand = new RelayCommand(_ => ToggleStartup());

            RefreshMonitors();
            
            if (_hotkeyManager != null)
            {
                RegisterHotkeys();
                _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            }
        }
        
        private void RegisterHotkeys()
        {
            if (_hotkeyManager == null) return;

            // Win+Shift+1 -> Gamma 2.2
            int id22 = _hotkeyManager.Register(Key.D1, ModifierKeys.Windows | ModifierKeys.Shift);
            if (id22 > 0) _hotkeyActions[id22] = m => ApplyProfile(m, GammaMode.Gamma22);
            
            // Win+Shift+2 -> Gamma 2.4
            int id24 = _hotkeyManager.Register(Key.D2, ModifierKeys.Windows | ModifierKeys.Shift);
            if (id24 > 0) _hotkeyActions[id24] = m => ApplyProfile(m, GammaMode.Gamma24);
            
            // Win+Shift+3 -> Default
            int idDef = _hotkeyManager.Register(Key.D3, ModifierKeys.Windows | ModifierKeys.Shift);
            if (idDef > 0) _hotkeyActions[idDef] = m => ApplyProfile(m, GammaMode.WindowsDefault);
            
            // Panic: Ctrl+Alt+Shift+R
            _panicId = _hotkeyManager.Register(Key.R, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        }

        private void OnHotkeyPressed(int id)
        {
            if (id == _panicId)
            {
                PanicAll();
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
            monitor.CurrentGamma = mode;
            // Update saved config so it persists on next refresh
            if (!string.IsNullOrEmpty(monitor.MonitorDevicePath))
            {
                 _savedConfigs[monitor.MonitorDevicePath] = monitor;
            }

            try
            {
                _dispwinRunner.ApplyGamma(monitor, mode, monitor.SdrWhiteLevel);
            }
            catch {}
        }
        
        private void ApplyAll()
        {
            foreach(var item in TrayItems)
            {
                if (item is MonitorViewModel vm && vm.Model.IsHdrActive && vm.Model.CurrentGamma != GammaMode.WindowsDefault)
                {
                     try 
                     { 
                        _dispwinRunner.ApplyGamma(vm.Model, vm.Model.CurrentGamma, vm.Model.SdrWhiteLevel); 
                     } catch {}
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

        public void RefreshMonitors()
        {
            Console.WriteLine("TrayViewModel: Refreshing monitors...");
            // Capture existing monitor ViewModels if possible? No, full refresh is safer for ID changes.
            TrayItems.Clear();
            var monitors = _monitorManager.EnumerateMonitors();
            Console.WriteLine($"TrayViewModel: Enumerated {monitors.Count} monitors.");
            
            if (monitors.Count == 0)
            {
                TrayItems.Add(new ActionViewModel("No Monitors Found", RefreshCommand)); 
            }
            else
            {
                foreach (var m in monitors)
                {
                    // Restore persistent state
                    if (!string.IsNullOrEmpty(m.MonitorDevicePath) && 
                        _savedConfigs.TryGetValue(m.MonitorDevicePath, out var saved))
                    {
                        m.CurrentGamma = saved.CurrentGamma;
                        m.SdrWhiteLevel = saved.SdrWhiteLevel;
                    }
                    
                    // Update saved mapping
                    if (!string.IsNullOrEmpty(m.MonitorDevicePath))
                    {
                        _savedConfigs[m.MonitorDevicePath] = m;
                    }

                    TrayItems.Add(new MonitorViewModel(m, _profileManager, _dispwinRunner));
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
