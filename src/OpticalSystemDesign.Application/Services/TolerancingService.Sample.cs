using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class TolerancingService
{
    private const int SampleSchemaVersion = 1;
    private const int JointSampleSchemaVersion = 2;
    private const int StageObjectiveSampleSchemaVersion = 3;
    private const int SystemMeritSampleSchemaVersion = 4;
    private const int BoundMeritSampleSchemaVersion = 5;
    private const long MaximumSampleBytes = BoundedApplicationFile.MaximumExportBytes;
    private static readonly JsonSerializerOptions SampleJson = new()
    {
        MaxDepth = 96,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    private static TolerancingRequestDto FreezeRequest(TolerancingRequestDto request) => request with
    {
        Operands = request.Operands?.ToArray(),
        StageObjectives = request.StageObjectives?.Select(stage =>
            stage is null ? stage! : stage with { MeritTable = FreezeMeritTable(stage.MeritTable) }).ToArray(),
        MeritTable = FreezeMeritTable(request.MeritTable),
        AdditionalCompensators = request.AdditionalCompensators?.ToArray(),
        ConfigurationSettings = request.ConfigurationSettings is { } joint ? joint with
        {
            Configurations = joint.Configurations?.ToArray(),
            Limits = joint.Limits?.Select(item => item with { FieldLimits = item.FieldLimits?.ToArray() }).ToArray(),
            MceRows = joint.MceRows?.ToArray()
        } : null,
        MtfSettings = request.MtfSettings is { } settings
            ? settings with { FieldLimits = settings.FieldLimits?.ToArray() } : null
    };

    private static ToleranceMeritTableDto? FreezeMeritTable(ToleranceMeritTableDto? table) =>
        table is null ? null : table with
        {
            Rows = table.Rows?.Select(row => row is null ? row! : row with
            {
                ZemaxIntegerParameters = row.ZemaxIntegerParameters?.ToList(),
                ZemaxDataParameters = row.ZemaxDataParameters?.ToList()
            }).ToArray()!
        };

    private static ToleranceSampleDto? CaptureWorstSample(OpticSnapshot? nominal, TolerancingRequestDto request,
        TolerancingView view, IReadOnlyList<TolerancingTrialRowDto> trials)
    {
        if (nominal is null || trials.Count == 0) return null;
        var worst = SelectWorstTrial(request, trials);
        var effective = FreezeRequest(request with
        {
            Operands = view.AdjustedOperands ?? request.Operands,
            MtfSettings = view.MtfSettings,
            Mode = ToleranceAnalysisMode.SkipSensitivity,
            InverseValue = 0,
            MaxDegreeOfParallelism = 1
        });
        return new(SampleVersion(effective, joint: false), JsonSerializer.Serialize(nominal, SampleJson), effective, worst);
    }

    internal static ToleranceSampleDto CaptureJointWorstSample(JointToleranceSnapshot snapshot,
        TolerancingRequestDto request, IReadOnlyList<TolerancingTrialRowDto> trials) =>
        new(SampleVersion(request, joint: true), "", FreezeRequest(request with
        {
            Mode = ToleranceAnalysisMode.SkipSensitivity,
            InverseValue = 0,
            MaxDegreeOfParallelism = 1
        }), SelectWorstTrial(request, trials), JsonSerializer.Serialize(snapshot, SampleJson));

    private static int SampleVersion(TolerancingRequestDto request, bool joint) =>
        HasBoundMeritTables(request) ? BoundMeritSampleSchemaVersion
            : UsesSystemMerit(request) ? SystemMeritSampleSchemaVersion
            : request.StageObjectives is { Count: > 0 } ? StageObjectiveSampleSchemaVersion
            : joint ? JointSampleSchemaVersion : SampleSchemaVersion;

    private static bool IsJointSample(ToleranceSampleDto sample) =>
        sample.Request.ConfigurationSettings is { Scope: not ToleranceConfigurationScope.Current };

    private static TolerancingTrialRowDto SelectWorstTrial(TolerancingRequestDto request, IReadOnlyList<TolerancingTrialRowDto> trials) =>
        trials.OrderBy(row => double.IsFinite(row.CriterionValue ?? double.NaN) ? 1 : 0)
            .ThenBy(row => row.AcceptanceMargin ?? (request.Criterion == ToleranceCriterion.Mtf
                ? row.CriterionValue ?? double.NegativeInfinity : -(row.CriterionValue ?? double.PositiveInfinity)))
            .ThenBy(row => row.Trial).First();

    public Task SaveSampleAsync(ToleranceSampleDto sample, string path, CancellationToken cancellationToken = default)
    {
        ValidateSample(sample);
        return BoundedApplicationFile.WriteAllTextAtomicAsync(path, JsonSerializer.Serialize(sample, SampleJson),
            MaximumSampleBytes, "Tolerance sample", cancellationToken);
    }

    public async Task<ToleranceSampleReplayDto> ReplaySampleAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await BoundedApplicationFile.ReadAllTextAsync(path, MaximumSampleBytes, "Tolerance sample", cancellationToken)
            .ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var sample = JsonSerializer.Deserialize<ToleranceSampleDto>(json, SampleJson);
            ValidateSample(sample);
            using var computation = ComputationCancellation.Push(cancellationToken);
            var replayed = IsJointSample(sample!)
                ? WorkbenchRuntime.ReplayJointTolerance(
                    JsonSerializer.Deserialize<JointToleranceSnapshot>(sample!.JointSnapshotJson!, SampleJson)
                        ?? throw new InvalidDataException("样件缺少联合配置快照。"),
                    sample!.Request, sample.Recorded.Trial, cancellationToken)
                : ReplaySingleSample(sample!, cancellationToken);
            var matched = JsonSerializer.Serialize(sample!.Recorded with { Degradation = "" }, SampleJson)
                == JsonSerializer.Serialize(replayed, SampleJson);
            return new ToleranceSampleReplayDto(sample, replayed, matched);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static TolerancingTrialRowDto ReplaySingleSample(ToleranceSampleDto sample, CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(sample.NominalSnapshotJson, SampleJson)
            ?? throw new InvalidDataException("样件缺少名义镜头快照。");
        using var detached = new WorkspaceCoordinator(new OpticContext(Optic.FromSnapshot(snapshot)));
        new TolerancingService(detached).ValidateRequest(sample.Request);
        return detached.Runtime.ReplayToleranceSample(sample.Request, sample.Recorded.Trial, cancellationToken);
    }

    private static void ValidateSample(ToleranceSampleDto? sample)
    {
        if (sample is not { Request: not null, Recorded: not null }
            || sample.SchemaVersion != SampleVersion(sample.Request, IsJointSample(sample))
            || (IsJointSample(sample)
                ? string.IsNullOrWhiteSpace(sample.JointSnapshotJson) : string.IsNullOrWhiteSpace(sample.NominalSnapshotJson))
            || sample.Request.Mode != ToleranceAnalysisMode.SkipSensitivity
            || sample.Recorded.Trial < 1 || sample.Recorded.Trial > sample.Request.Trials)
            throw new InvalidDataException("公差样件版本、试验编号或输入无效。");
    }
}
