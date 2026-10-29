using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Visualization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.Modules;

/// <summary>Maps shared Core results to UI data; never calculates alternative optical results.</summary>
public static class InitialStructurePresentation
{
    public static TopologyStructureScene BuildTopologyScene(DesignTopology topology, TopologyConstraints constraints,
        CancellationToken cancellationToken = default, Action<long>? traceCompleted = null)
    {
        using var measurement = SequentialTraceMeasurement.BeginCpuBudget(50_000);
        try
        {
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, cancellationToken);
            var source = TopologyCompiler.Compile(topology, constraints); var optic = source.CreateOptic();
            cancellationToken.ThrowIfCancellationRequested();
            var scene = WorkbenchMapper.ToScene2Dto(new Layout2DBuilder(optic).Build(
                options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false)));
            cancellationToken.ThrowIfCancellationRequested();
            return new(scene, source.OpticFingerprint, optic.Paraxial.EstimateEffectiveFocalLength(), optic.Paraxial.EstimateFNumber(),
                source.SurfaceIndices[topology.Reference.Id], source.SurfaceIndices[topology.Image.Id], measurement.RayCount);
        }
        finally { traceCompleted?.Invoke(measurement.RayCount); }
    }

    public static Scene2Dto? BuildScene(CandidateSnapshot? candidate) => candidate is null ? null
        : WorkbenchMapper.ToScene2Dto(new Layout2DBuilder(Optic.FromSnapshot(candidate.Optic)).Build(
            options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false)));

    public static double TotalTrack(CandidateSnapshot candidate)
    {
        var optic = Optic.FromSnapshot(candidate.Optic);
        return Core.Domain.ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items[0]) ? optic.SurfaceGroup.TotalTrack
            : Core.Services.RotationalDesignGeometry.VertexSeparation(optic, 1, optic.SurfaceGroup.Items.Count - 1);
    }
    public static double BackFocus(CandidateSnapshot candidate) => candidate.Optic.Surfaces[^2].Thickness;
    public static IReadOnlyList<PrescriptionSurface> Prescription(CandidateSnapshot candidate) => candidate.Optic.Surfaces
        .Select(surface => new PrescriptionSurface(surface.IsStop, surface.Radius, surface.RadiusVariable,
            surface.Thickness, surface.ThicknessVariable, surface.Material, surface.SemiDiameter, surface.MechanicalSemiDiameter)).ToArray();
}

public sealed record TopologyStructureScene(Scene2Dto Scene, string OpticFingerprint, double FocalMillimeters, double FNumber,
    int ReferenceSurfaceNumber, int ImageSurfaceNumber, long DisplayTracedRays);

public sealed record PrescriptionSurface(bool IsStop, double Radius, bool RadiusVariable, double Thickness,
    bool ThicknessVariable, string Material, double SemiDiameter, double? MechanicalSemiDiameter);
