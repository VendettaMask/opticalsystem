using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public abstract class BaseAnalysis
{
    protected BaseAnalysis(Optic optic)
    {
        Optic = optic;
    }

    protected Optic Optic { get; }

    public abstract string Name { get; }

    public abstract AnalysisData GenerateData();
}

public sealed record AnalysisData(string Name, IReadOnlyDictionary<string, object> Values)
{
    public string ExportText()
    {
        return string.Join(Environment.NewLine, Values.Select(item => $"{item.Key}: {item.Value}"));
    }
}

public sealed class SpotDiagramAnalysis : BaseAnalysis
{
    public SpotDiagramAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Spot Diagram";

    public override AnalysisData GenerateData()
    {
        var summary = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = summary.RayCount,
            ["VignettedRayCount"] = summary.VignettedRayCount,
            ["Centroid"] = summary.Centroid,
            ["RmsSpotRadius"] = summary.RmsSpotRadius,
            ["MaxSpotRadius"] = summary.MaxSpotRadius
        });
    }
}

public sealed class RayFanAnalysis : BaseAnalysis
{
    public RayFanAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Ray Fan";

    public override AnalysisData GenerateData()
    {
        var fan = new AnalysisRunner(Optic).BuildRayFan();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = fan.Count,
            ["Min"] = fan.Count == 0 ? 0 : fan.Min(),
            ["Max"] = fan.Count == 0 ? 0 : fan.Max()
        });
    }
}

public sealed class FirstOrderAnalysis : BaseAnalysis
{
    public FirstOrderAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "First Order";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}

public sealed class DistortionAnalysis : BaseAnalysis
{
    public DistortionAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Distortion";

    public override AnalysisData GenerateData()
    {
        var maxField = Optic.Fields.Count == 0 ? 0 : Optic.Fields.Max(field => Math.Abs(field.YAngleDegrees));
        var idealHeight = Math.Tan(maxField * Math.PI / 180.0) * Math.Max(1, Math.Abs(Optic.Paraxial.EstimateEffectiveFocalLength()));
        var trace = Optic.RealRayTracer.TraceMeridionalRays(3);
        var actualHeight = trace.Paths.Where(path => path.Segments.Count > 0).Select(path => path.Segments[^1].End.Y).DefaultIfEmpty(0).Average();
        var distortion = Math.Abs(idealHeight) < 1e-12 ? 0 : (actualHeight - idealHeight) / idealHeight * 100.0;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaxFieldDegrees"] = maxField,
            ["IdealImageHeight"] = idealHeight,
            ["MeanActualHeight"] = actualHeight,
            ["DistortionPercent"] = distortion
        });
    }
}

public sealed class FieldCurvatureAnalysis : BaseAnalysis
{
    public FieldCurvatureAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Field Curvature";

    public override AnalysisData GenerateData()
    {
        var focalLength = Optic.Paraxial.EstimateEffectiveFocalLength();
        var curvature = Math.Abs(focalLength) < 1e-12 ? 0 : 1.0 / focalLength;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["PetzvalProxy"] = curvature,
            ["SagAtFullField"] = curvature * Math.Pow(Optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max(), 2)
        });
    }
}

public sealed class EncircledEnergyAnalysis : BaseAnalysis
{
    public EncircledEnergyAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var spot = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Radius50"] = spot.RmsSpotRadius * 0.6745,
            ["Radius80"] = spot.RmsSpotRadius * 1.2816,
            ["Radius95"] = spot.RmsSpotRadius * 1.96
        });
    }
}

public sealed class PupilAberrationAnalysis : BaseAnalysis
{
    public PupilAberrationAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Pupil Aberration";

    public override AnalysisData GenerateData()
    {
        var aperture = Optic.SurfaceGroup.ApertureRadius();
        var focalLength = Optic.Paraxial.EstimateEffectiveFocalLength();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["ApertureRadius"] = aperture,
            ["EntrancePupilEstimate"] = aperture * 2,
            ["ChiefRayPupilShiftProxy"] = Math.Abs(focalLength) < 1e-12 ? 0 : aperture / Math.Abs(focalLength)
        });
    }
}

