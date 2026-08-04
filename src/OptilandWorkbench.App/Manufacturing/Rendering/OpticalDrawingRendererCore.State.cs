using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    private const float MillimetersToPoints = 72f / 25.4f;
    private const float A4Width = 210 * MillimetersToPoints;
    private const float A4Height = 297 * MillimetersToPoints;
    private static readonly Lazy<SKTypeface> ChineseTypeface = new(ResolveChineseTypeface);
    private static readonly Lazy<byte[]?> DefaultCompanyLogoPng = new(LoadDefaultCompanyLogo);
    private static readonly ConcurrentDictionary<int, SKTypeface> FallbackTypefaces = new();

    internal static (float Width, float Height) PageDimensions(OpticalDrawingPageSize pageSize) =>
        pageSize == OpticalDrawingPageSize.A3
            ? (297 * MillimetersToPoints, 420 * MillimetersToPoints)
            : (A4Width, A4Height);
}
