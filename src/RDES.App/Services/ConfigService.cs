using System;
using System.IO;
using System.Text.Json;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ConfigService
    {
        // ================================================================================================
        // 🛠️ HARDCODED CENTRAL DATABASE PATH FOR CLIENT WORKSTATIONS (EDIT THIS LINE)
        // Set your company's network shared database path here (UNC path or mapped drive letter).
        // Any RDES-Client build will automatically connect to this location on first launch!
        // Example: @"\\Server\Shared\RDES_Data\rdes_shared.db" or @"Z:\RDES_Data\rdes_shared.db"
        // ================================================================================================
        public const string HardcodedCentralDatabasePath = @"\\Server\Shared\RDES_Data\rdes_shared.db";

        private static readonly string ConfigFileName = "config.json";
        private readonly string _configFilePath;
        private AppConfig _currentConfig;

        public AppConfig CurrentConfig => _currentConfig;

        public bool IsClientMode =>
#if RDES_CLIENT
            true;
#else
            _currentConfig?.IsClientMode ?? false;
#endif

        /// <summary>
        /// Returns the real disk folder where the .exe is running, avoiding any %TEMP% single-file extraction directories.
        /// </summary>
        public static string GetAppDirectory()
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                string? dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }
            return AppContext.BaseDirectory;
        }

        public ConfigService()
        {
            string baseDir = GetAppDirectory();
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
#if RDES_CLIENT
                        loaded.IsClientMode = true;
                        if (string.IsNullOrWhiteSpace(loaded.DatabasePath))
                        {
                            loaded.DatabasePath = HardcodedCentralDatabasePath;
                        }
#else
                        if (string.IsNullOrWhiteSpace(loaded.DatabasePath) && !loaded.IsClientMode)
                        {
                            loaded.DatabasePath = GetDefaultServerDbPath();
                        }
#endif
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
#if RDES_CLIENT
                IsClientMode = true,
                DatabasePath = HardcodedCentralDatabasePath // Auto-links to your hardcoded central path
#else
                IsClientMode = false,
                DatabasePath = GetDefaultServerDbPath()     // Creates rdes_shared.db in the same folder as RDES-Server.exe
#endif
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

        /// <summary>
        /// Gets default database path for RDES-Server.exe (directly in the exact same folder as the executable).
        /// </summary>
        public string GetDefaultServerDbPath()
        {
            string baseDir = GetAppDirectory();
            return Path.Combine(baseDir, "rdes_shared.db");
        }
    }
}
