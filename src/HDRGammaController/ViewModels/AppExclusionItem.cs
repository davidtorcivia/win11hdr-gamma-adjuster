using HDRGammaController.Core;
using System.Collections.ObjectModel;

namespace HDRGammaController.ViewModels
{
    public class AppExclusionItem 
    {
        public ObservableCollection<AppExclusionRule> ExcludedApps { get; set; } = new ObservableCollection<AppExclusionRule>();
        public ObservableCollection<string> RunningApps { get; set; } = new ObservableCollection<string>();
    }
}
