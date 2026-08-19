using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal sealed partial class HostedKickService : IDisposable
{
    private static Exception ApiException(byte[] bytes, HttpStatusCode code, string fallback)
    {
        string message = "";
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            message = String(doc.RootElement, "kick_message");
            if (message.Length == 0)
                message = String(doc.RootElement, "error").Replace('_', ' ');
        }
        catch { }
        if (message.Length == 0)
            message = fallback + " (HTTP " + (int)code + ")";
        return code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new UnauthorizedAccessException(message)
            : new InvalidOperationException(message);
    }

    private static string String(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
            ? (value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : value.ToString().Trim())
            : "";

    private static long Int64(JsonElement root, string name, long fallback) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
        && (value.TryGetInt64(out var number) || long.TryParse(value.ToString(), out number))
            ? number
            : fallback;

    private static bool Bool(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
        && (value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));

    private static HostedKickState Clone(HostedKickState value) =>
        JsonSerializer.Deserialize<HostedKickState>(JsonSerializer.Serialize(value, AppUtil.Json), AppUtil.Json)!;

    public void Dispose()
    {
        try { lifetime.Cancel(); } catch { }
        StopDeliveryLoops();
        lifetime.Dispose();
        http.Dispose();
    }
}
