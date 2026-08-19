using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal sealed class HostedKickState
{
    public string ApiBase { get; set; } = HostedKickService.ApiBase;
    public string Status { get; set; } = "disconnected";
    public bool Connected { get; set; }
    [JsonPropertyName("oauth_pending")]
    public bool OAuthPending { get; set; }
    public string KickUserID { get; set; } = "";
    public string KickUsername { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    [JsonPropertyName("can_send_chat")]
    public bool CanSendChat { get; set; }
    public string EventsStatus { get; set; } = "inactive";
    public bool EventsReady { get; set; }
    public string RealtimeStatus { get; set; } = "disconnected";
    public int RealtimeClients { get; set; }
    public bool FallbackQueue { get; set; } = true;
    public string LastError { get; set; } = "";
    public long LastEventAt { get; set; }
    public long DeliveredEvents { get; set; }
    public bool CredentialStored { get; set; }
    public string CredentialStorage { get; set; } = "";
}

internal sealed record HostedChatEvent(long Sequence, string EventType, JsonElement Payload);
