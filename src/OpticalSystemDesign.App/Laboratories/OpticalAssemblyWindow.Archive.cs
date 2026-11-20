using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

internal sealed record AssemblyArchiveInput(int Version, IReadOnlyDictionary<string, decimal?> Numbers, int Direction, int Mode);
internal sealed record AssemblyArchiveResult(int Version, OpticalAssemblyResult? Conjugates, AssemblyReticleResult? Image);

public sealed partial class OpticalAssemblyWindow : IProjectArchiveParticipant
{
    private bool _viewingArchive;
    public async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken)
    {
        var values = this.GetLogicalDescendants().OfType<NumericUpDown>().Where(control => control.Name is not null)
            .ToDictionary(control => control.Name!, control => control.Value);
        var files = new List<ProjectArchiveFileDto>
        {
            await ProjectArchiveArtifacts.WriteAsync(directory, "labs/assembly/input.json", "assembly-input",
                new AssemblyArchiveInput(1, values, _direction.SelectedIndex, _probeMode.SelectedIndex), token: cancellationToken)
        };
        if (_result is not null || _image.Result is not null)
            files.Add(await ProjectArchiveArtifacts.WriteAsync(directory, "labs/assembly/result.json", "assembly-result",
                new AssemblyArchiveResult(1, _result, _image.Result), true, cancellationToken));
        return files;
    }
    internal void OpenArchiveInput(string path)
    {
        var value = ProjectArchiveArtifacts.Read<AssemblyArchiveInput>(path);
        if (value.Version != 1) throw new InvalidDataException("Unsupported assembly archive input.");
        foreach (var number in this.GetLogicalDescendants().OfType<NumericUpDown>())
            if (number.Name is { } name && value.Numbers.TryGetValue(name, out var saved)) number.Value = saved;
        _direction.SelectedIndex = value.Direction; _probeMode.SelectedIndex = value.Mode;
    }
    internal void OpenArchiveResult(string path)
    {
        var value = ProjectArchiveArtifacts.Read<AssemblyArchiveResult>(path);
        if (value.Version != 1) throw new InvalidDataException("Unsupported assembly archive result.");
        _viewingArchive = true;
        CancelOperation(); _result = value.Conjugates; BindRows();
        if (value.Image is { } image) _image.SetResult(image);
        SetStatus("已打开归档结果。");
        UpdateAvailability();
    }
}
