using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SleepyChat;

internal sealed class BackendHost : IDisposable
{
    public const int Port = 17892;
    public const string BaseUrl = "http://127.0.0.1:17892/";

    private const int MaxJsonBytes = 4 << 20;
    private const int MaxBadgeBytes = 2 << 20;

    private WebApplication? app;
    private readonly HttpClient http;
    private readonly HttpClient noRedirectHttp;
    private readonly object cacheLock = new();
    private readonly Dictionary<string, JsonCacheEntry> jsonCache = new(StringComparer.Ordinal);

    private sealed record JsonCacheEntry(byte[] Body, DateTimeOffset ExpiresAt);

    public BackendHost()
    {
        http = CreateHttpClient(allowRedirects: true);
        noRedirectHttp = CreateHttpClient(allowRedirects: false);
    }

    private static HttpClient CreateHttpClient(bool allowRedirects)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowRedirects,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SleepyChat", AppUtil.Version));
        return client;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (app is not null)
            return;

        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(BackendHost).Assembly.GetName().Name
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.UseUrls(BaseUrl.TrimEnd('/'));
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4L << 20);

        var web = builder.Build();
        ConfigureMiddleware(web);
        MapStatic(web);
        MapApi(web);
        app = web;

        try
        {
            await web.StartAsync(ct);
        }
        catch
        {
            app = null;
            await web.DisposeAsync();
            throw;
        }
    }

    public void Stop()
    {
        var running = app;
        app = null;
        if (running is null)
            return;

        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            running.StopAsync(stopCts.Token).GetAwaiter().GetResult();
        }
        catch { }

        try { running.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { }
    }

    private static void ConfigureMiddleware(WebApplication web)
    {
        web.Use(async (ctx, next) =>
        {
            if (!RequestAllowed(ctx.Request))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("local SleepyChat request required");
                return;
            }

            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                await next();
            }
            catch (Exception ex) when (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = ex is InvalidDataException ? 400 : 500;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.WriteAsync(ex is InvalidDataException ? ex.Message : "SleepyChat request failed");
            }
        });
    }

    private static bool RequestAllowed(HttpRequest request)
    {
        var host = request.Host.Host.Trim('[', ']').ToLowerInvariant();
        if (host is not ("127.0.0.1" or "localhost" or "::1"))
            return false;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
            return true;

        var origin = request.Headers.Origin.ToString().Trim();
        if (origin.Length == 0)
            return true;
        if (origin == "null")
            return false;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme == "http"
            && uri.Port == Port
            && (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "::1");
    }

    private static void MapStatic(WebApplication web)
    {
        web.MapMethods("/", ["GET", "HEAD"], (HttpContext ctx) =>
            FileResult(Path.Combine(AppContext.BaseDirectory, "web", "index.html"), ctx, "text/html; charset=utf-8"));

        web.MapMethods("/manifest.webmanifest", ["GET", "HEAD"], (HttpContext ctx) =>
            FileResult(Path.Combine(AppContext.BaseDirectory, "web", "manifest.webmanifest"), ctx, "application/json; charset=utf-8"));

        web.MapMethods("/favicon.ico", ["GET", "HEAD"], (HttpContext ctx) =>
            FileResult(Path.Combine(AppContext.BaseDirectory, "web", "favicon.ico"), ctx, "image/x-icon"));
        web.MapMethods("/favicon-64.png", ["GET", "HEAD"], (HttpContext ctx) =>
            FileResult(Path.Combine(AppContext.BaseDirectory, "web", "favicon-64.png"), ctx, "image/png"));
        web.MapMethods("/favicon-192.png", ["GET", "HEAD"], (HttpContext ctx) =>
            FileResult(Path.Combine(AppContext.BaseDirectory, "web", "favicon-192.png"), ctx, "image/png"));

        web.MapMethods("/assets/{**name}", ["GET", "HEAD"], (HttpContext ctx, string name) =>
        {
            var safe = name.Replace('/', Path.DirectorySeparatorChar);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "assets"));
            var path = Path.GetFullPath(Path.Combine(root, safe));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();
            return FileResult(path, ctx);
        });
    }

    private static IResult FileResult(string path, HttpContext ctx, string? type = null)
    {
        if (!File.Exists(path))
            return Results.NotFound();
        if (HttpMethods.IsHead(ctx.Request.Method))
        {
            ctx.Response.ContentType = type ?? AppUtil.ContentTypeForPath(path);
            return Results.NoContent();
        }
        return Results.File(path, type ?? AppUtil.ContentTypeForPath(path), enableRangeProcessing: false);
    }

    private void MapApi(WebApplication web)
    {
        web.MapGet("/api/status", () => Results.Json(new
        {
            ok = true,
            name = "SleepyChat",
            version = AppUtil.Version,
            platform = "windows",
            host = "csharp-webview2"
        }, AppUtil.Json));

        web.MapPost("/api/ping", () => Results.NoContent());

        // The C# native host owns X / Alt+F4 shutdown. Keep this route for UI
        // compatibility with the final 1.0.0 web bundle without making reloads exit.
        web.MapPost("/api/window-closing", () => Results.NoContent());

        web.MapPost("/api/open-data", () =>
        {
            Directory.CreateDirectory(AppUtil.DataDir);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", AppUtil.DataDir) { UseShellExecute = true });
            }
            catch { }
            return Results.NoContent();
        });

        web.MapGet("/api/kick-config", () =>
        {
            var auth = ConfiguredIntegrationUrl(IntegrationConfig.KickAuthUrl, "SLEEPYCHAT_KICK_AUTH_URL");
            var relay = ConfiguredIntegrationUrl(IntegrationConfig.KickRelayUrl, "SLEEPYCHAT_KICK_RELAY_URL");

            var authOk = auth.Length == 0
                || auth.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || auth.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase);
            var relayOk = relay.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
                || relay.StartsWith("ws://127.0.0.1:", StringComparison.OrdinalIgnoreCase);

            if (!authOk) auth = "";
            if (!relayOk) relay = "";

            return Results.Json(new
            {
                available = relay.Length > 0,
                auth_url = auth,
                relay_url = relay
            }, AppUtil.Json);
        });

        web.MapGet("/api/7tv", SevenTvAsync);
        web.MapGet("/api/twitch-badges", TwitchBadgesAsync);
        web.MapGet("/api/kick-badges", KickBadgesAsync);
        web.MapMethods("/api/kick-badge-image", ["GET", "HEAD"], KickBadgeImageAsync);
    }

    private async Task<IResult> SevenTvAsync(HttpRequest request, CancellationToken ct)
    {
        var platform = request.Query["platform"].ToString().Trim().ToLowerInvariant();
        var id = request.Query["id"].ToString().Trim();
        var global = request.Query["global"].ToString() == "1";

        if (!global)
        {
            if (platform is not ("twitch" or "kick"))
                return Results.BadRequest("unsupported platform");
            if (!ulong.TryParse(id, out _))
                return Results.BadRequest("invalid id");
        }

        string[] upstreams = global
            ? ["https://7tv.io/v3/emote-sets/global", "https://api.7tv.app/v3/emote-sets/global"]
            : [
                $"https://7tv.io/v3/users/{platform.ToUpperInvariant()}/{id}",
                $"https://7tv.io/v3/users/{platform}/{id}",
                $"https://api.7tv.app/v3/users/{platform}/{id}"
              ];

        foreach (var upstream in upstreams)
        {
            try
            {
                using var response = await http.GetAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                    continue;
                var body = await ReadAtMostAsync(response.Content, MaxJsonBytes, ct);
                if (body.Length == 0 || !JsonLooksValid(body))
                    continue;
                return Results.Bytes(body, "application/json; charset=utf-8");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
        }

        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private async Task<IResult> TwitchBadgesAsync(HttpRequest request, HttpResponse response, CancellationToken ct)
    {
        var roomId = request.Query["room_id"].ToString().Trim();
        if (roomId.Length > 0 && !ulong.TryParse(roomId, out _))
            return Results.BadRequest("invalid room_id");

        byte[]? globalBody = null;
        byte[]? channelBody = null;
        Exception? globalError = null;
        Exception? channelError = null;

        try { globalBody = await FetchCachedJsonAsync("https://api.ivr.fi/v2/twitch/badges/global", TimeSpan.FromHours(6), ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException) { globalError = ex; }

        if (roomId.Length > 0)
        {
            try
            {
                channelBody = await FetchCachedJsonAsync(
                    "https://api.ivr.fi/v2/twitch/badges/channel?id=" + Uri.EscapeDataString(roomId),
                    TimeSpan.FromMinutes(15), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException) { channelError = ex; }
        }

        if (globalError is not null && (roomId.Length == 0 || channelError is not null))
            return Results.StatusCode(StatusCodes.Status502BadGateway);

        object? global = ParseJsonClone(globalBody);
        object? channel = ParseJsonClone(channelBody);
        response.Headers["Cache-Control"] = "public, max-age=300";
        return Results.Json(new { global, channel }, AppUtil.Json);
    }

    private async Task<IResult> KickBadgesAsync(HttpRequest request, HttpResponse response, CancellationToken ct)
    {
        var slug = request.Query["slug"].ToString().Trim().ToLowerInvariant();
        if (!ValidKickSlug(slug))
            return Results.BadRequest("invalid slug");

        byte[] body;
        try
        {
            body = await FetchCachedJsonAsync("https://kick.com/api/v2/channels/" + slug, TimeSpan.FromMinutes(10), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("subscriber_badges", out var badges))
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            response.Headers["Cache-Control"] = "public, max-age=300";
            return Results.Json(new { subscriber_badges = badges.Clone() }, AppUtil.Json);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private async Task<IResult> KickBadgeImageAsync(HttpRequest request, HttpResponse response, CancellationToken ct)
    {
        if (HttpMethods.IsHead(request.Method))
            return Results.NoContent();

        var rawUrl = request.Query["url"].ToString().Trim();
        if (rawUrl.Length > 0)
        {
            var proxied = await TryProxyBadgeImageAsync(rawUrl, ["files.kick.com"], ct);
            if (proxied is null)
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Bytes(proxied.Value.Body, proxied.Value.ContentType);
        }

        var role = NormalizeKickBadgeType(request.Query["role"].ToString());
        _ = int.TryParse(request.Query["count"].ToString(), out var count);
        var file = KickRoleBadgeFile(role, count);
        if (file is null)
            return Results.NotFound();

        (string Url, string Host)[] mirrors =
        [
            ($"https://www.kickdatabase.com/kickBadges/{file}", "www.kickdatabase.com"),
            ($"https://cpwemotes.co.uk/kick/kickBadges/{file}", "cpwemotes.co.uk")
        ];

        foreach (var mirror in mirrors)
        {
            var proxied = await TryProxyBadgeImageAsync(mirror.Url, [mirror.Host], ct);
            if (proxied is null)
                continue;
            response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Bytes(proxied.Value.Body, proxied.Value.ContentType);
        }

        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private async Task<(byte[] Body, string ContentType)?> TryProxyBadgeImageAsync(
        string endpoint,
        IReadOnlyCollection<string> allowedHosts,
        CancellationToken ct)
    {
        if (!BadgeImageUrlAllowed(endpoint, allowedHosts, out var current))
            return null;

        for (var redirectCount = 0; redirectCount <= 3; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
            using var response = await noRedirectHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirectCount == 3 || response.Headers.Location is null)
                    return null;
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);
                if (!BadgeImageUrlAllowed(next.ToString(), allowedHosts, out current))
                    return null;
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType?.Trim().ToLowerInvariant() ?? "";
            if (!contentType.StartsWith("image/", StringComparison.Ordinal))
                return null;

            var body = await ReadAtMostAsync(response.Content, MaxBadgeBytes, ct);
            if (body.Length == 0)
                return null;
            return (body, contentType);
        }

        return null;
    }

    private async Task<byte[]> FetchCachedJsonAsync(string url, TimeSpan ttl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        lock (cacheLock)
        {
            if (jsonCache.TryGetValue(url, out var cached) && cached.ExpiresAt > now)
                return cached.Body.ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"upstream returned {(int)response.StatusCode}");

        var body = await ReadAtMostAsync(response.Content, MaxJsonBytes, ct);
        if (body.Length == 0 || !JsonLooksValid(body))
            throw new InvalidDataException("upstream returned invalid JSON");

        lock (cacheLock)
            jsonCache[url] = new JsonCacheEntry(body.ToArray(), now.Add(ttl));
        return body;
    }

    private static async Task<byte[]> ReadAtMostAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
            throw new InvalidDataException("upstream response too large");

        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
                break;
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException("upstream response too large");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static bool JsonLooksValid(byte[] body)
    {
        try
        {
            using var _ = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? ParseJsonClone(byte[]? body)
    {
        if (body is null || body.Length == 0)
            return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string ConfiguredIntegrationUrl(string buildValue, string envName)
    {
        var env = Environment.GetEnvironmentVariable(envName)?.Trim();
        return string.IsNullOrWhiteSpace(env) ? buildValue.Trim() : env;
    }

    private static bool ValidKickSlug(string slug)
    {
        if (slug.Length is 0 or > 80)
            return false;
        foreach (var ch in slug)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                continue;
            return false;
        }
        return true;
    }

    private static string NormalizeKickBadgeType(string raw)
    {
        var value = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_').Replace('.', '_');
        return value switch
        {
            "broadcaster" or "channel_host" or "channel_owner" or "owner" or "host" => "broadcaster",
            "moderator" or "mod" => "moderator",
            "vip" => "vip",
            "og" or "original" or "original_gangster" => "og",
            "founder" or "founding_subscriber" or "founding_sub" => "founder",
            "subscriber" or "sub" or "subscription" => "subscriber",
            "sub_gifter" or "subgifter" or "gifter" => "sub_gifter",
            "verified" or "verification" => "verified",
            "staff" or "kick_staff" or "admin" => "staff",
            "sidekick" => "sidekick",
            _ => value
        };
    }

    private static string? KickRoleBadgeFile(string role, int count)
    {
        if (role == "sub_gifter")
        {
            if (count >= 200) return "subGifter200.svg";
            if (count >= 100) return "subGifter100.svg";
            if (count >= 50) return "subGifter50.svg";
            if (count >= 25) return "subGifter25.svg";
            return "subGifter.svg";
        }

        return role switch
        {
            "broadcaster" => "broadcaster.svg",
            "moderator" => "moderator.svg",
            "vip" => "vip.svg",
            "og" => "og.svg",
            "founder" => "founder.svg",
            "subscriber" => "subscriber.svg",
            "verified" => "verified.svg",
            "staff" => "staff.svg",
            "sidekick" => "sidekick.svg",
            _ => null
        };
    }

    private static bool BadgeImageUrlAllowed(
        string raw,
        IReadOnlyCollection<string> allowedHosts,
        out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        var host = parsed.Host.TrimEnd('.');
        if (!allowedHosts.Any(allowed => host.Equals(allowed.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase)))
            return false;

        uri = parsed;
        return true;
    }

    public void Dispose()
    {
        Stop();
        http.Dispose();
        noRedirectHttp.Dispose();
    }
}
