using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel
{
    internal async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken token)
    {
        ApplySelectedOperand();
        var files = new List<ProjectArchiveFileDto>
        {
            await ProjectArchiveArtifacts.WriteAsync(directory, "settings/tolerances.startol.json", "tolerances", BuildStudyFile(), token: token)
        };
        if (_lastResult is { } result)
            files.Add(await ProjectArchiveArtifacts.WriteAsync(directory, "results/tolerances.json", "tolerance-result", result, true, token));
        return files;
    }
    internal void OpenArchiveInput(string path)
    {
        var document = ParseStudyFile(BoundedApplicationFile.ReadAllText(path, BoundedApplicationFile.MaximumSettingsBytes, "Tolerance settings"));
        _suppressDirtyTracking = true;
        try
        {
            TrackSelectedOperand(null); _operands.Clear();
            foreach (var operand in document.Operands) _operands.Add(new ToleranceOperandEditorRow(operand));
            Renumber(); _criterion.SelectedIndex = (int)document.Criterion;
            _trials.Value = document.Trials; _seed.Value = document.Seed;
            _compensationIterations.Value = document.CompensationIterations; _yieldLimit.Value = ToDecimal(document.YieldLimit);
            _analysisMode = document.Mode; _inverseValue = document.InverseValue; _distributionOverride = document.DistributionOverride;
            _maxDegreeOfParallelism = Math.Clamp(document.MaxDegreeOfParallelism, 1, Math.Max(1, Environment.ProcessorCount));
            _worstSensitivityCount = document.WorstSensitivityCount; _showMonteCarloTrials = document.ShowMonteCarloTrials;
            _mtfSettings = document.MtfSettings ?? new(); _compensationAlgorithm = document.CompensationAlgorithm;
            _additionalCompensators = document.AdditionalCompensators ?? []; _configurationSettings = document.ConfigurationSettings;
            _stageObjectives = document.StageObjectives; _meritTable = document.MeritTable;
            _operandGrid.SelectedItem = _operands.FirstOrDefault(); _currentTolerancePath = Path.GetFullPath(path); _hasUnsavedChanges = false;
        }
        finally { _suppressDirtyTracking = false; }
        InvalidateResults("已载入新的公差定义，请重新运行。");
    }
}
