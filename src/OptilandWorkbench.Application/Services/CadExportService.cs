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
            optic = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
        }

        var document = await Task.Run(
            () => StepCadExporter.Build(
                optic,
                new StepCadExportOptions(
                    options.SurfaceSamples,
                    options.AngularSamples,
                    optic.Name,
                    MaximumChordErrorMillimeters: options.MaximumChordErrorMillimeters,
                    MaximumTrianglesPerPart: options.MaximumTrianglesPerPart),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("CAD 导出路径没有有效目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                document.Content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new CadExportResultDto(
            fullPath,
            CadExportFormat.Step,
            Encoding.UTF8.GetByteCount(document.Content),
            document.PartCount,
            document.VertexCount,
            document.TriangleCount,
            document.Warnings);
    }
}
