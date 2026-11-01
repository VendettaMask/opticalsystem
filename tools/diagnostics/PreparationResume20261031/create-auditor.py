"""Reuse the original physical acceptance audit for the checkpoint evidence format."""
from pathlib import Path
import sys

repo=Path(sys.argv[1]).resolve()
out=repo/'tools/diagnostics/PreparationCheckpointAudit20261031'
out.mkdir(exist_ok=True)
text=(repo/'tools/diagnostics/F8ColdVerify/Program.cs').read_text(encoding='utf-8-sig')
text=text.replace('new[] { "control", "candidate" }','new[] { "continuous", "paused" }')
start=text.index('    using var saved =')
end=text.index('    var work =',start)
text=text[:start]+'''    var checkpoint = await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(directory,"run.preparation.json"));
    using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"source.json")));
    var result = SeedPreparationSearch.Result(checkpoint).ColdStart!;
    Check(input.RootElement.GetProperty("sourceSha256").GetString() == sourceHash,"original input hash");
    spec ??= ContentFingerprint.Compute(result.ColdPlan with { Options = result.ColdPlan.Options with { Seed = 0, SeedPreparation = null } });
    Check(!input.RootElement.GetProperty("importedEndpoint").GetBoolean() && !input.RootElement.GetProperty("importedCheckpoint").GetBoolean(),"cold imports");
    Check(result.ColdPlan.Options.Seed==seed && result.Policy.Version==6,"seed and cold version");
    var expected = ColdStartDesignSearch.ColdPlan(originalPlan with { Options = originalPlan.Options with { Seed = seed, SeedPreparation = checkpoint.Options } });
    Check(ContentFingerprint.Compute(expected)==ContentFingerprint.Compute(result.ColdPlan),"unchanged original task");
    Check(checkpoint.Options.MaximumFunctionEvaluations==600 && result.Policy.MaximumGradientEvaluations==600
        && checkpoint.Options.MaximumTracedRays==10000000 && checkpoint.Options.TimeLimit==TimeSpan.FromSeconds(600),"same declared budget");
    Check(checkpoint.Status==SeedPreparationStatus.Completed && checkpoint.LostReservations==0,"normal completed continuation");
    var segment=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"segment.json")));
    var actual=segment.RootElement.GetProperty("actualSegmentRays").GetInt64();
    if(arm=="paused")
    {
        var suffix=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"resumed-segment.json")));
        actual+=suffix.RootElement.GetProperty("actualSegmentRays").GetInt64();
        var prefix=await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(directory,"suspended.preparation.json"));
        Check(prefix.Status==SeedPreparationStatus.Suspended && prefix.Consumed.TracedRays==segment.RootElement.GetProperty("actualSegmentRays").GetInt64(),"saved exact prefix");
        Check(prefix.RandomState==checkpoint.RandomState || prefix.Stage=="Construction","random state continuation");
    }
    Check(actual==checkpoint.Consumed.TracedRays,"independent measured prefix plus suffix ray settlement");
''' + text[end:]
text=text.replace('if (!completeStudy && !File.Exists(Path.Combine(directory, "result.json"))) continue;','if (!completeStudy && !File.Exists(Path.Combine(directory, "run.preparation.json"))) continue;')
text=text.replace('result.Work.Elapsed <= TimeSpan.FromSeconds(600);','result.Work.Elapsed <= TimeSpan.FromSeconds(600);\n    Check(result.Qualified && withinBudget,"original complete qualification within unchanged caps");')
text=text.replace('        var readback = TopologyValidator.Validate', '''        Check(imported.Topology.Reference.ZMillimeters==originalPlan.Seeds[0].Reference.ZMillimeters
            && imported.Topology.Stop.ZMillimeters==originalPlan.Seeds[0].Stop.ZMillimeters
            && imported.Topology.Stop.ClearSemiDiameterMillimeters==originalPlan.Seeds[0].Stop.ClearSemiDiameterMillimeters
            && imported.Topology.Image.ZMillimeters==originalPlan.Seeds[0].Image.ZMillimeters,"native fixed frame/stop");
        Check(imported.Topology.Elements.Length==originalPlan.Seeds[0].Elements.Length,"native element count");
        for(var i=0;i<imported.Topology.Elements.Length;i++)
        {
            var restoredElement=imported.Topology.Elements[i];var originalElement=originalPlan.Seeds[0].Elements[i];
            foreach(var faces in new[]{(restoredElement.Front,originalElement.Front),(restoredElement.Back,originalElement.Back)})
                Check(faces.Item1.ClearSemiDiameterMillimeters==faces.Item2.ClearSemiDiameterMillimeters
                    && faces.Item1.MechanicalSemiDiameterMillimeters==faces.Item2.MechanicalSemiDiameterMillimeters,"native original clear/mechanical apertures");
        }
        var readback = TopologyValidator.Validate''')
(out/'Program.cs').write_text(text,encoding='utf8')
project=(repo/'tools/diagnostics/F8ColdVerify/F8ColdVerify.csproj').read_text(encoding='utf-8-sig')
(out/'PreparationCheckpointAudit20261031.csproj').write_text(project,encoding='utf8')
print('Created independent frozen-runtime physical/QA/native readback auditor.')
