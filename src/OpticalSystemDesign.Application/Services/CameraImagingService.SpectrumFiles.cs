using System.Globalization;
using System.Text;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    public Task<CameraSpectrumCurve> ImportIlluminantAsync(string path, CancellationToken cancellationToken = default)
        => ImportSpectrumAsync(path, CameraSpectrumKind.Illuminant, cancellationToken);
    public Task<CameraSpectrumCurve> ImportReflectanceAsync(string path, CancellationToken cancellationToken = default)
        => ImportSpectrumAsync(path, CameraSpectrumKind.Reflectance, cancellationToken);

    private static async Task<CameraSpectrumCurve> ImportSpectrumAsync(string path, CameraSpectrumKind kind, CancellationToken token)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumSensorFileBytes, "Camera spectrum", token).ConfigureAwait(false);
        try
        {
            var samples = new List<CameraSpectrumSample>(); var seenHeader = false; var scale = 1d;
            foreach (var row in CameraSpectralCsvReader.Read(bytes, token))
            {
                if (!seenHeader)
                {
                    var header = row.Header;
                    if (kind == CameraSpectrumKind.Illuminant && header != "wavelength_nm,irradiance_w_m2_nm")
                        throw new InvalidDataException("光源表头应为 wavelength_nm,irradiance_w_m2_nm，使用绝对 W/m²/nm，不自动归一化。");
                    if (kind == CameraSpectrumKind.Reflectance)
                        scale = header switch
                        {
                            "wavelength_nm,reflectance" => 1,
                            "wavelength_nm,reflectance_percent" => .01,
                            _ => throw new InvalidDataException("反射率表头应为 wavelength_nm,reflectance；百分数使用 reflectance_percent。")
                        };
                    seenHeader = true; continue;
                }
                if (row.Cells.Length != 2 || samples.Count >= SensorSpectralResponse.MaximumSamples
                    || !row.TryNumbers(out var values, requireFinite: false))
                    throw new InvalidDataException($"光谱 CSV 第 {row.LineNumber} 行无效：需要两列数值，最多 4096 点。");
                samples.Add(new(values[0], values[1] * scale));
            }
            return FreezeSpectrum(new(Path.GetFileNameWithoutExtension(path),
                kind == CameraSpectrumKind.Illuminant ? "CSV absolute ideal-white image-plane spectral irradiance, W/m²/nm"
                    : scale == 1 ? "CSV material reflectance, fraction" : "CSV material reflectance, percent converted to fraction",
                kind, samples, Origin(path, bytes)))!;
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        { throw new InvalidDataException("光谱导入失败：" + ex.Message, ex); }
    }

    private static CameraSpectrumCurve? FreezeSpectrum(CameraSpectrumCurve? curve)
    {
        if (curve is null) return null;
        Label(curve.Name, 256, "Spectrum name", allowEmpty: false); Label(curve.Source, 2048, "Spectrum source");
        ValidateOrigin(curve.Origin);
        if (curve.Samples is null || curve.Samples.Count > SensorSpectralResponse.MaximumSamples)
            throw new ArgumentException("Spectrum samples are missing or too numerous.");
        var samples = curve.Samples.ToArray();
        _ = ToCore(curve with { Samples = samples });
        return curve with { Samples = Array.AsReadOnly(samples) };
    }
    private static ScalarSpectrum ToCore(CameraSpectrumCurve curve) => new((ScalarSpectrumKind)curve.Kind,
        curve.Samples.Select(s => s is null ? throw new ArgumentException("Null spectral sample.") : new ScalarSpectrumSample(s.WavelengthNanometers, s.Value)));

    private static string SpectrumCsv(CameraSpectrumCurve curve)
    {
        var text = new StringBuilder(curve.Kind == CameraSpectrumKind.Illuminant ? "wavelength_nm,irradiance_w_m2_nm\n" : "wavelength_nm,reflectance\n");
        foreach (var s in curve.Samples) text.Append(CultureInfo.InvariantCulture, $"{s.WavelengthNanometers:G17},{s.Value:G17}\n");
        return text.ToString();
    }
    private static string IntegrationCsv(CameraIntegrationDiagnostics integration)
    {
        var text = new StringBuilder("wavelength_nm,weight_nm,irradiance_w_m2_nm,reflectance,qe_r,qe_g,qe_b\n");
        foreach (var n in integration.Nodes) text.Append(CultureInfo.InvariantCulture,
            $"{n.WavelengthNanometers:G17},{n.WeightNanometers:G17},{n.IrradianceWattsPerSquareMeterPerNanometer:G17},{n.Reflectance:G17},{n.RedQe:G17},{n.GreenQe:G17},{n.BlueQe:G17}\n");
        return text.ToString();
    }
}
