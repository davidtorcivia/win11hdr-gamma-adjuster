using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HDRGammaController.Core
{
    public class SettingsManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HDRGammaController");
        
        private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "settings.json");
        
        private SettingsData _data = new SettingsData();

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
                string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
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
            
            if (_data.MonitorProfiles.TryGetValue(monitorDevicePath, out var mode))
            {
                return mode;
            }
            return null;
        }

        public void SetProfileForMonitor(string monitorDevicePath, GammaMode mode)
        {
            if (string.IsNullOrEmpty(monitorDevicePath)) return;
            
            _data.MonitorProfiles[monitorDevicePath] = mode;
            Save();
        }

        private class SettingsData
        {
            public Dictionary<string, GammaMode> MonitorProfiles { get; set; } = new Dictionary<string, GammaMode>();
        }
    }
}
