using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed record CoatingPlotDefaults(AnalysisSpectrumOptions Sampling, CoatingWavelengthUnit WavelengthUnit,
    CoatingValueUnit ValueUnit, bool AutomaticY, double MinimumY, double MaximumY, bool ShowGrid = true)
{
    public static CoatingPlotDefaults Factory => new(AnalysisSpectrumOptions.Default, CoatingWavelengthUnit.Nanometers, CoatingValueUnit.Percent, false, 0, 100);
    internal static string DefaultPath => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY") is { Length: > 0 } custom ? custom
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpticalSystemDesign"), "coating-plot-defaults.json");
    public void Validate()
    {
        if (Sampling is null) throw new ArgumentException("评估默认设置缺少采样参数。");
        Sampling.Validate(); var max = ValueUnit == CoatingValueUnit.Percent ? 100 : ValueUnit == CoatingValueUnit.Fraction ? 1 : 12;
        if (!Enum.IsDefined(WavelengthUnit) || !Enum.IsDefined(ValueUnit) || !double.IsFinite(MinimumY) || !double.IsFinite(MaximumY)
            || MinimumY < 0 || MaximumY <= MinimumY || MaximumY > max) throw new ArgumentException("纵轴默认范围无效。");
    }
    public static CoatingPlotDefaults Load(string path)
    {
        if (!File.Exists(path)) return Factory;
        var value = JsonSerializer.Deserialize<CoatingPlotDefaults>(BoundedFile.ReadAllBytes(path, BoundedFile.MaximumSettingsBytes, "镀膜评估默认设置"), Experiment.JsonOptions)
            ?? throw new ArgumentException("默认设置无效。"); value.Validate(); return value;
    }
    public void Save(string path) { Validate(); BoundedFile.WriteAllTextAtomic(path, JsonSerializer.Serialize(this, Experiment.JsonOptions), BoundedFile.MaximumSettingsBytes, "镀膜评估默认设置"); }
    public void Apply(SpectrumPlot plot)
    { Validate(); plot.SetUnits(WavelengthUnit, ValueUnit); plot.SetAutomaticY(AutomaticY); plot.SetGrid(ShowGrid); plot.ConfigureAnalysis(Sampling); plot.SetYRange(MinimumY, MaximumY); }
}
