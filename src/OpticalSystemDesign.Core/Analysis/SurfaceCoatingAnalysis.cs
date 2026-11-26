using System.Globalization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record SurfaceCoatingResponse(string Name,
    Func<double, double, ThinFilmPolarization, ThinFilmPower> Power,
    Func<double, double, ThinFilmPolarization, ThinFilmAmplitude>? Amplitude = null,
    bool OpaqueMirror = false)
{
    public bool IsTabulated { get; init; }
    public CoherentMultilayerCoating? TransmissionStack { get; init; }
}

public sealed record SurfaceCoatingSettings(int SurfaceIndex = 1, int WavelengthNumber = 0,
    double MinimumAngle = 0, double MaximumAngle = 90, double IncidenceAngle = 0,
    double MinimumWavelengthNm = 400, double MaximumWavelengthNm = 700, int Samples = 361, double FixedWavelengthNm = 0);

public sealed class SurfaceCoatingAnalysis(Optic optic, string key, SurfaceCoatingSettings? settings = null,
    Func<double, double, SurfaceCoatingResponse>? design = null) : BaseAnalysis(optic)
{
    public const string Spectrum = "Transmission vs. Wavelength";
    public const string Transmission = "Transmission vs. Angle";
    public const string Reflection = "Reflection vs. Angle";
    public const string Phase = "Phase vs. Angle";
    public override string Name => key;

    public override AnalysisData GenerateData()
    {
        var config = settings ?? new();
        var spectral = key == Spectrum;
        if (key is not (Spectrum or Transmission or Reflection or Phase)) throw new ArgumentOutOfRangeException(nameof(key));
        if (config.SurfaceIndex < 1 || config.SurfaceIndex >= Optic.SurfaceGroup.Items.Count - 1)
            return AnalysisData.Unavailable(Name, "请选择物面与像面之间的光学表面。");
        var surface = Optic.SurfaceGroup.Items[config.SurfaceIndex];
        if (surface.InteractionModel is Interactions.CoordinateReferenceInteractionModel)
            return AnalysisData.Unavailable(Name, "坐标参考面没有可分析的光学界面。");
        var minimum = spectral ? config.MinimumWavelengthNm : config.MinimumAngle;
        var maximum = spectral ? config.MaximumWavelengthNm : config.MaximumAngle;
        if (config.Samples is < 2 or > 10001 || !double.IsFinite(minimum) || !double.IsFinite(maximum)
            || minimum >= maximum || (spectral ? minimum <= 0 || !double.IsFinite(config.IncidenceAngle)
                || config.IncidenceAngle is < 0 or >= 90 : minimum < 0 || maximum > 90))
            return AnalysisData.Unavailable(Name, "请检查扫描范围、入射角和采样点数。");
        var wave = config.WavelengthNumber == 0 ? Optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? Optic.Wavelengths.FirstOrDefault()
            : Optic.Wavelengths.ElementAtOrDefault(config.WavelengthNumber - 1);
        var wavelength = config.FixedWavelengthNm == 0 ? wave?.Nanometers ?? 0 : config.FixedWavelengthNm;
        if (!spectral && (!double.IsFinite(wavelength) || wavelength <= 0))
            return AnalysisData.Unavailable(Name, "请选择有效的系统波长或输入分析波长。");

        try
        {
            var response = design?.Invoke(spectral ? minimum : wavelength, spectral ? maximum : wavelength)
                ?? FromSurface(surface);
            if (key == Phase && response.Amplitude is null)
                return AnalysisData.Unavailable(Name, "当前膜系只定义了功率响应，无法确定相位。");
            return Sweep(config, response, surface.IsReflective, minimum, maximum, wavelength);
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or NotSupportedException
            or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return AnalysisData.Unavailable(Name, error.Message);
        }
    }

    private static SurfaceCoatingResponse FromSurface(OpticalSurface surface)
    {
        if (surface.ReferencedCoating is { } reference)
            throw new NotSupportedException($"表面引用膜系 {reference}。请在设置中选择对应的镀膜实验室设计文件。");
        if (surface.CoatingModel is SimpleCoatingModel simple)
            return new(surface.Coating, (_, _, _) => new(simple.Reflectance, simple.Transmittance,
                1 - simple.Reflectance - simple.Transmittance, Math.Log(simple.Transmittance)));
        if (surface.CoatingModel is not (CoherentMultilayerCoating or NoneCoatingModel))
            throw new NotSupportedException("当前表面的经验膜层缺少复折射率和物理厚度，请选择镀膜设计文件。");
        var layers = (surface.CoatingModel as CoherentMultilayerCoating)?.Layers ?? [];
        var defaultMirror = surface.IsReflective && surface.CoatingModel is NoneCoatingModel;
        // The lens-file MIRROR default is optically thick aluminium (n=0.7, k=7).
        var substrate = defaultMirror ? new ConstantIndexMaterial("Al", .7, 7) : surface.MaterialAfter;
        var solver = new CoherentThinFilmSolver(surface.MaterialBefore, substrate, layers);
        return new(defaultMirror ? "Al (n=0.7, k=7)" : surface.Coating, solver.Evaluate, solver.EvaluateAmplitude, defaultMirror);
    }

    private AnalysisData Sweep(SurfaceCoatingSettings config, SurfaceCoatingResponse response, bool mirror,
        double minimum, double maximum, double wavelength)
    {
        var spectral = key == Spectrum;
        var phase = key == Phase;
        var xLabel = spectral ? "Wavelength (nm)" : "Incidence angle (°)";
        var yLabel = phase ? (mirror ? "Reflection phase (°)" : "Transmission phase (°)") :
            key == Reflection ? "Reflectance" : "Transmittance";
        var curves = new List<AnalysisSeries>();
        var rows = new List<IReadOnlyList<string>>();
        string[] labels = phase ? ["S", "P"] : ["S", "P", "(S + P) / 2"];
        var segments = labels.Select(_ => new List<AnalysisPoint>()).ToArray();
        void Finish(int j)
        {
            if (segments[j].Count == 0) return;
            curves.Add(new(xLabel, yLabel, segments[j].ToArray(), Name: labels[j], LegendKey: labels[j], ColorIndex: j, ShowMarkers: segments[j].Count == 1,
                XQuantity: spectral ? AnalysisAxisQuantity.Wavelength : AnalysisAxisQuantity.IncidentAngle,
                XUnit: spectral ? AnalysisAxisUnit.Nanometer : AnalysisAxisUnit.Degree,
                YQuantity: phase ? AnalysisAxisQuantity.Angle : AnalysisAxisQuantity.EnergyFraction,
                YUnit: phase ? AnalysisAxisUnit.Degree : AnalysisAxisUnit.Dimensionless));
            segments[j].Clear();
        }
        for (var i = 0; i < config.Samples; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            // Grazing incidence is outside the solver's domain. Keep the axis ending at 90°.
            var openEnd = !spectral && maximum == 90;
            var x = !openEnd && i == config.Samples - 1 ? maximum :
                minimum + (maximum - minimum) * i / (openEnd ? config.Samples : config.Samples - 1);
            var angle = spectral ? config.IncidenceAngle : x;
            var nm = spectral ? x : wavelength;
            double? Value(ThinFilmPolarization pol)
            {
                if (phase)
                {
                    var a = response.Amplitude!(nm, angle, pol);
                    if (mirror) return a.Reflection.Magnitude > 1e-14 ? a.Reflection.Phase * 180 / Math.PI : null;
                    return a.Power.Transmittance > 0 ? a.TransmissionPhaseRadians * 180 / Math.PI : null;
                }
                var power = response.Power(nm, angle, pol);
                return key == Reflection ? power.Reflectance : response.OpaqueMirror ? 0 : power.Transmittance;
            }
            double? s, p;
            try
            {
                s = Value(ThinFilmPolarization.S);
                p = Value(ThinFilmPolarization.P);
            }
            catch (CriticalCoatingAngleException)
            {
                // The solver has no value exactly at this singular point. Preserve the gap.
                s = p = null;
            }
            double?[] values = phase ? [s, p] : [s, p, (s + p) / 2];
            rows.Add(new[] { x.ToString("G12", CultureInfo.InvariantCulture) }.Concat(
                values.Select(v => v?.ToString("G12", CultureInfo.InvariantCulture) ?? "—")).ToArray());
            for (var j = 0; j < values.Length; j++)
            {
                if (values[j] is not { } value) { Finish(j); continue; }
                if (phase && segments[j].Count > 0 && Math.Abs(segments[j][^1].Y - value) > 180) Finish(j);
                segments[j].Add(new(x, value));
            }
        }
        for (var j = 0; j < labels.Length; j++) Finish(j);
        if (curves.Count == 0) return AnalysisData.Unavailable(Name, "扫描范围内没有可定义的相位或功率数据。");
        return new(Name, new Dictionary<string, object> { ["Surface"] = config.SurfaceIndex, ["Coating"] = response.Name,
            [spectral ? "Incidence angle (°)" : "Wavelength (nm)"] = spectral ? config.IncidenceAngle : wavelength },
            SeriesList: curves, PlotOptions: new(ShowLegend: true, XMinimum: minimum, XMaximum: maximum,
                YMinimum: phase ? -180 : 0, YMaximum: phase ? 180 : 1),
            Table: new(new[] { xLabel }.Concat(labels).ToArray(), rows));
    }
}
