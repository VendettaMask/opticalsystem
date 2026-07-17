using System.Collections.ObjectModel;
using System.Globalization;
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

namespace OptilandWorkbench.App.Connectors;

public sealed class OptilandConnector
{
    private readonly UndoRedoManager _undoRedo = new();
    private MultiConfiguration _multiConfiguration;
    private int _activeConfigurationIndex;

    public OptilandConnector(Optic optic)
    {
        CurrentOptic = optic;
        _multiConfiguration = new MultiConfiguration(optic);
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

    public IReadOnlyList<string> ApodizationKinds { get; } = new[]
    {
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

    public static bool IsPythonOptilandJsonPath(string path)
    {
        return path.EndsWith(".optiland-python.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".python-optiland.json", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatNameForPath(string path)
    {
        return IsPythonOptilandJsonPath(path)
            ? "python-optiland-json"
            : IsNativeJsonPath(path)
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
        return BuildAnalysisView(analysisName, null);
    }

    public AnalysisView BuildAnalysisView(string analysisName, IReadOnlyDictionary<string, string>? settings)
    {
        var analysis = CreateAnalysis(CanonicalAnalysisName(analysisName), settings ?? new Dictionary<string, string>());
        var data = analysis.GenerateData();
        var rows = data.Values
            .Select(item => new AnalysisRow(DisplayAnalysisKey(item.Key), FormatAnalysisValue(item.Value)))
            .ToArray();
        var fallback = BuildFallbackSeries(data.Values);
        var plotSeries = data.PlotSeries.Count > 0
            ? data.PlotSeries
            : fallback is null
                ? Array.Empty<AnalysisSeries>()
                : new[] { fallback };
        return new AnalysisView(
            DisplayAnalysisName(data.Name),
            rows,
            FormatAnalysisData(data),
            plotSeries.FirstOrDefault(),
            plotSeries,
            data.PlotOptions ?? new AnalysisPlotOptions(),
            data.PlotPanes ?? Array.Empty<AnalysisPlotPane>(),
            data.PlotPaneColumns);
    }

    public string CanonicalAnalysisKey(string analysisName)
    {
        return CanonicalAnalysisName(analysisName);
    }

    public IReadOnlyList<AnalysisParameterDescriptor> GetAnalysisParameters(string analysisName)
    {
        var distributionChoices = new[] { "hexapolar", "uniform", "random", "line_x", "line_y", "ring" };
        return CanonicalAnalysisName(analysisName) switch
        {
            "Spot Diagram" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "6", 1, 32),
                ChoiceParameter("Distribution", "瞳孔采样分布", "hexapolar", distributionChoices)
            },
            "Ray Fan" => new[] { IntParameter("NumPoints", "采样点数", "256", 3, 1024) },
            "Best Fit Ray Fan" => new[]
            {
                IntParameter("NumPoints", "采样点数", "256", 3, 1024),
                IntParameter("FitRings", "拟合球六角采样环数", "8", 2, 32)
            },
            "Distortion" => DistortionParameters("128"),
            "Grid Distortion" => DistortionParameters("10"),
            "Field Curvature" => new[]
            {
                IntParameter("NumPoints", "采样点数", "128", 3, 1024),
                DoubleParameter("ParabasalDelta", "近轴光线间隔", "0.00001", 1e-8, 0.1, 0.00001)
            },
            "Encircled Energy" => new[]
            {
                IntParameter("NumRays", "光线数", "100000", 1, 200000),
                IntParameter("NumPoints", "曲线采样点数", "256", 2, 2048),
                ChoiceParameter("Distribution", "瞳孔采样分布", "random", distributionChoices)
            },
            "Pupil Aberration" => new[] { IntParameter("NumPoints", "采样点数", "256", 3, 1024) },
            "RMS vs Field" => new[]
            {
                IntParameter("NumFields", "视场数", "64", 2, 256),
                IntParameter("NumRings", "六角采样环数", "6", 1, 32),
                ChoiceParameter("Distribution", "瞳孔采样分布", "hexapolar", distributionChoices)
            },
            "RMS Wavefront vs Field" => new[]
            {
                IntParameter("NumFields", "视场数", "32", 2, 256),
                IntParameter("NumRings", "六角采样环数", "12", 1, 32)
            },
            "Through Focus" => new[]
            {
                DoubleParameter("FocusStep", "焦移步长 (mm)", "0.1", 0, 10, 0.01),
                IntParameter("FocusPlaneCount", "焦面数量", "5", 1, 7),
                IntParameter("NumRings", "六角采样环数", "6", 1, 32),
                ChoiceParameter("Distribution", "瞳孔采样分布", "hexapolar", distributionChoices)
            },
            "Through Focus MTF" => new[]
            {
                DoubleParameter("SpatialFrequency", "空间频率 (cycles/mm)", "20", 0, 1000, 1),
                DoubleParameter("FocusStep", "焦移步长 (mm)", "0.1", 0, 10, 0.01),
                IntParameter("FocusPlaneCount", "焦面数量", "5", 1, 15),
                IntParameter("PupilSampling", "瞳面采样数", "128", 8, 512)
            },
            "Angle vs Image Height - Through Pupil" or "Angle vs Image Height - Through Field" => new[]
            {
                IntParameter("SurfaceIndex", "测量表面序号", "-1", -128, 128),
                ChoiceParameter("Axis", "测量轴", "Y", new[] { "Y", "X" }),
                IntParameter("NumPoints", "采样点数", "128", 2, 1024)
            },
            "Incoherent Irradiance" => new[]
            {
                IntParameter("NumRays", "光线数", "5", 1, 100000),
                IntParameter("ResolutionX", "X 分辨率", "128", 1, 1024),
                IntParameter("ResolutionY", "Y 分辨率", "128", 1, 1024),
                IntParameter("DetectorSurfaceIndex", "探测器表面序号", "-1", -128, 128),
                ChoiceParameter("Distribution", "瞳孔采样分布", "random", distributionChoices),
                BoolParameter("Normalized", "归一化显示", "true")
            },
            "Radiant Intensity" => new[]
            {
                IntParameter("AngularBinsX", "X 角度分箱", "101", 1, 1024),
                IntParameter("AngularBinsY", "Y 角度分箱", "101", 1, 1024),
                IntParameter("NumRays", "光线数", "2048", 1, 200000),
                IntParameter("ReferenceSurfaceIndex", "参考表面序号", "-1", -128, 128),
                ChoiceParameter("Distribution", "瞳孔采样分布", "random", distributionChoices),
                BoolParameter("UseAbsoluteUnits", "使用绝对单位", "true")
            },
            "PSF" or "MTF" => new[]
            {
                IntParameter("NumRays", "光线数", "128", 2, 512),
                IntParameter("GridSize", "网格尺寸（0=自动）", "0", 0, 2048)
            },
            "MMDFT PSF" => new[]
            {
                IntParameter("NumRays", "光线数", "16", 2, 256),
                IntParameter("ImageSize", "图像尺寸", "32", 1, 512),
                DoubleParameter("PixelPitchMicrometers", "像素间距 µm（0=自动）", "0", 0, 1000, 0.1)
            },
            "Huygens PSF" or "Huygens MTF" => new[]
            {
                IntParameter("NumRays", "光线数", "9", 2, 128),
                IntParameter("ImageSize", "图像尺寸", "32", 1, 256),
                DoubleParameter("PixelPitchMillimeters", "像素间距 (mm)", "0.005", 1e-6, 10, 0.001)
            },
            "Geometric MTF" => new[]
            {
                IntParameter("NumRays", "光线数", "32", 2, 10000),
                IntParameter("PlotPointCount", "曲线采样点数", "128", 2, 2048),
                ChoiceParameter("Distribution", "瞳孔采样分布", "uniform", distributionChoices),
                DoubleParameter("MaximumFrequency", "最大频率（0=截止）", "0", 0, 10000, 10),
                BoolParameter("ScaleByDiffractionLimit", "乘以衍射极限包络", "true")
            },
            "Sampled MTF" => new[]
            {
                IntParameter("PupilSampling", "瞳面采样数", "32", 8, 512),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                IntParameter("PlotPointCount", "曲线采样点数", "128", 2, 2048),
                DoubleParameter("MaximumFrequency", "最大频率（0=截止）", "0", 0, 10000, 10)
            },
            "Wavefront" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257)
            },
            "Centroid Sphere Wavefront" or "Best Fit Sphere Wavefront" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "8", 2, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257),
                DoubleParameter("RobustTrimStandardDeviations", "鲁棒裁剪 sigma", "3", 0, 10, 0.5)
            },
            "Zernike" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257)
            },
            "Image Simulation" => new[]
            {
                IntParameter("PsfSize", "PSF 尺寸", "32", 8, 256),
                IntParameter("NumRays", "光线数", "16", 2, 256),
                IntParameter("EigenPsfComponents", "EigenPSF 分量数", "3", 1, 12),
                IntParameter("DistortionGridSize", "畸变采样网格", "9", 3, 33),
                IntParameter("DistortionPolynomialDegree", "畸变拟合阶数", "5", 1, 9)
            },
            "Jones Pupil" => new[] { IntParameter("GridSize", "网格尺寸", "65", 3, 257) },
            _ => Array.Empty<AnalysisParameterDescriptor>()
        };
    }

    public Dictionary<string, string> MergeAnalysisSettings(
        string analysisName,
        IReadOnlyDictionary<string, string>? saved)
    {
        var merged = GetAnalysisParameters(analysisName)
            .ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue);
        if (saved is not null)
        {
            foreach (var item in saved)
            {
                if (merged.ContainsKey(item.Key))
                {
                    merged[item.Key] = item.Value;
                }
            }
        }

        return merged;
    }

    private BaseAnalysis CreateAnalysis(
        string name,
        IReadOnlyDictionary<string, string> settings)
    {
        int Int(string key, int fallback)
        {
            return TryReadInt(settings, key, fallback);
        }

        double Double(string key, double fallback)
        {
            return TryReadDouble(settings, key, fallback);
        }

        bool Bool(string key, bool fallback)
        {
            return TryReadBool(settings, key, fallback);
        }

        string Text(string key, string fallback)
        {
            return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        int? OptionalGridSize()
        {
            var gridSize = Int("GridSize", 0);
            return gridSize <= 0 ? null : gridSize;
        }

        double? OptionalFrequency()
        {
            var frequency = Double("MaximumFrequency", 0);
            return frequency <= 0 ? null : frequency;
        }

        var axis = Text("Axis", "Y").Equals("X", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return name switch
        {
            "First Order" => new FirstOrderAnalysis(CurrentOptic),
            "Spot Diagram" => new SpotDiagramAnalysis(CurrentOptic, Int("NumRings", 6), Text("Distribution", "hexapolar")),
            "Ray Fan" => new RayFanAnalysis(CurrentOptic, Int("NumPoints", 256)),
            "Best Fit Ray Fan" => new BestFitRayFanAnalysis(CurrentOptic, Int("NumPoints", 256), Int("FitRings", 8)),
            "Distortion" => new DistortionAnalysis(CurrentOptic, Int("NumPoints", 128), Text("DistortionType", "f-tan")),
            "Grid Distortion" => new GridDistortionAnalysis(CurrentOptic, Int("NumPoints", 10), Text("DistortionType", "f-tan")),
            "Field Curvature" => new FieldCurvatureAnalysis(CurrentOptic, Int("NumPoints", 128), Double("ParabasalDelta", 1e-5)),
            "Encircled Energy" => new EncircledEnergyAnalysis(
                CurrentOptic,
                Int("NumRays", 100_000),
                Text("Distribution", "random"),
                Int("NumPoints", 256)),
            "Pupil Aberration" => new PupilAberrationAnalysis(CurrentOptic, Int("NumPoints", 256)),
            "RMS vs Field" => new RmsVsFieldAnalysis(
                CurrentOptic,
                Int("NumFields", 64),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar")),
            "RMS Wavefront vs Field" => new RmsWavefrontVsFieldAnalysis(
                CurrentOptic,
                Int("NumFields", 32),
                Int("NumRings", 12)),
            "Through Focus" => new ThroughFocusAnalysis(
                CurrentOptic,
                Double("FocusStep", 0.1),
                Int("FocusPlaneCount", 5),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar")),
            "Through Focus MTF" => new ThroughFocusMtfAnalysis(
                CurrentOptic,
                Double("SpatialFrequency", 20),
                Double("FocusStep", 0.1),
                Int("FocusPlaneCount", 5),
                Int("PupilSampling", 128)),
            "Angle vs Image Height - Through Pupil" => new IncidentAngleVsHeightAnalysis(
                CurrentOptic,
                AngleScanMode.ThroughPupil,
                Int("SurfaceIndex", -1),
                axis,
                Int("NumPoints", 128)),
            "Angle vs Image Height - Through Field" => new IncidentAngleVsHeightAnalysis(
                CurrentOptic,
                AngleScanMode.ThroughField,
                Int("SurfaceIndex", -1),
                axis,
                Int("NumPoints", 128)),
            "Incoherent Irradiance" => new IncoherentIrradianceAnalysis(
                CurrentOptic,
                Int("NumRays", 5),
                Int("ResolutionX", 128),
                Int("ResolutionY", 128),
                Int("DetectorSurfaceIndex", -1),
                Text("Distribution", "random"),
                Bool("Normalized", true)),
            "Radiant Intensity" => new RadiantIntensityAnalysis(
                CurrentOptic,
                Int("AngularBinsX", 101),
                Int("AngularBinsY", 101),
                useAbsoluteUnits: Bool("UseAbsoluteUnits", true),
                referenceSurfaceIndex: Int("ReferenceSurfaceIndex", -1),
                numRays: Int("NumRays", 2048),
                distribution: Text("Distribution", "random")),
            "Y-Ybar" => new YYbarAnalysis(CurrentOptic),
            "PSF" => new PsfAnalysis(CurrentOptic, Int("NumRays", 128), OptionalGridSize()),
            "MMDFT PSF" => new MmdftPsfAnalysis(
                CurrentOptic,
                Int("NumRays", 16),
                Int("ImageSize", 32),
                Double("PixelPitchMicrometers", 0) <= 0 ? null : Double("PixelPitchMicrometers", 0)),
            "Huygens PSF" => new HuygensPsfAnalysis(
                CurrentOptic,
                Int("NumRays", 9),
                Int("ImageSize", 32),
                Double("PixelPitchMillimeters", 0.005)),
            "MTF" => new MtfAnalysis(CurrentOptic, Int("NumRays", 128), OptionalGridSize()),
            "Huygens MTF" => new HuygensMtfAnalysis(
                CurrentOptic,
                Int("NumRays", 9),
                Int("ImageSize", 32),
                Double("PixelPitchMillimeters", 0.005)),
            "Geometric MTF" => new GeometricMtfAnalysis(
                CurrentOptic,
                Int("NumRays", 32),
                Text("Distribution", "uniform"),
                Int("PlotPointCount", 128),
                OptionalFrequency(),
                Bool("ScaleByDiffractionLimit", true)),
            "Sampled MTF" => new SampledMtfAnalysis(
                CurrentOptic,
                Int("PupilSampling", 32),
                Int("ZernikeTerms", 37),
                Int("PlotPointCount", 128),
                OptionalFrequency()),
            "Wavefront" => new WavefrontAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("MapSize", 65)),
            "Centroid Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                CurrentOptic,
                ReferenceSphereStrategy.CentroidSphere,
                Int("NumRings", 8),
                Int("MapSize", 65),
                Double("RobustTrimStandardDeviations", 3)),
            "Best Fit Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                CurrentOptic,
                ReferenceSphereStrategy.BestFitSphere,
                Int("NumRings", 8),
                Int("MapSize", 65),
                Double("RobustTrimStandardDeviations", 3)),
            "Zernike" => new ZernikeAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("ZernikeTerms", 37),
                Int("MapSize", 65)),
            "Image Simulation" => new ImageSimulationAnalysis(CurrentOptic, new ImageSimulationConfig
            {
                PsfSize = Int("PsfSize", 32),
                NumRays = Int("NumRays", 16),
                Components = Int("EigenPsfComponents", 3),
                DistortionGridSize = Int("DistortionGridSize", 9),
                DistortionPolynomialDegree = Int("DistortionPolynomialDegree", 5),
                PsfGridRows = 3,
                PsfGridColumns = 3,
                Padding = 16
            }),
            "Jones Pupil" => new JonesPupilAnalysis(CurrentOptic, Int("GridSize", 65)),
            "Prescription Report" => new PrescriptionReportAnalysis(CurrentOptic),
            _ => CurrentOptic.Analyses.Create(name)
        };
    }

    public void NewBlank()
    {
        ReplaceOptic(Optic.CreateBlank(), "已创建空白光学系统。");
    }

    public void NewDemo()
    {
        ReplaceOptic(Optic.CreateCookeTriplet(), "已创建与 Optiland 官方样例一致的 Cooke 三片式镜头。");
    }

    public void NewTessar()
    {
        ReplaceOptic(Optic.CreateTessarLens(), "已创建 Optiland 官方 Tessar F/4.5 四片式镜头。");
    }

    private void ReplaceOptic(Optic optic, string status)
    {
        CurrentOptic = optic;
        _multiConfiguration = new MultiConfiguration(CurrentOptic);
        _activeConfigurationIndex = 0;
        _undoRedo.Clear();
        SetStatus(status);
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
        string physicalApertureKind,
        int gratingOrder = 1,
        double gratingPeriodMicrometers = 1,
        double grooveOrientationAngleDegrees = 0)
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
        ApplyGratingParameters(
            surface,
            gratingOrder,
            gratingPeriodMicrometers,
            grooveOrientationAngleDegrees);
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

    public void SetApodization(string apodizationKind, double firstParameter, double secondParameter)
    {
        var canonical = CanonicalApodizationKind(apodizationKind);
        CaptureCurrentState();
        CurrentOptic.Apodization = canonical switch
        {
            "None" => null,
            "Uniform" => new UniformApodization(),
            "Gaussian" => new GaussianApodization(Math.Max(0.001, firstParameter)),
            "CosineSquared" => new CosineSquaredApodization(Math.Max(0.001, firstParameter)),
            "Hann" => new HannApodization(Math.Max(0.001, firstParameter)),
            "Polynomial" => new PolynomialApodization(
                Math.Max(0.001, firstParameter),
                Math.Max(0, secondParameter)),
            "SuperGaussian" => new SuperGaussianApodization(
                Math.Max(0.001, firstParameter),
                Math.Max(2, secondParameter)),
            "Tukey" => new TukeyApodization(
                Math.Max(0.001, firstParameter),
                Math.Clamp(secondParameter, 0, 1)),
            _ => null
        };
        SetStatus("光瞳切趾已更新。");
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
        if (IsPythonOptilandJsonPath(path))
        {
            await PythonOptilandJsonStore.SaveAsync(CurrentOptic, path);
        }
        else if (IsNativeJsonPath(path))
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
        _multiConfiguration = new MultiConfiguration(CurrentOptic);
        _activeConfigurationIndex = 0;
        SetStatus($"已打开 {Path.GetFileName(path)}（{FormatNameForPath(path)}）。");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public TolerancingView RunTolerancing(
        OpticalSurface? surface,
        double radiusSigma,
        double thicknessSigma,
        int trials,
        int seed,
        int compensationIterations)
    {
        surface ??= Surfaces.FirstOrDefault(item => item.Number > 1) ?? Surfaces.FirstOrDefault();
        if (surface is null)
        {
            return TolerancingView.Empty("没有可用于公差分析的表面。");
        }

        var tolerancing = BuildDefaultTolerancing(surface.Number, radiusSigma, thicknessSigma, compensationIterations);
        if (tolerancing.Perturbations.Count == 0)
        {
            return TolerancingView.Empty("请至少设置一个非零扰动 sigma。");
        }

        var sensitivity = new SensitivityAnalysis(CurrentOptic, tolerancing)
            .Run(compensationIterations)
            .Select(result => new TolerancingSensitivityRow(result.Perturbation, result.DeltaMerit.ToString("0.######")))
            .ToArray();
        var monteCarlo = new MonteCarlo(CurrentOptic, tolerancing)
            .RunDetailed(Math.Clamp(trials, 1, 10_000), seed, compensationIterations)
            .Select(result => new TolerancingTrialRow(
                result.Trial + 1,
                result.Merit.ToString("0.######"),
                result.CompensatedMerit.ToString("0.######")))
            .ToArray();

        SetStatus($"公差分析完成：表面 {surface.Number}，{monteCarlo.Length} 次 Monte Carlo。");
        return new TolerancingView(
            $"表面 {surface.Number} 公差分析",
            sensitivity,
            monteCarlo,
            $"扰动数：{tolerancing.Perturbations.Count}    Monte Carlo：{monteCarlo.Length}    补偿迭代：{Math.Max(0, compensationIterations)}");
    }

    public IReadOnlyList<MultiConfigurationRow> GetMultiConfigurationRows()
    {
        SyncActiveConfigurationFromCurrent();
        return _multiConfiguration.Configurations
            .Select((optic, index) => new MultiConfigurationRow(
                index,
                index == 0 ? "Base" : $"Config {index}",
                index == _activeConfigurationIndex,
                optic.SurfaceGroup.Items.Count,
                optic.SurfaceGroup.TotalTrack.ToString("0.###"),
                optic.Paraxial.EstimateEffectiveFocalLength().ToString("0.###")))
            .ToArray();
    }

    public int AddMultiConfiguration()
    {
        SyncActiveConfigurationFromCurrent();
        var index = _multiConfiguration.AddConfiguration(_activeConfigurationIndex);
        SetStatus($"已添加配置 {index}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return index;
    }

    public void ActivateMultiConfiguration(int configIndex)
    {
        if (configIndex < 0 || configIndex >= _multiConfiguration.Configurations.Count)
        {
            return;
        }

        SyncActiveConfigurationFromCurrent();
        _activeConfigurationIndex = configIndex;
        CurrentOptic = Optic.FromSnapshot(_multiConfiguration.Configurations[configIndex].ToSnapshot());
        _undoRedo.Clear();
        SetStatus($"已激活配置 {configIndex}。");
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void SetMultiConfigurationThickness(int configIndex, int surfaceNumber, double thickness)
    {
        if (configIndex < 0 || configIndex >= _multiConfiguration.Configurations.Count)
        {
            return;
        }

        SyncActiveConfigurationFromCurrent();
        _multiConfiguration.SetThickness(configIndex, surfaceNumber, Math.Max(0, thickness));
        if (configIndex == 0)
        {
            _multiConfiguration.PropagateBaseLinks();
        }

        if (configIndex == _activeConfigurationIndex)
        {
            CurrentOptic = Optic.FromSnapshot(_multiConfiguration.Configurations[configIndex].ToSnapshot());
            _undoRedo.Clear();
            SetStatus($"配置 {configIndex} 表面 {surfaceNumber} 厚度已更新。");
            OpticLoaded?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            SetStatus($"配置 {configIndex} 表面 {surfaceNumber} 厚度已更新。");
            OpticChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
    }

    private Tolerancing BuildDefaultTolerancing(int surfaceNumber, double radiusSigma, double thicknessSigma, int compensationIterations)
    {
        var tolerancing = CurrentOptic.CreateTolerancing();
        var baselineRms = new AnalysisRunner(CurrentOptic).EvaluateSpotDiagram().RmsSpotRadius;
        tolerancing.AddOperand(new Operand(
            "RMS spot radius",
            baselineRms,
            1,
            () => new AnalysisRunner(CurrentOptic).EvaluateSpotDiagram().RmsSpotRadius));

        var target = GetSurfaceByNumber(surfaceNumber);
        if (Math.Abs(radiusSigma) > 1e-12)
        {
            var span = Math.Max(Math.Abs(target.Radius) * 3, Math.Abs(radiusSigma) * 10);
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"表面 {surfaceNumber} 半径 N(0,{Math.Abs(radiusSigma):0.###})",
                new DelegateVariable(
                    $"表面 {surfaceNumber} 半径",
                    () => GetSurfaceByNumber(surfaceNumber).Radius,
                    value => SetSurfaceRadius(GetSurfaceByNumber(surfaceNumber), value),
                    -Math.Max(1, span),
                    Math.Max(1, span),
                    Math.Max(1e-6, Math.Abs(radiusSigma))),
                new NormalSampler(0, Math.Abs(radiusSigma))));
        }

        if (Math.Abs(thicknessSigma) > 1e-12)
        {
            tolerancing.AddPerturbation(new VariablePerturbation(
                $"表面 {surfaceNumber} 厚度 N(0,{Math.Abs(thicknessSigma):0.###})",
                new DelegateVariable(
                    $"表面 {surfaceNumber} 厚度",
                    () => GetSurfaceByNumber(surfaceNumber).Thickness,
                    value =>
                    {
                        GetSurfaceByNumber(surfaceNumber).Thickness = value;
                        CurrentOptic.SurfaceGroup.Renumber(syncComposition: false);
                    },
                    0,
                    Math.Max(1, target.Thickness * 4),
                    Math.Max(1e-6, Math.Abs(thicknessSigma))),
                new NormalSampler(0, Math.Abs(thicknessSigma))));
        }

        if (compensationIterations > 0 && Surfaces.Count > 0)
        {
            var imageSurfaceNumber = Surfaces[^1].Number;
            var imageThickness = GetSurfaceByNumber(imageSurfaceNumber).Thickness;
            tolerancing.AddCompensator(new DelegateVariable(
                $"表面 {imageSurfaceNumber} 像面厚度补偿",
                () => GetSurfaceByNumber(imageSurfaceNumber).Thickness,
                value =>
                {
                    GetSurfaceByNumber(imageSurfaceNumber).Thickness = value;
                    CurrentOptic.SurfaceGroup.Renumber(syncComposition: false);
                },
                0,
                Math.Max(1, imageThickness + 100),
                0.5));
        }

        return tolerancing;
    }

    private OpticalSurface GetSurfaceByNumber(int surfaceNumber)
    {
        return Surfaces.First(surface => surface.Number == surfaceNumber);
    }

    private void SyncActiveConfigurationFromCurrent()
    {
        if (_activeConfigurationIndex >= 0 && _activeConfigurationIndex < _multiConfiguration.Configurations.Count)
        {
            _multiConfiguration.Configurations[_activeConfigurationIndex].ApplySnapshot(CurrentOptic.ToSnapshot());
        }
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
        surface.Geometry = surface.Geometry switch
        {
            IGratingGeometry grating when Math.Abs(surface.Radius) < 1e-9 =>
                new PlaneGratingGeometry(
                    grating.GratingOrder,
                    grating.GratingPeriodMicrometers,
                    grating.GrooveOrientationAngleRadians),
            IGratingGeometry grating => new StandardGratingGeometry(
                surface.Radius,
                surface.Conic,
                grating.GratingOrder,
                grating.GratingPeriodMicrometers,
                grating.GrooveOrientationAngleRadians),
            _ when Math.Abs(surface.Radius) < 1e-9 => new PlaneGeometry(),
            _ => new StandardGeometry(surface.Radius, surface.Conic)
        };
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
            case "Plane Grating":
                surface.Radius = 0;
                surface.Geometry = new PlaneGratingGeometry(1, 1, 0);
                break;
            case "Standard Grating":
                surface.Radius = radius;
                surface.Geometry = new StandardGratingGeometry(radius, surface.Conic, 1, 1, 0);
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
        if (interactionKind is "Diffractive" or "Reflective Diffractive")
        {
            surface.Geometry = surface.Geometry switch
            {
                IGratingGeometry grating => grating,
                PlaneGeometry => new PlaneGratingGeometry(1, 1, 0),
                StandardGeometry standard => new StandardGratingGeometry(
                    standard.Radius,
                    standard.Conic,
                    1,
                    1,
                    0),
                _ when Math.Abs(surface.Radius) < 1e-9 => new PlaneGratingGeometry(1, 1, 0),
                _ => new StandardGratingGeometry(surface.Radius, surface.Conic, 1, 1, 0)
            };
        }
        else
        {
            surface.Geometry = surface.Geometry switch
            {
                PlaneGratingGeometry => new PlaneGeometry(),
                StandardGratingGeometry grating => new StandardGeometry(grating.Base.Radius, grating.Base.Conic),
                _ => surface.Geometry
            };
        }

        surface.IsReflective = interactionKind is "Reflective" or "Reflective Diffractive";
        surface.InteractionModel = interactionKind switch
        {
            "Reflective" => new RefractiveReflectiveInteractionModel(true),
            "Thin Lens" => new ThinLensInteractionModel(50),
            "Diffractive" => new DiffractiveInteractionModel(),
            "Reflective Diffractive" => new DiffractiveInteractionModel(true),
            "Phase" => new PhaseInteractionModel(new ConstantPhaseProfile()),
            _ => new RefractiveReflectiveInteractionModel(false)
        };
    }

    private static void ApplyGratingParameters(
        OpticalSurface surface,
        int order,
        double periodMicrometers,
        double angleDegrees)
    {
        if (surface.Geometry is not IGratingGeometry grating)
        {
            return;
        }

        periodMicrometers = Math.Max(1e-6, periodMicrometers);
        var angleRadians = angleDegrees * Math.PI / 180.0;
        surface.Geometry = grating switch
        {
            PlaneGratingGeometry => new PlaneGratingGeometry(order, periodMicrometers, angleRadians),
            StandardGratingGeometry standard => new StandardGratingGeometry(
                standard.Base.Radius,
                standard.Base.Conic,
                order,
                periodMicrometers,
                angleRadians),
            _ => surface.Geometry
        };
    }

    private static void ApplyPhysicalAperture(OpticalSurface surface, string physicalApertureKind)
    {
        surface.PhysicalAperture = CanonicalPhysicalApertureKind(physicalApertureKind) switch
        {
            "Annular" => new AnnularAperture(surface.SemiDiameter, surface.SemiDiameter * 0.5),
            "Offset Radial" => new OffsetRadialAperture(
                surface.SemiDiameter * 0.8,
                offsetX: surface.SemiDiameter * 0.2),
            "Rectangular" => new RectangularAperture(surface.SemiDiameter, surface.SemiDiameter),
            "Elliptical" => new EllipticalAperture(surface.SemiDiameter, surface.SemiDiameter * 0.75),
            "Polygon" => new PolygonAperture(new[]
            {
                (-surface.SemiDiameter, -surface.SemiDiameter),
                (surface.SemiDiameter, -surface.SemiDiameter),
                (surface.SemiDiameter, surface.SemiDiameter),
                (-surface.SemiDiameter, surface.SemiDiameter)
            }),
            "Boolean" => new DifferenceAperture(
                new CircularAperture(surface.SemiDiameter),
                new CircularAperture(surface.SemiDiameter * 0.5)),
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

    private static AnalysisParameterDescriptor IntParameter(
        string key,
        string displayName,
        string defaultValue,
        double minimum,
        double maximum)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Integer,
            defaultValue,
            minimum,
            maximum,
            1);
    }

    private static AnalysisParameterDescriptor DoubleParameter(
        string key,
        string displayName,
        string defaultValue,
        double minimum,
        double maximum,
        double increment)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Double,
            defaultValue,
            minimum,
            maximum,
            increment);
    }

    private static AnalysisParameterDescriptor ChoiceParameter(
        string key,
        string displayName,
        string defaultValue,
        IReadOnlyList<string> choices)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Choice,
            defaultValue,
            Choices: choices);
    }

    private static AnalysisParameterDescriptor BoolParameter(
        string key,
        string displayName,
        string defaultValue)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Boolean,
            defaultValue);
    }

    private static AnalysisParameterDescriptor[] DistortionParameters(string defaultPoints)
    {
        return new[]
        {
            IntParameter("NumPoints", "采样点数", defaultPoints, 3, 1024),
            ChoiceParameter("DistortionType", "畸变模型", "f-tan", new[] { "f-tan", "f-theta" })
        };
    }

    private static int TryReadInt(IReadOnlyDictionary<string, string> settings, string key, int fallback)
    {
        return settings.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static double TryReadDouble(IReadOnlyDictionary<string, string> settings, string key, double fallback)
    {
        return settings.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, string> settings, string key, bool fallback)
    {
        return settings.TryGetValue(key, out var value) && bool.TryParse(value, out var flag)
            ? flag
            : fallback;
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

    private static AnalysisSeries? BuildFallbackSeries(IReadOnlyDictionary<string, object> values)
    {
        var points = values
            .Select(item => (item.Key, Value: TryConvertFiniteNumber(item.Value)))
            .Where(item => item.Value.HasValue)
            .Select((item, index) => new AnalysisPoint(index, item.Value!.Value, DisplayAnalysisKey(item.Key)))
            .ToArray();
        return points.Length == 0
            ? null
            : new AnalysisSeries("Metric", "Value", points, AnalysisSeriesKind.Bar);
    }

    private static double? TryConvertFiniteNumber(object value)
    {
        if (value is not IConvertible || value is string or bool or char)
        {
            return null;
        }

        try
        {
            var number = Convert.ToDouble(value);
            return double.IsFinite(number) ? number : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
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

        if (key.StartsWith("Field ", StringComparison.OrdinalIgnoreCase))
        {
            return "视场 " + key["Field ".Length..];
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
            "平面光栅" => "Plane Grating",
            "标准曲面光栅" => "Standard Grating",
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
            "反射衍射" => "Reflective Diffractive",
            "相位" => "Phase",
            _ => value
        };
    }

    private static string CanonicalPhysicalApertureKind(string value)
    {
        return value switch
        {
            "圆形" => "Circular",
            "环形" => "Annular",
            "偏心圆" => "Offset Radial",
            "矩形" => "Rectangular",
            "椭圆" => "Elliptical",
            "多边形" => "Polygon",
            "组合孔径" => "Boolean",
            "无" => "None",
            _ => value
        };
    }

    private static string CanonicalApodizationKind(string value)
    {
        return value switch
        {
            "无" => "None",
            "均匀" => "Uniform",
            "高斯" => "Gaussian",
            "余弦平方" => "CosineSquared",
            "Hann" => "Hann",
            "多项式" => "Polynomial",
            "超高斯" => "SuperGaussian",
            "Tukey" => "Tukey",
            _ => value
        };
    }

    private static readonly IReadOnlyDictionary<string, string> AnalysisDisplayNamesByKey = new Dictionary<string, string>
    {
        ["First Order"] = "一级像差/一阶量",
        ["Spot Diagram"] = "点列图",
        ["Ray Fan"] = "光线扇形图",
        ["Best Fit Ray Fan"] = "最佳拟合光线扇形图",
        ["Distortion"] = "畸变",
        ["Grid Distortion"] = "网格畸变",
        ["Field Curvature"] = "场曲",
        ["Encircled Energy"] = "包围能量",
        ["Pupil Aberration"] = "瞳孔像差",
        ["RMS vs Field"] = "RMS-视场",
        ["RMS Wavefront vs Field"] = "RMS 波前-视场",
        ["Through Focus"] = "离焦扫描",
        ["Through Focus MTF"] = "离焦 MTF",
        ["Angle vs Image Height - Through Pupil"] = "入射角-像高（扫描瞳孔）",
        ["Angle vs Image Height - Through Field"] = "入射角-像高（扫描视场）",
        ["Incoherent Irradiance"] = "非相干照度",
        ["Radiant Intensity"] = "辐射强度",
        ["Y-Ybar"] = "Y-Ybar",
        ["PSF"] = "点扩散函数 PSF",
        ["MMDFT PSF"] = "矩阵乘法 DFT PSF",
        ["Huygens PSF"] = "惠更斯 PSF",
        ["MTF"] = "调制传递函数 MTF",
        ["Huygens MTF"] = "惠更斯 MTF",
        ["Geometric MTF"] = "几何 MTF",
        ["Sampled MTF"] = "采样 MTF",
        ["Wavefront"] = "波前",
        ["Centroid Sphere Wavefront"] = "质心参考球波前",
        ["Best Fit Sphere Wavefront"] = "最佳拟合球波前",
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
        ["CentroidX"] = "质心 X",
        ["CentroidY"] = "质心 Y",
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
        ["TotalWeight"] = "总权重",
        ["ApertureRadius"] = "孔径半径",
        ["EntrancePupilEstimate"] = "入瞳估计",
        ["ChiefRayPupilShiftProxy"] = "主光线瞳移近似",
        ["WeightedMean"] = "加权平均",
        ["IncludedFieldWeight"] = "参与聚合视场权重",
        ["FocusStep"] = "离焦步长",
        ["Minus2StepRms"] = "-2 步 RMS",
        ["Minus1StepRms"] = "-1 步 RMS",
        ["NominalRms"] = "名义焦点 RMS",
        ["Plus1StepRms"] = "+1 步 RMS",
        ["Plus2StepRms"] = "+2 步 RMS",
        ["BestFocusShift"] = "最佳焦移",
        ["BestRmsSpotRadius"] = "最佳 RMS 点半径",
        ["Radius80AtBest"] = "最佳 80% 半径",
        ["ReferenceOpticalPathLength"] = "参考光程",
        ["MeanOpticalPathDifference"] = "平均光程差",
        ["RmsOpticalPathDifference"] = "RMS 光程差",
        ["PeakToValleyOpticalPathDifference"] = "PV 光程差",
        ["RmsWaves"] = "RMS 波数",
        ["RmsWavefrontProxy"] = "RMS 波前近似",
        ["PeakToValleyProxy"] = "PV 波前近似",
        ["Reference"] = "参考",
        ["Method"] = "方法",
        ["Sigma"] = "Sigma",
        ["PeakNormalized"] = "归一化峰值",
        ["Pipeline"] = "仿真流程",
        ["OutputShape"] = "输出形状",
        ["WavelengthsMicrometers"] = "仿真波长 (µm)",
        ["PsfGridShape"] = "PSF 视场网格",
        ["PsfSize"] = "PSF 尺寸",
        ["EigenPsfComponents"] = "EigenPSF 分量数",
        ["DistortionGridSize"] = "畸变采样网格",
        ["DistortionPolynomialDegree"] = "畸变拟合阶数",
        ["MeanAbsoluteChange"] = "平均绝对变化",
        ["MaximumOutputValue"] = "最大输出值",
        ["Field"] = "视场",
        ["ValidRayCount"] = "有效光线数",
        ["CoatingMode"] = "镀膜模式",
        ["Layout"] = "图形布局",
        ["Name"] = "名称",
        ["SurfaceCount"] = "表面数",
        ["FieldCount"] = "视场数",
        ["WavelengthCount"] = "波长数",
        ["WavelengthMicrometers"] = "分析波长 (µm)",
        ["DistortionType"] = "畸变模型",
        ["MaximumAbsoluteDistortionPercent"] = "最大绝对畸变 (%)",
        ["MaximumDistortionPercent"] = "最大网格畸变 (%)",
        ["MaximumAbsoluteImagePlaneDelta"] = "最大像面偏移 (mm)",
        ["GridSize"] = "网格尺寸",
        ["ImageSize"] = "图像尺寸",
        ["PixelPitchMicrometers"] = "像素间距 (µm)",
        ["PixelPitchMillimeters"] = "像素间距 (mm)",
        ["Samples"] = "采样点数",
        ["NumRings"] = "六角采样环数",
        ["NumRays"] = "光线数",
        ["Distribution"] = "瞳孔采样分布",
        ["PlotPointCount"] = "曲线采样点数",
        ["MaximumGeometricSpotRadius"] = "最大几何点半径 (mm)",
        ["MaximumRmsSpotSize"] = "最大 RMS 点尺寸 (mm)",
        ["MaximumRmsWavefrontError"] = "最大 RMS 波前误差 (波)",
        ["ScanMode"] = "扫描模式",
        ["SurfaceIndex"] = "测量表面序号",
        ["Axis"] = "测量轴",
        ["PointCount"] = "采样点数",
        ["FixedCoordinates"] = "固定坐标",
        ["FocusPlaneCount"] = "焦面数量",
        ["SpatialFrequency"] = "空间频率 (cycles/mm)",
        ["RawTangential"] = "切向 MTF 原始数据",
        ["RawSagittal"] = "弧矢 MTF 原始数据",
        ["ParaxialStopRadius"] = "近轴停光面半径 (mm)",
        ["MinimumPupilAberration"] = "最小光瞳像差 (%)",
        ["MaximumPupilAberration"] = "最大光瞳像差 (%)",
        ["MinimumRayAberration"] = "最小光线像差 (mm)",
        ["MaximumRayAberration"] = "最大光线像差 (mm)",
        ["PupilSampling"] = "瞳面采样数",
        ["DetectorSurfaceIndex"] = "探测器表面序号",
        ["DetectorExtent"] = "探测器范围",
        ["Resolution"] = "探测器分辨率",
        ["Normalized"] = "归一化显示",
        ["PeakIrradiance"] = "峰值照度 (W/mm²)",
        ["PythonRequirement"] = "Python Optiland 要求",
        ["ReferenceSurfaceIndex"] = "参考表面序号",
        ["AngularBins"] = "角度分箱",
        ["AngleXRange"] = "X 角度范围",
        ["AngleYRange"] = "Y 角度范围",
        ["UseAbsoluteUnits"] = "使用绝对单位",
        ["PeakRadiantIntensity"] = "峰值辐射强度 (W/sr)",
        ["ScaleByDiffractionLimit"] = "乘以衍射极限包络",
        ["FitRings"] = "拟合球六角采样环数",
        ["ReferenceCenters"] = "最佳拟合球中心",
        ["ZernikeTerms"] = "Zernike 拟合项数",
        ["WorkingFNumber"] = "工作 F/#",
        ["StrehlRatio"] = "斯特列尔比",
        ["PeakStrehlRatio"] = "峰值斯特列尔比",
        ["CutoffFrequency"] = "截止频率 (cycles/mm)",
        ["ReferenceSphereCenter"] = "参考球中心",
        ["ReferenceSphereRadius"] = "参考球半径 (mm)",
        ["ZernikeType"] = "Zernike 类型",
        ["FieldHx"] = "归一化视场 Hx",
        ["FieldHy"] = "归一化视场 Hy",
        ["ParabasalDelta"] = "近轴光线间隔",
        ["MaxFieldDegrees"] = "最大视场 (deg)",
        ["EFL"] = "有效焦距",
        ["WeightedMetric"] = "加权指标",
        ["Status"] = "状态"
    };
}

public sealed record AnalysisView(
    string Name,
    IReadOnlyList<AnalysisRow> Rows,
    string ReportText,
    AnalysisSeries? Series,
    IReadOnlyList<AnalysisSeries> SeriesList,
    AnalysisPlotOptions PlotOptions,
    IReadOnlyList<AnalysisPlotPane> PlotPanes,
    int PlotPaneColumns);

public sealed record AnalysisRow(string Metric, string Value);

public enum AnalysisParameterKind
{
    Integer,
    Double,
    Choice,
    Boolean
}

public sealed record AnalysisParameterDescriptor(
    string Key,
    string DisplayName,
    AnalysisParameterKind Kind,
    string DefaultValue,
    double Minimum = 0,
    double Maximum = 1,
    double Increment = 1,
    IReadOnlyList<string>? Choices = null);

public sealed record TolerancingView(
    string Summary,
    IReadOnlyList<TolerancingSensitivityRow> SensitivityRows,
    IReadOnlyList<TolerancingTrialRow> TrialRows,
    string Details)
{
    public static TolerancingView Empty(string message)
    {
        return new TolerancingView(message, Array.Empty<TolerancingSensitivityRow>(), Array.Empty<TolerancingTrialRow>(), message);
    }
}

public sealed record TolerancingSensitivityRow(string Perturbation, string DeltaMerit);

public sealed record TolerancingTrialRow(int Trial, string Merit, string CompensatedMerit);

public sealed record MultiConfigurationRow(int Index, string Name, bool Active, int SurfaceCount, string TotalTrack, string EffectiveFocalLength);
