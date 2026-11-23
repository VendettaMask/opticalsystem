using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Core.FileIO;

public static partial class ZemaxNonSequentialReader
{
    private static (IReadOnlyList<NonSequentialSpectralSample> Bands, string Protocol) ReadSourceSpectrum(ZemaxNscRecord record)
    {
        var v = record.Values;
        var mode = Integer(v[2]); var from = Number(v[6]) * 1000; var to = Number(v[7]) * 1000; var count = Integer(v[8]);
        if (mode is 4 or 7)
            return (NonSequentialColorimetry.SampleSpectrum(from, to, count, mode == 7 ? Number(v[3]) : null),
                mode == 7 ? NonSequentialColorimetry.BlackBodySpectrumProtocol : NonSequentialColorimetry.UniformSpectrumProtocol);
        double x, y, z;
        if (mode == 1) { x = Number(v[3]); y = Number(v[4]); z = Number(v[5]); }
        else if (mode == 5) { x = .9505; y = 1; z = 1.089; }
        else if (mode is 2 or 9)
        {
            var a = Number(v[3]); var b = Number(v[4]);
            var denominator = 6 * a - 16 * b + 12;
            var cx = mode == 2 ? a : 9 * a / denominator;
            var cy = mode == 2 ? b : 4 * b / denominator;
            if (!double.IsFinite(cx) || !double.IsFinite(cy) || cx < 0 || cy <= 0 || cx + cy > 1)
                throw new InvalidDataException("光源色品坐标无效。");
            x = cx / cy; y = 1; z = (1 - cx - cy) / cy;
        }
        else throw new InvalidDataException("此光源的颜色模型尚未支持；未替换其光谱。");
        return (NonSequentialColorimetry.FitSpectrum(x, y, z, from, to, count), NonSequentialColorimetry.SpectrumFitProtocol);
    }
}
