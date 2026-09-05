using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RelativeIlluminationAnalysis : BaseAnalysis
{
    private readonly int _rayDensity;
    private readonly int _fieldDensity;
    private readonly int _wavelengthNumber;
    private readonly string _scanDirection;
    private readonly bool _removeVignettingFactors;

    public RelativeIlluminationAnalysis(
        Optic optic,
        int rayDensity = 10,
        int fieldDensity = 21,
        int wavelengthNumber = 0,
        string scanDirection = "+y",
        bool removeVignettingFactors = true) : base(optic)
    {
        _rayDensity = Math.Clamp(rayDensity, 5, 128);
        _fieldDensity = Math.Clamp(fieldDensity, 2, 201);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _scanDirection = AnalysisTrace.NormalizeScanDirection(scanDirection);
        _removeVignettingFactors = removeVignettingFactors;
    }

    public override string Name => "Relative Illumination";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0 || Optic.Wavelengths.Count == 0)
        {
            return Status("No optical data");
        }

        var workingOptic = _removeVignettingFactors
            ? Optic.FromSnapshot(Optic.ToSnapshot())
            : Optic;
        if (_removeVignettingFactors)
        {
            foreach (var field in workingOptic.Fields)
            {
                field.VignetteFactorX = 0;
                field.VignetteFactorY = 0;
            }
        }

        var wavelength = SelectWavelength(workingOptic);
        var maximumField = FieldCoordinates.MaximumRadius(workingOptic.Fields);
        var rawIllumination = new double[_fieldDensity];
        var effectiveFNumbers = new double[_fieldDensity];
        var validRays = new int[_fieldDensity];
        var foldedCells = new int[_fieldDensity];

        for (var index = 0; index < _fieldDensity; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var fraction = index / (_fieldDensity - 1.0);
            var normalizedField = AnalysisTrace.ScanField(_scanDirection, fraction);
            var result = EvaluateField(workingOptic, normalizedField, wavelength.Micrometers, _rayDensity);
            rawIllumination[index] = result.ProjectedCosineArea;
            validRays[index] = result.ValidRays;
            foldedCells[index] = result.FoldedCells;
            effectiveFNumbers[index] = EffectiveFNumber(
                result.ProjectedCosineArea,
                ImageSpaceRefractiveIndex(workingOptic, wavelength.Nanometers));
        }

        var maximumIllumination = rawIllumination.DefaultIfEmpty(0).Max();
        var points = Enumerable.Range(0, _fieldDensity)
            .Select(index => new AnalysisPoint(
                AnalysisTrace.ScanFieldValue(
                    _scanDirection,
                    maximumField * index / (_fieldDensity - 1.0)),
                maximumIllumination > 0 ? rawIllumination[index] / maximumIllumination : 0))
            .ToArray();
        var fieldAxisLabel = ScanFieldAxisLabel(workingOptic, _scanDirection);
        var fieldUnit = workingOptic.FieldDefinition == FieldDefinitionKind.Angle ? "deg" : "mm";
        var series = new AnalysisSeries(
            fieldAxisLabel,
            "Relative Illumination",
            points,
            Name: $"{wavelength.Micrometers:0.0000} µm",
            XQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
            XUnit: AnalysisTrace.FieldAxisUnit(workingOptic),
            YQuantity: AnalysisAxisQuantity.Irradiance,
            YUnit: AnalysisAxisUnit.Dimensionless);

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            [AnalysisTrace.MaximumFieldValueKey(workingOptic)] = maximumField,
            ["FieldUnit"] = fieldUnit,
            ["RayDensity"] = _rayDensity,
            ["FieldDensity"] = _fieldDensity,
            ["WavelengthNumber"] = WavelengthNumber(workingOptic, wavelength),
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["ScanDirection"] = _scanDirection,
            ["RemoveVignettingFactors"] = _removeVignettingFactors,
            ["RawProjectedCosineArea"] = rawIllumination,
            ["RelativeIllumination"] = points.Select(point => point.Y).ToArray(),
            ["EffectiveFNumbers"] = effectiveFNumbers,
            ["ValidRayCounts"] = validRays,
            ["FoldedCellCounts"] = foldedCells,
            ["MaximumProjectedCosineArea"] = maximumIllumination
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"Relative Illumination, λ = {wavelength.Micrometers:0.0000} µm",
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: false,
            DottedGrid: true,
            GridOpacity: 0.35));
    }

    private static string ScanFieldAxisLabel(Optic optic, string scanDirection)
    {
        var axis = scanDirection.EndsWith('x') ? "X" : "Y";
        return optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => $"{axis} Object Height (mm)",
            FieldDefinitionKind.ParaxialImageHeight => $"{axis} Paraxial Image Height (mm)",
            FieldDefinitionKind.RealImageHeight => $"{axis} Real Image Height (mm)",
            _ => $"{axis} Field Angle (deg)"
        };
    }

    internal static double ProjectedCosineArea(
        Optic optic,
        (double Hx, double Hy) normalizedField,
        double wavelengthMicrometers,
        int rayDensity)
    {
        return EvaluateField(optic, normalizedField, wavelengthMicrometers, rayDensity).ProjectedCosineArea;
    }

    private static IlluminationResult EvaluateField(
        Optic optic,
        (double Hx, double Hy) normalizedField,
        double wavelengthMicrometers,
        int rayDensity)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            normalizedField.Hx,
            normalizedField.Hy,
            0,
            0,
            wavelengthMicrometers,
            aimAtStop: true);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single()
            ?? throw new InvalidOperationException("Chief ray did not reach the image surface.");
        var chiefDirection = Normalize(imageSurface.CoordinateSystem.ToLocalDirection(chief.Direction));
        var tangentX = TangentX(chiefDirection);
        var tangentY = Normalize(Cross(chiefDirection, tangentX));
        if (Dot(tangentY, new Vector3D(0, 1, 0)) < 0)
        {
            tangentY = -tangentY;
        }

        // The projected solid angle is an area enclosed by the traced rim of the
        // entrance pupil. Integrating only cells whose four rectangular-grid nodes
        // fall inside the unit circle discards the outer annulus and biases both the
        // effective F/# and its field dependence. Green's theorem gives the complete
        // area directly from the ordered image-space pupil boundary.
        var boundaryCount = Math.Max(48, rayDensity * 24);
        var samples = Enumerable.Range(0, boundaryCount)
            .Select(index =>
            {
                var angle = 2 * Math.PI * index / boundaryCount;
                return new PupilSample(Math.Cos(angle), Math.Sin(angle), 1);
            })
            .ToArray();

        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalizedField.Hx,
            normalizedField.Hy,
            wavelengthMicrometers,
            samples,
            aimAtStop: true);
        var traced = optic.SequentialRayTracer.TraceFinalSamples(bundle);
        var boundary = new PupilNode?[boundaryCount];
        for (var index = 0; index < traced.Count; index++)
        {
            var sample = traced[index];
            if (sample is null
                || sample.SurfaceNumber != imageSurface.Number
                || sample.Vignetted
                || sample.Intensity <= 0)
            {
                continue;
            }

            var localDirection = Normalize(imageSurface.CoordinateSystem.ToLocalDirection(sample.Direction));
            boundary[index] = new PupilNode(
                Dot(localDirection, tangentX),
                Dot(localDirection, tangentY),
                sample.Intensity);
        }

        var area = 0.0;
        var positiveCells = 0;
        var negativeCells = 0;
        for (var index = 0; index < boundaryCount; index++)
        {
            var first = boundary[index];
            var second = boundary[(index + 1) % boundaryCount];
            if (!first.HasValue || !second.HasValue)
            {
                continue;
            }

            var cross = (first.Value.L * second.Value.M) - (second.Value.L * first.Value.M);
            if (cross > 1e-18)
            {
                positiveCells++;
            }
            else if (cross < -1e-18)
            {
                negativeCells++;
            }

            area += 0.25 * Math.Abs(cross) * (first.Value.Intensity + second.Value.Intensity);
        }

        return new IlluminationResult(
            area,
            boundary.Count(node => node.HasValue),
            Math.Min(positiveCells, negativeCells));
    }

    private Wavelength SelectWavelength(Optic optic)
    {
        if (_wavelengthNumber > 0)
        {
            return optic.Wavelengths[Math.Clamp(_wavelengthNumber - 1, 0, optic.Wavelengths.Count - 1)];
        }

        return optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? optic.Wavelengths[0];
    }

    private static int WavelengthNumber(Optic optic, Wavelength wavelength)
    {
        for (var index = 0; index < optic.Wavelengths.Count; index++)
        {
            if (ReferenceEquals(optic.Wavelengths[index], wavelength))
            {
                return index + 1;
            }
        }

        return 1;
    }

    private static double ImageSpaceRefractiveIndex(Optic optic, double wavelengthNanometers)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        return Math.Max(1e-12, imageSurface.MaterialBefore.RefractiveIndex(wavelengthNanometers));
    }

    private static double EffectiveFNumber(double projectedCosineArea, double imageSpaceIndex)
    {
        return projectedCosineArea <= 1e-30
            ? double.PositiveInfinity
            : 0.5 * Math.Sqrt(Math.PI / projectedCosineArea) / imageSpaceIndex;
    }

    private AnalysisData Status(string message) => AnalysisData.Unavailable(Name, message);

    private static Vector3D TangentX(Vector3D normal)
    {
        var reference = Math.Abs(normal.X) < 0.95
            ? new Vector3D(1, 0, 0)
            : new Vector3D(0, 1, 0);
        return Normalize(reference - (normal * Dot(reference, normal)));
    }

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Vector3D Normalize(Vector3D value)
    {
        var length = value.Length;
        return length <= 1e-15 ? new Vector3D(0, 0, 1) : value / length;
    }

    private readonly record struct PupilNode(double L, double M, double Intensity);

    private readonly record struct IlluminationResult(
        double ProjectedCosineArea,
        int ValidRays,
        int FoldedCells);
}
