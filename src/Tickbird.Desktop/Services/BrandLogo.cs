using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tickbird.Desktop.Services;

/// <summary>Displays the Tickbird logo (transparent, tightly cropped PNG).</summary>
public sealed class BrandLogo : Border
{
    public BrandLogo()
    {
        Width = 220;
        Height = 38;
        Background = new ImageBrush(Icons.Get("BrandLogo.png"))
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Left
        };
        IsHitTestVisible = false;
    }
}
