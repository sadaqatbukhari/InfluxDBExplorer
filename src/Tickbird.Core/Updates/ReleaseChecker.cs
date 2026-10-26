using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tickbird.Core.Updates;

public sealed record UpdateCheckResult(bool UpdateAvailable, string Message, string? ReleaseUrl = null);

/// <summary>
/// Framework-independent GitHub release check (ported from the WinForms UpdateChecker).
/// </summary>
public sealed class ReleaseChecker
{
    public const string ReleasesUrl = "https://github.com/sadaqatbukhari/Tickbird/releases";
    public const string LatestReleaseApi = "https://api.github.com/repos/sadaqatbukhari/Tickbird/releases/latest";

    private readonly HttpClient client;

    public ReleaseChecker(HttpClient client) => this.client = client;

    public async Task<UpdateCheckResult> CheckAsync(Version installed, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd("Tickbird/" + installed);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(false, "No published release found on GitHub.");
            response.EnsureSuccessStatusCode();
            var release = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true)
                return new(false, "No stable release found on GitHub.");
            var tag = ((string?)release["tag_name"] ?? string.Empty).Trim();
            var versionText = tag.TrimStart('v', 'V').Split('+')[0];
            if (!Version.TryParse(versionText, out var latest))
                return new(false, "Unable to read the latest release version.");
            if (Normalize(latest) <= Normalize(installed))
                return new(false, "You are up to date.");
            return new(true, "Version " + versionText + " is available.",
                ReleasesUrl + "/tag/" + Uri.EscapeDataString(tag));
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException
            || ex is JsonException || ex is ArgumentException)
        {
            return new(false, "Unable to check for updates. Try again later.");
        }
    }

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
