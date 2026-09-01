using System;
using System.IO;
using System.Text.Json;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ConfigService
    {
        private static readonly string ConfigFileName = "config.json";
        private readonly string _configFilePath;
        private AppConfig _currentConfig;

        public AppConfig CurrentConfig => _currentConfig;

        public ConfigService()
        {
            // First check adjacent to executable
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(baseDir, ConfigFileName);
            _currentConfig = LoadConfig();
        }

        public AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.DatabasePath))
                        {
                            loaded.DatabasePath = GetDefaultDbPath();
                        }
                        _currentConfig = loaded;
                        return _currentConfig;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }

            _currentConfig = new AppConfig
            {
                DatabasePath = GetDefaultDbPath()
            };
            SaveConfig(_currentConfig);
            return _currentConfig;
        }

        public bool SaveConfig(AppConfig config)
        {
            try
            {
                _currentConfig = config ?? new AppConfig();
                string json = JsonSerializer.Serialize(_currentConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
                return false;
            }
        }

        public string GetDefaultDbPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(baseDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                try
                {
                    Directory.CreateDirectory(dataDir);
                }
                catch
                {
                    // Fallback to local appdata if base directory is read-only
                    string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDES");
                    Directory.CreateDirectory(appData);
                    return Path.Combine(appData, "rdes_shared.db");
                }
            }
            return Path.Combine(dataDir, "rdes_shared.db");
        }
    }
}
