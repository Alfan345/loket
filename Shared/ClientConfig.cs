using System;
using System.IO;
using System.Text.Json;

namespace Shared
{
    public static class ClientConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "QueueSystem");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static string LoadServerUrl()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile).Trim();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var obj = System.Text.Json.JsonDocument.Parse(json);
                        if (obj.RootElement.TryGetProperty("ServerUrl", out var v))
                        {
                            var raw = v.GetString() ?? "";
                            return Sanitize(raw);
                        }
                    }
                }
            }
            catch { }
            return "http://localhost:5000";
        }

        public static void SaveServerUrl(string url)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var sanitized = Sanitize(url);
                File.WriteAllText(ConfigFile, "{\n  \"ServerUrl\": \"" + sanitized + "\"\n}");
            }
            catch { }
        }

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "http://localhost:5000";

            raw = raw.Trim();
            // Jika user hanya isi IP:PORT atau hostname:PORT
            if (!raw.Contains("://"))
                raw = "http://" + raw;

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "http://localhost:5000";

            // Hilangkan trailing slash
            return uri.ToString().TrimEnd('/');
        }
    }
}