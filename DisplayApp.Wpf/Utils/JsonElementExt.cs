using System.Text.Json;

namespace DisplayApp.Wpf;

internal static class JsonElementExt
{
    public static JsonElement? GetPropertyOrNull(this JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var val))
            return val;
        return null;
    }
}