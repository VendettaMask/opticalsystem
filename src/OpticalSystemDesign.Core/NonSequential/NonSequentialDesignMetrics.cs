namespace OpticalSystemDesign.Core.NonSequential;

internal static class NonSequentialDesignMetrics
{
    internal static NonSequentialMetricValue Evaluate(NonSequentialDocument doc, NonSequentialDetectorFrame frame, NonSequentialDesignTarget target)
    {
        var planes = frame.PowerByWavelength.Where(p => target.WavelengthNumber == 0 || target.WavelengthNumber == p.Key).ToArray();
        var count = frame.PixelsX * frame.PixelsY; var pixels = new double[count];
        foreach (var plane in planes) for (var i = 0; i < count; i++) pixels[i] += plane.Value[i];
        var total = pixels.Sum(); var mean = total / count;
        if (!doc.TryFindObject(target.DetectorId, out var detector) || detector.Parameters is not DetectorRectangleParameters geometry) throw new ArgumentException("Target detector is absent.");
        var area = geometry.WidthMillimeters * geometry.HeightMillimeters / count;
        var moments = frame.MomentsByWavelength?.Where(p => target.WavelengthNumber == 0 || p.Key == target.WavelengthNumber).Select(p => p.Value).ToArray() ?? [];
        double Moment(NonSequentialMetric kind)
        {
            var power = moments.Sum(m => m.PowerWatts);
            if (power <= 0) throw new InvalidOperationException("目标探测器没有光，质心/RMS 未定义。");
            var x = moments.Sum(m => m.PowerWatts * m.CentroidXMillimeters) / power;
            var y = moments.Sum(m => m.PowerWatts * m.CentroidYMillimeters) / power;
            var vx = moments.Sum(m => m.PowerWatts * (m.RmsXMillimeters * m.RmsXMillimeters + Math.Pow(m.CentroidXMillimeters - x, 2))) / power;
            var vy = moments.Sum(m => m.PowerWatts * (m.RmsYMillimeters * m.RmsYMillimeters + Math.Pow(m.CentroidYMillimeters - y, 2))) / power;
            return kind switch { NonSequentialMetric.CentroidX => x, NonSequentialMetric.CentroidY => y,
                NonSequentialMetric.RmsX => Math.Sqrt(Math.Max(0, vx)), NonSequentialMetric.RmsY => Math.Sqrt(Math.Max(0, vy)), _ => Math.Sqrt(Math.Max(0, vx + vy)) };
        }
        double Stokes(Func<NonSequentialStokesFrame, IReadOnlyList<double>> select) => frame.StokesByWavelength is { } s
            ? s.Where(p => target.WavelengthNumber == 0 || p.Key == target.WavelengthNumber).Sum(p => select(p.Value).Sum())
            : doc.TraceSettings.UsePolarization || doc.TraceSettings.CoherentDetection ? 0 : throw new NotSupportedException("Stokes 目标需要偏振追迹。");
        var value = target.Metric switch
        {
            NonSequentialMetric.TotalPower => total,
            NonSequentialMetric.PixelPower => (uint)target.PixelIndex < count ? pixels[target.PixelIndex] : throw new ArgumentException("Pixel target index is outside the detector."),
            NonSequentialMetric.MeanIrradiance => mean / area,
            NonSequentialMetric.PeakIrradiance => pixels.Max() / area,
            NonSequentialMetric.RelativeUniformityRms => mean > 0 ? Math.Sqrt(pixels.Average(v => Math.Pow(v / mean - 1, 2))) : throw new InvalidOperationException("无光场的相对均匀度未定义。"),
            NonSequentialMetric.CentroidX or NonSequentialMetric.CentroidY or NonSequentialMetric.RmsX or NonSequentialMetric.RmsY or NonSequentialMetric.RmsRadius => Moment(target.Metric),
            NonSequentialMetric.CoherentPower => frame.CoherentByWavelength is { } c
                ? c.Where(p => target.WavelengthNumber == 0 || target.WavelengthNumber == p.Key).Sum(p => p.Value.PowerWatts.Sum())
                : doc.TraceSettings.CoherentDetection ? 0 : throw new NotSupportedException("相干目标需要声明相干探测。"),
            NonSequentialMetric.StokesQ => Stokes(s => s.Q), NonSequentialMetric.StokesU => Stokes(s => s.U), NonSequentialMetric.StokesV => Stokes(s => s.V),
            _ => throw new ArgumentException("Unknown non-sequential objective.")
        };
        if (!double.IsFinite(value)) throw new ArithmeticException("Invalid detector objective value.");
        var unit = target.Metric switch
        {
            NonSequentialMetric.CentroidX or NonSequentialMetric.CentroidY or NonSequentialMetric.RmsX or NonSequentialMetric.RmsY or NonSequentialMetric.RmsRadius => "mm",
            NonSequentialMetric.MeanIrradiance or NonSequentialMetric.PeakIrradiance => "W/mm²",
            NonSequentialMetric.RelativeUniformityRms => "1", _ => "W"
        };
        return new(target, unit, value, Math.Abs(value - target.Target) <= target.Tolerance);
    }
}
