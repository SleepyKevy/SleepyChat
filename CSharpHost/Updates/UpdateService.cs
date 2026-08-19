using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SleepyChat;

internal sealed class UpdateService : IDisposable
{
    public const string RepositoryURL = "https://github.com/SleepyKevy/SleepyChat";
    public const string LatestReleaseURL = RepositoryURL + "/releases/latest";
    private const string API = "https://api.github.com/repos/SleepyKevy/SleepyChat/releases/latest";

    private static readonly Regex Numbers = new(@"\d+", RegexOptions.Compiled);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(7) };

    public UpdateService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SleepyChat/1.0.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<object> CheckAsync(CancellationToken ct)
    {
        var checkedAt = DateTime.UtcNow.ToString("O");
        try
        {
            using var response = await http.GetAsync(API, ct);
            if (!response.IsSuccessStatusCode)
            {
                var noRelease = response.StatusCode == HttpStatusCode.NotFound;
                return new
                {
                    status = noRelease ? "no_release" : "error",
                    current_version = AppUtil.Version,
                    checked_at = checkedAt,
                    update_available = false,
                    repository_url = RepositoryURL,
                    release_url = LatestReleaseURL,
                    message = noRelease
                        ? "No published SleepyChat release was found on GitHub."
                        : "Unable to check for SleepyChat updates."
                };
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var release = document.RootElement;
            var tag = Get(release, "tag_name");
            var name = Get(release, "name");
            var latest = (tag.Length > 0 ? tag : name).Trim().TrimStart('v', 'V');
            if (latest.Length == 0)
                throw new InvalidDataException("GitHub release version is missing.");

            var comparison = Compare(AppUtil.Version, latest);
            var notes = Get(release, "body").Replace("\r\n", "\n").Trim();
            if (notes.Length > 12000)
                notes = notes[..12000] + "\n\n…View the full release notes on GitHub.";

            var releaseUrl = Get(release, "html_url");
            if (!SafeRepositoryUrl(releaseUrl))
                releaseUrl = LatestReleaseURL;

            var status = comparison < 0 ? "available" : comparison > 0 ? "ahead" : "up_to_date";
            return new
            {
                status,
                current_version = AppUtil.Version,
                latest_version = latest,
                release_name = name,
                release_url = releaseUrl,
                repository_url = RepositoryURL,
                release_notes = notes,
                published_at = Get(release, "published_at"),
                checked_at = checkedAt,
                update_available = comparison < 0,
                message = comparison < 0
                    ? $"SleepyChat {latest} is available."
                    : comparison > 0
                        ? "This SleepyChat build is newer than the latest published release."
                        : "SleepyChat is up to date."
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error(checkedAt);
        }
        catch (HttpRequestException)
        {
            return Error(checkedAt);
        }
        catch (JsonException)
        {
            return Error(checkedAt);
        }
        catch (InvalidDataException)
        {
            return Error(checkedAt);
        }
    }

    private static object Error(string checkedAt) => new
    {
        status = "error",
        current_version = AppUtil.Version,
        checked_at = checkedAt,
        update_available = false,
        repository_url = RepositoryURL,
        release_url = LatestReleaseURL,
        message = "Unable to check for SleepyChat updates."
    };

    private static string Get(JsonElement release, string property) =>
        release.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int Compare(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        if (a.Count == 0 || b.Count == 0)
            throw new InvalidDataException("Invalid release version.");

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    private static List<int> Parts(string value) =>
        Numbers.Matches(value).Select(match => int.Parse(match.Value)).ToList();

    private static bool SafeRepositoryUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.AbsolutePath.StartsWith("/SleepyKevy/SleepyChat", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => http.Dispose();
}
