using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
public bool Undo()
    {
        var changed = _undoRedo.TryUndo(CurrentOptic);
        if (changed)
        {
            SetStatus("撤销完成。");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public bool Redo()
    {
        var changed = _undoRedo.TryRedo(CurrentOptic);
        if (changed)
        {
            SetStatus("重做完成。");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public static async Task SaveOpticAsync(
        Optic optic,
        string path,
        CancellationToken cancellationToken = default)
    {
        await SaveDocumentAsync(
            new LoadedOpticalDocument(optic, new[] { optic }, 0),
            path,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task SaveDocumentAsync(
        LoadedOpticalDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsStarOptProjectPath(path))
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(document.Configurations, document.ActiveConfigurationIndex),
                path,
                cancellationToken).ConfigureAwait(false);
        }
        else if (IsPythonOptilandJsonPath(path))
        {
            await PythonOptilandJsonStore.SaveAsync(document.ActiveOptic, path, cancellationToken).ConfigureAwait(false);
        }
        else if (IsNativeJsonPath(path))
        {
            await OpticJsonStore.SaveAsync(document.ActiveOptic, path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var text = OpticalFormatCatalog.Export(document.ActiveOptic, Path.GetExtension(path));
            await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await SaveDocumentAsync(CaptureDocument(), path, cancellationToken).ConfigureAwait(false);

        SetStatus($"已保存 {Path.GetFileName(path)}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifySaved(string path)
    {
        SetStatus($"已保存 {Path.GetFileName(path)}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public static async Task<Optic> ReadOpticAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return (await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false)).ActiveOptic;
    }

    public static async Task<LoadedOpticalDocument> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsStarOptProjectPath(path) ||
            await StarOptProjectStore.HasMagicAsync(path, cancellationToken).ConfigureAwait(false))
        {
            var project = await StarOptProjectStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            return new LoadedOpticalDocument(
                project.Configurations[project.ActiveConfigurationIndex],
                project.Configurations,
                project.ActiveConfigurationIndex);
        }

        if (IsNativeJsonPath(path))
        {
            var optic = await OpticJsonStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            return new LoadedOpticalDocument(optic, new[] { optic }, 0);
        }

        if (Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
        {
            var imported = await new ZemaxZmxImporter()
                .ImportConfigurationSetFileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            return new LoadedOpticalDocument(
                imported.ActiveOptic,
                imported.Configurations,
                imported.ActiveConfigurationIndex);
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = OpticalFormatCatalog.Import(text, Path.GetExtension(path));
        return new LoadedOpticalDocument(loaded, new[] { loaded }, 0);
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyLoadedDocument(document, path);
    }

    public void ApplyLoadedOptic(Optic optic, string path)
    {
        ApplyLoadedDocument(new LoadedOpticalDocument(optic, new[] { optic }, 0), path);
    }

    public void ApplyLoadedDocument(LoadedOpticalDocument document, string path)
    {
        _undoRedo.Clear();
        _multiConfiguration = new MultiConfiguration(document.Configurations);
        _activeConfigurationIndex = Math.Clamp(
            document.ActiveConfigurationIndex,
            0,
            _multiConfiguration.Configurations.Count - 1);
        CurrentOptic = Optic.FromSnapshot(
            _multiConfiguration.Configurations[_activeConfigurationIndex].ToSnapshot());
        var configurationSummary = _multiConfiguration.Configurations.Count > 1
            ? $"，{_multiConfiguration.Configurations.Count} 个配置"
            : string.Empty;
        SetStatus($"已打开 {Path.GetFileName(path)}（{FormatNameForPath(path)}{configurationSummary}）。");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public LoadedOpticalDocument CaptureDocument()
    {
        SyncActiveConfigurationFromCurrent();
        var configurations = _multiConfiguration.Configurations
            .Select(configuration => Optic.FromSnapshot(configuration.ToSnapshot()))
            .ToArray();
        return new LoadedOpticalDocument(
            configurations[_activeConfigurationIndex],
            configurations,
            _activeConfigurationIndex);
    }
}
