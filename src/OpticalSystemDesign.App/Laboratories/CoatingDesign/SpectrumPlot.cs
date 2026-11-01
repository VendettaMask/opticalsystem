using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Modules;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public enum CoatingQuantity { T, R, A }
public enum CoatingCurvePolarization { Average, S, P }
public enum CoatingWavelengthUnit { Nanometers, Micrometers }
public enum CoatingValueUnit { Percent, Fraction, OpticalDensity }
public sealed record CoatingPlotRange(double MinimumNm, double MaximumNm, double MinimumY, double MaximumY);

public sealed class SpectrumPlot : Control, IDisposable
{
    private Experiment? _experiment;
    private CoatingPolarizedPlotData? _data;
    private CancellationTokenSource? _request;
    private long _generation;
    private readonly bool[,] _visible = new bool[3, 3];
    private bool _od, _grid = true, _targets = true, _exporting;
    private double _width = 1.5;
    private CoatingPlotRange? _range;
    private int? _hover;
    private string? _error;
    private AnalysisSpectrumOptions _options = AnalysisSpectrumOptions.Default;
    private CoatingValueUnit _valueUnit;
    public CoatingWavelengthUnit WavelengthUnit { get; private set; }
    public CoatingValueUnit ValueUnit => _valueUnit;
    public bool AutomaticY { get; private set; }
    public AnalysisSpectrumOptions AnalysisOptions => _options with { AnglesDegrees = _options.AnglesDegrees.ToArray() };
    public event EventHandler? StateChanged;
    public Task PendingEvaluation { get; private set; } = Task.CompletedTask;
    public bool IsPreparing { get; private set; }
    public bool HasData => _data is not null;
    public bool OpticalDensity => _od;
    public bool ShowGrid => _grid;
    public bool ShowTargets => _targets;
    public double CurveWidthScale => _width;
    public string? DisplayError => _error;
    public int SampleCount => _data?.Average.Length ?? 0;
    internal int DisplayComputations { get; private set; }
    internal CoatingPolarizedPlotData? DisplayData => _data;
    private double MaximumY => _od ? 12 : _valueUnit == CoatingValueUnit.Fraction ? 1 : 100;
    public CoatingPlotRange Range
    {
        get
        {
            var range = _range ?? new(_options.MinimumNm, _options.MaximumNm, 0, MaximumY);
            if (!AutomaticY || _data is null) return range;
            var max = _data.Angles.SelectMany(a => new[] { a.Average, a.S, a.P }).SelectMany(a => a)
                .Max(s => _od ? Math.Min(12, s.Power.OpticalDensity) : Math.Max(s.Power.Reflectance, Math.Max(s.Power.Transmittance, s.Power.Absorptance)) * (_valueUnit == CoatingValueUnit.Fraction ? 1 : 100));
            return range with { MinimumY = 0, MaximumY = Math.Min(MaximumY, Math.Max(MaximumY / 100, max * 1.05)) };
        }
    }
    public void ConfigureAnalysis(AnalysisSpectrumOptions options)
    {
        options.Validate(); _options = options with { AnglesDegrees = options.AnglesDegrees.ToArray() }; _range = null;
        SetExperiment(_experiment);
    }
    public void SetUnits(CoatingWavelengthUnit wavelength, CoatingValueUnit value)
    {
        if (!Enum.IsDefined(wavelength) || !Enum.IsDefined(value)) throw new ArgumentException("显示单位无效。");
        WavelengthUnit = wavelength;
        if (_valueUnit != value) { _valueUnit = value; _od = value == CoatingValueUnit.OpticalDensity; var r = Range; _range = r with { MinimumY = 0, MaximumY = MaximumY }; }
        ChangedView();
    }
    public void SetAutomaticY(bool value) { AutomaticY = value; ChangedView(); }
    public void SetYRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum < 0 || maximum <= minimum || maximum > MaximumY)
            throw new ArgumentException("纵轴范围无效。");
        var r = _range ?? new CoatingPlotRange(_options.MinimumNm, _options.MaximumNm, 0, MaximumY);
        _range = r with { MinimumY = minimum, MaximumY = maximum }; ChangedView();
    }
    private static readonly IBrush[] CurveColors = [new ImmutableSolidColorBrush(Color.Parse("#2196F3")), new ImmutableSolidColorBrush(Color.Parse("#F24E51")), new ImmutableSolidColorBrush(Color.Parse("#5DB665"))];
    public static IBrush CurveBrush(CoatingQuantity quantity) => CurveColors[(int)quantity];
    public static string PolarizationLabel(CoatingCurvePolarization polarization) => polarization switch
    { CoatingCurvePolarization.S => "s", CoatingCurvePolarization.P => "p", _ => UiLocalization.Text("平均") };

    public SpectrumPlot()
    {
        for (var q = 0; q < 3; q++) _visible[q, 0] = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        PointerMoved += (_, e) => UpdateHover(e.GetPosition(this));
        PointerExited += (_, _) => { _hover = null; ToolTip.SetIsOpen(this, false); InvalidateVisual(); };
    }
    public bool CurveVisible(CoatingQuantity q, CoatingCurvePolarization p) => _visible[(int)q, (int)p];
    public void SetCurveVisible(CoatingQuantity q, CoatingCurvePolarization p, bool visible) { _visible[(int)q, (int)p] = visible; ChangedView(); }
    public void SetGrid(bool value) { _grid = value; ChangedView(); }
    public void SetTargets(bool value) { _targets = value; ChangedView(); }
    public void SetCurveWidth(double scale)
    { if (scale is not (1 or 1.5 or 2)) throw new ArgumentException("线宽请选择细、标准或粗。"); _width = scale; ChangedView(); }
    public void SetOpticalDensity(bool value) => SetUnits(WavelengthUnit, value ? CoatingValueUnit.OpticalDensity : CoatingValueUnit.Percent);
    public void ApplyRange(CoatingPlotRange range)
    {
        if (_experiment is null || !double.IsFinite(range.MinimumNm) || !double.IsFinite(range.MaximumNm)
            || !double.IsFinite(range.MinimumY) || !double.IsFinite(range.MaximumY)
            || range.MinimumNm < _options.MinimumNm || range.MaximumNm > _options.MaximumNm
            || range.MinimumNm >= range.MaximumNm || range.MinimumY < 0 || range.MinimumY >= range.MaximumY || range.MaximumY > MaximumY)
            throw new ArgumentException("显示范围必须位于已计算波段内，纵轴需在 0–100% 或 0–12 OD 内。");
        _range = range; ChangedView();
    }
    public void ResetView() { _range = null; ChangedView(); }
    private void ChangedView() { _hover = null; ToolTip.SetIsOpen(this, false); InvalidateVisual(); StateChanged?.Invoke(this, EventArgs.Empty); }
    public void SetExperiment(Experiment? experiment)
    {
        var previous = _experiment?.Fingerprint(); CancelPendingEvaluation();
        _experiment = experiment?.Result is null ? null : experiment.Snapshot(); _data = null; _error = null; _hover = null;
        if (_experiment?.Fingerprint() != previous) _range = null;
        if (_experiment is not null)
        {
            _request = new CancellationTokenSource(); IsPreparing = true; DisplayComputations++;
            PendingEvaluation = PrepareAsync(_experiment, AnalysisOptions, _generation, _request.Token);
        }
        else PendingEvaluation = Task.CompletedTask;
        ChangedView();
    }
    private async Task PrepareAsync(Experiment experiment, AnalysisSpectrumOptions options, long generation, CancellationToken token)
    {
        CoatingPolarizedPlotData? data = null; string? error = null;
        try { data = await Task.Run(() => CoatingPresentation.AnalysisPlot(new DesignService().EvaluateAnalysisDisplay(experiment, options, token)), token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != _generation || token.IsCancellationRequested) return;
            _data = data; _error = error; IsPreparing = false; ChangedView();
        });
    }
    public void CancelPendingEvaluation()
    {
        _generation++; _request?.Cancel(); _request?.Dispose(); _request = null;
        if (IsPreparing) _error = "显示光谱计算已取消。";
        IsPreparing = false; ChangedView();
    }
    public void Dispose() { CancelPendingEvaluation(); _data = null; }
    internal IBrush PlotTextBrush => ThemeBrush(ThemeResourceBindings.PlotText, ThemePalette.Light.PlotText);
    internal IBrush PlotBackgroundBrush => ThemeBrush(ThemeResourceBindings.PlotBackground, ThemePalette.Light.PlotBackground);
    private IBrush ThemeBrush(string key, Color fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
    private Rect PlotRect => new(DisplayTypography.Scale(48), 16, Math.Max(1, Bounds.Width - DisplayTypography.Scale(48) - 18), Math.Max(1, Bounds.Height - DisplayTypography.Scale(38) - 22));
    private IEnumerable<(CoatingQuantity Q, CoatingCurvePolarization P)> SelectedCurves()
    {
        foreach (var q in Enum.GetValues<CoatingQuantity>()) foreach (var p in Enum.GetValues<CoatingCurvePolarization>())
            if (_visible[(int)q, (int)p] && (!_od || q == CoatingQuantity.T)) yield return (q, p);
    }
    private CoatingSample[] Samples(CoatingCurvePolarization p, int angle = 0) => p switch { CoatingCurvePolarization.S => _data!.Angles[angle].S, CoatingCurvePolarization.P => _data!.Angles[angle].P, _ => _data!.Angles[angle].Average };
    public CoatingResultSnapshot ResultsSnapshot()
    {
        if (_data is null) throw new InvalidOperationException("请先计算评估光谱。");
        var columns = SelectedCurves().SelectMany(c => Enumerable.Range(0, _data.Angles.Length).Select(a => (c.Q, c.P, Angle: a))).ToArray();
        var unit = _od ? "OD" : _valueUnit == CoatingValueUnit.Percent ? "%" : "1";
        var headers = new[] { WavelengthUnit == CoatingWavelengthUnit.Micrometers ? "λ (µm)" : "λ (nm)" }
            .Concat(columns.Select(c => $"{c.Q} {PolarizationLabel(c.P)}" + (_data.Angles.Length > 1 ? $" {_data.Angles[c.Angle].AngleDegrees:G5}°" : "") + $" ({unit})")).ToArray();
        var ids = new[] { WavelengthUnit == CoatingWavelengthUnit.Micrometers ? "wavelength_um" : "wavelength_nm" }.Concat(columns.Select(c =>
            (_od ? "OD" : c.Q.ToString()) + "_" + (c.P == CoatingCurvePolarization.Average ? "average" : c.P.ToString().ToLowerInvariant())
            + "_" + _data.Angles[c.Angle].AngleDegrees.ToString("G17", CultureInfo.InvariantCulture) + "deg"
            + (_od ? "" : _valueUnit == CoatingValueUnit.Percent ? "_percent" : "_fraction"))).ToArray();
        var rows = _data.Average.Select((sample, index) => new CoatingResultRow(new[] { sample.WavelengthNanometers / (WavelengthUnit == CoatingWavelengthUnit.Micrometers ? 1000 : 1) }
            .Concat(columns.Select(c => Value(Samples(c.P, c.Angle)[index].Power, c.Q))).ToArray())).ToArray();
        return new(headers, ids, new CoatingQuantity?[] { null }.Concat(columns.Select(c => (CoatingQuantity?)c.Q)).ToArray(), rows);
    }
    private double Value(CoatingPower power, CoatingQuantity q) => _od ? power.OpticalDensity : (_valueUnit == CoatingValueUnit.Fraction ? 1 : 100) * (q switch
    { CoatingQuantity.T => power.Transmittance, CoatingQuantity.R => power.Reflectance, _ => power.Absorptance });
    private void UpdateHover(Point point)
    {
        if (_data is null || !PlotRect.Contains(point)) { _hover = null; InvalidateVisual(); return; }
        var waves = _data.Average; var view = Range;
        var wavelength = view.MinimumNm + (point.X - PlotRect.Left) / PlotRect.Width * (view.MaximumNm - view.MinimumNm);
        var lo = 0; var hi = waves.Length - 1;
        while (lo < hi) { var mid = (lo + hi) / 2; if (waves[mid].WavelengthNanometers < wavelength) lo = mid + 1; else hi = mid; }
        var index = lo > 0 && Math.Abs(waves[lo - 1].WavelengthNanometers - wavelength) < Math.Abs(waves[lo].WavelengthNanometers - wavelength) ? lo - 1 : lo;
        _hover = index;
        var tip = new StackPanel { Spacing = 5 };
        tip.Children.Add(new TextBlock { Text = $"{waves[index].WavelengthNanometers / (WavelengthUnit == CoatingWavelengthUnit.Micrometers ? 1000 : 1):G10} {(WavelengthUnit == CoatingWavelengthUnit.Micrometers ? "µm" : "nm")} · " + UiLocalization.Text("实算采样"), FontWeight = FontWeight.SemiBold });
        foreach (var (q, p) in SelectedCurves())
        for (var a = 0; a < _data.Angles.Length; a++)
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = CurveBrush(q), VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = $"{q} {PolarizationLabel(p)}" + (_data.Angles.Length > 1 ? $" {_data.Angles[a].AngleDegrees:G5}°" : "") }; Grid.SetColumn(label, 1); row.Children.Add(label);
            var number = new TextBlock { Text = Value(Samples(p, a)[index].Power, q).ToString("0.###", CultureInfo.CurrentCulture) + (_od ? " OD" : _valueUnit == CoatingValueUnit.Percent ? "%" : ""), FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(number, 2); row.Children.Add(number); tip.Children.Add(row);
        }
        ToolTip.SetTip(this, tip); InvalidateVisual();
    }
    internal byte[] CapturePng()
    {
        if (!HasData) throw new InvalidOperationException("请先计算光谱。");
        var width = checked((int)Math.Ceiling(Bounds.Width * 2)); var height = checked((int)Math.Ceiling(Bounds.Height * 2));
        if (width < 1 || height < 1 || (long)width * height > 32_000_000) throw new InvalidOperationException("图像尺寸超过导出上限。");
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(192, 192));
        _exporting = true; try { bitmap.Render(this); } finally { _exporting = false; }
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(PlotBackgroundBrush, new Rect(Bounds.Size));
        var rect = PlotRect; var view = Range; var text = PlotTextBrush;
        context.FillRectangle(ThemeBrush(ThemeResourceBindings.SubtleSurface, Colors.WhiteSmoke), rect);
        double X(double w) => rect.Left + (w - view.MinimumNm) / (view.MaximumNm - view.MinimumNm) * rect.Width;
        double Y(double v) => rect.Bottom - (v - view.MinimumY) / (view.MaximumY - view.MinimumY) * rect.Height;
        void Label(string value, double x, double y, double anchor = 0)
        {
            var label = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, DisplayTypography.Typeface(), LaboratoryTypography.Caption, text);
            context.DrawText(label, new Point(x - label.Width * anchor, y));
        }
        var grid = new Pen(ThemeBrush(ThemeResourceBindings.PlotGrid, Colors.LightGray), .6);
        foreach (var v in Ticks(view.MinimumY, view.MaximumY, Math.Clamp((int)(rect.Height / 44), 2, 10)))
        {
            if (_grid) context.DrawLine(grid, new Point(rect.Left, Y(v)), new Point(rect.Right, Y(v)));
            Label(v.ToString("G6", CultureInfo.CurrentCulture), rect.Left - 8, Y(v) - LaboratoryTypography.Caption / 2, 1);
        }
        foreach (var w in Ticks(view.MinimumNm, view.MaximumNm, Math.Clamp((int)(rect.Width / 65), 2, 8)))
        {
            if (_grid) context.DrawLine(grid, new Point(X(w), rect.Top), new Point(X(w), rect.Bottom));
            Label((w / (WavelengthUnit == CoatingWavelengthUnit.Micrometers ? 1000 : 1)).ToString("G10", CultureInfo.CurrentCulture), X(w), rect.Bottom + 6, .5);
        }
        context.DrawRectangle(null, new Pen(text, 1), rect);
        Label(_od ? "OD" : _valueUnit == CoatingValueUnit.Percent ? "%" : "1", 5, rect.Top); Label(WavelengthUnit == CoatingWavelengthUnit.Micrometers ? "λ (µm)" : UiLocalization.Text("波长 (nm)"), rect.Center.X, Bounds.Height - LaboratoryTypography.Caption - 4, .5);
        if (_data is null)
        {
            var message = _error ?? (IsPreparing ? "正在计算平均 / s / p 显示光谱…" : "生成膜系或计算光谱后显示真实 R / T / A");
            var hint = new FormattedText(UiLocalization.Text(message), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, DisplayTypography.Typeface(), LaboratoryTypography.Caption, text) { MaxTextWidth = Math.Max(1, rect.Width - 24) };
            context.DrawText(hint, rect.TopLeft + new Vector(12, 20)); return;
        }
        using (context.PushClip(rect))
        {
            if (_targets && _experiment is { } experiment)
            {
                var t = experiment.Target;
                if (t.Kind == DesignKind.NarrowBand)
                {
                    Shade(t.CenterNm - t.FwhmNm / 2, t.CenterNm + t.FwhmNm / 2, Color.Parse("#152196F3"));
                    foreach (var band in t.StopBands ?? []) { Shade(band.MinimumNm, band.MaximumNm, Color.Parse("#15F24E51")); TargetLine(band.MinimumNm, band.MaximumNm, _od ? t.BlockingOd : 100 * Math.Pow(10, -t.BlockingOd), CoatingQuantity.T); }
                }
                else if (!_od) TargetLine(t.MinimumNm, t.MaximumNm, t.Reflectance * 100, CoatingQuantity.R);
            }
            foreach (var (q, p) in SelectedCurves())
            for (var a = 0; a < _data.Angles.Length; a++)
            {
                var geometry = new StreamGeometry(); var samples = Samples(p, a);
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(X(samples[0].WavelengthNanometers), Y(DisplayValue(samples[0]))), false);
                    foreach (var sample in samples.Skip(1)) path.LineTo(new Point(X(sample.WavelengthNanometers), Y(DisplayValue(sample))));
                    path.EndFigure(false);
                }
                var dash = p switch { CoatingCurvePolarization.S => DashStyle.Dash, CoatingCurvePolarization.P => DashStyle.Dot, _ => a == 0 ? null : new DashStyle([2 + a * 2, 3], 0) };
                context.DrawGeometry(null, new Pen(CurveBrush(q), 1.5 * _width, dash), geometry);
                double DisplayValue(CoatingSample sample) => _od ? Math.Min(12, Value(sample.Power, q)) : Value(sample.Power, q);
            }
            if (!_exporting && _hover is { } index) context.DrawLine(new Pen(text, .7, DashStyle.Dash), new Point(X(_data.Average[index].WavelengthNanometers), rect.Top), new Point(X(_data.Average[index].WavelengthNanometers), rect.Bottom));
        }
        void TargetLine(double min, double max, double value, CoatingQuantity q) => context.DrawLine(new Pen(CurveBrush(q), 1, DashStyle.Dash), new Point(X(min), Y(value)), new Point(X(max), Y(value)));
        void Shade(double min, double max, Color color) => context.FillRectangle(new SolidColorBrush(color), new Rect(X(min), rect.Top, Math.Max(0, X(max) - X(min)), rect.Height));
    }
    private static IEnumerable<double> Ticks(double minimum, double maximum, int count)
    {
        var raw = (maximum - minimum) / count; var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power; var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        for (var i = 0; i <= 100; i++) { var value = (Math.Ceiling(minimum / step) + i) * step; if (value > maximum + step * 1e-9) yield break; yield return value; }
    }
}