public sealed class RmsVsFieldAnalysis : BaseAnalysis
{
    public RmsVsFieldAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "RMS vs Field";

    public override AnalysisData GenerateData()
    {
        var baseRms = new AnalysisRunner(Optic).EvaluateSpotDiagram().RmsSpotRadius;
        var values = Optic.Fields.ToDictionary(
            field => field.Label,
            field => (object)(baseRms * (1.0 + Math.Abs(field.YAngleDegrees) / 20.0)));
        values["WeightedMean"] = values.Values.OfType<double>().DefaultIfEmpty(0).Average();
        return new AnalysisData(Name, values);
    }
}

public sealed class ThroughFocusAnalysis : BaseAnalysis
{
    public ThroughFocusAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Through Focus";

    public override AnalysisData GenerateData()
    {
        var rms = new AnalysisRunner(Optic).EvaluateSpotDiagram().RmsSpotRadius;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FocusMinus"] = rms * 1.2,
            ["FocusNominal"] = rms,
            ["FocusPlus"] = rms * 1.2,
            ["BestFocusShift"] = 0.0
        });
    }
}

public sealed class YYbarAnalysis : BaseAnalysis
{
    public YYbarAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Y-Ybar";

    public override AnalysisData GenerateData()
    {
        var trace = Optic.SequentialRayTracer.Trace();
        var values = new Dictionary<string, object>();
        foreach (var surface in Optic.SurfaceGroup.Items)
        {
            var heights = trace.RayHistories
                .SelectMany(history => history)
                .Where(sample => sample.SurfaceNumber == surface.Number)
                .Select(sample => sample.Position.Y)
                .ToArray();
            values[$"Surface {surface.Number}"] = heights.Length == 0 ? 0 : heights.Average();
        }

        return new AnalysisData(Name, values);
    }
}

public sealed class WavefrontAnalysis : BaseAnalysis
{
    public WavefrontAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Wavefront";

    public override AnalysisData GenerateData()
    {
        var aberrations = Optic.Aberrations.Estimate();
        var rms = Math.Sqrt((aberrations.Spherical * aberrations.Spherical) + (aberrations.Coma * aberrations.Coma) + (aberrations.Astigmatism * aberrations.Astigmatism));
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RmsWavefrontProxy"] = rms,
            ["PeakToValleyProxy"] = rms * 4,
            ["Reference"] = "chief-ray"
        });
    }
}

public sealed class ZernikeAnalysis : BaseAnalysis
{
    public ZernikeAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Zernike";

    public override AnalysisData GenerateData()
    {
        var aberrations = Optic.Aberrations.Estimate();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Z4 Defocus"] = Optic.Paraxial.EstimateFNumber(),
            ["Z5 Astigmatism 45"] = aberrations.Astigmatism * 0.5,
            ["Z6 Astigmatism 0"] = aberrations.Astigmatism,
            ["Z7 Coma Y"] = aberrations.Coma,
            ["Z11 Spherical"] = aberrations.Spherical
        });
    }
}

public sealed class PsfAnalysis : BaseAnalysis
{
    public PsfAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "PSF";

    public override AnalysisData GenerateData()
    {
        var rms = new AnalysisRunner(Optic).EvaluateSpotDiagram().RmsSpotRadius;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "gaussian-proxy",
            ["Sigma"] = rms,
            ["PeakNormalized"] = rms <= 1e-12 ? 1.0 : 1.0 / (2 * Math.PI * rms * rms)
        });
    }
}

public sealed class MtfAnalysis : BaseAnalysis
{
    public MtfAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "MTF";

