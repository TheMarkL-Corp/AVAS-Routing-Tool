using System;
using System.IO;
using System.Text.Json;

namespace AvasRoutingTool.Configuration
{
    public interface IConfigService
    {
        AppConfig Load();
        void Save(AppConfig config);
    }

    public class ConfigService : IConfigService
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ConfigService(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        }

        public AppConfig Load()
        {
            if (!File.Exists(_filePath))
            {
                var defaults = new AppConfig();
                Save(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(AppConfig config)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_filePath, json);
        }
    }
}
