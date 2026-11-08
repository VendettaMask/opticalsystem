using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class FirstOrderAnalysis : BaseAnalysis
{
    /// <summary>Vertex powers and ordinary scalar indices; excludes affine chief-ray walk-off.</summary>
    public const string ReferenceConvention = "CenteredVertexOrdinaryIndexV1";
    public FirstOrderAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "First Order";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Any(surface => surface.Geometry is PolygonGroupGeometry))
        {
            const string reason = "A mixed sequential/non-sequential polygon group has no general paraxial equivalent. EFL and F-number are not applicable; use the physical ray and angular image-space analyses.";
            var values = new Dictionary<string, object>
            {
                ["CardinalParametersApplicable"] = false,
                ["ApplicabilityReason"] = reason,
                ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
                ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
            };
            if (Optic.Aperture.Kind == ApertureKind.EntrancePupilDiameter)
                values["EntrancePupilDiameter"] = Optic.Aperture.Value;
            return new AnalysisData(Name, values, ReportText: reason);
        }
        if (AxialMirrorDirectionJump() is { } jump)
        {
            const string reason = "The axial pupil crosses a mirror-state boundary with distinct limiting ray directions. A two-sided first-order derivative is not defined.";
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["CardinalParametersApplicable"] = false,
                ["ApplicabilityReason"] = reason,
                ["AxialDirectionJumpDegrees"] = jump,
                ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
                ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
            }, ReportText: reason, Outcome: AnalysisOutcome.NotApplicable, OutcomeReason: reason);
        }
        var (effective, imageSpace, power) = Optic.Paraxial.EstimateFirstOrderFocalLengths();
        var zeroPower = power == 0;
        var diameter = zeroPower ? 0 : Optic.Paraxial.EstimateEntrancePupilDiameter();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = effective,
            ["ImageSpaceEffectiveFocalLength"] = imageSpace,
            ["FNumber"] = zeroPower ? double.PositiveInfinity : diameter > 0 ? Math.Abs(effective) / diameter : 0,
            ["OpticalPowerInverseMillimeters"] = power,
            ["EffectiveFocalLengthStatus"] = zeroPower ? "Infinite" : "Finite",
            ["CardinalParametersApplicable"] = true,
            ["FirstOrderReferenceConvention"] = ReferenceConvention,
            ["IncludesChiefRayWalkOff"] = false,
            ["DirectionalMediumUsesOrdinaryIndex"] = Optic.SurfaceGroup.Items.Any(s =>
                s.MaterialAfter is UniaxialMaterial { TraceExtraordinary: true, ParaxialIgnore: false }),
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }

    private double? AxialMirrorDirectionJump()
    {
        var surfaces = Optic.SurfaceGroup.Items;
        if (!surfaces.Any(surface => surface.Geometry is TernaryMirrorArrayGeometry)
            || Optic.RayAimingEnabled || !FocalImageDomain.IsCollimatedPlaneTrain(Optic, allowSegmentedMirrors: true))
            return null;
        var wavelength = Optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null) return null;

        var tracer = Optic.SequentialRayTracer;
        RealRayBundle Launch(double x, double y) => tracer.RayGenerator.GenerateGeneric(0, 0, x, y, wavelength.Micrometers, false);
        var chief = Launch(0, 0);
        if (Direction(chief) is null) return null;

        var atBoundary = false;
        for (var index = 1; index < surfaces.Count; index++)
        {
            var surface = surfaces[index];
            if (surface is not { IsReflective: true, Geometry: TernaryMirrorArrayGeometry array }
                || tracer.TraceToSurface(chief.Rays.Single(), index) is not { Vignetted: false } sample)
                continue;
            var local = surface.CoordinateSystem.ToLocalPoint(sample.Position);
            if (Math.Abs(local.X) >= array.WidthX / 2 || Math.Abs(local.Y) >= array.WidthY / 2) continue;
            if ((array.AddressingMode != 0 && AtInteriorGridLine(local.X, array.WidthX, array.PixelsX))
                || (array.AddressingMode != 1 && AtInteriorGridLine(local.Y, array.WidthY, array.PixelsY)))
            {
                atBoundary = true;
                break;
            }
        }
        if (!atBoundary) return null;

        foreach (var horizontal in new[] { false, true })
        {
            (Vector3D Negative, Vector3D Positive)? limits = null;
            foreach (var step in new[] { .01, .001, .0001 })
            {
                var minus = Direction(Launch(horizontal ? -step : 0, horizontal ? 0 : -step));
                var plus = Direction(Launch(horizontal ? step : 0, horizontal ? 0 : step));
                if (minus is not { } negative || plus is not { } positive || (positive - negative).Length < 1e-6
                    || limits is { } previous && ((negative - previous.Negative).Length > 1e-9
                        || (positive - previous.Positive).Length > 1e-9))
                {
                    limits = null;
                    break;
                }
                limits = (negative, positive);
            }
            if (limits is { } pair)
                return 2 * Math.Asin(Math.Min(1, (pair.Positive - pair.Negative).Length / 2)) * 180 / Math.PI;
        }
        return null;

        Vector3D? Direction(RealRayBundle bundle) => tracer.TraceFinalSamples(bundle).Single() is { Vignetted: false, Intensity: > 0 } ray
            && double.IsFinite(ray.Direction.Length) && ray.Direction.Length > 0 ? ray.Direction / ray.Direction.Length : null;

        static bool AtInteriorGridLine(double coordinate, double width, int cells)
        {
            if (cells < 2) return false;
            var pitch = width / cells;
            var line = Math.Round((coordinate + width / 2) / pitch);
            // Finite probes can straddle a nearby boundary even when the axial
            // ray remains in a smooth cell. Require the axial hit itself on the line.
            return line > 0 && line < cells && coordinate == line * pitch - width / 2;
        }
    }
}
