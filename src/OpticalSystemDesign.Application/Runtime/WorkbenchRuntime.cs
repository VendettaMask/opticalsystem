using System.Collections.ObjectModel;
using System.Globalization;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;
using ContractMeritFunctionPreset = OpticalSystemDesign.Application.Contracts.MeritFunctionPreset;

namespace OpticalSystemDesign.Application.Runtime;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed record LoadedOpticalDocument(
    Optic ActiveOptic,
    IReadOnlyList<Optic> Configurations,
    int ActiveConfigurationIndex,
    IReadOnlyList<MultiConfigurationLinkOverride>? BrokenLinks = null,
    NonSequentialDocument? NonSequentialDocument = null,
    IReadOnlyList<MultiConfigurationOperand>? OperandRows = null,
    IReadOnlyList<MultiConfigurationVariable>? OperandVariables = null,
    IReadOnlyList<MultiConfigurationPickup>? OperandPickups = null);

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
        "余弦立方（Zemax）"
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
        // Only the current explicit snapshot suffix is routed as native JSON.
        return path.EndsWith(".optic.json", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStarOptProjectPath(string path)
    {
        return path.EndsWith(StarOptProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatNameForPath(string path)
    {
        ValidateDocumentExtension(path);
        return IsStarOptProjectPath(path)
            ? "staropt-project"
            : IsNativeJsonPath(path)
            ? "native-json"
            : OpticalFormatCatalog.FindImporter(Path.GetExtension(path)).FormatName;
    }

    private static void ValidateDocumentExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (!IsStarOptProjectPath(path) && !IsNativeJsonPath(path)
            && !OpticalFormatCatalog.Importers.Any(importer => importer.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException("The selected optical document extension is not supported.");
        }
    }

}
