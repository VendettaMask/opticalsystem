using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    private const float MillimetersToPoints = 72f / 25.4f;
    private const float A4Width = 210 * MillimetersToPoints;
    private const float A4Height = 297 * MillimetersToPoints;
    private static readonly IReadOnlyList<float> CurrentOpticalGlassHatchHalfLengths =
        Array.AsReadOnly(new[] { 3.0f, 5.6f, 3.0f });
    private static readonly IReadOnlyList<float> LegacyGbOpticalGlassHatchHalfLengths =
        Array.AsReadOnly(new[] { 4.6f, 4.6f, 4.6f });
    private static readonly float[] CurrentOpticalAxisDash = { 9f, 4f, 2f, 4f, 2f, 4f };
    private static readonly float[] LegacyGbOpticalAxisDash = { 9f, 4f, 2f, 4f };
    private static readonly double[] PreferredDrawingScales =
    {
        100d,
        50d,
        20d,
        10d,
        5d,
        2d,
        1d,
        0.5d,
        0.2d,
        0.1d,
        0.05d,
        0.02d,
        0.01d
    };
    private static readonly Lazy<SKTypeface> ChineseTypeface = new(ResolveChineseTypeface);
    private static readonly Lazy<byte[]?> DefaultCompanyLogoPng = new(LoadDefaultCompanyLogo);
    private static readonly ConcurrentDictionary<int, SKTypeface> FallbackTypefaces = new();

    internal static (float Width, float Height) PageDimensions(OpticalDrawingPageSize pageSize) =>
        pageSize == OpticalDrawingPageSize.A3
            ? (297 * MillimetersToPoints, 420 * MillimetersToPoints)
            : (A4Width, A4Height);

    private static bool IsLegacyGb1991(OpticalDrawingStandard standard) =>
        standard == OpticalDrawingStandard.GbT13323_1991;

    private static float[] OpticalAxisDashPattern(OpticalDrawingStandard standard) =>
        IsLegacyGb1991(standard)
            ? LegacyGbOpticalAxisDash
            : CurrentOpticalAxisDash;
}
