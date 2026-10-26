using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.Threading;
using Tickbird.Core.Mapping;

namespace Tickbird.Desktop.Views.Requests;

/// <summary>A map background made of one or more web-map tile layers ({z}/{x}/{y} URL templates).</summary>
public sealed class MapBackground
{
    private const string EsriImagery = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    private const string EsriRoads = "https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Transportation/MapServer/tile/{z}/{y}/{x}";
    private const string EsriPlaces = "https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}";
    private const string Osm = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public static readonly MapBackground Hybrid = new("Hybrid", 19, "Imagery © Esri, Maxar, Earthstar Geographics", EsriImagery, EsriRoads, EsriPlaces);
    public static readonly MapBackground Satellite = new("Satellite", 19, "Imagery © Esri, Maxar, Earthstar Geographics", EsriImagery);
    public static readonly MapBackground Streets = new("Streets", 19, "© OpenStreetMap contributors", Osm);
    public static readonly MapBackground None = new("None", 22, "");

    public static readonly IReadOnlyList<MapBackground> All = new[] { Hybrid, Satellite, Streets, None };

    private MapBackground(string name, int maxZoom, string attribution, params string[] layers)
    {
        Name = name;
        MaxZoom = maxZoom;
        Attribution = attribution;
        Layers = layers;
    }

    public string Name { get; }
    public int MaxZoom { get; }
    public string Attribution { get; }
    public IReadOnlyList<string> Layers { get; }

    public static MapBackground Get(string? name) => All.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Hybrid;
}

/// <summary>Downloads and caches map tiles in memory, shared by every map. All state is touched on the UI thread.</summary>
public static class MapTiles
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, (Bitmap Bitmap, long Stamp)> Cache = new();
    private static readonly HashSet<string> Pending = new();
    private static readonly Dictionary<string, DateTime> Failed = new();
    private static readonly SemaphoreSlim Gate = new(6);
    private static long clock;
    private const int Capacity = 600;
    private const int MaxPending = 96;

    /// <summary>Turn off to keep maps offline (tests, or no network); lines are still drawn.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Raised on the UI thread when a tile arrives.</summary>
    public static event Action? Loaded;

    public static Bitmap? Get(string url)
    {
        if (!Cache.TryGetValue(url, out var entry)) return null;
        Cache[url] = (entry.Bitmap, ++clock);
        return entry.Bitmap;
    }

    public static void Request(string url)
    {
        if (!Enabled || Cache.ContainsKey(url) || Pending.Contains(url) || Pending.Count >= MaxPending) return;
        if (Failed.TryGetValue(url, out var failedAt) && DateTime.UtcNow - failedAt < TimeSpan.FromMinutes(2)) return;
        Pending.Add(url);
        _ = LoadAsync(url);
    }

    private static async Task LoadAsync(string url)
    {
        Bitmap? bitmap = null;
        try
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                using var stream = new MemoryStream(bytes);
                bitmap = new Bitmap(stream);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception)
        {
            bitmap = null;   // offline, blocked or not an image: the map keeps working without it
        }
        Dispatcher.UIThread.Post(() =>
        {
            Pending.Remove(url);
            if (bitmap == null)
            {
                Failed[url] = DateTime.UtcNow;
                return;
            }
            Cache[url] = (bitmap, ++clock);
            if (Cache.Count > Capacity)
                foreach (var old in Cache.OrderBy(kv => kv.Value.Stamp).Take(Capacity / 4).Select(kv => kv.Key).ToList())
                    Cache.Remove(old);   // not disposed: a frame being composed may still use it
            Loaded?.Invoke();
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(MapTiles).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        // Tile servers (OpenStreetMap in particular) require an identifying User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Tickbird/{version}");
        return client;
    }
}

