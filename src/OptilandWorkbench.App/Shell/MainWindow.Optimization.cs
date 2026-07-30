using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
    private async Task QuickFocusAsync()
    {
        _statusText.Text = "正在执行快速聚焦…";
        var result = await _application.Optimization.QuickFocusAsync();
        _statusText.Text =
            $"快速聚焦完成：面 {result.SurfaceNumber} 像方厚度 " +
            $"{NumericDisplayFormatter.Format(result.InitialThickness)} → " +
            $"{NumericDisplayFormatter.Format(result.FinalThickness)} mm，" +
            $"焦移 {NumericDisplayFormatter.Format(result.AppliedShift)} mm。";
        _panels.ShowViewer(OpticSceneViewMode.TwoDimensional);
    }

    private async Task ShowOptimizationSliderAsync()
    {
        await new OptimizationVariableSliderWindow(_application.Prescription)
            .ShowDialog(this);
    }

    private async Task ShowOptimizationWizardAsync()
    {
        await new OptimizationWizardWindow(
                _application.Prescription,
                _application.Optimization)
            .ShowDialog<bool>(this);
        _panels.Show(WorkspacePanelId.Optimization);
    }

    private async Task RunRibbonOptimizationAsync(
        string optimizer,
        int iterations,
        string displayName)
    {
        if (_application.Optimization.GetMeritFunction()
            .All(operand => !operand.Enabled || operand.Type is "BLNK" or "DMFS"))
        {
            _application.Optimization.GenerateDefaultMeritFunction(
                MeritFunctionPreset.RmsSpot);
        }

        _statusText.Text = $"正在执行{displayName}…";
        var result = await _application.Optimization.OptimizeVariablesAsync(
            optimizer,
            iterations);
        _statusText.Text =
            $"{displayName}完成：评价函数 " +
            $"{NumericDisplayFormatter.Format(result.InitialMerit)} → " +
            $"{NumericDisplayFormatter.Format(result.FinalMerit)}，" +
            $"迭代 {result.Iterations} 次。";
        _panels.Show(WorkspacePanelId.Optimization);
    }

    private void UpdateAllOptimizationVariables(
        OptimizationVariableUpdateMode mode,
        string actionName)
    {
        var result = _application.Optimization.UpdateAllSurfaceVariables(mode);
        _statusText.Text =
            $"{actionName}：半径变量 {result.RadiusVariableCount} 个，" +
            $"厚度变量 {result.ThicknessVariableCount} 个。";
        _panels.Show(WorkspacePanelId.LensEditor);
    }

    private void OpenGlassReplacementTemplate()
    {
        if (_application.Optimization.GetMeritFunction().Count == 0)
        {
            _application.Optimization.GenerateDefaultMeritFunction(
                MeritFunctionPreset.RmsSpot);
        }

        _panels.ShowGlassCatalog();
        _panels.Show(WorkspacePanelId.Optimization);
        _statusText.Text =
            "已打开玻璃目录与评价函数模板；选择候选玻璃后可继续执行几何变量优化。";
    }
}
