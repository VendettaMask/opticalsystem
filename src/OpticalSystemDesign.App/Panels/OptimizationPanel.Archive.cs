using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

internal sealed record OptimizationArchiveInput(int Version, string Algorithm, int Iterations);

public sealed partial class OptimizationPanel
{
    internal async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken token)
    {
        var files = new List<ProjectArchiveFileDto>
        {
            await ProjectArchiveArtifacts.WriteAsync(directory, "settings/optimization.json", "optimization-input",
                new OptimizationArchiveInput(1, _optimizerPicker.SelectedItem as string ?? "Damped Least Squares", (int)(_iterationsInput.Value ?? 30)), token: token)
        };
        if (!string.IsNullOrEmpty(_result.Text))
            files.Add(await ProjectArchiveArtifacts.WriteAsync(directory, "results/optimization.json", "optimization-result", _result.Text, true, token));
        return files;
    }
    internal void OpenArchiveInput(string path)
    {
        var value = ProjectArchiveArtifacts.Read<OptimizationArchiveInput>(path);
        if (value.Version != 1 || value.Iterations is < 1 or > 1000 || !_optimization.OptimizerNames.Contains(value.Algorithm))
            throw new InvalidDataException("Unsupported optimization archive settings.");
        _optimizerPicker.SelectedItem = value.Algorithm; _iterationsInput.Value = value.Iterations;
    }
}