/// <summary>
/// Slippy map (Web Mercator) that draws GPS tracks as colored lines over tiles: drag to pan, wheel or
/// double-click to zoom, <see cref="FitToData"/> to show everything.
/// </summary>
public sealed class MapCanvas : Control, ICustomHitTest
{
    private const double TileSize = 256;
    private const double MinZoom = 1;
    private const double MaxZoom = 22;
    private static readonly IBrush EmptyBrush = Brush.Parse("#dde6ee");
    private static readonly IBrush GridBrush = Brush.Parse("#cfdbe6");
    private static readonly IBrush LabelBrush = Brush.Parse("#10314F");
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.Parse("#E6FFFFFF"));

    private MapData? data;
    private double[][] xs = Array.Empty<double[]>();
    private double[][] ys = Array.Empty<double[]>();
    private Rect[] trackBounds = Array.Empty<Rect>();
    private readonly Dictionary<int, Pen> pens = new();
    private Func<int, Color> legendColor = _ => Color.Parse("#2979FF");
    private MapBackground background = MapBackground.Hybrid;
    private double lineWidth = 3;
    private bool showEndpoints = true;

    private double centerX = 0.5;
    private double centerY = 0.5;
    private double zoom = 2;
    private bool fitPending;
    private bool panning;
    private Point panLast;
    private (int Track, int Point)? highlight;

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Pointer position over the map (null when it leaves), for hover read-outs.</summary>
    public event Action<Point?>? HoverMoved;

    public double Zoom => zoom;
    public MapData? Data => data;

    public void SetData(MapData? newData, bool fit)
    {
        data = newData;
        var tracks = newData?.Tracks ?? Array.Empty<MapTrack>();
        xs = new double[tracks.Count][];
        ys = new double[tracks.Count][];
        trackBounds = new Rect[tracks.Count];
        for (var t = 0; t < tracks.Count; t++)
        {
            var points = tracks[t].Points;
            var tx = new double[points.Length];
            var ty = new double[points.Length];
            double x0 = 1, y0 = 1, x1 = 0, y1 = 0;
            for (var i = 0; i < points.Length; i++)
            {
                tx[i] = MercatorX(points[i].Longitude);
                ty[i] = MercatorY(points[i].Latitude);
                x0 = Math.Min(x0, tx[i]); x1 = Math.Max(x1, tx[i]);
                y0 = Math.Min(y0, ty[i]); y1 = Math.Max(y1, ty[i]);
            }
            xs[t] = tx;
            ys[t] = ty;
            trackBounds[t] = points.Length > 0 ? new Rect(x0, y0, x1 - x0, y1 - y0) : default;
        }
        highlight = null;
        pens.Clear();
        if (fit) FitToData();
        InvalidateVisual();
    }

    public void SetStyle(Func<int, Color> colorOfLegend, double width, MapBackground tiles, bool endpoints)
    {
        legendColor = colorOfLegend;
        lineWidth = width;
        background = tiles;
        showEndpoints = endpoints;
        pens.Clear();
        InvalidateVisual();
    }

    public void Highlight((int Track, int Point)? hit)
    {
        if (highlight == hit) return;
        highlight = hit;
        InvalidateVisual();
    }

    /// <summary>Zooms and centers so every track is visible.</summary>
    public void FitToData()
    {
        fitPending = true;
        InvalidateVisual();
    }

    public void ZoomBy(double delta) => ZoomAt(zoom + delta, new Point(Bounds.Width / 2, Bounds.Height / 2));

    public void ZoomAt(double newZoom, Point anchor)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        var size = Bounds.Size;
        var before = World(zoom);
        var mx = centerX + (anchor.X - size.Width / 2) / before;
        var my = centerY + (anchor.Y - size.Height / 2) / before;
        zoom = newZoom;
        var after = World(zoom);
        centerX = mx - (anchor.X - size.Width / 2) / after;
        centerY = my - (anchor.Y - size.Height / 2) / after;
        ClampCenter();
        InvalidateVisual();
    }

    /// <summary>The point nearest to <paramref name="position"/> within <paramref name="radius"/> pixels.</summary>
    public (int Track, int Point)? HitTestPoint(Point position, double radius = 10)
    {
        if (data == null) return null;
        var size = Bounds.Size;
        var world = World(zoom);
        var ox = size.Width / 2 - centerX * world;
        var oy = size.Height / 2 - centerY * world;
        var mx = (position.X - ox) / world;
        var my = (position.Y - oy) / world;
        var r = radius / world;
        (int, int)? best = null;
        var bestDistance = radius * radius;
        for (var t = 0; t < xs.Length; t++)
        {
            if (!trackBounds[t].Inflate(r).Contains(new Point(mx, my))) continue;
            var tx = xs[t];
            var ty = ys[t];
            for (var i = 0; i < tx.Length; i++)
            {
                var dx = (tx[i] - mx) * world;
                var dy = (ty[i] - my) * world;
                var d = dx * dx + dy * dy;
                if (d <= bestDistance) { bestDistance = d; best = (t, i); }
            }
        }
        return best;
    }

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MapTiles.Loaded += OnTileLoaded;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        MapTiles.Loaded -= OnTileLoaded;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTileLoaded() => InvalidateVisual();

    // ------------------------------------------------------------------ input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            ZoomAt(zoom + 1, point.Position);
            e.Handled = true;
            return;
        }
        panning = true;
        panLast = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (panning)
        {
            var world = World(zoom);
            centerX -= (position.X - panLast.X) / world;
            centerY -= (position.Y - panLast.Y) / world;
            panLast = position;
            ClampCenter();
            InvalidateVisual();
            HoverMoved?.Invoke(null);
            return;
        }
        HoverMoved?.Invoke(position);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!panning) return;
        panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        panning = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoverMoved?.Invoke(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(zoom + Math.Clamp(e.Delta.Y, -3, 3) * 0.5, e.GetPosition(this));
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drawing

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(EmptyBrush, new Rect(size));
        if (size.Width < 2 || size.Height < 2) return;
        if (fitPending) ApplyFit(size);

        var world = World(zoom);
        var ox = size.Width / 2 - centerX * world;
        var oy = size.Height / 2 - centerY * world;

        if (background.Layers.Count == 0) DrawGrid(context, size, world, ox, oy);
        foreach (var layer in background.Layers) DrawTiles(context, layer, size, world, ox, oy);
        DrawTracks(context, size, world, ox, oy);
        DrawScale(context, size, world);
        DrawAttribution(context, size);
    }

    private void ApplyFit(Size size)
    {
        fitPending = false;
        if (data?.Bounds is not { } b) return;
        double x0 = MercatorX(b.West), x1 = MercatorX(b.East);
        double y0 = MercatorY(b.North), y1 = MercatorY(b.South);
        const double pad = 48;
        var w = Math.Max(40, size.Width - 2 * pad);
        var h = Math.Max(40, size.Height - 2 * pad);
        var zx = Math.Log2(w / (Math.Max(x1 - x0, 1e-12) * TileSize));
        var zy = Math.Log2(h / (Math.Max(y1 - y0, 1e-12) * TileSize));
        zoom = Math.Clamp(Math.Min(zx, zy), MinZoom, 18);
        centerX = (x0 + x1) / 2;
        centerY = (y0 + y1) / 2;
    }

    private void DrawTiles(DrawingContext context, string layer, Size size, double world, double ox, double oy)
    {
        var z = (int)Math.Clamp(Math.Round(zoom), 0, background.MaxZoom);
        var n = 1 << z;
        var tile = world / n;
        var x0 = (int)Math.Floor(-ox / tile);
        var x1 = (int)Math.Floor((size.Width - ox) / tile);
        var y0 = Math.Max(0, (int)Math.Floor(-oy / tile));
        var y1 = Math.Min(n - 1, (int)Math.Floor((size.Height - oy) / tile));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var wx = ((x % n) + n) % n;
                // Half a pixel of overlap hides seams between neighbouring tiles.
                var dest = new Rect(ox + x * tile, oy + y * tile, tile + 0.5, tile + 0.5);
                var url = TileUrl(layer, z, wx, y);
                var bitmap = MapTiles.Get(url);
                if (bitmap != null)
                {
                    context.DrawImage(bitmap, new Rect(bitmap.Size), dest);
                    continue;
                }
                MapTiles.Request(url);
                // Until it arrives, stretch a cached lower-zoom tile so the map is never blank while zooming.
                for (var k = 1; k <= 6 && z - k >= 0; k++)
                {
                    var parent = MapTiles.Get(TileUrl(layer, z - k, wx >> k, y >> k));
                    if (parent == null) continue;
                    var part = parent.Size.Width / (1 << k);
                    var source = new Rect((wx - ((wx >> k) << k)) * part, (y - ((y >> k) << k)) * part, part, part);
                    context.DrawImage(parent, source, dest);
                    break;
                }
            }
        }
    }

    private static void DrawGrid(DrawingContext context, Size size, double world, double ox, double oy)
    {
        // Offline background: a light graticule every few degrees so the shape still has context.
        var pen = new Pen(GridBrush, 1);
        var step = world > 200000 ? 0.01 : world > 20000 ? 0.1 : world > 2000 ? 1 : 10;
        for (var lon = -180.0; lon <= 180; lon += step)
        {
            var x = ox + MercatorX(lon) * world;
            if (x < 0 || x > size.Width) continue;
            context.DrawLine(pen, new Point(x, 0), new Point(x, size.Height));
        }
        for (var lat = -85.0; lat <= 85; lat += step)
        {
            var y = oy + MercatorY(lat) * world;
            if (y < 0 || y > size.Height) continue;
            context.DrawLine(pen, new Point(0, y), new Point(size.Width, y));
        }
    }

    private Pen PenFor(int legend)
    {
        if (!pens.TryGetValue(legend, out var pen))
        {
            pen = new Pen(new SolidColorBrush(legendColor(legend)), lineWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            pens[legend] = pen;
        }
        return pen;
    }

    private void DrawTracks(DrawingContext context, Size size, double world, double ox, double oy)
    {
        if (data == null) return;
        var view = new Rect(-ox / world, -oy / world, size.Width / world, size.Height / world).Inflate(16 / world);
        var visible = Enumerable.Range(0, data.Tracks.Count)
            .Where(t => data.Tracks[t].Points.Length > 0 && view.Intersects(trackBounds[t].Inflate(1e-9))).ToList();

        // Halo layers go under the track so its own color stays visible inside the band.
        for (var l = 0; l < data.Layers.Count; l++)
            if (data.Layers[l] is { Visible: true, Style: MapLayerStyle.Halo })
                foreach (var t in visible) DrawLayer(context, t, l, data.Layers[l], world, ox, oy);

        foreach (var t in visible)
        {
            var points = data.Tracks[t].Points;
            var tx = xs[t];
            var ty = ys[t];
            if (points.Length == 1)
            {
                var only = new Point(ox + tx[0] * world, oy + ty[0] * world);
                context.DrawEllipse(PenFor(points[0].Legend).Brush, null, only, lineWidth + 1, lineWidth + 1);
                continue;
            }
            // Consecutive points with the same color form one polyline; each run ends on the next run's first point.
            var start = 0;
            for (var i = 1; i <= points.Length; i++)
            {
                if (i < points.Length && points[i].Legend == points[start].Legend) continue;
                var end = Math.Min(i, points.Length - 1);
                if (end > start) DrawRun(context, tx, ty, start, end, PenFor(points[start].Legend), world, ox, oy);
                start = i;
            }
        }

        // Line, dashed and point layers on top, in layer order (later layers draw over earlier ones).
        for (var l = 0; l < data.Layers.Count; l++)
            if (data.Layers[l] is { Visible: true } layer && layer.Style != MapLayerStyle.Halo)
                foreach (var t in visible) DrawLayer(context, t, l, layer, world, ox, oy);

        if (showEndpoints && data.Tracks.Count <= 50)
        {
            for (var t = 0; t < data.Tracks.Count; t++)
            {
                var points = data.Tracks[t].Points;
                if (points.Length == 0) continue;
                var first = new Point(ox + xs[t][0] * world, oy + ys[t][0] * world);
                var last = new Point(ox + xs[t][^1] * world, oy + ys[t][^1] * world);
                context.DrawEllipse(Brushes.White, new Pen(LabelBrush, 1.5), first, 4, 4);
                context.DrawEllipse(PenFor(points[^1].Legend).Brush, new Pen(Brushes.White, 2), last, 6, 6);
                if (data.Tracks.Count > 1 || !string.IsNullOrEmpty(data.Tracks[t].Label) && data.Tracks[t].Label != "Track")
                    DrawLabel(context, data.Tracks[t].Label, new Point(last.X + 9, last.Y - 9));
            }
        }

        if (highlight is { } h && h.Track < xs.Length && h.Point < xs[h.Track].Length)
        {
            var p = new Point(ox + xs[h.Track][h.Point] * world, oy + ys[h.Track][h.Point] * world);
            context.DrawEllipse(null, new Pen(Brushes.White, 3), p, lineWidth + 5, lineWidth + 5);
            context.DrawEllipse(null, new Pen(LabelBrush, 1.5), p, lineWidth + 5, lineWidth + 5);
        }
    }

    private void DrawLayer(DrawingContext context, int track, int layer, MapLayer definition, double world, double ox, double oy)
    {
        var points = data!.Tracks[track].Points;
        var tx = xs[track];
        var ty = ys[track];
        var bit = 1UL << layer;
        var color = Color.TryParse(definition.Color, out var parsed) ? parsed : Colors.DodgerBlue;
        var width = Math.Clamp(definition.Width, 1, 20);

        if (definition.Style == MapLayerStyle.Points)
        {
            var brush = new SolidColorBrush(color);
            var outline = new Pen(Brushes.White, 1);
            var radius = Math.Max(2.5, width);
            double lx = double.NaN, ly = double.NaN;
            for (var i = 0; i < points.Length; i++)
            {
                if ((points[i].Layers & bit) == 0) continue;
                var x = ox + tx[i] * world;
                var y = oy + ty[i] * world;
                // One dot per spot: a long event becomes a short row of dots rather than a smear.
                if (!double.IsNaN(lx) && Math.Abs(x - lx) < radius * 1.5 && Math.Abs(y - ly) < radius * 1.5) continue;
                context.DrawEllipse(brush, outline, new Point(x, y), radius, radius);
                lx = x;
                ly = y;
            }
            return;
        }

        var pen = definition.Style switch
        {
            MapLayerStyle.Halo => new Pen(new SolidColorBrush(color, 0.85), lineWidth + 2 * width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            MapLayerStyle.Dashed => new Pen(new SolidColorBrush(color), width, new DashStyle(new double[] { 2, 1.5 }, 0), PenLineCap.Flat, PenLineJoin.Round),
            _ => new Pen(new SolidColorBrush(color), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        };
        // A segment belongs to the layer when its first point does (as with the track colors).
        var start = -1;
        for (var i = 0; i <= points.Length; i++)
        {
            var inLayer = i < points.Length && (points[i].Layers & bit) != 0;
            if (inLayer && start < 0) start = i;
            if (inLayer || start < 0) continue;
            var end = Math.Min(i, points.Length - 1);
            if (end > start) DrawRun(context, tx, ty, start, end, pen, world, ox, oy);
            else context.DrawEllipse(pen.Brush, null, new Point(ox + tx[start] * world, oy + ty[start] * world), pen.Thickness / 2, pen.Thickness / 2);
            start = -1;
        }
    }

    private static void DrawRun(DrawingContext context, double[] tx, double[] ty, int start, int end, Pen pen, double world, double ox, double oy)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var lx = ox + tx[start] * world;
            var ly = oy + ty[start] * world;
            g.BeginFigure(new Point(lx, ly), false);
            for (var k = start + 1; k <= end; k++)
            {
                var x = ox + tx[k] * world;
                var y = oy + ty[k] * world;
                // Skip points that land on (almost) the same pixel; large tracks stay fast without changing the shape.
                if (k != end && Math.Abs(x - lx) < 0.75 && Math.Abs(y - ly) < 0.75) continue;
                g.LineTo(new Point(x, y));
                lx = x;
                ly = y;
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        if (text.Length > 40) text = text[..39] + "…";
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, LabelBrush);
        var box = new Rect(at.X - 5, at.Y - 3, formatted.Width + 10, formatted.Height + 6);
        context.DrawRectangle(PanelBrush, new Pen(Brush.Parse("#9fb3c6"), 1), box, 4, 4);
        context.DrawText(formatted, at);
    }

    private void DrawScale(DrawingContext context, Size size, double world)
    {
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * centerY)));
        var metersPerPixel = Math.Cos(latitude) * 2 * Math.PI * 6378137 / world;
        if (metersPerPixel <= 0 || double.IsNaN(metersPerPixel)) return;
        var target = metersPerPixel * 100;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var nice = new[] { 5, 2, 1 }.Select(f => f * magnitude).First(v => v <= target);
        var width = nice / metersPerPixel;
        var text = nice >= 1000 ? $"{nice / 1000:0.##} km" : $"{nice:0} m";
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        var x = 10.0;
        var y = size.Height - 10;
        context.DrawRectangle(PanelBrush, null, new Rect(x - 4, y - formatted.Height - 8, Math.Max(width, formatted.Width) + 8, formatted.Height + 12), 3, 3);
        var pen = new Pen(LabelBrush, 2);
        context.DrawLine(pen, new Point(x, y - 2), new Point(x + width, y - 2));
        context.DrawLine(pen, new Point(x, y - 6), new Point(x, y - 2));
        context.DrawLine(pen, new Point(x + width, y - 6), new Point(x + width, y - 2));
        context.DrawText(formatted, new Point(x, y - formatted.Height - 5));
    }

    private void DrawAttribution(DrawingContext context, Size size)
    {
        if (string.IsNullOrEmpty(background.Attribution)) return;
        var formatted = new FormattedText(background.Attribution, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, LabelBrush);
        var at = new Point(size.Width - formatted.Width - 6, size.Height - formatted.Height - 3);
        context.FillRectangle(PanelBrush, new Rect(at.X - 4, at.Y - 1, formatted.Width + 10, formatted.Height + 4));
        context.DrawText(formatted, at);
    }

    private void ClampCenter()
    {
        centerY = Math.Clamp(centerY, 0, 1);
        centerX = Math.Clamp(centerX, -0.5, 1.5);
    }

    private static double World(double z) => TileSize * Math.Pow(2, z);

    private static string TileUrl(string template, int z, int x, int y) => template
        .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

    public static double MercatorX(double longitude) => (longitude + 180) / 360;

    public static double MercatorY(double latitude)
    {
        var radians = Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return (1 - Math.Log(Math.Tan(radians) + 1 / Math.Cos(radians)) / Math.PI) / 2;
    }
}
