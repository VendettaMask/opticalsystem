using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

internal static class AiryDiskSupport
{
    public static double CalculateRadius(
        Optic optic,
        IReadOnlyList<(double Hx, double Hy)> fields,
        IReadOnlyList<Wavelength> wavelengths,
        bool enabled)
    {
        if (!enabled || fields.Count == 0 || wavelengths.Count == 0)
        {
            return 0;
        }

        var wavelength = wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? wavelengths[0];
        if (ImageSpaceAnalysisSupport.UsesAfocalAngleCoordinates(optic))
        {
            return ImageSpaceAnalysisSupport.AfocalAiryRadiusMilliradians(optic, wavelength);
        }

        var workingFNumber = DiffractionEngine.WorkingFNumber(optic, fields[0], wavelength);
        return 1.22 * wavelength.Micrometers * workingFNumber / 1000.0;
    }

    public static AnalysisSeries CreateSeries(
        double radius,
        ImageSpaceCoordinateDescriptor? descriptor = null)
    {
        var imageSpace = descriptor ?? new ImageSpaceCoordinateDescriptor(
            ImageSpaceCoordinateKind.ImageHeight,
            "mm",
            "µm",
            AnalysisAxisQuantity.ImageHeight,
            AnalysisAxisUnit.Millimeter,
            MetricScale: 1_000);
        var points = Enumerable.Range(0, 65)
            .Select(index =>
            {
                var angle = 2 * Math.PI * index / 64;
                return new AnalysisPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
            })
            .ToArray();
        return new AnalysisSeries(
            $"X ({imageSpace.AxisUnitLabel})",
            $"Y ({imageSpace.AxisUnitLabel})",
            points,
            AnalysisSeriesKind.Line,
            "艾里斑",
            ColorIndex: 7,
            LineWidth: 1.2,
            XQuantity: imageSpace.Quantity,
            XUnit: imageSpace.Unit,
            YQuantity: imageSpace.Quantity,
            YUnit: imageSpace.Unit);
    }
}
