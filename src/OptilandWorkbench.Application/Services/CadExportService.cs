using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Application.Services;

internal sealed class CadExportService : WorkbenchServiceBase, ICadExportService
{
    public CadExportService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public async Task<CadExportResultDto> ExportAsync(
        string path,
        CadExportOptionsDto? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new CadExportOptionsDto();
        if (options.Format != CadExportFormat.Step)
        {
            throw new NotSupportedException($"不支持 CAD 格式“{options.Format}”。");
        }

        Optic optic;
        lock (Gate)
        {
            optic = Optic.FromSnapshot(Connector.CurrentOptic.ToSnapshot());
        }

        var step = await Task.Run(
            () => StepCadExporter.Serialize(
                optic,
                new StepCadExportOptions(
                    options.SurfaceSamples,
                    options.AngularSamples,
                    optic.Name),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var fullPath = Path.GetFullPath(path);
        await File.WriteAllTextAsync(
            fullPath,
            step,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        return new CadExportResultDto(
            fullPath,
            CadExportFormat.Step,
            Encoding.UTF8.GetByteCount(step));
    }
}
