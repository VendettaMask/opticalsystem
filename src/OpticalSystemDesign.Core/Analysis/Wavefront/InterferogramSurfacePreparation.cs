using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Analysis;

internal static class InterferogramSurfacePreparation
{
    internal static Optic Create(Optic source, int number, Wavelength wavelength)
    {
        var index = source.SurfaceGroup.Items.ToList().FindIndex(s => s.Number == number);
        if (index <= 0) throw new ArgumentOutOfRangeException(nameof(number), "Select a physical or image surface.");
        var stop = source.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
        if (stop > index && source.RayAimingEnabled)
            throw new NotSupportedException("Intermediate surfaces before the stop with ray aiming require a physical intermediate pupil model.");
        var clone = Optic.FromSnapshot(source.ToSnapshot());
        if (source.FieldDefinition is FieldDefinitionKind.RealImageHeight or FieldDefinitionKind.ParaxialImageHeight)
        {
            var infinite = ObjectConjugate.IsInfinite(source.SurfaceGroup.Items[0]);
            var fields = SpotAnalysisEngine.DefinedFields(source);
            var primary = source.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelength;
            for (var i = 0; i < fields.Count; i++)
            {
                var ray = source.SequentialRayTracer.RayGenerator.GenerateGeneric(fields[i].Hx, fields[i].Hy,
                    0, 0, primary.Micrometers, source.RayAimingEnabled).Rays[0];
                var origin = source.SurfaceGroup.Items[0].CoordinateSystem.ToLocalPoint(ray.Origin);
                clone.Fields[i].X = infinite ? Math.Atan2(ray.Direction.X, ray.Direction.Z) * 180 / Math.PI : origin.X;
                clone.Fields[i].Y = infinite ? Math.Atan2(ray.Direction.Y, ray.Direction.Z) * 180 / Math.PI : origin.Y;
            }
            clone.FieldDefinition = infinite ? FieldDefinitionKind.Angle : FieldDefinitionKind.ObjectHeight;
        }
        if (stop > index)
        {
            var pupilZ = source.Paraxial.EstimateEntrancePupilLocation();
            var firstZ = source.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z;
            if (clone.Aperture.Kind != ApertureKind.NumericalAperture)
            {
                clone.Aperture.Value = source.Paraxial.EstimateEntrancePupilDiameter();
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
                MaterialBefore = clone.SurfaceGroup.Items[0].MaterialAfter,
                MaterialAfter = clone.SurfaceGroup.Items[0].MaterialAfter,
                SemiDiameter = source.Paraxial.EstimateEntrancePupilDiameter() / 2
            };
            if (!ObjectConjugate.IsInfinite(clone.SurfaceGroup.Items[0])) clone.SurfaceGroup.Items[0].Thickness = pupilZ;
            clone.SurfaceGroup.Items.Insert(1, dummy); index++;
        }
        var marginal = clone.ImageSpaceAfocal ? null : clone.Paraxial.MarginalRay(wavelength.Micrometers);
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
            MaterialBefore = selected.MaterialAfter,
            MaterialAfter = selected.MaterialAfter,
            SemiDiameter = 1_000_000
        });
        clone.SurfaceGroup.Renumber();
        return clone;
    }
}
