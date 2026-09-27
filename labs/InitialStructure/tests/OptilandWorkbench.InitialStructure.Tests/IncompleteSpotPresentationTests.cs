using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.App;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class IncompleteSpotPresentationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    public void IncompleteAreaPropagationCannotAppearAsPassingImageQuality(int index)
    {
        var (specification, root) = Load(index);
        var problem = new FlatStartDesignProblem(specification, root.Family.ElementCount, root.Template, root.Family);
        var failed = problem.Evaluate(Optic.FromSnapshot(root.Start), FlatStartDesignProblem.FullStage, true, default);
        Assert.False(failed.HasContinuousSearchResiduals);
        var row = new CandidateRow(1, new FamilyTrial
        {
            Family = root.Family,
            FinalValidation = failed,
            Candidate = new()
            {
                Optic = failed.EvaluatedOptic!,
                Status = CandidateStatus.Rejected,
                Violations = failed.Violations,
                Lineage = new() { ElementCount = root.Family.ElementCount },
                Evaluation = new()
                {
                    FlatStartObjective = failed.Objective,
                    RmsSpotRadiusMillimeters = failed.Fields.Max(field => field.RmsRadiusMillimeters),
                    MaximumSpotRadiusMillimeters = failed.Fields.Max(field => field.MaximumRadiusMillimeters)
                }
            }
        });
        Assert.Equal("光线不完整", row.Rms);
        Assert.Equal("光线不完整", row.Status);
        Assert.All(row.Targets(specification).Skip(2).Take(2), target => Assert.Equal("无法判定", target.State));
        Assert.Contains(row.Fields(), field => field.Rms.Contains("（诊断）", StringComparison.Ordinal));
        Assert.Contains("不代表完整口径像质", row.Details(new() { Specification = specification }));
    }

    private static (InitialStructureSpecification, Root) Load(int index)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/stage-recovery-20260927/probe-inputs.json")), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new JsonStringEnumConverter() }
            })!;
        return (fixture.Specification, fixture.Trials.Single(root => root.TrialId == $"trial-{index:D4}"));
    }

    private sealed record Fixture(InitialStructureSpecification Specification, Root[] Trials);
    private sealed record Root(string TrialId, FlatStartFamily Family, OpticSnapshot Template, OpticSnapshot Start);
}
