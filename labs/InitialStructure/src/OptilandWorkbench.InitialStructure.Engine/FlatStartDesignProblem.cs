using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Parameter/target adapter only. All optical quantities come from formal Core.</summary>
internal sealed class FlatStartDesignProblem
{
    private readonly InitialStructureSpecification _specification;
    private readonly int _elementCount;
    private readonly FlatStartProblem _geometry;
    private readonly OpticSnapshot _template;
    private readonly double _focalLength;
    private readonly double _maximumCurvature;
    private int SurfaceCount => 2 * _elementCount;
    public int Dimension => 2 * SurfaceCount - (_specification.FlatStart!.FixedBackFocusMillimeters.HasValue ? 1 : 0);
    public long TracedRayCount { get; private set; }
    public static DesignStage FullStage { get; } = new(1, 1, true);

    public FlatStartDesignProblem(InitialStructureSpecification specification, int elementCount, OpticSnapshot template)
    {
        _specification = specification;
        _elementCount = elementCount;
        _geometry = new(specification, elementCount, .3);
        _template = template;
        _focalLength = specification.EffectiveFocalLengthMillimeters;
        _maximumCurvature = .9 * _focalLength / template.Surfaces.Skip(1).Take(SurfaceCount).Max(surface => surface.SemiDiameter);
    }

    public double[] Vector(OpticSnapshot snapshot)
    {
        var vector = new double[Dimension];
        for (var index = 0; index < SurfaceCount; index++)
        {
            var surface = snapshot.Surfaces[index + 1];
            vector[index] = surface.Radius == 0 ? 0 : _focalLength / surface.Radius;
            if (SurfaceCount + index < Dimension) vector[SurfaceCount + index] = surface.Thickness / _focalLength;
        }
        return Project(vector);
    }

