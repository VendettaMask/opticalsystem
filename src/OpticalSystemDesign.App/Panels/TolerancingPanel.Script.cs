using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel
{
    private async Task ImportToleranceScriptAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "打开 OpticStudio 公差脚本",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("OpticStudio TSC") { Patterns = ["*.tsc"] }]
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var script = await BoundedApplicationFile.ReadAllTextAsync(path, 256 * 1024, "TSC script");
            var revision = _events.Revision;
            var options = CurrentRunOptions();
            var rows = _operands.Select(row => row.ToDto()).ToArray();
            var window = new ToleranceScriptImportWindow(Path.GetFileName(path), script, _prescription.GetSurfaces().Count,
                _multiConfiguration?.GetOperandRows() ?? [], options.ConfigurationSettings, ValidatePreview);
            var imported = await window.ShowDialog<ToleranceScriptPreview?>(owner);
            if (imported is null) return;
            if (_disposed || revision != _events.Revision || !rows.SequenceEqual(_operands.Select(row => row.ToDto())))
                throw new InvalidOperationException("脚本预览期间研究输入已修改，请重新打开导入。");
            if (ValidatePreview(imported) is { } error) throw new InvalidOperationException(error);

            var applied = ApplyScriptOptions(options, imported);
            _criterion.SelectedIndex = (int)applied.Criterion;
            _compensationIterations.Value = applied.CompensationIterations;
            _compensationAlgorithm = applied.CompensationAlgorithm;
            _additionalCompensators = applied.AdditionalCompensators!;
            _stageObjectives = applied.StageObjectives;
            _meritTable = applied.MeritTable;
            _configurationSettings = applied.ConfigurationSettings;
            _analysisMode = applied.Mode;
            _yieldLimit.Value = 0;
            MarkDirty();
            _operationStatus.MarkSynced("已应用公差脚本");
            _summary.Text = $"已导入 {imported.Stages.Count} 个装调阶段。请按系统评价值设置验收上限。";

            string? ValidatePreview(ToleranceScriptPreview candidate)
            {
                if (rows.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator))
                    return "脚本自带可调量。请先停用公差表已有的 COMP 行。";
                var validation = _tolerancing.ValidateStudy(CreateRequest(ApplyScriptOptions(options, candidate), rows));
                return validation.IsValid ? null : string.Join("；", validation.Messages);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _operationStatus.MarkFailed("公差脚本导入失败");
            _summary.Text = error.Message;
        }
    }

    internal static TolerancingRunOptions ApplyScriptOptions(TolerancingRunOptions options, ToleranceScriptPreview script)
    {
        if (!script.CanImport) throw new ArgumentException("公差脚本含未解决的行。");
        var settings = options.ConfigurationSettings;
        if (settings is not null)
        {
            var manufacturing = settings.MceRows?.Where(row => !row.Compensator) ?? [];
            settings = settings with { MceRows = manufacturing.Concat(script.ConfigurationCompensators).ToArray() };
        }
        return options with
        {
            Criterion = ToleranceCriterion.SystemMerit,
            Mode = options.Mode is ToleranceAnalysisMode.InverseIncrement or ToleranceAnalysisMode.InverseLimit
                ? ToleranceAnalysisMode.Sensitivity : options.Mode,
            YieldLimit = 0,
            CompensationIterations = script.Iterations,
            CompensationAlgorithm = ToleranceCompensationAlgorithm.DampedLeastSquares,
            AdditionalCompensators = script.Compensators,
            StageObjectives = script.Stages,
            MeritTable = script.FinalMeritTable,
            ConfigurationSettings = settings
        };
    }
}
