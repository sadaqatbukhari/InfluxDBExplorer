using Tickbird.Core.Settings;

namespace Tickbird.Tests;

public class PlatformPathsTests
{
    [Fact]
    public void Windows_UsesTickbirdDirectory()
    {
        var dir = PlatformPaths.GetSettingsDirectory(DesktopPlatform.Windows, "home", appData: "roam");
        Assert.Equal(Path.Combine("roam", "Tickbird"), dir);
    }

    [Fact]
    public void MacOS_UsesApplicationSupport() =>
        Assert.Equal(Path.Combine("h", "Library", "Application Support", "Tickbird"),
            PlatformPaths.GetSettingsDirectory(DesktopPlatform.MacOS, "h"));

    [Fact]
    public void Linux_HonoursXdgConfigHome() =>
        Assert.Equal(Path.Combine("xdg", "tickbird"),
            PlatformPaths.GetSettingsDirectory(DesktopPlatform.Linux, "h", xdgConfigHome: "xdg"));

    [Fact]
    public void Linux_FallsBackToDotConfig() =>
        Assert.Equal(Path.Combine("h", ".config", "tickbird"),
            PlatformPaths.GetSettingsDirectory(DesktopPlatform.Linux, "h", xdgConfigHome: " "));

    [Fact]
    public void MissingHome_Throws() =>
        Assert.Throws<ArgumentException>(() => PlatformPaths.GetSettingsDirectory(DesktopPlatform.Linux, ""));

    [Fact]
    public void PreviousDirectories_AreTheOldInfluxDbStudioFolders()
    {
        Assert.Equal(Path.Combine("roam", "InfluxDB Studio Preview"),
            PlatformPaths.GetPreviousSettingsDirectory(DesktopPlatform.Windows, "h", appData: "roam"));
        Assert.Equal(Path.Combine("h", "Library", "Application Support", "InfluxDB Studio"),
            PlatformPaths.GetPreviousSettingsDirectory(DesktopPlatform.MacOS, "h"));
        Assert.Equal(Path.Combine("xdg", "influxdb-studio"),
            PlatformPaths.GetPreviousSettingsDirectory(DesktopPlatform.Linux, "h", xdgConfigHome: "xdg"));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tickbird-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Migration_CopiesSettingsAndHistory_AndKeepsTheOldFolder()
    {
        var root = TempDirectory();
        var old = Path.Combine(root, "old");
        var now = Path.Combine(root, "new");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "settings.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(old, "query-history.json"), "[]");

        Assert.True(PlatformPaths.MigratePreviousSettings(old, now));

        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(now, "settings.json")));
        Assert.True(File.Exists(Path.Combine(now, "query-history.json")));
        Assert.True(File.Exists(Path.Combine(old, "settings.json")));
    }

    [Fact]
    public void Migration_NeverOverwritesExistingTickbirdSettings()
    {
        var root = TempDirectory();
        var old = Path.Combine(root, "old");
        var now = Path.Combine(root, "new");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(now);
        File.WriteAllText(Path.Combine(old, "settings.json"), "old");
        File.WriteAllText(Path.Combine(now, "settings.json"), "new");

        Assert.False(PlatformPaths.MigratePreviousSettings(old, now));
        Assert.Equal("new", File.ReadAllText(Path.Combine(now, "settings.json")));
    }

    [Fact]
    public void Migration_DoesNothingWithoutOldSettings() =>
        Assert.False(PlatformPaths.MigratePreviousSettings(Path.Combine(TempDirectory(), "none"), Path.Combine(TempDirectory(), "new")));
}
