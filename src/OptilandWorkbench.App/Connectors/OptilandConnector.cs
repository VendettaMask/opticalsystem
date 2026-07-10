using System.Collections.ObjectModel;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.App.Connectors;

public sealed class OptilandConnector
{
    private readonly UndoRedoManager _undoRedo = new();

    public OptilandConnector(Optic optic)
    {
        CurrentOptic = optic;
        Status = "就绪";
    }

    public event EventHandler? OpticLoaded;

    public event EventHandler? OpticChanged;

    public event EventHandler? SurfaceDataChanged;

    public Optic CurrentOptic { get; private set; }

    public ObservableCollection<OpticalSurface> Surfaces => CurrentOptic.SurfaceGroup.Items;

    public ObservableCollection<FieldPoint> Fields => CurrentOptic.Fields;

    public ObservableCollection<Wavelength> Wavelengths => CurrentOptic.Wavelengths;

    public string Status { get; private set; }

    public bool CanUndo => _undoRedo.CanUndo;

    public bool CanRedo => _undoRedo.CanRedo;

    public IReadOnlyList<string> AnalysisNames => CurrentOptic.Analyses.Names;

    public IReadOnlyList<string> AnalysisDisplayNames => CurrentOptic.Analyses.Names.Select(DisplayAnalysisName).ToArray();

    public IReadOnlyList<string> OptimizerNames => OptimizerCatalog.Names;

    public IReadOnlyList<string> BackendNames => CurrentOptic.Backend.Names.OrderBy(name => name).ToArray();

    public IReadOnlyList<string> ApertureKindNames { get; } = new[]
    {
        "入瞳直径",
        "F 数",
        "数值孔径"
    };

    public IReadOnlyList<string> GeometryKinds { get; } = new[]
    {
        "平面",
        "标准球面/圆锥",
        "偶次非球面",
        "奇次非球面",
        "双圆锥",
        "环形面",
        "XY 多项式",
        "Chebyshev 曲面",
        "Zernike 曲面",
        "Forbes Q 曲面"
    };

    public IReadOnlyList<string> MaterialNames => CurrentOptic.Materials.Names.OrderBy(name => name).ToArray();

    public IReadOnlyList<string> CoatingKinds { get; } = new[]
    {
        "无镀膜",
        "MgF2 单层",
        "四分之一波堆栈"
    };

    public IReadOnlyList<string> InteractionKinds { get; } = new[]
    {
        "折射",
        "反射",
        "薄透镜",
        "衍射",
        "相位"
    };

    public IReadOnlyList<string> PhysicalApertureKinds { get; } = new[]
    {
        "圆形",
        "矩形",
        "无"
    };

