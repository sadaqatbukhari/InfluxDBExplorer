using Tickbird.Core.Data;
using Newtonsoft.Json;

namespace Tickbird.Core.Settings;

/// <summary>Versioned JSON settings with atomic writes and a last-known-good backup.</summary>
public sealed class SettingsStore
{
    private readonly string path;

    public string FilePath => path;

    public SettingsStore(string path) => this.path = path;

    public string BackupPath => path + ".bak";

    public TickbirdSettings Load()
    {
        foreach (var candidate in new[] { path, BackupPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var loaded = JsonConvert.DeserializeObject<TickbirdSettings>(File.ReadAllText(candidate));
                if (loaded != null) return loaded;
            }
            catch (JsonException) { }
        }
        return new TickbirdSettings();
    }

    public void Save(TickbirdSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(settings, Formatting.Indented));
        if (File.Exists(path))
        {
            File.Copy(path, BackupPath, overwrite: true);
            File.Move(temp, path, overwrite: true);
        }
        else File.Move(temp, path);
    }
}
