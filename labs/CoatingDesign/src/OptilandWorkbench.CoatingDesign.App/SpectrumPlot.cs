using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OptilandWorkbench.CoatingDesign.Engine;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core.Coatings;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class SpectrumPlot : Control
{
    private Experiment? _experiment;
    private bool _od;
    public SpectrumPlot()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        PointerMoved += (_, e) =>
        {
            if (_experiment?.Result is not { } result) return;
            var x = e.GetPosition(this).X;
            var lambda = _experiment.Target.MinimumNm + Math.Clamp((x - 52) / Math.Max(1, Bounds.Width - 76), 0, 1) * (_experiment.Target.MaximumNm - _experiment.Target.MinimumNm);
            var point = result.Spectrum.MinBy(s => Math.Abs(s.WavelengthNanometers - lambda))!;
            ToolTip.SetTip(this, $"{point.WavelengthNanometers:0.####} nm   R {point.Power.Reflectance:P3}   T {point.Power.Transmittance:P3}   A {point.Power.Absorptance:P3}   OD {point.Power.OpticalDensity:G8}（最近实算采样）");
        };
    }
    public void SetOpticalDensity(bool value) { _od = value; InvalidateVisual(); }
    // Physical data colors, separate from UI states.
    private static readonly IBrush[] CurveColors = [new ImmutableSolidColorBrush(Color.Parse("#B94338")), new ImmutableSolidColorBrush(Color.Parse("#167944")), new ImmutableSolidColorBrush(Color.Parse("#315EB5"))];
    public void SetExperiment(Experiment? experiment) { _experiment = experiment; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = this.TryFindResource(ThemeResourceBindings.PlotBackground, out var fill) ? (IBrush)fill! : Brushes.White;
        context.FillRectangle(background, new Rect(Bounds.Size));
        var text = this.TryFindResource(ThemeResourceBindings.PlotText, out var foreground) ? (IBrush)foreground! : Brushes.Black;
        var axis = new Pen(text, 1);
        var rect = new Rect(52, 24, Math.Max(1, Bounds.Width - 76), Math.Max(1, Bounds.Height - 64));
        void Label(string value, double x, double y) => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default), LaboratoryTypography.Caption, text), new Point(x, y));
        context.DrawLine(axis, rect.BottomLeft, rect.BottomRight); context.DrawLine(axis, rect.TopLeft, rect.BottomLeft);
        Label(_od ? "OD（图上限 12）" : "能量 (%)", 4, 2); Label("波长 (nm)", Math.Max(56, Bounds.Width / 2 - 24), Bounds.Height - 18);
        if (_experiment?.Result is not { } result) { Label("生成膜系或计算光谱后显示真实 R / T / A", rect.Left + 16, rect.Top + 32); return; }
        var target = _experiment.Target;
        double X(double lambda) => rect.Left + (lambda - target.MinimumNm) / (target.MaximumNm - target.MinimumNm) * rect.Width;
        double Y(double value) => rect.Bottom - value / (_od ? 12 : 100) * rect.Height;
        for (var i = 0; i <= 4; i++)
        {
            Label((i * (_od ? 3 : 25)).ToString(CultureInfo.InvariantCulture), 18, Y(i * (_od ? 3 : 25)) - 6);
            var lambda = target.MinimumNm + (target.MaximumNm - target.MinimumNm) * i / 4;
            Label(lambda.ToString("0.##", CultureInfo.InvariantCulture), X(lambda) - 12, rect.Bottom + 6);
        }
        using (context.PushClip(rect))
        {
            var shade = new SolidColorBrush(Color.Parse("#20315EB5"));
            if (target.Kind == DesignKind.NarrowBand)
            {
                context.FillRectangle(shade, new Rect(X(target.CenterNm - target.FwhmNm / 2), rect.Top,
                    X(target.CenterNm + target.FwhmNm / 2) - X(target.CenterNm - target.FwhmNm / 2), rect.Height));
                foreach (var band in target.StopBands ?? [])
                {
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#20B94338")), new Rect(X(band.MinimumNm), rect.Top, X(band.MaximumNm) - X(band.MinimumNm), rect.Height));
                    context.DrawLine(new Pen(CurveColors[1], 1, DashStyle.Dash), new Point(X(band.MinimumNm), Y(_od ? target.BlockingOd : 100 * Math.Pow(10, -target.BlockingOd))),
                        new Point(X(band.MaximumNm), Y(_od ? target.BlockingOd : 100 * Math.Pow(10, -target.BlockingOd))));
                }
            }
            else if (!_od) context.DrawLine(new Pen(CurveColors[0], 1, DashStyle.Dash), new Point(rect.Left, Y(target.Reflectance * 100)), new Point(rect.Right, Y(target.Reflectance * 100)));
            if (_od)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(X(result.Spectrum[0].WavelengthNanometers), Y(Math.Min(12, result.Spectrum[0].Power.OpticalDensity))), false);
                    foreach (var point in result.Spectrum.Skip(1)) path.LineTo(new Point(X(point.WavelengthNanometers), Y(Math.Min(12, point.Power.OpticalDensity))));
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(CurveColors[1], 1.5), geometry);
            }
            foreach (var series in _od ? [] : ThinFilmSpectrum.ToSeries(result.Spectrum))
            {
                if (series.XQuantity != AnalysisAxisQuantity.Wavelength || series.XUnit != AnalysisAxisUnit.Nanometer
                    || series.YQuantity != AnalysisAxisQuantity.EnergyFraction || series.YUnit != AnalysisAxisUnit.Percent) continue;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(X(series.Points[0].X), Y(series.Points[0].Y)), false);
                    foreach (var point in series.Points.Skip(1)) path.LineTo(new Point(X(point.X), Y(point.Y)));
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(CurveColors[series.ColorIndex], 1.5), geometry);
            }
        }
        Label(_od ? "OD · 图形在 12 饱和，计算 / 导出保留真实数值" : "R 反射   T 透射   A 吸收    虚线 / 阴影：目标", rect.Left, 3);
    }
}