    public static bool IsNativeJsonPath(string path)
    {
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".optiland", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatNameForPath(string path)
    {
        return IsNativeJsonPath(path)
            ? "native-json"
            : OpticalFormatCatalog.FindImporter(Path.GetExtension(path)).FormatName;
    }

    public string BuildAnalysisReport()
    {
        return BuildAnalysisReport("Prescription Report");
    }

    public string BuildAnalysisReport(string analysisName)
    {
        return BuildAnalysisView(analysisName).ReportText;
    }

    public AnalysisView BuildAnalysisView(string analysisName)
    {
        var analysis = CurrentOptic.Analyses.Create(CanonicalAnalysisName(analysisName));
        var data = analysis.GenerateData();
        var rows = data.Values
            .Select(item => new AnalysisRow(DisplayAnalysisKey(item.Key), FormatAnalysisValue(item.Value)))
            .ToArray();
        return new AnalysisView(DisplayAnalysisName(data.Name), rows, FormatAnalysisData(data));
    }

    public void NewDemo()
    {
        CurrentOptic = Optic.CreateDemo();
        _undoRedo.Clear();
        SetStatus("已创建演示光学系统。");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void CaptureCurrentState()
    {
        _undoRedo.Capture(CurrentOptic);
    }

    public void CommitSurfaceEdit()
    {
        CurrentOptic.Pickups.ApplyAll();
        CurrentOptic.Solves.ApplyAll();
        SetStatus("表面数据已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitSystemEdit()
    {
        SetPrimaryWavelengthGuard();
        SetStatus("系统属性已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddSurface()
    {
        CaptureCurrentState();
        CurrentOptic.SurfaceGroup.AddDefaultSurface();
        SetStatus("已添加表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSurface(OpticalSurface? surface)
    {
        if (surface is null)
        {
            return;
        }

        CaptureCurrentState();
        CurrentOptic.SurfaceGroup.Remove(surface);
        SetStatus("已删除表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySurfaceComponents(
        OpticalSurface? surface,
        string geometryKind,
        string materialName,
        string coatingKind,
        string interactionKind,
        string physicalApertureKind)
    {
        if (surface is null)
        {
            return;
        }

        CaptureCurrentState();
        ApplyGeometry(surface, geometryKind);
        ApplyMaterial(surface, materialName);
        ApplyCoating(surface, coatingKind);
        ApplyInteraction(surface, interactionKind);
        ApplyPhysicalAperture(surface, physicalApertureKind);
        SetStatus($"表面 {surface.Number} 组件已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddField()
    {
        CaptureCurrentState();
        Fields.Add(new FieldPoint
        {
            Label = $"视场 {Fields.Count}",
            YAngleDegrees = Fields.Count * 4,
            Weight = 1
        });
        SetStatus("已添加视场。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddWavelength()
    {
        CaptureCurrentState();
        Wavelengths.Add(new Wavelength
        {
            Label = $"W{Wavelengths.Count + 1}",
            Nanometers = 550,
            Weight = 1,
            IsPrimary = Wavelengths.Count == 0
        });
        SetPrimaryWavelengthGuard();
        SetStatus("已添加波长。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSystemAperture(string apertureKindName, double value)
    {
        if (!TryNormalizeApertureKind(apertureKindName, out var kind))
        {
            return;
        }

        CaptureCurrentState();
        CurrentOptic.Aperture.Kind = kind;
        CurrentOptic.Aperture.Value = Math.Max(0.001, value);
        SetStatus("系统孔径已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetBackend(string backendName)
    {
        if (string.IsNullOrWhiteSpace(backendName) || !CurrentOptic.Backend.Names.Contains(backendName))
        {
            return;
        }

        CurrentOptic.Backend.SetBackend(backendName);
        SetStatus($"后端已切换为 {backendName}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public OptimizationResult OptimizeRadius(OpticalSurface surface)
    {
        CaptureCurrentState();
        var result = new SimpleOptimizer(CurrentOptic).OptimizeRadius(surface);
        SyncSurfaceGeometry(surface);
        SetStatus(result.Message);
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public OptimizerResult OptimizeSurfaceRadius(OpticalSurface surface, string optimizerName, int maxIterations)
    {
        CaptureCurrentState();
        if (surface.IsPlane)
        {
            SetSurfaceRadius(surface, 40);
        }

        var initialRadius = surface.Radius;
        var span = Math.Max(10, Math.Abs(initialRadius) * 1.5);
        var lower = Math.Max(-1_000_000, initialRadius - span);
        var upper = Math.Min(1_000_000, initialRadius + span);
        var problem = CurrentOptic.CreateOptimizationProblem();
        problem.AddVariable(new DelegateVariable(
            $"Surface {surface.Number} radius",
            () => surface.Radius,
            next => SetSurfaceRadius(surface, next),
            lower,
            upper,
            stepHint: Math.Max(0.25, span * 0.1),
            scaler: new UnitRangeScaler(lower, upper)));
        problem.AddOperand(new Operand(
            "RMS spot radius",
            0,
            1,
            () => new AnalysisRunner(CurrentOptic).EvaluateSpotDiagram().RmsSpotRadius));

        var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, Math.Clamp(maxIterations, 1, 1_000));
        SetSurfaceRadius(surface, surface.Radius);
        SetStatus($"{DisplayOptimizerMessage(result.Message)}。半径 {initialRadius:0.###} -> {surface.Radius:0.###}。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

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

    public async Task SaveAsync(string path)
    {
        if (IsNativeJsonPath(path))
        {
            await OpticJsonStore.SaveAsync(CurrentOptic, path);
        }
        else
        {
            var text = OpticalFormatCatalog.Export(CurrentOptic, Path.GetExtension(path));
            await File.WriteAllTextAsync(path, text);
        }

        SetStatus($"已保存 {Path.GetFileName(path)}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadAsync(string path)
    {
        if (IsNativeJsonPath(path))
        {
            CurrentOptic = await OpticJsonStore.LoadAsync(path);
        }
        else
        {
            var text = await File.ReadAllTextAsync(path);
            CurrentOptic = OpticalFormatCatalog.Import(text, Path.GetExtension(path));
        }

        _undoRedo.Clear();
        SetStatus($"已打开 {Path.GetFileName(path)}（{FormatNameForPath(path)}）。");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string status)
    {
        Status = status;
    }

    private static void SetSurfaceRadius(OpticalSurface surface, double radius)
    {
        surface.Radius = Math.Abs(radius) < 1e-9
            ? Math.CopySign(1e-9, radius == 0 ? 1 : radius)
            : radius;
        SyncSurfaceGeometry(surface);
    }

    private static void SyncSurfaceGeometry(OpticalSurface surface)
    {
        surface.Geometry = Math.Abs(surface.Radius) < 1e-9
            ? new PlaneGeometry()
            : new StandardGeometry(surface.Radius, surface.Conic);
    }

    private static void ApplyGeometry(OpticalSurface surface, string geometryKind)
    {
        geometryKind = CanonicalGeometryKind(geometryKind);
        var radius = Math.Abs(surface.Radius) < 1e-9 ? 40 : surface.Radius;
        switch (geometryKind)
        {
            case "Plane":
                surface.Radius = 0;
                surface.Geometry = new PlaneGeometry();
                break;
            case "Even Asphere":
                surface.Radius = radius;
                surface.Geometry = new EvenAsphereGeometry(radius, surface.Conic, new[] { 0.0, 0.0 });
                break;
            case "Odd Asphere":
                surface.Radius = radius;
                surface.Geometry = new OddAsphereGeometry(radius, surface.Conic, new[] { 0.0, 0.0 });
                break;
            case "Biconic":
                surface.Radius = radius;
                surface.Geometry = new BiconicGeometry(radius, radius, surface.Conic, surface.Conic);
                break;
            case "Toroidal":
                surface.Radius = radius;
                surface.Geometry = new ToroidalGeometry(radius, radius);
                break;
            case "Polynomial":
                surface.Radius = radius;
                surface.Geometry = new PolynomialGeometry(new Dictionary<(int X, int Y), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 1.0 / (2.0 * radius)
                });
                break;
            case "Chebyshev":
                surface.Radius = radius;
                surface.Geometry = new ChebyshevGeometry(new Dictionary<(int XOrder, int YOrder), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius),
                    [(0, 2)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius)
                }, Math.Max(1, surface.SemiDiameter), Math.Max(1, surface.SemiDiameter));
                break;
            case "Zernike":
                surface.Radius = radius;
                surface.Geometry = new ZernikeGeometry(new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius)
                }, Math.Max(1, surface.SemiDiameter));
                break;
            case "Forbes Q":
                surface.Radius = radius;
                surface.Geometry = new ForbesQGeometry(radius, surface.Conic, Math.Max(1, surface.SemiDiameter), new[] { 0.0, 0.0 });
                break;
            default:
                surface.Radius = radius;
                surface.Geometry = new StandardGeometry(radius, surface.Conic);
                break;
        }
    }

    private void ApplyMaterial(OpticalSurface surface, string materialName)
    {
        var selectedMaterial = string.IsNullOrWhiteSpace(materialName) ? "Air" : materialName;
        surface.Material = selectedMaterial;
        surface.MaterialAfter = CurrentOptic.Materials.Resolve(selectedMaterial);
    }

    private static void ApplyCoating(OpticalSurface surface, string coatingKind)
    {
        switch (CanonicalCoatingKind(coatingKind))
        {
            case "MgF2":
                surface.Coating = "MgF2";
                surface.CoatingModel = new ThinFilmStackCoating(new[] { new ThinFilmLayer("MgF2", 120) });
                break;
            case "Quarter-wave Stack":
                surface.Coating = "Quarter-wave Stack";
                surface.CoatingModel = new NeedleSynthesisDesigner().DesignQuarterWaveStack(new[] { "MgF2", "TiO2" }, 587.6, 4);
                break;
            default:
                surface.Coating = "None";
                surface.CoatingModel = new NoneCoatingModel();
                break;
        }
    }

    private static void ApplyInteraction(OpticalSurface surface, string interactionKind)
    {
        interactionKind = CanonicalInteractionKind(interactionKind);
        surface.IsReflective = interactionKind == "Reflective";
        surface.InteractionModel = interactionKind switch
        {
            "Reflective" => new RefractiveReflectiveInteractionModel(true),
            "Thin Lens" => new ThinLensInteractionModel(50),
            "Diffractive" => new DiffractiveInteractionModel(1),
            "Phase" => new PhaseInteractionModel((_, _) => (0, 0)),
            _ => new RefractiveReflectiveInteractionModel(false)
        };
    }

    private static void ApplyPhysicalAperture(OpticalSurface surface, string physicalApertureKind)
    {
        surface.PhysicalAperture = CanonicalPhysicalApertureKind(physicalApertureKind) switch
        {
            "Rectangular" => new RectangularAperture(surface.SemiDiameter, surface.SemiDiameter),
            "None" => null,
            _ => new CircularAperture(surface.SemiDiameter)
        };
    }

    private void SetPrimaryWavelengthGuard()
    {
        if (Wavelengths.Count == 0)
        {
            return;
        }

        if (!Wavelengths.Any(item => item.IsPrimary))
        {
            Wavelengths[0].IsPrimary = true;
        }
    }

    private static string FormatAnalysisData(AnalysisData data)
    {
        var lines = new List<string> { $"分析：{DisplayAnalysisName(data.Name)}" };
        lines.AddRange(data.Values.Select(item => $"{DisplayAnalysisKey(item.Key)}：{FormatAnalysisValue(item.Value)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatAnalysisValue(object value)
    {
        return value switch
        {
            double number => number.ToString("0.######"),
            float number => number.ToString("0.######"),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static string DisplayOptimizerMessage(string message)
    {
        const string optimizedWith = "Optimized with ";
        if (message.StartsWith(optimizedWith, StringComparison.Ordinal))
        {
            var optimizer = message[optimizedWith.Length..];
            const string fallback = " using orthogonal descent fallback";
            if (optimizer.EndsWith(fallback, StringComparison.Ordinal))
            {
                optimizer = optimizer[..^fallback.Length];
                return $"已使用 {optimizer} 的正交下降回退完成优化";
            }

            return $"已使用 {optimizer} 完成优化";
        }

        return message;
    }

    private static string DisplayAnalysisName(string name)
    {
        return AnalysisDisplayNamesByKey.TryGetValue(name, out var display) ? display : name;
    }

    private static string CanonicalAnalysisName(string name)
    {
        return AnalysisDisplayNamesByKey.ContainsKey(name)
            ? name
            : AnalysisDisplayNamesByKey.FirstOrDefault(item => item.Value == name).Key ?? name;
    }

    private static string DisplayAnalysisKey(string key)
    {
        if (key.StartsWith("Surface ", StringComparison.OrdinalIgnoreCase))
        {
            return "表面 " + key["Surface ".Length..];
        }

        return AnalysisKeyDisplayNames.TryGetValue(key, out var display) ? display : key;
    }

    private static bool TryNormalizeApertureKind(string name, out ApertureKind kind)
    {
        if (ApertureKind.TryParse(name, out kind))
        {
            return true;
        }

        kind = name switch
        {
            "入瞳直径" => ApertureKind.EntrancePupilDiameter,
            "F 数" => ApertureKind.FNumber,
            "数值孔径" => ApertureKind.NumericalAperture,
            _ => ApertureKind.EntrancePupilDiameter
        };

        return name is "入瞳直径" or "F 数" or "数值孔径";
    }

    private static string CanonicalGeometryKind(string value)
    {
        return value switch
        {
            "平面" => "Plane",
            "标准球面/圆锥" => "Standard",
            "偶次非球面" => "Even Asphere",
            "奇次非球面" => "Odd Asphere",
            "双圆锥" => "Biconic",
            "环形面" => "Toroidal",
            "XY 多项式" => "Polynomial",
            "Chebyshev 曲面" => "Chebyshev",
            "Zernike 曲面" => "Zernike",
            "Forbes Q 曲面" => "Forbes Q",
            _ => value
        };
    }

    private static string CanonicalCoatingKind(string value)
    {
        return value switch
        {
            "无镀膜" => "None",
            "MgF2 单层" => "MgF2",
            "四分之一波堆栈" => "Quarter-wave Stack",
            _ => value
        };
    }

    private static string CanonicalInteractionKind(string value)
    {
        return value switch
        {
            "折射" => "Refractive",
            "反射" => "Reflective",
            "薄透镜" => "Thin Lens",
            "衍射" => "Diffractive",
            "相位" => "Phase",
            _ => value
        };
    }

    private static string CanonicalPhysicalApertureKind(string value)
    {
        return value switch
        {
            "圆形" => "Circular",
            "矩形" => "Rectangular",
            "无" => "None",
            _ => value
        };
    }

    private static readonly IReadOnlyDictionary<string, string> AnalysisDisplayNamesByKey = new Dictionary<string, string>
    {
        ["First Order"] = "一级像差/一阶量",
        ["Spot Diagram"] = "点列图",
        ["Ray Fan"] = "光线扇形图",
        ["Distortion"] = "畸变",
        ["Grid Distortion"] = "网格畸变",
        ["Field Curvature"] = "场曲",
        ["Encircled Energy"] = "包围能量",
        ["Pupil Aberration"] = "瞳孔像差",
        ["RMS vs Field"] = "RMS-视场",
        ["Through Focus"] = "离焦扫描",
        ["Y-Ybar"] = "Y-Ybar",
        ["PSF"] = "点扩散函数 PSF",
        ["MTF"] = "调制传递函数 MTF",
        ["Wavefront"] = "波前",
        ["Zernike"] = "Zernike 系数",
        ["Image Simulation"] = "成像仿真",
        ["Jones Pupil"] = "Jones 瞳",
        ["Prescription Report"] = "处方报告"
    };

    private static readonly IReadOnlyDictionary<string, string> AnalysisKeyDisplayNames = new Dictionary<string, string>
    {
        ["RayCount"] = "光线数",
        ["VignettedRayCount"] = "渐晕光线数",
        ["Centroid"] = "质心",
        ["RmsSpotRadius"] = "RMS 点半径",
        ["MaxSpotRadius"] = "最大点半径",
        ["Samples"] = "采样数",
        ["Min"] = "最小值",
        ["Max"] = "最大值",
        ["EffectiveFocalLength"] = "有效焦距",
        ["FNumber"] = "F 数",
        ["TotalTrack"] = "系统总长",
        ["MaxFieldDegrees"] = "最大视场角",
        ["IdealImageHeight"] = "理想像高",
        ["MeanActualHeight"] = "平均实际像高",
        ["DistortionPercent"] = "畸变百分比",
        ["PetzvalProxy"] = "Petzval 近似",
        ["SagAtFullField"] = "全视场场曲矢高",
        ["Radius50"] = "50% 半径",
        ["Radius80"] = "80% 半径",
        ["Radius95"] = "95% 半径",
        ["ApertureRadius"] = "孔径半径",
        ["EntrancePupilEstimate"] = "入瞳估计",
        ["ChiefRayPupilShiftProxy"] = "主光线瞳移近似",
        ["WeightedMean"] = "加权平均",
        ["FocusMinus"] = "负离焦",
        ["FocusNominal"] = "名义焦点",
        ["FocusPlus"] = "正离焦",
        ["BestFocusShift"] = "最佳焦移",
        ["RmsWavefrontProxy"] = "RMS 波前近似",
        ["PeakToValleyProxy"] = "PV 波前近似",
        ["Reference"] = "参考",
        ["Method"] = "方法",
        ["Sigma"] = "Sigma",
        ["PeakNormalized"] = "归一化峰值",
        ["BlurKernelRadius"] = "模糊核半径",
        ["LateralColorProxy"] = "横向色差近似",
        ["DistortionProxy"] = "畸变近似",
        ["PolarizationState"] = "偏振状态",
        ["Name"] = "名称",
        ["SurfaceCount"] = "表面数",
        ["FieldCount"] = "视场数",
        ["WavelengthCount"] = "波长数",
        ["EFL"] = "有效焦距",
        ["WeightedMetric"] = "加权指标",
        ["Status"] = "状态"
    };
}

public sealed record AnalysisView(string Name, IReadOnlyList<AnalysisRow> Rows, string ReportText);

public sealed record AnalysisRow(string Metric, string Value);
