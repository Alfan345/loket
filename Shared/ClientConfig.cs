using System;
using System.IO;
using System.Text.Json;

namespace Shared
{
    public class ClientConfig
    {
        public string ServerUrl { get; set; } = "http://localhost:5000";

        public static string ConfigDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "QueueSystem");

        public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

        public static ClientConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<ClientConfig>(json);
                    if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ServerUrl))
                        return cfg;
                }
            }
            catch { }
            return new ClientConfig();
        }

        public static void Save(ClientConfig cfg)
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        public static string LoadServerUrl() => Load().ServerUrl.TrimEnd('/');
        public static void SaveServerUrl(string url) => Save(new ClientConfig { ServerUrl = url.TrimEnd('/') });
    }
}