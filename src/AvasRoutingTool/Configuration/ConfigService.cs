using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch
            {
                return new AppConfig();
            }

            try
            {
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch
            {
                return LoadSkippingCorruptCameras(json);
            }
        }

        /// <summary>
        /// Fallback when the whole file fails to deserialise: keeps every non-camera setting and
        /// every valid RoomCameras entry (in order), dropping only the entries that fail to parse.
        /// Returns defaults only when the root itself is not a readable settings object.
        /// </summary>
        private static AppConfig LoadSkippingCorruptCameras(string json)
        {
            try
            {
                if (JsonNode.Parse(json) is not JsonObject root)
                {
                    return new AppConfig();
                }

                var cameraNodes = root["RoomCameras"] as JsonArray;
                root.Remove("RoomCameras");

                var config = root.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
                config.RoomCameras = new List<RoomCameraConfig>();

                if (cameraNodes != null)
                {
                    foreach (JsonNode? node in cameraNodes)
                    {
                        try
                        {
                            var camera = node?.Deserialize<RoomCameraConfig>(JsonOptions);
                            if (camera != null)
                            {
                                config.RoomCameras.Add(camera);
                            }
                        }
                        catch
                        {
                            // Skip only this corrupt camera entry.
                        }
                    }
                }

                return config;
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
