using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Visualization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.Modules;

/// <summary>Maps shared Core results to UI data; never calculates alternative optical results.</summary>
public static class InitialStructurePresentation
{
    public static Scene2Dto? BuildScene(CandidateSnapshot? candidate) => candidate is null ? null
        : WorkbenchMapper.ToScene2Dto(new Layout2DBuilder(Optic.FromSnapshot(candidate.Optic)).Build(
            options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false)));

    public static double TotalTrack(CandidateSnapshot candidate) => Optic.FromSnapshot(candidate.Optic).SurfaceGroup.TotalTrack;
    public static double BackFocus(CandidateSnapshot candidate) => candidate.Optic.Surfaces[^2].Thickness;
    public static IReadOnlyList<PrescriptionSurface> Prescription(CandidateSnapshot candidate) => candidate.Optic.Surfaces
        .Select(surface => new PrescriptionSurface(surface.IsStop, surface.Radius, surface.RadiusVariable,
            surface.Thickness, surface.ThicknessVariable, surface.Material, surface.SemiDiameter, surface.MechanicalSemiDiameter)).ToArray();
}

public sealed record PrescriptionSurface(bool IsStop, double Radius, bool RadiusVariable, double Thickness,
    bool ThicknessVariable, string Material, double SemiDiameter, double? MechanicalSemiDiameter);
