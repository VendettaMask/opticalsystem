namespace OpticalSystemDesign.Core.NonSequential;

internal static class NonSequentialFieldBudget
{
    internal static void Validate(NonSequentialDocument document)
    {
        foreach (var source in document.Objects.Select(o => o.Parameters).OfType<SourceParameters>())
        {
            if (source.Physics is not { } p) continue;
            p.Polarization?.Validate();
            if (!double.IsFinite(p.PhaseDegrees) || p.CoherenceGroup is { Length: > 128 }
                || p.CoherenceGroup is not null && string.IsNullOrWhiteSpace(p.CoherenceGroup) || p.CoherenceGroup?.Any(char.IsControl) == true)
                throw new ArgumentException("光源需要有限初相位和有效相干组标识（至多 128 字符）。");
        }
        if (!document.TraceSettings.UsePolarization && !document.TraceSettings.CoherentDetection) return;
        var values = NumericSlots(document);
        if (values > NonSequentialDocument.MaximumDetectorPixelWavelengths)
            throw new InvalidOperationException($"偏振/相干探测器状态需要 {values:N0} 个数值槽，超过既有 {NonSequentialDocument.MaximumDetectorPixelWavelengths:N0} 限额。");
    }
    internal static long NumericSlots(NonSequentialDocument document, Guid? detector = null)
    {
        var pixels = document.Objects.Where(o => o.Enabled).Select(o => o.Parameters).OfType<DetectorRectangleParameters>()
            .Sum(d => (long)d.PixelsX * d.PixelsY);
        if (detector is { } id)
        { var p = (DetectorRectangleParameters)document.Objects.Single(o => o.Id == id).Parameters; pixels = (long)p.PixelsX * p.PixelsY; }
        if (!document.TraceSettings.UsePolarization && !document.TraceSettings.CoherentDetection)
            return checked(pixels * document.Wavelengths.Count * 8L);
        var groups = document.Objects.Where(o => o.Enabled && o.Parameters is SourceParameters)
            .SelectMany(o => ((SourceParameters)o.Parameters).WavelengthNumber == 0
                ? Enumerable.Range(1, document.Wavelengths.Count).Select(w => (w, ((SourceParameters)o.Parameters).Physics?.CoherenceGroup ?? o.Id.ToString("N")))
                : [(((SourceParameters)o.Parameters).WavelengthNumber, ((SourceParameters)o.Parameters).Physics?.CoherenceGroup ?? o.Id.ToString("N"))])
            .Distinct().Count();
        // Include mode sums, Stokes planes and immutable output copies in the established numerical-value cap.
        return checked(pixels * (18L * document.Wavelengths.Count + (document.TraceSettings.CoherentDetection ? 40L * Math.Max(1, groups) : 0)));
    }
}
