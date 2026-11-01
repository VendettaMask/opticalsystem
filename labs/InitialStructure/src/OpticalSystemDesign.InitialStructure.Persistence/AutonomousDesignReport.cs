using System.Globalization;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Human-readable report of existing evidence. It neither traces nor promotes an assessment.</summary>
public static class AutonomousDesignReport
{
    private static readonly JsonSerializerOptions ReportJson = new(AutonomousDesignStudyFiles.Json) { WriteIndented = true };
    public static async Task SaveMarkdownAsync(AutonomousDesignReview review, string path,
        Func<string, string>? translate = null, Func<string, string>? describeCode = null, CancellationToken token = default)
    {
        var text = BuildMarkdown(review, translate, describeCode);
        await BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "design report", async (stream, ct) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(text.AsMemory(), ct); await writer.FlushAsync(ct);
        }, token);
    }

    public static string BuildMarkdown(AutonomousDesignReview review, Func<string, string>? translate = null, Func<string, string>? describeCode = null)
    {
        // Ignore caller-modified projection rows: the checked original snapshot is authoritative.
        review = AutonomousDesignReview.Build(review.Study);
        var t = translate ?? (value => value); var code = describeCode ?? (value => value);
        var text = new StringBuilder(); var study = review.Study; var plan = study.Plan;
        Title(1, "自动初始结构设计报告"); text.AppendLine(E(study.Name)).AppendLine();
        text.AppendLine(t("此报告展示已保存证据，不重新计算或改变验收结论。"));
        text.AppendLine(t("一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。"));
        text.AppendLine(t("方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。"));
        Title(2, "来源与兼容性");
        Row("运行记录", study.RunId); Row("证据指纹", study.SourceFingerprint);
        Row("算法身份", study.Main?.Algorithm ?? study.Prepared?.Algorithm ?? study.Preparation?.Result?.Algorithm ?? t("未记录"));
        if (study.Preparation is { } preparation)
        { Row("准备模式", preparation.Mode); Row("原任务指纹", preparation.PlanFingerprint); }
        if (study.Main is { } record) Row("搜索计划指纹", record.PlanFingerprint);
        Row("版本 / 修订", $"{study.Main?.SchemaVersion ?? study.Preparation?.Version ?? 1} / {study.Main?.Revision ?? study.Preparation?.Revision ?? 0}");
        Row("计算版本", study.Main?.CementedKernelFingerprint ?? study.Preparation?.KernelFingerprint ?? t("未记录"));
        Row("状态", code(study.Main?.Status.ToString() ?? study.Preparation?.StopReason ?? study.Preparation?.Status.ToString() ?? study.Prepared!.Termination.ToString()));
        Title(2, "原验收规格");
        Row("目标焦距 / F数", $"{N(plan.Targets.EffectiveFocalLengthMillimeters)} mm / F/{N(plan.Targets.FNumber)}");
        Row("焦距 / F数相对容差", $"{N(plan.Targets.FocalLengthRelativeTolerance)} / {N(plan.Targets.FNumberRelativeTolerance)}");
        Row("RMS / 最大半径上限", $"{N(plan.Targets.MaximumRmsRadiusMillimeters * 1000)} / {N(plan.Targets.MaximumSpotRadiusMillimeters * 1000)} µm");
        Row("畸变 / 有效率", $"{N(plan.Targets.MaximumDistortionFraction)} / {N(plan.Targets.MinimumValidRayFraction)}");
        Row("中心与边界检查", t("沿用记录的原验收协议，丢失光线不得视为合格"));
        text.AppendLine(); text.AppendLine(t("全部原目标、制造限制、采样、权限和预算如下，未作放宽。"));
        Json(new { OriginalPreparationPlan = study.Preparation?.Plan, SearchOrSourcePlan = plan });
        Title(2, "预算与实际后端");
        text.AppendLine(t("准备和主搜索费用分列，阶段费用已包含在累计费用中，不得重复相加；未记录不代表零工作。"));
        if (study.Main is { } main)
        {
            Title(3, "主搜索累计费用"); Json(new { main.Consumed, main.Reservation, main.Plan.Options });
        }
        if (study.Preparation is { } prep)
        {
            Title(3, "准备累计费用"); Json(new { prep.Consumed, prep.Options, PendingPhase = prep.Pending?.Name,
                Reservation = prep.Pending?.Reservation, InFlight = prep.Pending?.InFlight, prep.PersistenceElapsed, prep.LostReservations });
        }
        else if (study.Prepared is { } prepared)
        { Title(3, "准备累计费用"); Json(new { prepared.Work, prepared.Options, prepared.MatrixRequests }); }
        text.AppendLine(t("实际设备只按已记录的执行证据报告，不能用所选设备代替实际后端。"));
        foreach (var backend in review.Decisions.Where(d => d.Backend is not null)) Row(backend.Id, backend.Backend!);
        if (review.Decisions.All(d => d.Backend is null)) text.AppendLine(t("未记录"));
        Title(2, "候选与原规格验收");
        foreach (var candidate in review.Candidates)
        {
            Title(3, candidate.Id, localize: false);
            Row("来源", t(candidate.Origin)); Row("父方案", candidate.Parent ?? "—");
            Row("验收", t(candidate.Classification)); Row("结果代码", code(candidate.Outcome) + " [" + candidate.Outcome + "]");
            Row("片数", candidate.Topology.Elements.Length.ToString(CultureInfo.InvariantCulture));
            Row("机械参考 / 光阑 / 像面位置", $"{N(candidate.Topology.Reference.ZMillimeters)} / {N(candidate.Topology.Stop.ZMillimeters)} / {N(candidate.Topology.Image.ZMillimeters)} mm");
            Row("光阑净半口径 (mm)", N(candidate.Topology.Stop.ClearSemiDiameterMillimeters));
            text.AppendLine().AppendLine($"| {t("镜片 / 面")} | z mm | {t("材料")} | {t("面型")} | {t("净半口径 mm")} | {t("机械半口径 mm")} | c mm⁻¹ | k | {t("偶次系数")} |")
                .AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var face in TopologyPrescriptionFaces.Enumerate(candidate.Topology))
                Face(face.Id, face.ZMillimeters, face.MaterialAfter, face.Face);
            if (candidate.Assessment is { } assessment)
            {
                if (!candidate.OriginalSpecification) { Title(4, "阶段验收条件"); Json(new { assessment.Targets, assessment.StructuralConstraints }); }
                Row("验收协议", assessment.ProtocolFingerprint);
                Row("实际焦距 / F数", $"{N(assessment.EffectiveFocalLengthMillimeters)} mm / {N(assessment.FNumber)}");
                text.AppendLine().AppendLine($"| {t("视场编号")} | RMS µm | {t("最大半径 µm")} | {t("有效 / 尝试")} | {t("边界 / 中心")} | {t("畸变 %")} |")
                    .AppendLine("| --- | --- | --- | --- | --- | --- |");
                for (var i = 0; i < assessment.Fields.Length; i++)
                {
                    var f = assessment.Fields[i];
                    text.AppendLine($"| {i + 1} | {N(f.RmsRadiusMillimeters * 1000)} | {N(f.MaximumRadiusMillimeters * 1000)} | {f.ValidRays}/{f.AttemptedRays} | {f.CheckValidRays}/{f.CheckAttemptedRays} | {N(f.DistortionFraction * 100)} |");
                }
                foreach (var violation in assessment.Violations) Row(violation.Code, $"{N(violation.Actual)} / {N(violation.Limit)}");
            }
            void Face(string id, double z, string material, TopologyFace face) => text.AppendLine($"| {E(id)} | {N(z)} | {E(material)} | {face.Profile.Kind} | {N(face.ClearSemiDiameterMillimeters)} | {N(face.MechanicalSemiDiameterMillimeters)} | {N(face.Profile.CurvaturePerMillimeter)} | {N(face.Profile.Conic)} | {E(string.Join("; ", face.Profile.Terms.Select(x => $"r{x.Power}={N(x.Coefficient)}")))} |");
        }
        Title(2, "阶段与淘汰原因");
        foreach (var decision in review.Decisions)
        {
            Title(3, decision.Id + " · " + code(decision.Stage), localize: false);
            Row("父方案", decision.Parent ?? "—"); Row("结果代码", code(decision.Outcome) + " [" + decision.Outcome + "]");
            if (!string.IsNullOrEmpty(decision.Detail)) Row("原因", decision.Detail);
            Json(new { decision.Work, decision.KnownFunctionEvaluations });
        }
        return text.ToString();
        void Title(int level, string value, bool localize = true) => text.AppendLine().AppendLine(new string('#', level) + " " + E(localize ? t(value) : value)).AppendLine();
        void Row(string key, string value) => text.AppendLine($"- **{E(t(key))}**: {E(value)}");
        void Json<T>(T value) => text.AppendLine().AppendLine("```json").AppendLine(JsonSerializer.Serialize(value, ReportJson)).AppendLine("```").AppendLine();
        string N(double? value) => value is { } v && double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : t("未记录");
    }
    private static string E(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
