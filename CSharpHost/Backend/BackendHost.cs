using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SleepyChat;

internal sealed partial class BackendHost : IDisposable
{
    public const int Port = 17892;
    public const string BaseUrl = "http://127.0.0.1:17892/";

    private const int MaxJsonBytes = 4 << 20;
    private const int MaxBadgeBytes = 2 << 20;

    private WebApplication? app;
    private readonly HttpClient http;
    private readonly HttpClient noRedirectHttp;
    private readonly HostedKickService kick;
    private readonly UpdateService updates;
    private readonly object cacheLock = new();
    private readonly Dictionary<string, JsonCacheEntry> jsonCache = new(StringComparer.Ordinal);

    private sealed record JsonCacheEntry(byte[] Body, DateTimeOffset ExpiresAt);

    public BackendHost()
    {
        http = CreateHttpClient(allowRedirects: true);
        noRedirectHttp = CreateHttpClient(allowRedirects: false);
        kick = new HostedKickService(AppUtil.DataDir);
        updates = new UpdateService();
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
            kick.Start();
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
            StaticFolderResult("assets", name, ctx));
        web.MapMethods("/css/{**name}", ["GET", "HEAD"], (HttpContext ctx, string name) =>
            StaticFolderResult(Path.Combine("web", "css"), name, ctx));
        web.MapMethods("/js/{**name}", ["GET", "HEAD"], (HttpContext ctx, string name) =>
            StaticFolderResult(Path.Combine("web", "js"), name, ctx));
    }

    private static IResult StaticFolderResult(string relativeRoot, string name, HttpContext ctx)
    {
        var safe = name.Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativeRoot));
        var path = Path.GetFullPath(Path.Combine(root, safe));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();
        return FileResult(path, ctx);
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

        web.MapGet("/api/update/check", async (CancellationToken ct) =>
            Results.Json(await updates.CheckAsync(ct), AppUtil.Json));

        web.MapPost("/api/update/open-repository", () =>
        {
            AppUtil.OpenExternal(UpdateService.RepositoryURL);
            return Results.NoContent();
        });

        web.MapGet("/api/kick-config", () => Results.Json(new
        {
            available = true,
            mode = "hosted_oauth",
            api_base = HostedKickService.ApiBase
        }, AppUtil.Json));

        web.MapGet("/api/kick/status", () => Results.Json(kick.State(), AppUtil.Json));

        web.MapPost("/api/kick/oauth/start", async (CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await kick.BeginOAuthAsync(ct), AppUtil.Json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
            {
                return Results.Json(new { error = ex.Message }, AppUtil.Json, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        web.MapPost("/api/kick/oauth/status", async (CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await kick.PollOAuthAsync(ct), AppUtil.Json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
            {
                return Results.Json(new { error = ex.Message }, AppUtil.Json, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        web.MapPost("/api/kick/events/sync", async (CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await kick.SyncEventsAsync(ct), AppUtil.Json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
            {
                return Results.Json(new { error = ex.Message }, AppUtil.Json, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        web.MapPost("/api/kick/disconnect", async (CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await kick.DisconnectAsync(ct), AppUtil.Json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
            {
                return Results.Json(new { error = ex.Message }, AppUtil.Json, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        web.MapGet("/api/kick/events/poll", (HttpRequest request) =>
        {
            var raw = request.Query["after"].ToString().Trim();
            if (raw.Length > 0 && (!long.TryParse(raw, out var parsed) || parsed < 0))
                return Results.BadRequest("invalid after sequence");
            var after = raw.Length == 0 ? 0L : long.Parse(raw);
            var events = kick.EventsAfter(after).Select(x => new
            {
                sequence = x.Sequence,
                event_type = x.EventType,
                payload = x.Payload
            }).ToArray();
            return Results.Json(new
            {
                events,
                latest_sequence = kick.LatestSequence
            }, AppUtil.Json);
        });

        web.MapPost("/api/kick/chat/send", async (HttpRequest request, CancellationToken ct) =>
        {
            try
            {
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                var content = body.RootElement.TryGetProperty("content", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
                var messageId = await kick.SendKickChatAsync(content, ct);
                return Results.Json(new { status = "sent", message_id = messageId }, AppUtil.Json);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "invalid request" }, AppUtil.Json, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
            {
                return Results.Json(new { error = ex.Message }, AppUtil.Json, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        web.MapGet("/api/7tv", SevenTvAsync);
        web.MapGet("/api/twitch-badges", TwitchBadgesAsync);
        web.MapGet("/api/kick-badges", KickBadgesAsync);
        web.MapMethods("/api/kick-badge-image", ["GET", "HEAD"], KickBadgeImageAsync);
    }


    public void Dispose()
    {
        Stop();
        kick.Dispose();
        updates.Dispose();
        http.Dispose();
        noRedirectHttp.Dispose();
    }
}
