using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal static class AppUtil
{
    public const string Version = "1.0.0";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "SleepyChat_Data");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string RuntimeDataDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ContentTypeForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" or ".webmanifest" => "application/json; charset=utf-8",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };
}
