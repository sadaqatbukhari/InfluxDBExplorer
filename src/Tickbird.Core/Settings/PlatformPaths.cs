namespace Tickbird.Core.Settings;

public enum DesktopPlatform { Windows, MacOS, Linux }

/// <summary>
/// Resolves the per-user settings location for each platform. The Windows location is separate from the
/// WinForms app's, so this app never touches that app's settings.
/// </summary>
public static class PlatformPaths
{
    public const string SettingsFileName = "settings.json";

    /// <summary>Files that belong to the user and move with the settings when the folder is renamed.</summary>
    private static readonly string[] MigratedFiles = { SettingsFileName, SettingsFileName + ".bak", "query-history.json" };

    public static string GetSettingsDirectory(DesktopPlatform platform, string home, string? appData = null, string? xdgConfigHome = null)
    {
        if (string.IsNullOrWhiteSpace(home)) throw new ArgumentException("Home directory is required.", nameof(home));
        switch (platform)
        {
            case DesktopPlatform.Windows:
                return Path.Combine(WindowsRoot(home, appData), "Tickbird");
            case DesktopPlatform.MacOS:
                return Path.Combine(home, "Library", "Application Support", "Tickbird");
            default:
                return Path.Combine(LinuxRoot(home, xdgConfigHome), "tickbird");
        }
    }

    /// <summary>The folder this app used before it was renamed to Tickbird, or null if there was none.</summary>
    public static string? GetPreviousSettingsDirectory(DesktopPlatform platform, string home, string? appData = null, string? xdgConfigHome = null)
    {
        if (string.IsNullOrWhiteSpace(home)) throw new ArgumentException("Home directory is required.", nameof(home));
        return platform switch
        {
            DesktopPlatform.Windows => Path.Combine(WindowsRoot(home, appData), "InfluxDB Studio Preview"),
            DesktopPlatform.MacOS => Path.Combine(home, "Library", "Application Support", "InfluxDB Studio"),
            _ => Path.Combine(LinuxRoot(home, xdgConfigHome), "influxdb-studio")
        };
    }

    /// <summary>
    /// Copies the settings, backup and query history from the previous folder into the Tickbird folder, but only
    /// when Tickbird has no settings yet. The previous folder is left in place. Returns true when files were copied.
    /// </summary>
    public static bool MigratePreviousSettings(string previousDirectory, string currentDirectory)
    {
        try
        {
            if (!File.Exists(Path.Combine(previousDirectory, SettingsFileName))) return false;
            if (File.Exists(Path.Combine(currentDirectory, SettingsFileName))) return false;
            Directory.CreateDirectory(currentDirectory);
            foreach (var name in MigratedFiles)
            {
                var source = Path.Combine(previousDirectory, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(currentDirectory, name), overwrite: false);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static DesktopPlatform Current =>
        OperatingSystem.IsWindows() ? DesktopPlatform.Windows :
        OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS : DesktopPlatform.Linux;

    public static string GetCurrentSettingsFile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var directory = GetSettingsDirectory(Current, home, appData, xdg);
        var previous = GetPreviousSettingsDirectory(Current, home, appData, xdg);
        if (previous != null) MigratePreviousSettings(previous, directory);
        return Path.Combine(directory, SettingsFileName);
    }

    private static string WindowsRoot(string home, string? appData) =>
        string.IsNullOrWhiteSpace(appData) ? Path.Combine(home, "AppData", "Roaming") : appData;

    private static string LinuxRoot(string home, string? xdgConfigHome) =>
        string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome;
}
