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

}
