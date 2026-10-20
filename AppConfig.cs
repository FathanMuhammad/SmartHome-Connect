using System;
using System.IO;
using System.Text.Json;

namespace SmartHomeAcController
{
    public class AppConfig
    {
        public string ClientId { get; set; } = "euvgdj83shdfd7vpepst";
        public string ClientSecret { get; set; } = "c73d12861c6245d7bc27cdada45a0806";
        public string BaseUrl { get; set; } = "https://openapi.tuyaus.com";
        public string InfraredId { get; set; } = "eb8db822f09c6f271acwdy";
        public string RemoteId { get; set; } = "eb370cb75d3a287396tzzq";
        public bool IsSwapped { get; set; } = false;

        // WiZ Lamps cached IPs
        public string LampuMejaIp { get; set; } = "";
        public string LampuLedStripIp { get; set; } = "";

        // Last state
        public int LastPower { get; set; } = 1;      // 1 = ON, 0 = OFF
        public int LastTemp { get; set; } = 22;      // 16 - 30
        public int LastMode { get; set; } = 0;      // 0 = Cool, 1 = Heat, 2 = Auto, 3 = Fan, 4 = Dry
        public int LastWind { get; set; } = 0;      // 0 = Auto, 1 = Low, 2 = Med, 3 = High

        private static string GetConfigPath()
        {
            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            string currentDirFile = Path.Combine(Directory.GetCurrentDirectory(), "config.json");

            if (File.Exists(currentDirFile))
            {
                return currentDirFile;
            }
            if (File.Exists(baseDirFile))
            {
                return baseDirFile;
            }

            return currentDirFile;
        }

        private static string? LoadSecretFromEnv()
        {
            try
            {
                // 1. Check direct system environment variable
                string? envVar = Environment.GetEnvironmentVariable("TUYA_CLIENT_SECRET");
                if (!string.IsNullOrWhiteSpace(envVar))
                {
                    return envVar.Trim();
                }

                // 2. Check .env file in current directory or base directory
                string[] possibleEnvPaths = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", ".env")
                };

                foreach (var path in possibleEnvPaths)
                {
                    if (File.Exists(path))
                    {
                        var lines = File.ReadAllLines(path);
                        foreach (var line in lines)
                        {
                            string trimmed = line.Trim();
                            if (trimmed.StartsWith("TUYA_CLIENT_SECRET=", StringComparison.OrdinalIgnoreCase))
                            {
                                return trimmed.Substring("TUYA_CLIENT_SECRET=".Length).Trim().Trim('"', '\'');
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore env loading errors
            }

            return null;
        }

        public static AppConfig Load()
        {
            AppConfig? config = null;
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    config = JsonSerializer.Deserialize<AppConfig>(json);
                }
            }
            catch
            {
                // Fallback to default
            }

            if (config == null)
            {
                config = new AppConfig();
            }

            // Fallback to .env or code default if empty
            if (string.IsNullOrWhiteSpace(config.ClientSecret))
            {
                string? envSecret = LoadSecretFromEnv();
                config.ClientSecret = !string.IsNullOrWhiteSpace(envSecret) 
                    ? envSecret 
                    : "c73d12861c6245d7bc27cdada45a0806";
            }

            config.Save();
            return config;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Silently ignore
            }
        }
    }
}
