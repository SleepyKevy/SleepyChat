using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal sealed partial class HostedKickService : IDisposable
{
    private void StartDeliveryLoops()
    {
        if (!HasCredential())
            return;

        lock (gate)
        {
            if (delivery is not null && !delivery.IsCancellationRequested)
                return;
            delivery = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = Task.Run(() => WebSocketLoopAsync(delivery.Token));
            _ = Task.Run(() => PollLoopAsync(delivery.Token));
        }
    }

    private void StopDeliveryLoops()
    {
        CancellationTokenSource? old;
        lock (gate)
        {
            old = delivery;
            delivery = null;
            state.RealtimeStatus = "disconnected";
            state.RealtimeClients = 0;
        }
        try { old?.Cancel(); } catch { }
        try { old?.Dispose(); } catch { }
    }

    private async Task WebSocketLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && HasCredential())
        {
            try
            {
                string id;
                string token;
                lock (gate)
                {
                    id = connectionID;
                    token = connectionToken;
                    state.RealtimeStatus = "connecting";
                }

                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("X-SleepySource-Connection-Id", id);
                ws.Options.SetRequestHeader("X-SleepySource-Connection-Token", token);
                var uri = new Uri(ApiBase.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase) + "/realtime/connect");
                await ws.ConnectAsync(uri, ct);

                lock (gate)
                {
                    state.RealtimeStatus = "connected";
                    state.RealtimeClients = 1;
                    state.LastError = "";
                }

                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var text = await ReceiveTextAsync(ws, ct);
                    if (text is null)
                        break;
                    if (text == "pong")
                        continue;

                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (String(root, "type") == "ready")
                        continue;
                    if (String(root, "type") == "kick_event" && root.TryGetProperty("event", out var ev))
                    {
                        var messageID = String(ev, "message_id");
                        if (ProcessEvent(ev) && messageID.Length > 0)
                            await AckAsync([messageID], ct);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                lock (gate)
                {
                    state.RealtimeStatus = "reconnecting";
                    state.RealtimeClients = 0;
                    state.LastError = "Realtime: " + ex.Message;
                }
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(3000, ct); } catch { }
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && HasCredential())
        {
            try
            {
                using var doc = await PostAuthenticatedAsync(
                    "/kick/events/delivery/poll",
                    new Dictionary<string, object?> { ["limit"] = 10 },
                    ct,
                    allowNonSuccess: true);

                var status = String(doc.RootElement, "status");
                if (status is "disconnected" or "reconnect_required")
                {
                    lock (gate)
                    {
                        state.Connected = false;
                        state.Status = "reconnect_required";
                    }
                    break;
                }

                var ack = new List<string>();
                if (doc.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ev in events.EnumerateArray())
                    {
                        var messageID = String(ev, "message_id");
                        if (ProcessEvent(ev) && messageID.Length > 0)
                            ack.Add(messageID);
                    }
                }
                if (ack.Count > 0)
                    await AckAsync(ack, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                lock (gate)
                    state.LastError = "Fallback delivery: " + ex.Message;
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(2000, ct); } catch { }
            }
        }
    }

    private bool ProcessEvent(JsonElement envelope)
    {
        var messageID = String(envelope, "message_id");
        var type = String(envelope, "event_type");
        if (messageID.Length == 0 || type.Length == 0 || !envelope.TryGetProperty("payload", out var payload))
            return false;

        lock (processedMessageIds)
        {
            if (!processedMessageIds.Add(messageID))
                return true;
            if (processedMessageIds.Count > 4096)
                processedMessageIds.Clear();
        }

        if (!type.Equals("chat.message.sent", StringComparison.OrdinalIgnoreCase))
            return true;

        var clonedPayload = payload.Clone();
        lock (eventGate)
        {
            var seq = ++nextSequence;
            recentEvents.Add(new HostedChatEvent(seq, type, clonedPayload));
            if (recentEvents.Count > MaxQueuedEvents)
                recentEvents.RemoveRange(0, recentEvents.Count - MaxQueuedEvents);
        }

        lock (gate)
        {
            state.LastEventAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            state.DeliveredEvents++;
            state.LastError = "";
        }
        return true;
    }

    private async Task AckAsync(IReadOnlyCollection<string> messageIDs, CancellationToken ct)
    {
        if (messageIDs.Count == 0)
            return;
        using var _ = await PostAuthenticatedAsync(
            "/kick/events/delivery/ack",
            new Dictionary<string, object?> { ["message_ids"] = messageIDs.ToArray() },
            ct,
            allowNonSuccess: true);
    }

    private async Task<JsonDocument> PostAuthenticatedAsync(
        string path,
        IDictionary<string, object?>? extra,
        CancellationToken ct,
        bool allowNonSuccess = false)
    {
        string id;
        string token;
        lock (gate)
        {
            id = connectionID;
            token = connectionToken;
        }
        if (id.Length == 0 || token.Length == 0)
            throw new UnauthorizedAccessException("Connect with Kick first");

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["connection_id"] = id,
            ["connection_token"] = token
        };
        if (extra is not null)
            foreach (var pair in extra)
                body[pair.Key] = pair.Value;

        var payload = JsonSerializer.SerializeToUtf8Bytes(body, AppUtil.Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + path)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.SendAsync(request, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
        }
        catch
        {
            throw new InvalidOperationException("SleepyChat's Kick service returned an invalid response");
        }

        if (!response.IsSuccessStatusCode && !allowNonSuccess)
        {
            var ex = ApiException(bytes, response.StatusCode, "SleepyChat Kick request failed");
            doc.Dispose();
            throw ex;
        }
        return doc;
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > 2L << 20)
                throw new InvalidDataException("Realtime event is too large");
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

}
