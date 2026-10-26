using Newtonsoft.Json;

namespace Tickbird.Core.Settings;

/// <summary>
/// Where the Object Explorer pane lives: docked left or right (optionally auto-hidden to an edge tab),
/// floating in its own window, or hidden. Saved with the settings so the layout survives restarts.
/// </summary>
public sealed class PaneLayout
{
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Float = "Float";

    public const double MinPaneWidth = 180;
    public const double MaxPaneWidth = 1200;
    public const double DefaultWidth = 320;
    public const double DefaultFloatWidth = 340;
    public const double DefaultFloatHeight = 600;

    /// <summary>Left, Right or Float.</summary>
    public string Placement { get; set; } = Left;

    /// <summary>The side the pane returns to when a floating pane is docked again.</summary>
    public string DockSide { get; set; } = Left;

    /// <summary>Docked pane collapsed to a tab on the window edge; it slides out when used.</summary>
    public bool AutoHide { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>Docked (and auto-hide) width in device-independent pixels.</summary>
    public double Width { get; set; } = DefaultWidth;

    /// <summary>Floating window position in screen pixels; null lets the app choose.</summary>
    public int? FloatX { get; set; }
    public int? FloatY { get; set; }
    public double FloatWidth { get; set; } = DefaultFloatWidth;
    public double FloatHeight { get; set; } = DefaultFloatHeight;

    [JsonIgnore] public bool IsFloating => Placement == Float;
    [JsonIgnore] public bool IsDockedRight => Placement == Right;

    public static string NormalizeSide(string? side) =>
        string.Equals(side, Right, StringComparison.OrdinalIgnoreCase) ? Right : Left;

    /// <summary>Docks (pinned) on the given side and makes the pane visible.</summary>
    public void DockTo(string side)
    {
        Placement = DockSide = NormalizeSide(side);
        AutoHide = false;
        Visible = true;
    }

    /// <summary>Moves the pane into its own window, remembering the side it came from.</summary>
    public void FloatOut()
    {
        if (!IsFloating) DockSide = NormalizeSide(Placement);
        Placement = Float;
        AutoHide = false;
        Visible = true;
    }

    /// <summary>Turns auto-hide on or off. A floating pane is docked back to its last side first.</summary>
    public void SetAutoHide(bool autoHide)
    {
        if (IsFloating) Placement = NormalizeSide(DockSide);
        AutoHide = autoHide;
        Visible = true;
    }

    /// <summary>Double-click on the pane title: float a docked pane, dock a floating one.</summary>
    public void ToggleFloat()
    {
        if (IsFloating) DockTo(DockSide);
        else FloatOut();
    }

    public void Reset()
    {
        var defaults = new PaneLayout();
        Placement = defaults.Placement;
        DockSide = defaults.DockSide;
        AutoHide = defaults.AutoHide;
        Visible = defaults.Visible;
        Width = defaults.Width;
        FloatX = FloatY = null;
        FloatWidth = defaults.FloatWidth;
        FloatHeight = defaults.FloatHeight;
    }

    /// <summary>Repairs values from a hand-edited or older settings file.</summary>
    public void Normalize()
    {
        Placement = string.Equals(Placement, Float, StringComparison.OrdinalIgnoreCase) ? Float : NormalizeSide(Placement);
        DockSide = NormalizeSide(DockSide);
        if (IsFloating) AutoHide = false;
        Width = Clamp(Width, MinPaneWidth, MaxPaneWidth, DefaultWidth);
        FloatWidth = Clamp(FloatWidth, 200, 4000, DefaultFloatWidth);
        FloatHeight = Clamp(FloatHeight, 200, 4000, DefaultFloatHeight);
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) && value > 0 ? Math.Clamp(value, min, max) : fallback;
}
