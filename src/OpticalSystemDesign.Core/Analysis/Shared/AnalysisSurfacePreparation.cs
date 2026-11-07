using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Isolated focal-analysis system, retaining refraction at the requested surface.</summary>
internal static class AnalysisSurfacePreparation
{
    internal static Optic Prepare(Optic source, int number, bool zeroMeansImage = false)
    {
        if (number == -1 || zeroMeansImage && number == 0
            || number == source.SurfaceGroup.Items.LastOrDefault()?.Number) return source;
        var index = source.SurfaceGroup.Items.ToList().FindIndex(s => s.Number == number);
        if (index <= 0) throw new ArgumentOutOfRangeException(nameof(number), "Select a physical or image surface.");
        var primary = source.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? source.Wavelengths.FirstOrDefault()
            ?? throw new InvalidOperationException("The system has no wavelength for intermediate-image preparation.");
        var stop = source.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
        if (stop > index && source.RayAimingEnabled)
            throw new NotSupportedException("Intermediate surfaces before the stop with ray aiming require a physical intermediate pupil model.");
        var clone = source.CreateMeritEvaluationCopy();
        if (source.FieldDefinition is FieldDefinitionKind.RealImageHeight or FieldDefinitionKind.ParaxialImageHeight)
        {
            var infinite = ObjectConjugate.IsInfinite(source.SurfaceGroup.Items[0]);
            var fields = SpotAnalysisEngine.DefinedFields(source);
            for (var i = 0; i < fields.Count; i++)
            {
                var ray = source.SequentialRayTracer.RayGenerator.GenerateGeneric(fields[i].Hx, fields[i].Hy,
                    0, 0, primary.Micrometers, source.RayAimingEnabled).Rays[0];
                var origin = source.SurfaceGroup.Items[0].CoordinateSystem.ToLocalPoint(ray.Origin);
                var direction = source.SurfaceGroup.Items[0].CoordinateSystem.ToLocalDirection(ray.Direction);
                clone.Fields[i].X = infinite ? Math.Atan2(direction.X, direction.Z) * 180 / Math.PI : origin.X;
                clone.Fields[i].Y = infinite ? Math.Atan2(direction.Y, direction.Z) * 180 / Math.PI : origin.Y;
            }
            clone.FieldDefinition = infinite ? FieldDefinitionKind.Angle : FieldDefinitionKind.ObjectHeight;
        }
        if (stop > index)
        {
            if (source.Aperture.Kind == ApertureKind.ObjectConeAngle)
                throw new NotSupportedException("An intermediate surface before the stop needs the original object-cone pupil.");
            var pupilZ = source.Paraxial.EstimateEntrancePupilLocation();
            var firstZ = source.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z;
            var pupilDiameter = source.Paraxial.EstimateEntrancePupilDiameter();
            if (!double.IsFinite(pupilZ) || !double.IsFinite(pupilDiameter) || pupilDiameter <= 0)
                throw new NotSupportedException("A finite original entrance pupil is required before the stop.");
            if (clone.Aperture.Kind != ApertureKind.NumericalAperture)
            {
                clone.Aperture.Value = pupilDiameter;
                clone.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
            }
            foreach (var surface in clone.SurfaceGroup.Items) surface.IsStop = false;
            var dummy = new OpticalSurface
            {
                Label = "Analysis entrance pupil",
                IsStop = true,
                Radius = double.PositiveInfinity,
                Thickness = firstZ - pupilZ,
                Material = clone.SurfaceGroup.Items[0].Material,
                MaterialBefore = clone.SurfaceGroup.Items[0].MaterialAfter.Clone(),
                MaterialAfter = clone.SurfaceGroup.Items[0].MaterialAfter.Clone(),
                InteractionModel = new CoordinateReferenceInteractionModel(),
                SemiDiameter = pupilDiameter / 2
            };
            if (!ObjectConjugate.IsInfinite(clone.SurfaceGroup.Items[0])) clone.SurfaceGroup.Items[0].Thickness = pupilZ;
            clone.SurfaceGroup.Items.Insert(1, dummy); index++;
        }
        // Native marginal-height solves use the primary wavelength, independent
        // of the wavelength selected for the displayed analysis.
        var marginal = clone.ImageSpaceAfocal ? null : clone.Paraxial.MarginalRay(primary.Micrometers);
        var thickness = 0d;
        if (marginal is not null)
        {
            var height = marginal.Heights[index][0]; var slope = marginal.Slopes[index][0];
            if (!double.IsFinite(slope) || Math.Abs(slope) < 1e-15)
                throw new NotSupportedException("The selected intermediate surface has no finite paraxial focus.");
            thickness = -height / slope;
        }
        while (clone.SurfaceGroup.Items.Count > index + 1) clone.SurfaceGroup.Items.RemoveAt(clone.SurfaceGroup.Items.Count - 1);
        var selected = clone.SurfaceGroup.Items[index]; selected.Thickness = thickness;
        clone.SurfaceGroup.Items.Add(new OpticalSurface
        {
            Label = "Intermediate paraxial image",
            Radius = double.PositiveInfinity,
            Thickness = 0,
            Geometry = new PlaneGeometry(),
            Material = selected.Material,
            MaterialBefore = selected.MaterialAfter.Clone(),
            MaterialAfter = selected.MaterialAfter.Clone(),
            InteractionModel = new CoordinateReferenceInteractionModel(),
            SemiDiameter = 1_000_000
        });
        clone.SurfaceGroup.Renumber();
        return clone;
    }
}
