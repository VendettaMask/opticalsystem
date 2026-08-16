using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;

namespace OptilandWorkbench.App.Services;

public static class WorkspaceDocumentTypes
{
    public const string LensEditor = "lens-editor";
    public const string Viewer2D = "viewer-2d";
    public const string Viewer3D = "viewer-3d";
    public const string Optimization = "optimization";
    public const string Tolerancing = "tolerancing";
    public const string MultiConfiguration = "multi-configuration";
    public const string Analysis = "analysis";
    public const string SolidModel = "solid-model";
    public const string MaterialLibrary = "material-library";
    public const string GlassCatalog = "glass-catalog";
    public const string Manufacturability = "manufacturability";
    public const string OpticalDrawing = "optical-drawing";
    public const string LensLibrary = "lens-library";
    public const string StockLensCatalog = "stock-lens-catalog";
    public const string StockLensMatching = "stock-lens-matching";
    public const string MaterialAnalysis = "material-analysis";
    public const string ToleranceReport = "tolerance-report";
    public const string ToleranceHistogram = "tolerance-histogram";
    public const string ToleranceYield = "tolerance-yield";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        LensEditor,
        Viewer2D,
        Viewer3D,
        Optimization,
        Tolerancing,
        MultiConfiguration,
        Analysis,
        SolidModel,
        MaterialLibrary,
        GlassCatalog,
        Manufacturability,
        OpticalDrawing,
        LensLibrary,
        StockLensCatalog,
        StockLensMatching,
        MaterialAnalysis,
        ToleranceReport,
        ToleranceHistogram,
        ToleranceYield
    };

    public static bool IsKnown(string? typeId) => typeId is not null && Known.Contains(typeId);
}

public sealed record WorkspaceDocumentDescriptor(
    string Id,
    string TypeId,
    string Title,
    string? AnalysisName = null,
    Guid? InstanceId = null,
    Dictionary<string, string>? Settings = null,
    bool IsLocked = false);

public sealed record WorkspaceSession(
    int Version,
    string DockLayoutJson,
    IReadOnlyList<WorkspaceDocumentDescriptor> Documents,
    string? ActiveDocumentId);