    public double[] Project(IReadOnlyList<double> vector)
    {
        if (vector.Count != Dimension || vector.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Invalid design parameter vector.", nameof(vector));
        var result = vector.ToArray();
        for (var index = 0; index < SurfaceCount; index++)
            result[index] = Math.Clamp(result[index], -_maximumCurvature, _maximumCurvature);
        var fixedBack = _specification.FlatStart!.FixedBackFocusMillimeters ?? 0;
        var minimumTotal = fixedBack;
        var slackTotal = 0.0;
        for (var index = SurfaceCount; index < Dimension; index++)
        {
            var minimum = MinimumThickness(index - SurfaceCount);
            result[index] = Math.Clamp(result[index], minimum / _focalLength, _specification.MaximumTrackLengthMillimeters / _focalLength);
            minimumTotal += minimum;
            slackTotal += result[index] * _focalLength - minimum;
        }
        var available = Math.Max(0, _specification.MaximumTrackLengthMillimeters - minimumTotal);
        if (slackTotal > available)
            for (var index = SurfaceCount; index < Dimension; index++)
            {
                var minimum = MinimumThickness(index - SurfaceCount) / _focalLength;
                result[index] = minimum + (result[index] - minimum) * available / slackTotal;
            }
        return result;
    }

    private double MinimumThickness(int surfaceIndex) => surfaceIndex == SurfaceCount - 1
        ? _specification.MinimumBackFocusMillimeters
        : surfaceIndex % 2 == 0 ? _specification.MinimumCenterThicknessMillimeters : _specification.MinimumAirGapMillimeters;

    public Optic CreateOptic(IReadOnlyList<double> values, DesignStage stage)
    {
        var vector = Project(values);
        var optic = Optic.FromSnapshot(_template);
        optic.Aperture.Value = _focalLength / _specification.FNumber * stage.PupilFraction;
        for (var index = 0; index < SurfaceCount; index++)
        {
            var surface = optic.SurfaceGroup.Items[index + 1];
            surface.Radius = vector[index] == 0 ? 0 : _focalLength / vector[index];
            surface.Thickness = SurfaceCount + index < Dimension
                ? vector[SurfaceCount + index] * _focalLength : _specification.FlatStart!.FixedBackFocusMillimeters!.Value;
            surface.ThicknessVariable = SurfaceCount + index < Dimension;
        }
        optic.SurfaceGroup.Renumber();
        return optic;
    }

    public DesignEvaluation Evaluate(Optic optic, DesignStage stage, bool dense, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var violations = _geometry.GeometryViolations(optic).ToList();
        var geometryValid = violations.Count == 0;
        var power = optic.Paraxial.EstimateOpticalPower();
        var efl = optic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = optic.Paraxial.EstimateFNumber();
        var residuals = new List<double> { double.IsFinite(power)
            ? (power * _focalLength - 1) / _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance : 1e3 };
        Upper("target.focal-length", efl > 0 ? Math.Abs(efl / _focalLength - 1) : null,
            _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance);
        Upper("target.f-number", fNumber > 0 ? Math.Abs(fNumber * stage.PupilFraction / _specification.FNumber - 1) : null,
            _specification.FlatStart.FNumberRelativeTolerance);
        double[] fields = _specification.MaximumFieldAngleDegrees == 0 || stage.FieldFraction == 0 ? [0]
            : dense ? [0, .25, .5, .7, .85, 1] : [0, .5, 1];
        var pupils = Pupils(dense).ToArray();
        var fieldResults = new List<DesignFieldEvaluation>();
        var wavelengthNumber = stage.AllWavelengths ? 0 : optic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1;
        var waves = optic.Wavelengths.Where(wave => wave.Weight > 0 && (stage.AllWavelengths || wave.IsPrimary)).ToArray();
        var allRaysValid = geometryValid;
        foreach (var field in fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = field * stage.FieldFraction;
            SampledSpotResult? result = null;
            if (geometryValid)
            {
                result = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, normalized, pupils, wavelengthNumber,
                    reference: "centroid", includeSurfaceTransmission: false, cancellationToken: cancellationToken);
                TracedRayCount += result.RayCount;
            }
            var attempted = pupils.Length * waves.Length;
            var valid = result is null ? 0 : result.RayCount - result.VignettedRayCount;
            var fieldIndex = fieldResults.Count;
            var waveResults = waves.Select((wave, index) => new DesignWavelengthEvaluation(wave.Nanometers, pupils.Length,
                result?.Wavelengths[index].Rays.Count ?? 0)).ToArray();
            fieldResults.Add(new(normalized, normalized * _specification.MaximumFieldAngleDegrees, attempted, valid,
                result?.Metrics?.RmsSpotRadius, result?.Metrics?.MaximumSpotRadius, waveResults));
            Upper($"field.{fieldIndex}.lost-fraction", 1 - (double)valid / attempted, 1 - _specification.FlatStart.MinimumValidRayFraction);
            Upper($"field.{fieldIndex}.rms", result?.Metrics?.RmsSpotRadius, _specification.MaximumRmsSpotRadiusMillimeters);
            Upper($"field.{fieldIndex}.maximum-radius", result?.Metrics?.MaximumSpotRadius, _specification.MaximumSpotRadiusMillimeters);
            // The formal maximum-radius metric is also an active search target, not merely an exit gate.
            residuals.Add(result?.Metrics is { } metrics
                ? Math.Max(0, metrics.MaximumSpotRadius / _specification.MaximumSpotRadiusMillimeters - 1) : 10);
            foreach (var (wave, index) in waveResults.Select((wave, index) => (wave, index)))
                Upper($"field.{fieldIndex}.wave.{index}.lost-fraction", 1 - (double)wave.ValidRays / wave.AttemptedRays,
                    1 - _specification.FlatStart.MinimumValidRayFraction);
            allRaysValid &= valid == attempted;
            if (valid != attempted || result?.Metrics is null)
                residuals.AddRange(Enumerable.Repeat(10.0, 2 * attempted));
            else
            {
                var totalWeight = result.Wavelengths.Sum(wave => wave.SpectralWeight * wave.Rays.Sum(ray => ray.Weight));
                foreach (var wave in result.Wavelengths)
                    foreach (var ray in wave.Rays)
                    {
                        var scale = Math.Sqrt(wave.SpectralWeight * ray.Weight / (totalWeight * fields.Length))
                            / _specification.MaximumRmsSpotRadiusMillimeters;
                        residuals.Add(ray.X * scale);
                        residuals.Add(ray.Y * scale);
                    }
            }
        }
        return new()
        {
            EffectiveFocalLengthMillimeters = efl > 0 && double.IsFinite(efl) ? efl : null,
            FNumber = fNumber > 0 && double.IsFinite(fNumber) ? fNumber : null,
            Merit = residuals.Sum(value => value * value),
            Residuals = residuals,
            Fields = fieldResults,
            Violations = violations,
            IsFeasible = allRaysValid
        };

        void Upper(string code, double? actual, double limit)
        {
            if (actual is null || !double.IsFinite(actual.Value) || actual > limit + 1e-12)
                violations.Add(new(code, ConstraintSeverity.Hard, "The requested design bound was not met.",
                    actual is { } value && double.IsFinite(value) ? value : null, limit));
        }
    }

    internal static IEnumerable<PupilSample> Pupils(bool dense)
    {
        yield return new(0, 0, 1);
        var rings = dense ? 6 : 3;
        var angles = dense ? 16 : 8;
        for (var ring = 1; ring <= rings; ring++)
            for (var angle = 0; angle < angles; angle++)
            {
                var theta = 2 * Math.PI * angle / angles;
                yield return new((double)ring / rings * Math.Cos(theta), (double)ring / rings * Math.Sin(theta), 1);
            }
    }
}
