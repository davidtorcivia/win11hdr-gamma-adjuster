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
    public class AppExclusionRule
    {
        public string AppName { get; set; } = string.Empty;
        public bool FullDisable { get; set; } = false;
    }

    /// <summary>
    /// Per-monitor profile data stored in settings.
    /// </summary>
    public class MonitorProfileData
    {
        public GammaMode GammaMode { get; set; } = GammaMode.Gamma22;
        public double Brightness { get; set; } = 100.0;
        public bool UseLinearBrightness { get; set; } = false;
        public double Temperature { get; set; } = 0.0;
        public double TemperatureOffset { get; set; } = 0.0;
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
            UseLinearBrightness = UseLinearBrightness,
            Temperature = Temperature,
            TemperatureOffset = TemperatureOffset,
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
            UseLinearBrightness = settings.UseLinearBrightness,
            Temperature = settings.Temperature,
            TemperatureOffset = settings.TemperatureOffset,
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
            UseLinearBrightness = UseLinearBrightness,
            Temperature = Temperature,
            TemperatureOffset = TemperatureOffset,
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
        public bool UseAutoSchedule { get; set; } = false;
        public double? Latitude { get; set; } = null;
        public double? Longitude { get; set; } = null;
        public string StartTime { get; set; } = "21:00";
        public string EndTime { get; set; } = "07:00";
        public int TemperatureKelvin { get; set; } = 2700;
        public int FadeMinutes { get; set; } = 30;
        public NightModeAlgorithm Algorithm { get; set; } = NightModeAlgorithm.Standard;
        
        public List<NightModeSchedulePoint> Schedule { get; set; } = new List<NightModeSchedulePoint>();
        
        public NightModeSettings ToNightModeSettings() => new NightModeSettings
        {
            Enabled = Enabled,
            UseAutoSchedule = UseAutoSchedule,
            Latitude = Latitude,
            Longitude = Longitude,
            StartTime = TimeSpan.TryParse(StartTime, out var start) ? start : new TimeSpan(21, 0, 0),
            EndTime = TimeSpan.TryParse(EndTime, out var end) ? end : new TimeSpan(7, 0, 0),
            TemperatureKelvin = TemperatureKelvin,
            FadeMinutes = FadeMinutes,
            Algorithm = Algorithm,
            Schedule = Schedule ?? new List<NightModeSchedulePoint>()
        };
        
        public static NightModeSettingsData FromNightModeSettings(NightModeSettings settings) => new NightModeSettingsData
        {
            Enabled = settings.Enabled,
            UseAutoSchedule = settings.UseAutoSchedule,
            Latitude = settings.Latitude,
            Longitude = settings.Longitude,
            StartTime = settings.StartTime.ToString(@"hh\:mm"),
            EndTime = settings.EndTime.ToString(@"hh\:mm"),
            TemperatureKelvin = settings.TemperatureKelvin,
            FadeMinutes = settings.FadeMinutes,
            Algorithm = settings.Algorithm,
            Schedule = settings.Schedule ?? new List<NightModeSchedulePoint>()
        };
    }
    
    public class SettingsManager
    {
        // Use LocalApplicationData to avoid Resilio Sync corruption
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HDRGammaController");
        
        private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "settings.json");
        
        private SettingsData _data = new SettingsData();

        public NightModeSettings NightMode => _data.NightMode.ToNightModeSettings();
        
        public event Action<NightModeSettings>? NightModeChanged;
        
        public void NotifyNightModeChanged(NightModeSettings? settings = null)
        {
            // Invoke with provided settings or current
             NightModeChanged?.Invoke(settings ?? NightMode);
        }

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
                    var options = new JsonSerializerOptions 
                    { 
                        Converters = { new JsonStringEnumConverter() },
                        PropertyNameCaseInsensitive = true
                    };

                    try
                    {
                        _data = JsonSerializer.Deserialize<SettingsData>(json, options) ?? new SettingsData();
                        ValidateAndClampSettings(_data);
                    }
                    catch (Exception ex)
                    {
                        // Fallback: Try defining a legacy structure or just reset ExcludedApps
                        Console.WriteLine($"SettingsManager: Primary deserialization failed ({ex.Message}), attempting legacy migration...");
                        try 
                        {
                            var legacy = JsonSerializer.Deserialize<LegacySettingsData>(json, options);
                            if (legacy != null)
                            {
                                _data = new SettingsData 
                                {
                                    MonitorProfiles = legacy.MonitorProfiles,
                                    NightMode = legacy.NightMode,
                                    ExcludedApps = legacy.ExcludedApps?.Select(path => new AppExclusionRule { AppName = path, FullDisable = false }).ToList() ?? new List<AppExclusionRule>()
                                };
                                Console.WriteLine("SettingsManager: Legacy migration successful.");
                                Save(); // Save immediately in new format
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"SettingsManager: Legacy migration failed ({innerEx.Message}). Using defaults.");
                            // Backup corrupted file
                            try { File.Copy(SettingsFilePath, SettingsFilePath + $".bak-{DateTime.Now.Ticks}", true); } catch { }
                            _data = new SettingsData();
                        }
                    }
                    
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
                Console.WriteLine($"SettingsManager.Save: JSON preview (first 500 chars):\n{json.Substring(0, Math.Min(500, json.Length))}");
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
            if (profile == null)
            {
                Console.WriteLine($"SettingsManager.SetMonitorProfile: WARNING - Null profile for {monitorDevicePath}, skipping save");
                return;
            }
            Console.WriteLine($"SettingsManager.SetMonitorProfile: Saving {monitorDevicePath} - Brightness={profile.Brightness}, Gamma={profile.GammaMode}");
            _data.MonitorProfiles[monitorDevicePath] = profile;
            Save();
        }
        
        public void SetNightMode(NightModeSettings settings)
        {
            _data.NightMode = NightModeSettingsData.FromNightModeSettings(settings);
            Save();
            NightModeChanged?.Invoke(settings);
        }

        public List<AppExclusionRule> ExcludedApps => _data.ExcludedApps;

        public void SetExcludedApps(List<AppExclusionRule> apps)
        {
            _data.ExcludedApps = apps ?? new List<AppExclusionRule>();
            Save();
        }

        /// <summary>
        /// Validates and clamps all settings to safe ranges to prevent malicious/corrupted values.
        /// </summary>
        private static void ValidateAndClampSettings(SettingsData data)
        {
            if (data == null) return;

            // Validate monitor profiles
            foreach (var profile in data.MonitorProfiles.Values)
            {
                if (profile == null) continue;

                // Brightness: 10-100%
                profile.Brightness = Math.Clamp(profile.Brightness, 10.0, 100.0);

                // Temperature offset: -50 to +50
                profile.Temperature = Math.Clamp(profile.Temperature, -50.0, 50.0);
                profile.TemperatureOffset = Math.Clamp(profile.TemperatureOffset, -50.0, 50.0);

                // Tint: -50 to +50
                profile.Tint = Math.Clamp(profile.Tint, -50.0, 50.0);

                // RGB Gains: 0.5 to 1.5
                profile.RedGain = Math.Clamp(profile.RedGain, 0.5, 1.5);
                profile.GreenGain = Math.Clamp(profile.GreenGain, 0.5, 1.5);
                profile.BlueGain = Math.Clamp(profile.BlueGain, 0.5, 1.5);

                // RGB Offsets: -0.5 to +0.5
                profile.RedOffset = Math.Clamp(profile.RedOffset, -0.5, 0.5);
                profile.GreenOffset = Math.Clamp(profile.GreenOffset, -0.5, 0.5);
                profile.BlueOffset = Math.Clamp(profile.BlueOffset, -0.5, 0.5);
            }

            // Validate night mode settings
            var nm = data.NightMode;
            if (nm != null)
            {
                // Latitude: -90 to +90
                if (nm.Latitude.HasValue)
                    nm.Latitude = Math.Clamp(nm.Latitude.Value, -90.0, 90.0);

                // Longitude: -180 to +180
                if (nm.Longitude.HasValue)
                    nm.Longitude = Math.Clamp(nm.Longitude.Value, -180.0, 180.0);

                // Temperature: 1900K to 6500K (valid color temperature range)
                nm.TemperatureKelvin = Math.Clamp(nm.TemperatureKelvin, 1900, 6500);

                // Fade duration: 0 to 120 minutes
                nm.FadeMinutes = Math.Clamp(nm.FadeMinutes, 0, 120);

                // Validate schedule points
                if (nm.Schedule != null)
                {
                    foreach (var point in nm.Schedule)
                    {
                        if (point == null) continue;
                        // Clamp TargetKelvin to valid range
                        point.TargetKelvin = Math.Clamp(point.TargetKelvin, 1900, 6500);
                        // Clamp FadeMinutes to valid range
                        point.FadeMinutes = Math.Clamp(point.FadeMinutes, 0, 120);
                        // Clamp OffsetMinutes to reasonable range (-120 to +120)
                        point.OffsetMinutes = Math.Clamp(point.OffsetMinutes, -120.0, 120.0);
                    }
                }
            }
        }

        private class SettingsData
        {
            public Dictionary<string, MonitorProfileData> MonitorProfiles { get; set; } = new Dictionary<string, MonitorProfileData>();
            public NightModeSettingsData NightMode { get; set; } = new NightModeSettingsData();
            public List<AppExclusionRule> ExcludedApps { get; set; } = new List<AppExclusionRule>();
        }

        private class LegacySettingsData
        {
            public Dictionary<string, MonitorProfileData> MonitorProfiles { get; set; } = new Dictionary<string, MonitorProfileData>();
            public NightModeSettingsData NightMode { get; set; } = new NightModeSettingsData();
            public List<string> ExcludedApps { get; set; } = new List<string>();
        }
    }
}
