using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HDRGammaController.Core;

namespace HDRGammaController.ViewModels
{
    public class MonitorViewModel
    {
        private readonly MonitorInfo _model;
        public MonitorInfo Model => _model;
        
        private readonly ProfileManager _profileManager;
        private readonly DispwinRunner _dispwinRunner;
        private readonly int _index;
        
        /// <summary>
        /// Callback to notify parent when profile changes (for persistence).
        /// </summary>
        public Action<MonitorInfo, GammaMode>? OnProfileChanged { get; set; }

        public string Header => $"{_index}: {_model.FriendlyName} ({(_model.IsHdrActive ? "HDR" : "SDR")})";
        
        // This view model represents a parent menu item, so it has no command but has children.
        public ICommand? Command => null;
        
        public ObservableCollection<ActionViewModel> SubItems { get; } = new ObservableCollection<ActionViewModel>();

        public MonitorViewModel(MonitorInfo model, ProfileManager profileManager, DispwinRunner dispwinRunner, int index)
        {
            _model = model;
            _profileManager = profileManager;
            _dispwinRunner = dispwinRunner;
            _index = index;

            RebuildSubItems();
        }
        
        private void RebuildSubItems()
        {
            SubItems.Clear();
            
            if (_model.IsHdrActive)
            {
                // HDR is active - show gamma options with checkmarks
                string g22Label = (_model.CurrentGamma == GammaMode.Gamma22 ? "✓ " : "   ") + "Gamma 2.2";
                string g24Label = (_model.CurrentGamma == GammaMode.Gamma24 ? "✓ " : "   ") + "Gamma 2.4";
                string defLabel = (_model.CurrentGamma == GammaMode.WindowsDefault ? "✓ " : "   ") + "Windows Default";
                
                SubItems.Add(new ActionViewModel(g22Label, new RelayCommand(_ => ApplyGamma(GammaMode.Gamma22))));
                SubItems.Add(new ActionViewModel(g24Label, new RelayCommand(_ => ApplyGamma(GammaMode.Gamma24))));
                SubItems.Add(new ActionViewModel(defLabel, new RelayCommand(_ => ApplyGamma(GammaMode.WindowsDefault))));
            }
            else
            {
                // HDR not active - gamma correction not applicable
                SubItems.Add(new ActionViewModel("(HDR not active)", null));
            }
        }

        private void ApplyGamma(GammaMode mode)
        {
             _model.CurrentGamma = mode;
             // For now use dispwin logic 
             // Ideally we run this on a background thread
             try
             {
                Console.WriteLine($"MonitorViewModel.ApplyGamma: Applying {mode} to {_model.DeviceName}");
                _dispwinRunner.ApplyGamma(_model, mode, _model.SdrWhiteLevel);
                Console.WriteLine($"MonitorViewModel.ApplyGamma: Success");
                
                // Rebuild sub-items to update checkmarks
                RebuildSubItems();
                
                // Notify parent for persistence
                OnProfileChanged?.Invoke(_model, mode);
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"MonitorViewModel.ApplyGamma: Exception: {ex.GetType().Name}: {ex.Message}");
                 System.Windows.MessageBox.Show(
                     $"Failed to apply gamma:\n\n{ex.Message}",
                     "HDR Gamma Controller - Error",
                     System.Windows.MessageBoxButton.OK,
                     System.Windows.MessageBoxImage.Error);
             }
        }
    }
}
