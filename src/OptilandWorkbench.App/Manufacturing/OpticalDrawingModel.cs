using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Manufacturing;

public enum OpticalDrawingPageSize
{
    A4,
    A3
}

public enum OpticalDrawingStandard
{
    Iso10110,
    GbT13323_2009
}

public sealed record OpticalSystemDrawingSheet(
    Scene2Dto Scene,
    OpticalDrawingPageSize PageSize,
    string DrawingNumber,
    string PartName,
    string Designer,
    string Reviewer,
    string Revision,
    byte[]? CompanyLogoPng = null,
    OpticalDrawingStandard Standard = OpticalDrawingStandard.Iso10110);

public sealed record OpticalDrawingSheet(
    OpticalDrawingElementDefinition Element,
    OpticalDrawingPageSize PageSize,
    string DrawingNumber,
    string PartName,
    string Designer,
    string Reviewer,
    string Revision,
    double DiameterUpperDeviation,
    double DiameterLowerDeviation,
    double CenterThicknessUpperDeviation,
    double CenterThicknessLowerDeviation,
    double FrontSurfaceFormNanometers,
    double BackSurfaceFormNanometers,
    double CenteringToleranceArcMinutes,
    double SurfaceTextureNanometers,
    string SurfaceImperfection,
    string Coating,
    string EdgeTreatment,
    string StressBirefringence,
    string BubblesAndInclusions,
    string HomogeneityAndStriae,
    GlassMaterialDto? MaterialData = null,
    byte[]? CompanyLogoPng = null,
    double RefractiveIndexTolerance = 0.0005,
    double AbbeNumberTolerance = 0.5,
    OpticalDrawingStandard Standard = OpticalDrawingStandard.Iso10110,
    double FrontRadiusTolerance = 0.1,
    double BackRadiusTolerance = 0.1,
    IReadOnlyList<GlassMaterialDto?>? ComponentMaterialData = null)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DiameterUpperDeviation < DiameterLowerDeviation)
        {
            errors.Add("直径上偏差不能小于下偏差");
        }

        if (CenterThicknessUpperDeviation < CenterThicknessLowerDeviation)
        {
            errors.Add("中心厚度上偏差不能小于下偏差");
        }

        CheckNonNegative(FrontSurfaceFormNanometers, "S1 面形公差");
        CheckNonNegative(BackSurfaceFormNanometers, "S2 面形公差");
        CheckNonNegative(CenteringToleranceArcMinutes, "偏心/倾斜公差");
        CheckNonNegative(SurfaceTextureNanometers, "表面纹理公差");
        CheckNonNegative(RefractiveIndexTolerance, "折射率公差");
        CheckNonNegative(AbbeNumberTolerance, "阿贝数公差");
        CheckNonNegative(FrontRadiusTolerance, "S1 曲率半径公差");
        CheckNonNegative(BackRadiusTolerance, "S2 曲率半径公差");

        Require(SurfaceImperfection, "5/ 表面缺陷标注");
        Require(StressBirefringence, "0/ 应力双折射标注");
        Require(BubblesAndInclusions, "1/ 气泡和夹杂标注");
        Require(HomogeneityAndStriae, "2/ 均匀性和条纹标注");
        return errors;

        void CheckNonNegative(double value, string name)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                errors.Add($"{name}必须是非负有限值");
            }
        }

        void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{name}不能为空");
            }
        }
    }
}
