using System.Diagnostics;
using System.Reflection;
using Tickbird.Core.Updates;

namespace Tickbird.Desktop.Services;

/// <summary>Shares one GitHub release check between startup and About.</summary>
public static class UpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly object Gate = new();
    private static Task<UpdateCheckResult>? current;

    public static Version InstalledVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static string InstalledVersionText => InstalledVersion.ToString();

    public static Task<UpdateCheckResult> CheckAsync(bool refresh = false)
    {
        lock (Gate)
        {
            if (current == null || (refresh && current.IsCompleted))
                current = new ReleaseChecker(Client).CheckAsync(InstalledVersion);
            return current;
        }
    }

    public static void OpenUrl(string url)
    {
        // Only follow links to the project's own GitHub releases.
        if (!url.StartsWith(ReleaseChecker.ReleasesUrl, StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://github.com/sadaqatbukhari/Tickbird", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no default browser available */ }
    }
}
