using System.Globalization;
using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.App;

internal sealed class CandidateRow(int number, FamilyTrial trial, bool inherited = false)
{
    public FamilyTrial Trial { get; } = trial;
    public CandidateSnapshot Candidate => Trial.Candidate!;
    public string Name => inherited ? "来源方案" : $"方案 {number}";
    public string Status => Candidate.Status == CandidateStatus.LabAccepted ? "已达标" : "有差距";
    public int Elements => Candidate.Lineage.ElementCount;
    public string FocalLength => Format(Candidate.Evaluation.EffectiveFocalLengthMillimeters);
    public string Rms => Format(Candidate.Evaluation.RmsSpotRadiusMillimeters);
    public string Transmission => Trial.FinalValidation is { Fields.Count: > 0 } result
        ? result.Fields.SelectMany(sample => sample.Wavelengths.Select(wave => wave.AttemptedRays > 0 ? (double)wave.ValidRays / wave.AttemptedRays : 0)
            .Append(sample.AttemptedRays > 0 ? (double)sample.ValidRays / sample.AttemptedRays : 0)).Min().ToString("P2", CultureInfo.CurrentCulture) : "—";

    public IReadOnlyList<TargetRow> Targets(InitialStructureSpecification specification)
    {
        var violations = Candidate.Violations;
        string State(Func<ConstraintViolation, bool> predicate) => violations.Any(predicate) ? "未满足" : "满足";
        var optics = Optic.FromSnapshot(Candidate.Optic);
        return
        [
            new("焦距 mm", $"{Format(specification.EffectiveFocalLengthMillimeters)} ± {specification.FlatStart!.EffectiveFocalLengthRelativeTolerance:P1}", FocalLength, State(item => item.Code == "target.focal-length")),
            new("F/#", $"{Format(specification.FNumber)} ± {specification.FlatStart.FNumberRelativeTolerance:P1}", Format(Candidate.Evaluation.FNumber), State(item => item.Code == "target.f-number")),
            new("最差 RMS mm", "≤ " + Format(specification.MaximumRmsSpotRadiusMillimeters), Rms, State(item => item.Code.EndsWith(".rms", StringComparison.Ordinal))),
            new("最大光斑半径 mm", "≤ " + Format(specification.MaximumSpotRadiusMillimeters), Format(Candidate.Evaluation.MaximumSpotRadiusMillimeters), State(item => item.Code.EndsWith(".maximum-radius", StringComparison.Ordinal))),
            new("逐视场／波长通光", "≥ " + specification.FlatStart.MinimumValidRayFraction.ToString("P1", CultureInfo.CurrentCulture), Transmission,
                State(item => item.Code.EndsWith(".lost-fraction", StringComparison.Ordinal))),
            new("总长 mm", "≤ " + Format(specification.MaximumTrackLengthMillimeters), Format(optics.SurfaceGroup.TotalTrack),
                optics.SurfaceGroup.TotalTrack <= specification.MaximumTrackLengthMillimeters + 1e-9 ? "满足" : "未满足"),
            new("后焦 mm", specification.FlatStart.FixedBackFocusMillimeters is { } back ? Format(back) + "（固定）" : "≥ " + Format(specification.MinimumBackFocusMillimeters),
                Format(Candidate.Optic.Surfaces[^2].Thickness), State(item => item.Code.Contains("back-focus", StringComparison.Ordinal))),
            new("完整目标验收", "全部条件满足", Status, Candidate.Status == CandidateStatus.LabAccepted ? "通过" : "未通过")
        ];
    }

    public IEnumerable<PrescriptionRow> Prescription() => Candidate.Optic.Surfaces.Select((surface, index) => new PrescriptionRow(
        surface.IsStop ? $"{index} 光阑" : index.ToString(CultureInfo.InvariantCulture),
        (surface.Radius == 0 ? "∞" : Format(surface.Radius)) + (surface.RadiusVariable ? " V" : ""),
        Format(surface.Thickness) + (surface.ThicknessVariable ? " V" : ""), surface.Material,
        Format(surface.SemiDiameter), Format(surface.MechanicalSemiDiameter)));

    public IEnumerable<FieldRow> Fields() => (Trial.FinalValidation?.Fields ?? []).Select(field => new FieldRow(
        Format(field.HalfFieldAngleDegrees), Format(field.RmsRadiusMillimeters), Format(field.MaximumRadiusMillimeters),
        field.AttemptedRays > 0 ? ((double)field.ValidRays / field.AttemptedRays).ToString("P2", CultureInfo.CurrentCulture) : "—",
        string.Join("；", field.Wavelengths.Select(wave => $"{wave.Nanometers:0.###} nm：{wave.ValidRays}/{wave.AttemptedRays}"))));

    public string Details(FlatStartSearchCheckpoint checkpoint)
    {
        var lines = new List<string>
        {
            $"{Name} · {Status} · {Elements} 片",
            "玻璃：" + string.Join(" / ", Trial.Family.GlassNames),
            $"光阑：面 {Trial.Family.StopSurfaceIndex}；从零曲率平板起步，当前为第 {Candidate.Lineage.Generation} 代。",
            $"本次已计入 {checkpoint.ChargedEvaluations}/{checkpoint.Specification.Budget.MaximumEvaluations} 次评价；已确认追迹 {checkpoint.TracedRealRayCount:N0} 条真实光线。"
        };
        if (checkpoint.Origin is { } origin) lines.Add($"本次是追加细化；来源运行已用 {origin.ChargedEvaluations} 次评价，未计入本次新预算。来源记录：{origin.RunId}");
        lines.Add("差距与约束：");
        lines.AddRange(Candidate.Violations.Count == 0 ? ["完整规格的最终验收通过。"] : Candidate.Violations.Select(violation =>
            $"{violation.Code}：{violation.Message}"));
        lines.Add("记录编号：" + Candidate.CandidateId);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(double? value) => value switch
    {
        null => "—",
        double.PositiveInfinity => "∞",
        double.NegativeInfinity => "−∞",
        _ => value.Value.ToString("0.######", CultureInfo.CurrentCulture)
    };
}

internal sealed record TargetRow(string Name, string Target, string Actual, string State);
internal sealed record PrescriptionRow(string Surface, string Radius, string Thickness, string Material, string SemiDiameter, string Mechanical);
internal sealed record FieldRow(string Field, string Rms, string Maximum, string Transmission, string Wavelengths);
