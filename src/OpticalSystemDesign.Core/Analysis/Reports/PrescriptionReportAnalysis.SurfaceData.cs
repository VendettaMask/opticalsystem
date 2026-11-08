using System.Globalization;
using System.Text;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed partial class PrescriptionReportAnalysis
{
    private string BuildSurfaceReport(IReadOnlyList<OpticalSurface> surfaces)
    {
        var report = new StringBuilder();
        report.AppendLine("表面数据报告");
        report.AppendLine("长度单位：mm    波长单位：µm    —：不适用或无法计算");
        var primary = Optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? Optic.Wavelengths.FirstOrDefault();
        for (var index = 0; index < surfaces.Count; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[index];
            var next = index + 1 < surfaces.Count ? surfaces[index + 1] : null;
            report.AppendLine().AppendLine($"表面 {surface.Number} · {surface.Label}");
            report.AppendLine($"类型：{SurfaceType(surface)}    曲率半径：{Display(surface.Radius == 0 ? double.PositiveInfinity : surface.Radius)}    圆锥系数：{Display(surface.Conic)}");
            report.AppendLine($"厚度：{Display(surface.Thickness)}    直径：{Display(2 * surface.SemiDiameter)}");
            report.AppendLine($"材料：{surface.Material}    光阑：{(surface.IsStop ? "是" : "否")}    镀膜：{surface.Coating}");
            if (next is not null)
            {
                report.AppendLine($"Y 边缘厚度：{Display(EdgeThickness(surface, next, alongY: true))}    " +
                    $"X 边缘厚度：{Display(EdgeThickness(surface, next, alongY: false))}");
            }

            report.AppendLine("折射率");
            report.AppendLine($"{"#",3}  {"λ (µm)",10}  {"n",13}");
            for (var waveIndex = 0; waveIndex < Optic.Wavelengths.Count; waveIndex++)
            {
                var wave = Optic.Wavelengths[waveIndex];
                report.AppendLine($"{waveIndex + 1,3}  {Display(wave.Micrometers, "0.000000"),10}  " +
                    $"{Display(RefractiveIndex(surface.MaterialAfter, wave), "0.0000000000"),13}");
            }

            var nBefore = RefractiveIndex(surface.MaterialBefore, primary);
            var nAfter = RefractiveIndex(surface.MaterialAfter, primary);
            var power = SurfacePower(surface, nBefore, nAfter);
            if (next is null || surface.MaterialAfter is AirMaterial || surface.IsReflective)
            {
                report.AppendLine($"表面光焦度（mm⁻¹）：{Display(power)}");
                continue;
            }

            var volume = Calculate(() => ElementVolumeMetrics.VolumeCubicCentimeters(surface, next, SurfaceDiameterMode.Clear));
            report.AppendLine($"元件体积（cm³，平边）：{Display(volume)}");
            var nExit = RefractiveIndex(next.MaterialAfter, primary);
            AppendElementPower(report, "当前环境", surface, next, nBefore, nAfter, nExit);
            AppendElementPower(report, "空气中", surface, next, 1, nAfter, 1);
            var frontCurvature = Curvature(surface);
            var backCurvature = Curvature(next);
            var shapeFactor = (frontCurvature + backCurvature) / (frontCurvature - backCurvature);
            report.AppendLine($"形状因子：{Display(double.IsFinite(shapeFactor) ? shapeFactor : double.NaN)}");
        }
        return report.ToString().TrimEnd();
    }

    private static void AppendElementPower(StringBuilder report, string environment, OpticalSurface front,
        OpticalSurface back, double nBefore, double nLens, double nAfter)
    {
        var frontPower = SurfacePower(front, nBefore, nLens);
        var backPower = SurfacePower(back, nLens, nAfter);
        var equivalentPower = frontPower + backPower - front.Thickness / nLens * frontPower * backPower;
        var focalLength = equivalentPower == 0 ? double.PositiveInfinity : nAfter / equivalentPower;
        var fNumber = front.SemiDiameter > 0 ? Math.Abs(focalLength) / (2 * front.SemiDiameter) : double.NaN;
        report.AppendLine($"表面光焦度（{environment}，mm⁻¹）");
        report.AppendLine($"面 {front.Number}：{Display(frontPower)}    面 {back.Number}：{Display(backPower)}");
        report.AppendLine($"组合光焦度：{Display(equivalentPower)}    EFL（mm）：{Display(focalLength)}    F/#：{Display(fNumber)}");
    }

    private static double EdgeThickness(OpticalSurface surface, OpticalSurface next, bool alongY)
    {
        if (!double.IsFinite(surface.Thickness) || surface.SemiDiameter <= 0 || next.SemiDiameter <= 0
            || surface.Geometry is INonComputableGeometry || next.Geometry is INonComputableGeometry)
            return double.NaN;
        return Calculate(() => surface.Thickness + Sag(next) - Sag(surface));

        double Sag(OpticalSurface face) => face.Geometry.Sag(alongY ? 0 : face.SemiDiameter, alongY ? face.SemiDiameter : 0);
    }

    private static double Curvature(OpticalSurface surface) =>
        surface.Geometry is PlaneGeometry or StandardGeometry or EvenAsphereGeometry
            ? 1 / Paraxial.ParaxialRadius(surface) : double.NaN;

    private static double SurfacePower(OpticalSurface surface, double before, double after) =>
        surface.IsReflective || surface.InteractionModel is not RefractiveReflectiveInteractionModel
            ? double.NaN : (after - before) * Curvature(surface);

    private static double RefractiveIndex(IMaterial material, Wavelength? wave) =>
        wave is not null && material.PropagationModel is HomogeneousPropagationModel
            ? Calculate(() => material.RefractiveIndex(wave.Nanometers)) : double.NaN;

    private static double Calculate(Func<double> calculate)
    {
        try
        {
            var result = calculate();
            return double.IsFinite(result) ? result : double.NaN;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or ArithmeticException)
        { return double.NaN; }
    }

    private static string Display(double value, string format = "0.######") => double.IsNaN(value) ? "—"
        : double.IsInfinity(value) ? "无限" : value.ToString(format, CultureInfo.InvariantCulture);
}
