using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Services;

/// <summary>Isolated current-image selection for ordered merit evaluation.</summary>
public static class IntermediateImageSystem
{
    public static Optic Create(Optic optic, int surfaceNumber, bool refocus)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ComputationCancellation.ThrowIfCancellationRequested();
        var source = optic.SurfaceGroup.Items;
        var index = source.ToList().FindIndex(surface => surface.Number == surfaceNumber);
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "中间像面必须是物面之后的有效表面。");
        if (optic.FieldDefinition is FieldDefinitionKind.ParaxialImageHeight or FieldDefinitionKind.RealImageHeight)
            throw new NotSupportedException("IMSF 的像高视场到物方坐标转换尚未实现；当前使用角度或物高视场。");
        var stopIndex = source.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex < 1 || index < stopIndex)
            throw new NotSupportedException("IMSF 当前要求所选表面位于原光阑处或之后；光阑前的虚拟入瞳重建尚未实现。");
        if (refocus && optic.ImageSpaceAfocal)
            throw new NotSupportedException("无焦像空间不能使用 IMSF 近轴调焦。");
        var result = optic.CreateMeritEvaluationCopy();
        var surfaces = source.Take(index + 1).Select(surface => surface.Clone()).ToList();
        if (refocus)
        {
            var last = surfaces[^1];
            // Append a plane in the same medium so the selected surface keeps its own optical interaction.
            surfaces.Add(new OpticalSurface
            {
                Label = "Intermediate image",
                Thickness = 0,
                SemiDiameter = last.SemiDiameter,
                Material = last.Material,
                MaterialBefore = last.MaterialAfter.Clone(),
                MaterialAfter = last.MaterialAfter.Clone(),
                Geometry = new PlaneGeometry(),
                InteractionModel = new RefractiveReflectiveInteractionModel()
            });
            result.SurfaceGroup.Replace(surfaces);
            var wavelength = result.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? result.Wavelengths.FirstOrDefault()
                ?? throw new InvalidOperationException("系统没有可用波长。");
            var ray = result.Paraxial.TraceNormalizedRay(last.Number, 0, 0, 0, 1, wavelength.Micrometers);
            var distance = -ray.Position.Y * ray.Direction.Z / ray.Direction.Y;
            if (!double.IsFinite(distance) || distance <= 0)
                throw new NotSupportedException("IMSF 当前调焦要求有限的正向近轴焦距；平行出射或虚焦追迹尚未实现。");
            last.Thickness = distance;
            result.SurfaceGroup.Renumber();
        }
        else
        {
            result.SurfaceGroup.Replace(surfaces);
            // Selection must preserve an explicitly located/tilted image surface.
            for (var i = 0; i < surfaces.Count; i++) surfaces[i].CoordinateSystem = source[i].CoordinateSystem;
        }
        return result;
    }
}