public sealed class WorkspaceSessionStore
{
    public const int CurrentVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public WorkspaceSessionStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OptilandWorkbench");
    }

    public string DefaultLayoutPath => Path.Combine(_rootDirectory, "workspace-default.json");

    public string SlotPath(int slot) => Path.Combine(_rootDirectory, $"workspace-slot-{Math.Clamp(slot, 1, 9)}.json");

    public string SessionPath(string documentPath)
    {
        return Path.Combine(_rootDirectory, "sessions", $"{PathHash(documentPath)}.workspace.json");
    }

    public static string PathHash(string documentPath)
    {
        var normalized = CanonicalizeExistingPath(Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(documentPath)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string CanonicalizeExistingPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) || fullPath.Length <= root.Length)
        {
            return fullPath;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return fullPath;
        }

        try
        {
            var current = root;
            var segments = fullPath[root.Length..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var entries = Directory.EnumerateFileSystemEntries(current).ToArray();
                var match = entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));
                if (match is null)
                {
                    var caseInsensitiveMatches = entries
                        .Where(entry => string.Equals(
                            Path.GetFileName(entry),
                            segment,
                            StringComparison.OrdinalIgnoreCase))
                        .Take(2)
                        .ToArray();
                    if (caseInsensitiveMatches.Length != 1)
                    {
                        return fullPath;
                    }

                    match = caseInsensitiveMatches[0];
                }

                current = match;
            }

            return current;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return OperatingSystem.IsWindows()
                ? fullPath.ToUpperInvariant()
                : fullPath;
        }
    }

    public async Task<WorkspaceSession?> LoadAsync(
        string? documentPath,
        CancellationToken cancellationToken = default)
    {
        var path = documentPath is null ? DefaultLayoutPath : SessionPath(documentPath);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadPathAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(
        string? documentPath,
        WorkspaceSession session,
        CancellationToken cancellationToken = default)
    {
        var path = documentPath is null ? DefaultLayoutPath : SessionPath(documentPath);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SavePathAsync(path, session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<WorkspaceSession?> LoadSlotAsync(
        int slot,
        CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadPathAsync(SlotPath(slot), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveSlotAsync(
        int slot,
        WorkspaceSession session,
        CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SavePathAsync(SlotPath(slot), session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public void Quarantine(string? documentPath)
    {
        BackupInvalidFile(documentPath is null ? DefaultLayoutPath : SessionPath(documentPath));
    }

    public void QuarantineSlot(int slot)
    {
        BackupInvalidFile(SlotPath(slot));
    }

    public static (double X, double Y, double Width, double Height) ClampWindowBounds(
        double x,
        double y,
        double width,
        double height,
        double workX,
        double workY,
        double workWidth,
        double workHeight)
    {
        width = Math.Clamp(width, 360, Math.Max(360, workWidth));
        height = Math.Clamp(height, 240, Math.Max(240, workHeight));
        x = Math.Clamp(x, workX, workX + Math.Max(0, workWidth - width));
        y = Math.Clamp(y, workY, workY + Math.Max(0, workHeight - height));
        return (x, y, width, height);
    }

    private static void BackupInvalidFile(string path)
    {
        try
        {
            var backup = $"{path}.invalid-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            File.Move(path, backup, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<WorkspaceSession?> LoadPathAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            WorkspaceSession? session;
            await using (var stream = File.OpenRead(path))
            {
                session = await JsonSerializer.DeserializeAsync<WorkspaceSession>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            if (session is null || session.Version != CurrentVersion)
            {
                BackupInvalidFile(path);
                return null;
            }

            return session;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupInvalidFile(path);
            return null;
        }
    }

    private static async Task SavePathAsync(
        string path,
        WorkspaceSession session,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    session,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed class WorkspaceDockLayoutSerializer
{
    private readonly DockSerializer _serializer = new();

    public string Serialize(IRootDock layout)
    {
        var clone = CloneLayout(layout);
        RemoveEmptyFloatingWindows(clone);
        return _serializer.Serialize(clone);
    }

    public RootDock? Deserialize(string json)
    {
        var layout = _serializer.Deserialize<RootDock>(json);
        if (layout is not null)
        {
            NormalizeDockRelations(layout);
            RestoreWindowReferences(layout, null);
            RemoveEmptyFloatingWindows(layout);
        }

        return layout;
    }

    internal static bool HasFloatingContent(IDockWindow window)
    {
        return window.Layout is not null
            && WorkspaceDockFactory.EnumerateDockables(window.Layout)
                .Any(dockable => dockable is Document or Tool);
    }

    internal static int RemoveEmptyFloatingWindows(IRootDock layout)
    {
        if (layout.Windows is null)
        {
            return 0;
        }

        var emptyWindows = layout.Windows
            .Where(window => !HasFloatingContent(window))
            .ToArray();
        foreach (var window in emptyWindows)
        {
            layout.Windows.Remove(window);
        }

        return emptyWindows.Length;
    }

    private static RootDock CloneLayout(IRootDock layout)
    {
        var included = new HashSet<IDockable>(ReferenceEqualityComparer.Instance);
        return CloneDockable((IDockable)layout, included) as RootDock
            ?? throw new InvalidOperationException("Workspace root must use the Dock MVVM RootDock model.");
    }

    private static IDockable? CloneDockable(
        IDockable source,
        HashSet<IDockable> included)
    {
        if (!included.Add(source))
        {
            return null;
        }

        var clone = Activator.CreateInstance(source.GetType()) as IDockable
            ?? throw new InvalidOperationException($"Dock model '{source.GetType().FullName}' cannot be cloned.");
        CopySerializableScalarProperties(source, clone);

        if (source is IDock sourceDock && clone is IDock cloneDock)
        {
            cloneDock.VisibleDockables = CloneCollection(sourceDock.VisibleDockables, included);
        }

        if (source is IRootDock sourceRoot && clone is IRootDock cloneRoot)
        {
            cloneRoot.HiddenDockables = CloneCollection(sourceRoot.HiddenDockables, included);
            cloneRoot.LeftPinnedDockables = CloneCollection(sourceRoot.LeftPinnedDockables, included);
            cloneRoot.RightPinnedDockables = CloneCollection(sourceRoot.RightPinnedDockables, included);
            cloneRoot.TopPinnedDockables = CloneCollection(sourceRoot.TopPinnedDockables, included);
            cloneRoot.BottomPinnedDockables = CloneCollection(sourceRoot.BottomPinnedDockables, included);
            cloneRoot.PinnedDock = sourceRoot.PinnedDock is null
                ? null
                : CloneDockable(sourceRoot.PinnedDock, included) as IToolDock;
            cloneRoot.Windows = CloneWindows(sourceRoot.Windows, included);
        }

        return clone;
    }

    private static IList<IDockable>? CloneCollection(
        IList<IDockable>? source,
        HashSet<IDockable> included)
    {
        if (source is null)
        {
            return null;
        }

        var clones = new List<IDockable>(source.Count);
        foreach (var child in source)
        {
            if (CloneDockable(child, included) is { } clone)
            {
                clones.Add(clone);
            }
        }

        return clones;
    }

    private static IList<IDockWindow>? CloneWindows(
        IList<IDockWindow>? source,
        HashSet<IDockable> included)
    {
        if (source is null)
        {
            return null;
        }

        var clones = new List<IDockWindow>(source.Count);
        foreach (var window in source)
        {
            if (CloneWindow(window, included) is { } clone
                && HasFloatingContent(clone))
            {
                clones.Add(clone);
            }
        }

        return clones;
    }

    private static IDockWindow? CloneWindow(
        IDockWindow source,
        HashSet<IDockable> included)
    {
        var layout = source.Layout is null
            ? null
            : CloneDockable(source.Layout, included) as IRootDock;
        if (source.Layout is not null && layout is null)
        {
            return null;
        }

        var clone = Activator.CreateInstance(source.GetType()) as IDockWindow
            ?? throw new InvalidOperationException($"Dock window '{source.GetType().FullName}' cannot be cloned.");
        CopySerializableScalarProperties(source, clone);
        clone.Layout = layout;
        return clone;
    }

    private static void CopySerializableScalarProperties(object source, object target)
    {
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || !property.CanWrite
                || property.GetIndexParameters().Length != 0
                || property.IsDefined(typeof(IgnoreDataMemberAttribute), inherit: true)
                || !IsSerializableScalar(property.PropertyType))
            {
                continue;
            }

            property.SetValue(target, property.GetValue(source));
        }
    }

    private static bool IsSerializableScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsValueType || type == typeof(string);
    }

    private static void NormalizeDockRelations(IRootDock root)
    {
        var structural = EnumerateStructuralDockables(root).ToArray();
        var structuralSet = new HashSet<IDockable>(structural, ReferenceEqualityComparer.Instance);
        var byId = structural
            .Where(dockable => !string.IsNullOrWhiteSpace(dockable.Id))
            .GroupBy(dockable => dockable.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var dock in structural.OfType<IDock>())
        {
            var fallback = dock.VisibleDockables?.FirstOrDefault();
            dock.ActiveDockable = ResolveStructuralReference(dock.ActiveDockable, structuralSet, byId) ?? fallback;
            dock.DefaultDockable = ResolveStructuralReference(dock.DefaultDockable, structuralSet, byId) ?? fallback;
            dock.FocusedDockable = ResolveStructuralReference(dock.FocusedDockable, structuralSet, byId)
                ?? dock.ActiveDockable;
        }

        root.PinnedDock = ResolveStructuralReference(root.PinnedDock, structuralSet, byId) as IToolDock;
    }

    private static IDockable? ResolveStructuralReference(
        IDockable? candidate,
        HashSet<IDockable> structural,
        IReadOnlyDictionary<string, IDockable> byId)
    {
        if (candidate is null)
        {
            return null;
        }

        if (structural.Contains(candidate))
        {
            return candidate;
        }

        return !string.IsNullOrWhiteSpace(candidate.Id) && byId.TryGetValue(candidate.Id, out var resolved)
            ? resolved
            : null;
    }

    private static IEnumerable<IDockable> EnumerateStructuralDockables(IRootDock root)
    {
        return EnumerateStructuralDockables(root, new HashSet<IDockable>(ReferenceEqualityComparer.Instance));
    }

    private static IEnumerable<IDockable> EnumerateStructuralDockables(
        IDockable dockable,
        HashSet<IDockable> seen)
    {
        if (!seen.Add(dockable))
        {
            yield break;
        }

        yield return dockable;

        if (dockable is IRootDock root)
        {
            foreach (var collection in new[]
                     {
                         root.HiddenDockables,
                         root.LeftPinnedDockables,
                         root.RightPinnedDockables,
                         root.TopPinnedDockables,
                         root.BottomPinnedDockables
                     })
            {
                if (collection is null)
                {
                    continue;
                }

                foreach (var child in collection)
                {
                    foreach (var descendant in EnumerateStructuralDockables(child, seen))
                    {
                        yield return descendant;
                    }
                }
            }

            if (root.PinnedDock is not null)
            {
                foreach (var descendant in EnumerateStructuralDockables(root.PinnedDock, seen))
                {
                    yield return descendant;
                }
            }

            if (root.Windows is not null)
            {
                foreach (var window in root.Windows)
                {
                    if (window.Layout is null)
                    {
                        continue;
                    }

                    foreach (var descendant in EnumerateStructuralDockables(window.Layout, seen))
                    {
                        yield return descendant;
                    }
                }
            }
        }

        if (dockable is not IDock dock || dock.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var descendant in EnumerateStructuralDockables(child, seen))
            {
                yield return descendant;
            }
        }
    }

    private static void RestoreWindowReferences(IRootDock root, IDockWindow? parentWindow)
    {
        root.Window = parentWindow;
        if (root.Windows is null)
        {
            return;
        }

        foreach (var window in root.Windows)
        {
            window.ParentWindow = parentWindow;
            if (window.Layout is not null)
            {
                RestoreWindowReferences(window.Layout, window);
            }
        }
    }
}
