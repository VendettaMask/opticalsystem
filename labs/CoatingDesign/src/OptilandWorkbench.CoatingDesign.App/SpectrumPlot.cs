using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.CoatingDesign.Engine;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Coatings;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class SpectrumPlot : Control
{
    private Experiment? _experiment;
    // Physical data colors, separate from UI states.
    private static readonly IBrush[] CurveColors = [new SolidColorBrush(Color.Parse("#B94338")), new SolidColorBrush(Color.Parse("#167944")), new SolidColorBrush(Color.Parse("#315EB5"))];
    public void SetExperiment(Experiment? experiment) { _experiment = experiment; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var text = this.TryFindResource("CoatingText", out var foreground) ? (IBrush)foreground! : Brushes.Black;
        var axis = new Pen(text, 1);
        var rect = new Rect(52, 24, Math.Max(1, Bounds.Width - 76), Math.Max(1, Bounds.Height - 64));
        void Label(string value, double x, double y) => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default), LaboratoryTypography.Caption, text), new Point(x, y));
        context.DrawLine(axis, rect.BottomLeft, rect.BottomRight); context.DrawLine(axis, rect.TopLeft, rect.BottomLeft);
        Label("能量 (%)", 4, 2); Label("波长 (nm)", Math.Max(56, Bounds.Width / 2 - 24), Bounds.Height - 18);
        if (_experiment?.Result is not { } result) { Label("生成膜系或计算光谱后显示真实 R / T / A", rect.Left + 16, rect.Top + 32); return; }
        var target = _experiment.Target;
        double X(double lambda) => rect.Left + (lambda - target.MinimumNm) / (target.MaximumNm - target.MinimumNm) * rect.Width;
        double Y(double percent) => rect.Bottom - percent / 100 * rect.Height;
        for (var i = 0; i <= 4; i++)
        {
            Label((i * 25).ToString(CultureInfo.InvariantCulture), 18, Y(i * 25) - 6);
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
                    context.DrawLine(new Pen(CurveColors[1], 1, DashStyle.Dash), new Point(X(band.MinimumNm), Y(100 * Math.Pow(10, -target.BlockingOd))),
                        new Point(X(band.MaximumNm), Y(100 * Math.Pow(10, -target.BlockingOd))));
                }
            }
            else context.DrawLine(new Pen(CurveColors[0], 1, DashStyle.Dash), new Point(rect.Left, Y(target.Reflectance * 100)), new Point(rect.Right, Y(target.Reflectance * 100)));
            foreach (var series in ThinFilmSpectrum.ToSeries(result.Spectrum))
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
        Label("R 反射   T 透射   A 吸收    虚线 / 阴影：目标", rect.Left, 3);
    }
}
