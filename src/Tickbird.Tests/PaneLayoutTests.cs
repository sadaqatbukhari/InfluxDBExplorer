using Tickbird.Core.Settings;
using Newtonsoft.Json;

namespace Tickbird.Tests;

public class PaneLayoutTests
{
    [Fact]
    public void Defaults_AreDockedLeftAndVisible()
    {
        var layout = new PaneLayout();
        Assert.Equal(PaneLayout.Left, layout.Placement);
        Assert.False(layout.AutoHide);
        Assert.True(layout.Visible);
    }

    [Fact]
    public void FloatThenToggle_ReturnsToTheSideItCameFrom()
    {
        var layout = new PaneLayout();
        layout.DockTo(PaneLayout.Right);
        layout.FloatOut();
        Assert.True(layout.IsFloating);
        layout.ToggleFloat();
        Assert.Equal(PaneLayout.Right, layout.Placement);
    }

    [Fact]
    public void AutoHide_FromFloating_DocksToLastSide()
    {
        var layout = new PaneLayout();
        layout.DockTo(PaneLayout.Right);
        layout.FloatOut();
        layout.SetAutoHide(true);
        Assert.Equal(PaneLayout.Right, layout.Placement);
        Assert.True(layout.AutoHide);
    }

    [Fact]
    public void Normalize_RepairsBadValues()
    {
        var layout = new PaneLayout { Placement = "float", DockSide = "nowhere", AutoHide = true, Width = double.NaN, FloatWidth = 5 };
        layout.Normalize();
        Assert.Equal(PaneLayout.Float, layout.Placement);
        Assert.Equal(PaneLayout.Left, layout.DockSide);
        Assert.False(layout.AutoHide);
        Assert.Equal(PaneLayout.DefaultWidth, layout.Width);
        Assert.Equal(200, layout.FloatWidth);
    }

    [Fact]
    public void Settings_WithoutExplorerSection_GetDefaultLayout()
    {
        var settings = JsonConvert.DeserializeObject<TickbirdSettings>("{\"TimeFormat\":\"HH:mm:ss\"}")!;
        Assert.NotNull(settings.Explorer);
        Assert.Equal(PaneLayout.Left, settings.Explorer.Placement);
    }

    [Fact]
    public void Layout_RoundTripsThroughJson()
    {
        var settings = new TickbirdSettings();
        settings.Explorer.DockTo(PaneLayout.Right);
        settings.Explorer.Width = 410;
        var copy = JsonConvert.DeserializeObject<TickbirdSettings>(JsonConvert.SerializeObject(settings))!;
        Assert.Equal(PaneLayout.Right, copy.Explorer.Placement);
        Assert.Equal(410, copy.Explorer.Width);
        Assert.DoesNotContain("IsFloating", JsonConvert.SerializeObject(settings));
    }
}
