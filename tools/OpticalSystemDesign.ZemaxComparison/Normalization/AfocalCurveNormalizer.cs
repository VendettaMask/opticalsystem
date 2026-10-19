using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class AfocalCurveNormalizer
{
    private static string Id(int field, int component, CanonicalAnalysisRequest request) =>
        (request.Field == 0 ? $"field:{field + 1}:" : "") + (component == 0 ? "tangential" : "sagittal");

    private static Axis X(CanonicalAnalysisRequest request) => request.CanonicalAnalysisKey == "MTF"
        ? new("SpatialFrequency", request.ImageSpaceAfocal ? "CyclesPerMilliradian" : "CyclesPerMillimeter")
        : new("PupilCoordinate", "Dimensionless");
    private static Axis Y(CanonicalAnalysisRequest request) => request.CanonicalAnalysisKey == "MTF"
        ? new("Modulation", "Dimensionless") : new("IncidentAngle", "Degree");

    public static NumericResult Workbench(AnalysisData data, CanonicalAnalysisRequest request)
    {
        var result = new NumericResult { Semantics = "Typed afocal angle/frequency contract; no focal length substitution." };
        var curves = data.PlotPanes is { Count: > 0 } ? data.PlotPanes.SelectMany(p => p.Series).ToArray() : data.PlotSeries.ToArray();
        var fields = request.Field == 0 ? request.FieldCount : 1;
        if (curves.Length != fields * 2) throw new InvalidDataException("Expected ordered T/S curves for every requested field.");
        for (var i = 0; i < curves.Length; i++)
        {
            var curve = curves[i];
            var item = new Series1DResult(Id(i / 2, i % 2, request), curve.Name,
                curve.Points.Select(p => p.X).ToArray(), curve.Points.Select(p => p.Y).ToArray(),
                new(curve.XQuantity.ToString(), curve.XUnit.ToString()), new(curve.YQuantity.ToString(), curve.YUnit.ToString()));
            result.Series.Add(PhysicalNormalization.Convert(item, X(request), Y(request), result.Transformations));
        }
        return result;
    }

    public static NumericResult Zemax(JsonElement root, CanonicalAnalysisRequest request)
    {
        var mtf = request.CanonicalAnalysisKey == "MTF";
        var fields = request.Field == 0 ? request.FieldCount : 1;
        var groups = root.GetProperty("dataSeries");
        if (groups.GetArrayLength() != (mtf ? fields : fields * 2))
            throw new InvalidDataException("Unexpected native T/S field group count.");
        var result = new NumericResult { Semantics = "Native typed afocal unit readback; ordered field/T/S columns, no label parsing." };
        for (var field = 0; field < fields; field++)
            for (var component = 0; component < 2; component++)
            {
                var group = groups[mtf ? field : field * 2 + component];
                var rows = group.GetProperty("y");
                var width = rows[0].GetArrayLength();
                if (rows.EnumerateArray().Any(row => row.GetArrayLength() != width))
                    throw new InvalidDataException("Ragged native curve columns.");
                var active = Enumerable.Range(0, width).Where(column => rows.EnumerateArray()
                    .Any(row => row[column].ValueKind == JsonValueKind.Number)).ToArray();
                if (active.Length != (mtf ? 2 : 1)) throw new InvalidDataException("Unexpected native active wavelength/component columns.");
                var column = active[mtf ? component : 0];
                var nativeX = mtf && request.ImageSpaceAfocal
                    ? new Axis("SpatialFrequency", AfocalAnalysisContracts.FrequencyUnit(request)) : X(request);
                var nativeY = mtf ? Y(request) : new Axis("IncidentAngle", request.NativeAfocalAngleUnit!);
                var series = new Series1DResult(Id(field, component, request), "Native ordered T/S",
                    group.GetProperty("x").EnumerateArray().Select(value => value.GetDouble()).ToArray(),
                    rows.EnumerateArray().Select(row => row[column].ValueKind == JsonValueKind.Number ? row[column].GetDouble() : double.NaN).ToArray(), nativeX, nativeY);
                result.Series.Add(PhysicalNormalization.Convert(series, X(request), Y(request), result.Transformations));
            }
        return result;
    }
}
