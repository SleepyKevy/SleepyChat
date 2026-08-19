using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal sealed partial class HostedKickService : IDisposable
{
    private bool HasCredential()
    {
        lock (gate)
            return HasCredentialLocked();
    }

    private bool HasCredentialLocked() => connectionID.Length > 0 && connectionToken.Length > 0;

    private void ApplyPublicConnectionLocked(JsonElement connection)
    {
        state.KickUserID = String(connection, "kick_user_id");
        state.KickUsername = String(connection, "kick_username").Trim().ToLowerInvariant();
        state.Scopes = [];
        if (connection.TryGetProperty("scopes", out var scopes))
        {
            if (scopes.ValueKind == JsonValueKind.Array)
            {
                foreach (var scope in scopes.EnumerateArray())
                {
                    var value = scope.GetString()?.Trim() ?? "";
                    if (value.Length > 0)
                        state.Scopes.Add(value);
                }
            }
            else if (scopes.ValueKind == JsonValueKind.String)
            {
                var raw = scopes.GetString() ?? "";
                foreach (var value in raw.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (value.Length > 0)
                        state.Scopes.Add(value);
                }
            }
        }
        state.Scopes = state.Scopes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.CanSendChat = state.Scopes.Any(
            x => x.Equals("chat:write", StringComparison.OrdinalIgnoreCase));
    }

    private void LoadCredential()
    {
        if (!File.Exists(credentialPath))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(credentialPath));
            var encoded = String(doc.RootElement, "protected_data");
            if (encoded.Length == 0)
                return;
            var raw = AppUtil.UnprotectCredential(Convert.FromBase64String(encoded));
            using var secret = JsonDocument.Parse(raw);
            connectionID = String(secret.RootElement, "connection_id");
            connectionToken = String(secret.RootElement, "connection_token");
        }
        catch
        {
            connectionID = connectionToken = "";
        }
    }

    private async Task SaveCredentialAsync()
    {
        string id;
        string token;
        lock (gate)
        {
            id = connectionID;
            token = connectionToken;
        }
        if (id.Length == 0 || token.Length == 0)
            return;

        var raw = JsonSerializer.SerializeToUtf8Bytes(new { connection_id = id, connection_token = token }, AppUtil.Json);
        var protectedData = AppUtil.ProtectCredential(raw);
        await AppUtil.AtomicWriteJsonAsync(
            credentialPath,
            new { version = 2, protected_data = Convert.ToBase64String(protectedData) });
    }

    private void ClearLocalCredential()
    {
        StopDeliveryLoops();
        lock (gate)
        {
            connectionID = connectionToken = pendingSessionID = pendingPollToken = "";
            pendingExpiresAt = default;
            state = new HostedKickState
            {
                Status = "disconnected",
                CredentialStorage = OperatingSystem.IsWindows()
                    ? "Windows encrypted SleepyChat connection"
                    : "local SleepyChat connection"
            };
        }
        lock (eventGate)
        {
            recentEvents.Clear();
            nextSequence = 0;
        }
        lock (processedMessageIds)
            processedMessageIds.Clear();
        try { File.Delete(credentialPath); } catch { }
    }

    private void SetDisconnectedState()
    {
        lock (gate)
        {
            state.Connected = false;
            state.CanSendChat = false;
            state.Status = "disconnected";
            state.OAuthPending = false;
            state.RealtimeStatus = "disconnected";
            state.EventsStatus = "inactive";
            state.EventsReady = false;
        }
    }

    private void ClearPending(string error)
    {
        lock (gate)
        {
            pendingSessionID = pendingPollToken = "";
            pendingExpiresAt = default;
            state.OAuthPending = false;
            state.Status = state.Connected ? "connected" : "disconnected";
            state.LastError = error;
        }
    }

    private void SetError(string error)
    {
        lock (gate)
        {
            state.LastError = (error ?? "").Trim();
            if (!state.Connected)
                state.Status = "degraded";
        }
    }

}
