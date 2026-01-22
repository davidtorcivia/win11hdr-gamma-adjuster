using System.Windows.Media;
using HDRGammaController.Core;

namespace HDRGammaController.ViewModels
{
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
}
