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
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed record LoadedOpticalDocument(
    Optic ActiveOptic,
    IReadOnlyList<Optic> Configurations,
    int ActiveConfigurationIndex,
    IReadOnlyList<MultiConfigurationLinkOverride>? BrokenLinks = null,
    NonSequentialDocument? NonSequentialDocument = null);

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public partial class WorkbenchRuntime
{
    private readonly DocumentUndoRedoManager _undoRedo = new();
    private MultiConfiguration _multiConfiguration;
    private int _activeConfigurationIndex;
    private NonSequentialDocument _nonSequentialDocument;
    private readonly IReadOnlyList<NonSequentialDetectorFrame>? _databaseDetectorFrames;

    public WorkbenchRuntime(
        Optic optic,
        NonSequentialDocument? nonSequentialDocument = null,
        IReadOnlyList<NonSequentialDetectorFrame>? databaseDetectorFrames = null)
    {
        CurrentOptic = optic;
        _multiConfiguration = new MultiConfiguration(optic);
        _nonSequentialDocument = (nonSequentialDocument
            ?? StarOptProjectStore.CreateDefaultNonSequentialDocument(optic)).Clone();
        _databaseDetectorFrames = databaseDetectorFrames;
        Status = "就绪";
    }

    public event EventHandler? OpticLoaded;

    public event EventHandler? OpticChanged;

    public event EventHandler? StatusChanged;

    public event EventHandler? SurfaceDataChanged;

    public Optic CurrentOptic { get; private set; }

    public NonSequentialDocument CurrentNonSequentialDocument => _nonSequentialDocument;

    public ObservableCollection<OpticalSurface> Surfaces => CurrentOptic.SurfaceGroup.Items;

    public ObservableCollection<FieldPoint> Fields => CurrentOptic.Fields;

    public ObservableCollection<Wavelength> Wavelengths => CurrentOptic.Wavelengths;

    public string Status { get; private set; }

    public bool CanUndo => _undoRedo.CanUndo;

    public bool CanRedo => _undoRedo.CanRedo;

    public IReadOnlyList<string> AnalysisNames => CurrentOptic.Analyses.Names;

    public IReadOnlyList<string> AnalysisDisplayNames => CurrentOptic.Analyses.Names.Select(DisplayAnalysisName).ToArray();

    public IReadOnlyList<string> OptimizerNames => OptimizerCatalog.Names;

    public void ReplaceMeritFunction(IEnumerable<MeritOperandDefinition> operands)
    {
        CaptureCurrentState();
        CurrentOptic.MeritFunctionOperands.Clear();
        foreach (var operand in operands)
        {
            CurrentOptic.MeritFunctionOperands.Add(operand.Clone());
        }

        SetStatus($"评价函数已更新，共 {CurrentOptic.MeritFunctionOperands.Count} 行。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GenerateDefaultMeritFunction(ContractMeritFunctionPreset preset)
    {
        var operands = preset == ContractMeritFunctionPreset.RmsWavefront
            ? MeritFunctionCatalog.CreateDefaultRmsWavefront(CurrentOptic)
            : MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic);
        ReplaceMeritFunction(operands);
    }

    public void GenerateMeritFunction(
        MeritFunctionWizardSettings settings,
        int startRow,
        bool replaceExisting)
    {
        var generated = MeritFunctionCatalog.CreateFromWizard(CurrentOptic, settings)
            .Select(operand => operand.Clone())
            .ToList();
        if (replaceExisting)
        {
            ReplaceMeritFunction(generated);
            return;
        }

        var combined = CurrentOptic.MeritFunctionOperands.Select(operand => operand.Clone()).ToList();
        combined.InsertRange(Math.Clamp(startRow - 1, 0, combined.Count), generated);
        ReplaceMeritFunction(combined);
    }

    public IReadOnlyList<string> BackendNames => CurrentOptic.Backend.Names.OrderBy(name => name).ToArray();

    public IReadOnlyList<string> ApertureKindNames { get; } = new[]
    {
        "入瞳直径",
        "像方 F 数",
        "物方数值孔径",
        "按光阑面尺寸浮动"
    };

    public IReadOnlyList<string> FieldDefinitionNames { get; } = new[]
    {
        "角度",
        "物高",
        "近轴像高",
        "实际像高"
    };

    public IReadOnlyList<string> ApodizationKinds { get; } = new[]
    {
        "均匀（Zemax）",
        "高斯（Zemax）",
        "余弦立方（Zemax）",
        "无",
        "均匀",
        "高斯",
        "余弦平方",
        "Hann",
        "多项式",
        "超高斯",
        "Tukey"
    };

    public IReadOnlyList<string> GeometryKinds { get; } = new[]
    {
        "平面",
        "标准球面/圆锥",
        "平面光栅",
        "标准曲面光栅",
        "偶次非球面",
        "奇次非球面",
        "双圆锥",
        "环形面",
        "XY 多项式",
        "Chebyshev 曲面",
        "Zernike 曲面",
        "Forbes Q 曲面"
    };

    public IReadOnlyList<string> MaterialNames => CurrentOptic.Materials.Names
        .Append("MIRROR")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToArray();

    public IReadOnlyList<string> CoatingKinds { get; } = new[]
    {
        "无镀膜",
        "Experimental：单层透过率起伏近似",
        "Experimental：交替层透过率起伏近似"
    };

    public IReadOnlyList<string> InteractionKinds { get; } = new[]
    {
        "折射",
        "反射",
        "薄透镜",
        "反射薄透镜",
        "衍射",
        "反射衍射",
        "相位"
    };

    public IReadOnlyList<string> PhysicalApertureKinds { get; } = new[]
    {
        "圆形",
        "环形",
        "偏心圆",
        "矩形",
        "椭圆",
        "多边形",
        "组合孔径",
        "无"
    };

    public static bool IsNativeJsonPath(string path)
    {
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".optiland", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStarOptProjectPath(string path)
    {
        return path.EndsWith(StarOptProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPythonOptilandJsonPath(string path)
    {
        return path.EndsWith(".optiland-python.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".python-optiland.json", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatNameForPath(string path)
    {
        return IsStarOptProjectPath(path)
            ? "staropt-project"
            : IsPythonOptilandJsonPath(path)
            ? "python-optiland-json"
            : IsNativeJsonPath(path)
            ? "native-json"
            : OpticalFormatCatalog.FindImporter(Path.GetExtension(path)).FormatName;
    }

}
