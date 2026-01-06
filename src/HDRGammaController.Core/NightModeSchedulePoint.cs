using System;

namespace HDRGammaController.Core
{
    public enum ScheduleTriggerType
    {
        FixedTime,
        Sunrise,
        Sunset
    }

    public class NightModeSchedulePoint
    {
        public ScheduleTriggerType TriggerType { get; set; } = ScheduleTriggerType.FixedTime;
        
        // For FixedTime
        public TimeSpan Time { get; set; }
        
        // For Sun triggers (e.g., -30 means 30 mins before sunset)
        public double OffsetMinutes { get; set; }
        
        public int TargetKelvin { get; set; } = 6500;
        public int FadeMinutes { get; set; } = 30;
        
        public TimeSpan GetTimeOfDay(double? lat, double? lon)
        {
            if (TriggerType == ScheduleTriggerType.FixedTime)
                return Time;
                
            if (lat.HasValue && lon.HasValue)
            {
                var (sunrise, sunset) = SunCalculator.CalculateToday(lat.Value, lon.Value);
                var baseTime = (TriggerType == ScheduleTriggerType.Sunrise) ? sunrise : sunset;
                return baseTime.Add(TimeSpan.FromMinutes(OffsetMinutes));
            }
            
            // Fallback if no location
            return (TriggerType == ScheduleTriggerType.Sunrise) 
                ? new TimeSpan(7, 0, 0) 
                : new TimeSpan(19, 0, 0);
        }
    }
}
