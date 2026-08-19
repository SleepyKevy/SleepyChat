using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SleepyChat;

internal sealed partial class HostedKickService : IDisposable
{
    public const string ApiBase = "https://sleepysource-api.sleepyservices.workers.dev";
    private const string ConnectionFile = "kick_connection.json";
    private const int MaxQueuedEvents = 500;

    private readonly object gate = new();
    private readonly object eventGate = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string credentialPath;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<HostedChatEvent> recentEvents = [];
    private readonly HashSet<string> processedMessageIds = new(StringComparer.Ordinal);
    private CancellationTokenSource? delivery;
    private string connectionID = "";
    private string connectionToken = "";
    private string pendingSessionID = "";
    private string pendingPollToken = "";
    private DateTime pendingExpiresAt;
    private long nextSequence;
    private HostedKickState state = new();

    public HostedKickService(string dataDir)
    {
        credentialPath = Path.Combine(dataDir, ConnectionFile);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SleepyChat/" + AppUtil.Version);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        LoadCredential();
        lock (gate)
        {
            state.CredentialStored = HasCredentialLocked();
            state.CredentialStorage = OperatingSystem.IsWindows()
                ? "Windows encrypted SleepyChat connection"
                : "local SleepyChat connection";
            if (state.CredentialStored)
                state.Status = "checking";
        }
    }

    public HostedKickState State()
    {
        lock (gate)
            return Clone(state);
    }

    public IReadOnlyList<HostedChatEvent> EventsAfter(long sequence)
    {
        lock (eventGate)
            return recentEvents.Where(x => x.Sequence > sequence).ToArray();
    }

