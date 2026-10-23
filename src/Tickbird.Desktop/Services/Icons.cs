using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Tickbird.Desktop.Services;

/// <summary>Toolbar/tree/tab icons carried over from the WinForms app (Assets/*.png).</summary>
public static class Icons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            var fileName = Path.HasExtension(name) ? name : name + ".png";
            var uri = new Uri($"avares://Tickbird.Desktop/Assets/{fileName}");
            if (AssetLoader.Exists(uri)) bitmap = new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception) { /* a missing icon must never break the UI */ }
        Cache[name] = bitmap;
        return bitmap;
    }
}