    public override AnalysisData GenerateData()
    {
        var rms = new AnalysisRunner(Optic).EvaluateSpotDiagram().RmsSpotRadius;
        double MtfAt(double frequency) => Math.Exp(-2 * Math.PI * Math.PI * rms * rms * frequency * frequency);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "geometric-gaussian-proxy",
            ["MTF10lpmm"] = MtfAt(0.01),
            ["MTF50lpmm"] = MtfAt(0.05),
            ["MTF100lpmm"] = MtfAt(0.1)
        });
    }
}

public sealed class ImageSimulationAnalysis : BaseAnalysis
{
    public ImageSimulationAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Image Simulation";

    public override AnalysisData GenerateData()
    {
        var spot = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["BlurKernelRadius"] = spot.RmsSpotRadius,
            ["LateralColorProxy"] = Optic.Wavelengths.Count == 0 ? 0 : Optic.Wavelengths.Max(w => w.Nanometers) - Optic.Wavelengths.Min(w => w.Nanometers),
            ["DistortionProxy"] = Optic.Fields.Count
        });
    }
}

public sealed class JonesPupilAnalysis : BaseAnalysis
{
    public JonesPupilAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Jones Pupil";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Jxx"] = 1.0,
            ["Jxy"] = 0.0,
            ["Jyx"] = 0.0,
            ["Jyy"] = 1.0,
            ["PolarizationState"] = "identity"
        });
    }
}

public sealed class PrescriptionReportAnalysis : BaseAnalysis
{
    public PrescriptionReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Prescription Report";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Name"] = Optic.Name,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count,
            ["FieldCount"] = Optic.Fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["EFL"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}

public sealed class GridDistortionAnalysis : PlaceholderAnalysis
{
    public GridDistortionAnalysis(Optic optic) : base(optic, "Grid Distortion")
    {
    }
}

public class PlaceholderAnalysis : BaseAnalysis
{
    public PlaceholderAnalysis(Optic optic, string name) : base(optic)
    {
        Name = name;
    }

    public override string Name { get; }

    public override AnalysisData GenerateData()
    {
        var spot = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["WeightedMetric"] = spot.RmsSpotRadius,
            ["Status"] = "framework-ready"
        });
    }
}

public sealed class AnalysisCatalog
{
    private readonly Optic _optic;

    public AnalysisCatalog(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<string> Names { get; } = new[]
    {
        "First Order",
        "Spot Diagram",
        "Ray Fan",
        "Distortion",
        "Grid Distortion",
        "Field Curvature",
        "Encircled Energy",
        "Pupil Aberration",
        "RMS vs Field",
        "Through Focus",
        "Y-Ybar",
        "PSF",
        "MTF",
        "Wavefront",
        "Zernike",
        "Image Simulation",
        "Jones Pupil",
        "Prescription Report"
    };

    public BaseAnalysis Create(string name)
    {
        return name switch
        {
            "First Order" => new FirstOrderAnalysis(_optic),
            "Spot Diagram" => new SpotDiagramAnalysis(_optic),
            "Ray Fan" => new RayFanAnalysis(_optic),
            "Distortion" => new DistortionAnalysis(_optic),
            "Grid Distortion" => new GridDistortionAnalysis(_optic),
            "Field Curvature" => new FieldCurvatureAnalysis(_optic),
            "Encircled Energy" => new EncircledEnergyAnalysis(_optic),
            "Pupil Aberration" => new PupilAberrationAnalysis(_optic),
            "RMS vs Field" => new RmsVsFieldAnalysis(_optic),
            "Through Focus" => new ThroughFocusAnalysis(_optic),
            "Y-Ybar" => new YYbarAnalysis(_optic),
            "PSF" => new PsfAnalysis(_optic),
            "MTF" => new MtfAnalysis(_optic),
            "Wavefront" => new WavefrontAnalysis(_optic),
            "Zernike" => new ZernikeAnalysis(_optic),
            "Image Simulation" => new ImageSimulationAnalysis(_optic),
            "Jones Pupil" => new JonesPupilAnalysis(_optic),
            "Prescription Report" => new PrescriptionReportAnalysis(_optic),
            _ => new PlaceholderAnalysis(_optic, name)
        };
    }
}
