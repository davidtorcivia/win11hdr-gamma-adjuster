using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HDRGammaController.Core
{
    /// <summary>
    /// Per-monitor profile data stored in settings.
    /// </summary>
    public class MonitorProfileData
    {
        public GammaMode GammaMode { get; set; } = GammaMode.Gamma22;
        public double Brightness { get; set; } = 100.0;
        public double Temperature { get; set; } = 0.0;
        public double Tint { get; set; } = 0.0;
        public double RedGain { get; set; } = 1.0;
        public double GreenGain { get; set; } = 1.0;
        public double BlueGain { get; set; } = 1.0;
        public double RedOffset { get; set; } = 0.0;
        public double GreenOffset { get; set; } = 0.0;
        public double BlueOffset { get; set; } = 0.0;
        
        public CalibrationSettings ToCalibrationSettings() => new CalibrationSettings
        {
            Brightness = Brightness,
            Temperature = Temperature,
            Tint = Tint,
            RedGain = RedGain,
            GreenGain = GreenGain,
            BlueGain = BlueGain,
            RedOffset = RedOffset,
            GreenOffset = GreenOffset,
            BlueOffset = BlueOffset
        };
        
        public static MonitorProfileData FromCalibrationSettings(CalibrationSettings settings, GammaMode mode) => new MonitorProfileData
        {
            GammaMode = mode,
            Brightness = settings.Brightness,
            Temperature = settings.Temperature,
            Tint = settings.Tint,
            RedGain = settings.RedGain,
            GreenGain = settings.GreenGain,
            BlueGain = settings.BlueGain,
            RedOffset = settings.RedOffset,
            GreenOffset = settings.GreenOffset,
            BlueOffset = settings.BlueOffset
        };
        
        public MonitorProfileData Clone() => new MonitorProfileData
        {
            GammaMode = GammaMode,
            Brightness = Brightness,
            Temperature = Temperature,
            Tint = Tint,
            RedGain = RedGain,
            GreenGain = GreenGain,
            BlueGain = BlueGain,
            RedOffset = RedOffset,
            GreenOffset = GreenOffset,
            BlueOffset = BlueOffset
        };
    }
    
    /// <summary>
    /// Night mode settings stored in settings file.
    /// </summary>
    public class NightModeSettingsData
    {
        public bool Enabled { get; set; } = false;
        public string StartTime { get; set; } = "21:00";
        public string EndTime { get; set; } = "07:00";
        public double Temperature { get; set; } = -30.0;
        public int FadeMinutes { get; set; } = 30;
        
        public NightModeSettings ToNightModeSettings() => new NightModeSettings
        {
            Enabled = Enabled,
            StartTime = TimeSpan.TryParse(StartTime, out var start) ? start : new TimeSpan(21, 0, 0),
            EndTime = TimeSpan.TryParse(EndTime, out var end) ? end : new TimeSpan(7, 0, 0),
            Temperature = Temperature,
            FadeMinutes = FadeMinutes
        };
        
        public static NightModeSettingsData FromNightModeSettings(NightModeSettings settings) => new NightModeSettingsData
        {
            Enabled = settings.Enabled,
            StartTime = settings.StartTime.ToString(@"hh\:mm"),
            EndTime = settings.EndTime.ToString(@"hh\:mm"),
            Temperature = settings.Temperature,
            FadeMinutes = settings.FadeMinutes
        };
    }
    
    public class SettingsManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HDRGammaController");
        
        private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "settings.json");
        
        private SettingsData _data = new SettingsData();

        public NightModeSettings NightMode => _data.NightMode.ToNightModeSettings();

        public SettingsManager()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    _data = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
                    Console.WriteLine($"SettingsManager: Loaded {_data.MonitorProfiles.Count} monitor profiles.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsManager: Failed to load settings: {ex.Message}");
                _data = new SettingsData();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataPath);
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                string json = JsonSerializer.Serialize(_data, options);
                File.WriteAllText(SettingsFilePath, json);
                Console.WriteLine($"SettingsManager: Saved {_data.MonitorProfiles.Count} monitor profiles.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsManager: Failed to save settings: {ex.Message}");
            }
        }

        public GammaMode? GetProfileForMonitor(string monitorDevicePath)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return null;
            
            if (_data.MonitorProfiles.TryGetValue(monitorDevicePath, out var profile))
            {
                return profile.GammaMode;
            }
            return null;
        }

        public void SetProfileForMonitor(string monitorDevicePath, GammaMode mode)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return;
            
            if (!_data.MonitorProfiles.TryGetValue(monitorDevicePath, out var profile))
            {
                profile = new MonitorProfileData();
                _data.MonitorProfiles[monitorDevicePath] = profile;
            }
            profile.GammaMode = mode;
            Save();
        }
        
        public MonitorProfileData? GetMonitorProfile(string monitorDevicePath)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return null;
            _data.MonitorProfiles.TryGetValue(monitorDevicePath, out var profile);
            return profile;
        }
        
        public void SetMonitorProfile(string monitorDevicePath, MonitorProfileData profile)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return;
            _data.MonitorProfiles[monitorDevicePath] = profile;
            Save();
        }
        
        public void SetNightMode(NightModeSettings settings)
        {
            _data.NightMode = NightModeSettingsData.FromNightModeSettings(settings);
            Save();
        }

        private class SettingsData
        {
            public Dictionary<string, MonitorProfileData> MonitorProfiles { get; set; } = new Dictionary<string, MonitorProfileData>();
            public NightModeSettingsData NightMode { get; set; } = new NightModeSettingsData();
        }
    }
}
