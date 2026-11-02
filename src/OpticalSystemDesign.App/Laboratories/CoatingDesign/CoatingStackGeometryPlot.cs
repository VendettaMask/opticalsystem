using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

internal sealed record CoatingStackLayer(int Number, string MaterialId, string MaterialName, double ThicknessNm, Color Color)
{
    public string PhysicalThickness => ThicknessNm.ToString("G12", CultureInfo.CurrentCulture) + " nm";
    public string Side { get; init; } = "正面";
    public int FaceNumber { get; init; }
    public string LayerLabel => FaceNumber > 0 ? $"{Side} {FaceNumber}" : Number.ToString();
}
internal sealed record CoatingStackSnapshot(string Name, string Incident, string Substrate, string Exit,
    double SubstrateThicknessMm, double ReferenceWavelengthNm, double AngleDegrees, IReadOnlyList<CoatingStackLayer> Layers, long Revision)
{
    public IReadOnlyList<CoatingStackLayer> BackLayers { get; init; } = [];
    public IEnumerable<CoatingStackLayer> AllLayers => Layers.Concat(BackLayers.Reverse());
}
internal sealed record CoatingStackBand(CoatingStackLayer Layer, Rect Front);

/// <summary>Material-identity illustration of the actual coating prescription, never an optical calculation.</summary>
internal sealed class CoatingStackGeometryPlot : Control
{
    private CoatingStackSnapshot? _snapshot;
    private int _selected;
    internal IReadOnlyList<CoatingStackBand> Bands { get; private set; } = [];
    internal int SelectedLayer => _selected;
    internal event Action<int>? SelectionChanged;
    private Rect _substrate;
    private const double DepthX = 70, DepthY = -24;

