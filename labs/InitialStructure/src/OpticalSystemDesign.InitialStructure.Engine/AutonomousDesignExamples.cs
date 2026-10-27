using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Product-authored infinite-conjugate seed, not a certified or optimized design; no validation-file runtime dependency.</summary>
public static class AutonomousDesignExamples
{
    public static AutonomousSearchPlan ThreeElement()
    {
        var elements = ImmutableArray.Create(Element("lens-1", 3, 45, -55, "N-BK7"),
            Element("lens-2", 10, -65, 70, "N-F2"), Element("lens-3", 18, 60, -50, "N-BK7"));
        elements = elements.SetItem(0, elements[0] with
        {
            Front = elements[0].Front with
            {
                Profile = elements[0].Front.Profile with
                { Kind = TopologyProfileKind.EvenAsphere, Conic = -.25, MaximumEvenPower = 18, Terms = [new(4, 1e-7), new(10, -1e-14), new(18, 1e-22)] }
            }
        });
        var stage = new FixedTopologyOptions
        {
            ComputeMode = DesignComputeMode.Cpu,
            PerformFinalAssessment = false,
            RequireDescent = true,
            TransmissionFirst = true,
            TransmissionPolicyVersion = 4,
            Sampling = new() { IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true, PupilRings = 4, PupilArms = 16 },
            MaximumUpdates = 12,
            MaximumFunctionEvaluations = 13,
            MaximumGradientEvaluations = 13
        };
        return new()
        {
            Seeds = [new() { Name = "Infinite three-element example", Stop = new("stop", 0, 2), Image = new("image", 55), Elements = elements }],
            Options = new()
            {
                CheckStageFeasibility = true,
                ProtectCompensationRayBudget = true,
                AxialCompensation = new(),
                MaximumTrials = 12,
                MaximumFunctionEvaluations = 1000,
                MaximumGradientEvaluations = 200,
                MaximumTracedRays = 2_000_000,
                MaximumStageTracedRays = 400_000,
                FinalValidationRayReserve = 200_000,
                TimeLimit = TimeSpan.FromMinutes(5),
                Local = stage with { MaximumUpdates = 8, MaximumFunctionEvaluations = 9, MaximumGradientEvaluations = 9, ActiveParameterLimit = 2 },
                Full = stage,
                Expansion = new()
            }
        };
    }

    private static LensElement Element(string id, double z, double frontRadius, double backRadius, string glass) => new()
    {
        Id = id,
        FrontSurfaceId = id + ":front",
        BackSurfaceId = id + ":back",
        FrontZMillimeters = z,
        ThicknessMillimeters = 2,
        Material = glass,
        Front = new() { Profile = new() { CurvaturePerMillimeter = 1 / frontRadius }, ClearSemiDiameterMillimeters = 8, MechanicalSemiDiameterMillimeters = 9 },
        Back = new() { Profile = new() { CurvaturePerMillimeter = 1 / backRadius }, ClearSemiDiameterMillimeters = 7.5 }
    };
}