    public long LatestSequence
    {
        get
        {
            lock (eventGate)
                return nextSequence;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            if (!HasCredentialLocked())
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (await RefreshConnectionAsync(lifetime.Token))
                {
                    await SyncEventsAsync(lifetime.Token);
                    StartDeliveryLoops();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(ex.Message); }
        });
    }

    public async Task<HostedKickState> BeginOAuthAsync(CancellationToken ct)
    {
        using var response = await http.PostAsync(
            ApiBase + "/oauth/kick/start",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw ApiException(bytes, response.StatusCode, "Could not start Kick authorization");

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var session = String(root, "session_id");
        var poll = String(root, "poll_token");
        var authorize = String(root, "authorize_url");
        var expires = Int64(root, "expires_in", 600);
        if (session.Length == 0 || poll.Length == 0 || authorize.Length == 0)
            throw new InvalidOperationException("SleepyChat's Kick service returned an incomplete authorization session");

        lock (gate)
        {
            pendingSessionID = session;
            pendingPollToken = poll;
            pendingExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(60, expires));
            state.OAuthPending = true;
            state.Status = "authorizing";
            state.LastError = "";
        }

        AppUtil.OpenExternal(authorize);
        return State();
    }

    public async Task<HostedKickState> PollOAuthAsync(CancellationToken ct)
    {
        string session;
        string poll;
        DateTime expires;
        lock (gate)
        {
            session = pendingSessionID;
            poll = pendingPollToken;
            expires = pendingExpiresAt;
        }

        if (session.Length == 0 || poll.Length == 0)
            return State();

        if (expires != default && DateTime.UtcNow > expires)
        {
            ClearPending("Kick authorization expired. Choose Connect with Kick and try again.");
            return State();
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { session_id = session, poll_token = poll }, AppUtil.Json);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync(ApiBase + "/oauth/kick/status", content, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw ApiException(bytes, response.StatusCode, "Could not check Kick authorization");

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var remoteStatus = String(root, "status");

        if (remoteStatus is "pending" or "processing")
        {
            lock (gate)
            {
                state.OAuthPending = true;
                state.Status = remoteStatus == "processing" ? "finishing" : "authorizing";
            }
            return State();
        }

        if (remoteStatus is "failed" or "disconnected")
        {
            var error = String(root, "error");
            ClearPending("Kick authorization was not completed: " + (error.Length > 0 ? error.Replace('_', ' ') : remoteStatus));
            return State();
        }

        if (remoteStatus != "completed" || !root.TryGetProperty("connection", out var connection))
        {
            ClearPending("SleepyChat's Kick service returned an unexpected authorization status");
            return State();
        }

        var id = String(connection, "connection_id");
        if (id.Length == 0)
            throw new InvalidOperationException("SleepyChat's Kick service did not return a connection ID");

        lock (gate)
        {
            connectionID = id;
            connectionToken = poll;
            pendingSessionID = pendingPollToken = "";
            pendingExpiresAt = default;
            state.OAuthPending = false;
            ApplyPublicConnectionLocked(connection);
            state.Status = "connected";
            state.Connected = true;
            state.CredentialStored = true;
            state.LastError = "";
        }

        await SaveCredentialAsync();
        try
        {
            await SyncEventsAsync(ct);
        }
        catch (Exception ex)
        {
            SetError("Kick event sync: " + ex.Message);
        }
        StartDeliveryLoops();
        return State();
    }

    public async Task<bool> RefreshConnectionAsync(CancellationToken ct)
    {
        if (!HasCredential())
        {
            SetDisconnectedState();
            return false;
        }

        using var result = await PostAuthenticatedAsync("/kick/connection/status", null, ct, allowNonSuccess: true);
        var status = String(result.RootElement, "status");
        if (status == "connected")
        {
            if (result.RootElement.TryGetProperty("connection", out var connection))
            {
                lock (gate)
                {
                    ApplyPublicConnectionLocked(connection);
                    state.Connected = true;
                    state.Status = "connected";
                    state.LastError = "";
                }
            }
            return true;
        }

        if (status == "refreshing")
        {
            lock (gate)
            {
                state.Status = "refreshing";
                state.LastError = "";
            }
            return true;
        }

        if (status is "disconnected" or "reconnect_required")
        {
            lock (gate)
            {
                state.Connected = false;
                state.Status = "reconnect_required";
                state.LastError = "Reconnect Kick to continue using Kick chat.";
            }
            StopDeliveryLoops();
            return false;
        }

        lock (gate)
        {
            state.Status = status.Length > 0 ? status : "degraded";
            state.LastError = String(result.RootElement, "error").Replace('_', ' ');
        }
        return false;
    }

    public async Task<HostedKickState> SyncEventsAsync(CancellationToken ct)
    {
        if (!HasCredential())
            return State();

        using var result = await PostAuthenticatedAsync("/kick/events/sync", null, ct, allowNonSuccess: true);
        var status = String(result.RootElement, "status");
        var ready = Bool(result.RootElement, "all_required_subscribed");
        lock (gate)
        {
            state.EventsStatus = status.Length > 0 ? status : (ready ? "subscribed" : "partial");
            state.EventsReady = ready;
            if (!ready)
                state.LastError = String(result.RootElement, "error").Replace('_', ' ');
        }
        return State();
    }

    public async Task<string> SendKickChatAsync(string content, CancellationToken ct)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
            throw new InvalidOperationException("Type a message first.");
        if (content.Length > 500)
            throw new InvalidOperationException("Kick chat messages can be up to 500 characters.");

        using var result = await PostAuthenticatedAsync(
            "/kick/chat/send",
            new Dictionary<string, object?> { ["content"] = content },
            ct,
            allowNonSuccess: true);

        var status = String(result.RootElement, "status");
        if (status == "sent")
            return String(result.RootElement, "message_id");

        var error = String(result.RootElement, "error");
        if (error == "chat_write_scope_missing")
        {
            lock (gate)
            {
                state.CanSendChat = false;
                state.LastError = "Reconnect Kick once to allow SleepyChat to send messages.";
            }
            throw new InvalidOperationException("Reconnect Kick once to allow SleepyChat to send messages.");
        }

        if (status is "disconnected" or "reconnect_required")
        {
            lock (gate)
            {
                state.Connected = false;
                state.Status = "reconnect_required";
                state.CanSendChat = false;
            }
            throw new InvalidOperationException("Reconnect Kick before sending a message.");
        }

        throw new InvalidOperationException(
            error.Length > 0
                ? "Kick could not send the message: " + error.Replace('_', ' ')
                : "Kick could not send the message.");
    }

    public async Task<HostedKickState> DisconnectAsync(CancellationToken ct)
    {
        if (HasCredential())
        {
            using var result = await PostAuthenticatedAsync("/kick/connection/disconnect", null, ct, allowNonSuccess: true);
            var status = String(result.RootElement, "status");
            if (status != "disconnected")
            {
                var error = String(result.RootElement, "error");
                throw new InvalidOperationException(error.Length > 0
                    ? "Kick disconnect failed: " + error.Replace('_', ' ')
                    : "Kick disconnect failed. Try again.");
            }
        }

        ClearLocalCredential();
        return State();
    }

}