    internal CoatingStackGeometryPlot()
    {
        Name = "CoatingVerticalStackPlot"; Focusable = true; MinHeight = 740;
    }
    internal void SetSnapshot(CoatingStackSnapshot snapshot)
    {
        _snapshot = snapshot;
        var count = snapshot.Layers.Count + snapshot.BackLayers.Count;
        double height = count == 0 ? 0 : Math.Clamp(count * 18, 120, 360);
        var top = 150 + (470 - height - 110) / 2;
        var total = snapshot.AllLayers.Sum(l => l.ThicknessNm); var y = top;
        var minimumBand = count == 0 ? 0 : height >= 1.2 * count ? 1.2 : height / count / 2;
        var bands = new List<CoatingStackBand>();
        foreach (var layer in snapshot.Layers)
        {
            var h = minimumBand + (height - minimumBand * count) * layer.ThicknessNm / total;
            bands.Add(new(layer, new(285, y, 320, h))); y += h;
        }
        _substrate = new(285, y, 320, 110); y += 110;
        foreach (var layer in snapshot.BackLayers.Reverse())
        {
            var h = minimumBand + (height - minimumBand * count) * layer.ThicknessNm / total;
            bands.Add(new(layer, new(285, y, 320, h))); y += h;
        }
        Bands = bands.AsReadOnly();
        Select(count == 0 ? 0 : Math.Clamp(_selected, 1, count)); InvalidateVisual();
    }
    internal void Select(int number)
    {
        _selected = number;
        var layer = Bands.FirstOrDefault(b => b.Layer.Number == number)?.Layer;
        AutomationProperties.SetName(this, layer is null ? "竖直堆栈剖面" : $"第 {layer.Number} 层，{layer.MaterialName}，{layer.PhysicalThickness}");
        InvalidateVisual(); SelectionChanged?.Invoke(number);
    }
    internal void ClearSnapshot() { _snapshot = null; Bands = []; _selected = 0; InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var band = Bands.FirstOrDefault(b => b.Front.Contains(e.GetPosition(this)));
        if (band is null) return; Focus(); Select(band.Layer.Number); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var band = Bands.FirstOrDefault(b => b.Front.Contains(e.GetPosition(this)));
        ToolTip.SetTip(this, band is null ? null : $"{band.Layer.Number} · {band.Layer.MaterialName}\n{band.Layer.MaterialId}\n{band.Layer.PhysicalThickness}");
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (Bands.Count == 0 || e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
        Select(e.Key == Key.Home ? 1 : e.Key == Key.End ? Bands.Count : Math.Clamp(_selected + (e.Key == Key.Up ? -1 : 1), 1, Bands.Count)); e.Handled = true;
    }

    private IBrush ThemeBrush(string key, Color fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = ThemeBrush(ThemeResourceBindings.PlotBackground, Colors.White);
        var text = ThemeBrush(ThemeResourceBindings.TextPrimary, Colors.Black);
        var muted = ThemeBrush(ThemeResourceBindings.TextSecondary, Colors.DimGray);
        var accent = ThemeBrush(ThemeResourceBindings.TextAccent, Colors.RoyalBlue);
        var line = ThemeBrush(ThemeResourceBindings.Border, Colors.Gray);
        context.DrawRectangle(background, null, new Rect(Bounds.Size));
        if (_snapshot is not { } snapshot) return;
        var font = DisplayTypography.BodySmall;
        void Text(string value, double x, double y, double size = 0, IBrush? brush = null, bool bold = false, double width = 0)
        {
            var label = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                DisplayTypography.Typeface(weight: bold ? FontWeight.SemiBold : FontWeight.Normal), size > 0 ? size : font, brush ?? text);
            if (width > 0) { label.MaxTextWidth = width; label.MaxTextHeight = font * 1.6; label.Trimming = TextTrimming.CharacterEllipsis; }
            context.DrawText(label, new Point(x, y));
        }
        Text(UiLocalization.Text("堆栈几何"), 40, 24, DisplayTypography.SectionTitle, bold: true);
        Text(UiLocalization.Text("竖直剖面 · 从入射侧到出射侧"), 40, 62, brush: muted);
        Text($"{UiLocalization.Text("参考波长")} {snapshot.ReferenceWavelengthNm:G7} nm     {UiLocalization.Text("入射角")} {snapshot.AngleDegrees:G6}°", 760, 35, brush: muted, width: 390);
        var top = Bands.Count > 0 ? Math.Min(Bands[0].Front.Top, _substrate.Top) : _substrate.Top;
        var bottom = Bands.Count > 0 ? Math.Max(Bands[^1].Front.Bottom, _substrate.Bottom) : _substrate.Bottom;
        // Soft depth shadow is illustrative; the material tint uses the editor's identity palette.
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 50, 80, 105)), null, new Rect(300, bottom + 10, 390, 16), 8, 8);
        Slab(_substrate, Color.Parse("#83C6DA"), substrate: true);
        foreach (var band in Bands.Reverse()) Slab(band.Front, band.Layer.Color);
        if (Bands.Count > 0)
        {
            var first = Bands[0].Front;
            Polygon(context, Mix(Bands[0].Layer.Color, Colors.White, .62), [first.TopLeft, first.TopRight,
                first.TopRight + new Vector(DepthX, DepthY), first.TopLeft + new Vector(DepthX, DepthY)], new Pen(line, .7));
        }
        var theta = snapshot.AngleDegrees * Math.PI / 180;
        var dx = Math.Min(130, 80 * Math.Tan(theta)); var dy = theta < 1e-10 ? 80 : dx / Math.Tan(theta);
        var end = new Point(445, top - 35); var start = end - new Vector(dx, dy);
        context.DrawLine(new Pen(accent, 2.3), start, end); Arrow(context, accent, start, end);
        Text(UiLocalization.Text("入射介质") + " · " + snapshot.Incident, 70, Math.Max(110, top - 112), brush: muted, width: 210);
        Text(UiLocalization.Text("入射方向"), (start.X + end.X) / 2 + 16, (start.Y + end.Y) / 2 - font, brush: accent);
        Text(UiLocalization.Text("出射介质") + " · " + snapshot.Exit, 285, bottom + 42, brush: muted, width: 430);
        Text(UiLocalization.Text(snapshot.BackLayers.Count == 0 ? "裸背面" : "背面膜系"), 655, _substrate.Bottom - 3, brush: muted);
        if (snapshot.Layers.Count > 0)
        {
            Dimension(245, top, _substrate.Top, snapshot.Layers.Sum(l => l.ThicknessNm).ToString("G7") + " nm");
            Text(UiLocalization.Text("膜层总厚度"), 70, (top + _substrate.Top) / 2 - 26, brush: muted);
        }
        else Text(UiLocalization.Text("未镀膜基板"), 70, top - 25, brush: muted);
        if (snapshot.BackLayers.Count > 0)
            Dimension(245, _substrate.Bottom, bottom, snapshot.BackLayers.Sum(l => l.ThicknessNm).ToString("G7") + " nm");
        Dimension(245, _substrate.Top, _substrate.Bottom, snapshot.SubstrateThicknessMm.ToString("G7") + " mm");
        Text(UiLocalization.Text("基板"), 70, _substrate.Center.Y - 27, brush: muted);
        var glassText = new SolidColorBrush(Color.Parse("#183C4D"));
        Text(snapshot.Substrate, 315, _substrate.Center.Y - 14, font + 2, glassText, true, 270);
        Text(UiLocalization.Text("基板"), 315, _substrate.Center.Y + 12, brush: glassText);
        DrawAnnotations();
        Text(UiLocalization.Text("膜层放大、基板压缩示意；颜色仅用于区分材料。完整膜层见“膜层数据”。"), 40, 701, brush: muted, width: 1120);

        void Dimension(double x, double y0, double y1, string value)
        {
            context.DrawLine(new Pen(line, 1), new(x, y0), new(x, y1));
            context.DrawLine(new Pen(line, 1), new(x - 5, y0), new(x + 5, y0));
            context.DrawLine(new Pen(line, 1), new(x - 5, y1), new(x + 5, y1));
            Text(value, 70, (y0 + y1) / 2 - 3, font + 1, width: 165);
        }
        void Slab(Rect front, Color color, bool substrate = false)
        {
            var side = new[] { front.TopRight, front.TopRight + new Vector(DepthX, DepthY), front.BottomRight + new Vector(DepthX, DepthY), front.BottomRight };
            Polygon(context, Mix(color, Colors.Black, substrate ? .12 : .25), side, new Pen(new SolidColorBrush(Mix(color, Colors.Black, .3)), .6));
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .3, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(Mix(color, Colors.White, .62), 0), new(Mix(color, Colors.White, .18), .15), new(color, .7), new(Mix(color, Colors.Black, .15), 1) }
            };
            context.DrawRectangle(fill, new Pen(new SolidColorBrush(Mix(color, Colors.Black, .2)), .6), front);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(125, 255, 255, 255)), .8), front.TopLeft, front.TopRight);
            if (substrate)
            {
                // A highlight across the glass body indicates depth, not transmission data.
                Polygon(context, Color.FromArgb(52, 255, 255, 255), [new(front.Left + 28, front.Top), new(front.Left + 66, front.Top),
                    new(front.Left + 36, front.Bottom), new(front.Left + 6, front.Bottom)]);
            }
            if (Bands.FirstOrDefault(b => b.Layer.Number == _selected)?.Front == front)
                context.DrawRectangle(null, new Pen(accent, 2), front);
        }
        void DrawAnnotations()
        {
            if (Bands.Count == 0) return;
            var pitch = Math.Max(21, font * 1.5); var capacity = Math.Max(4, (int)(430 / pitch));
            var indices = new SortedSet<int> { 0, Bands.Count - 1, Math.Clamp(_selected - 1, 0, Bands.Count - 1) };
            for (var k = 0; k < capacity - 3; k++) indices.Add((int)Math.Round(k * (Bands.Count - 1d) / Math.Max(1, capacity - 4)));
            if (Bands.Count <= capacity) indices = new(Enumerable.Range(0, Bands.Count));
            var y = Math.Max(150, (top + _substrate.Top) / 2 - indices.Count * pitch / 2);
            Text(UiLocalization.Text("膜层 · 入射侧起编号"), 760, y - 32, brush: muted);
            Text("nm", 1124, y - 32, brush: muted);
            foreach (var index in indices)
            {
                var band = Bands[index]; var selected = band.Layer.Number == _selected;
                var at = new Point(band.Front.Right + DepthX + 4, band.Front.Center.Y + DepthY / 2);
                var pen = new Pen(selected ? accent : line, selected ? 1.4 : .7);
                context.DrawLine(pen, at, new Point(722, at.Y)); context.DrawLine(pen, new Point(722, at.Y), new Point(749, y + font / 2));
                context.DrawEllipse(new SolidColorBrush(band.Layer.Color), null, new Point(763, y + font / 2), 3.5, 3.5);
                Text($"{band.Layer.LayerLabel}   {band.Layer.MaterialName}", 776, y - 2, bold: selected, width: 263);
                Text(band.Layer.ThicknessNm.ToString("G7"), 1052, y - 2, bold: selected, width: 110); y += pitch;
            }
            if (indices.Count < Bands.Count) Text($"… {UiLocalization.Text("完整膜层见“膜层数据”")}", 760, y + 10, brush: muted, width: 390);
        }
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)(a.R * (1 - amount) + b.R * amount),
        (byte)(a.G * (1 - amount) + b.G * amount), (byte)(a.B * (1 - amount) + b.B * amount));
    private static void Polygon(DrawingContext context, Color color, IReadOnlyList<Point> points, IPen? pen = null)
    {
        var shape = new StreamGeometry(); using (var path = shape.Open()) { path.BeginFigure(points[0], true); foreach (var p in points.Skip(1)) path.LineTo(p); path.EndFigure(true); }
        context.DrawGeometry(new SolidColorBrush(color), pen, shape);
    }
    private static void Arrow(DrawingContext context, IBrush brush, Point from, Point to)
    {
        var v = new Vector(to.X - from.X, to.Y - from.Y); if (v.Length < 1) return; v /= v.Length;
        var normal = new Vector(-v.Y, v.X); var shape = new StreamGeometry();
        using (var path = shape.Open()) { path.BeginFigure(to, true); path.LineTo(to - v * 12 + normal * 5); path.LineTo(to - v * 12 - normal * 5); path.EndFigure(true); }
        context.DrawGeometry(brush, null, shape);
    }
}
