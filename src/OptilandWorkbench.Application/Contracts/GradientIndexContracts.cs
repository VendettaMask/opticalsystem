namespace OptilandWorkbench.Application.Contracts;

public sealed record GradientIndexCoefficientOptionDto(string Key, string Unit, double DefaultValue, string? VariableDisabledReason = null);
public sealed record GradientIndexProfileOptionDto(string Kind, string Name, IReadOnlyList<GradientIndexCoefficientOptionDto> Coefficients);
public sealed record GradientIndexCoefficientEditDto(string Key, double Value, bool Variable = false, double Minimum = -1, double Maximum = 1);
public sealed record GradientIndexDispersionEditDto(double ReferenceWavelengthNanometers, double MinimumWavelengthNanometers,
    double MaximumWavelengthNanometers, IReadOnlyList<IReadOnlyList<double>> K, IReadOnlyList<IReadOnlyList<double>> L);
public sealed record GradientIndexIntegrationEditDto(double MaximumPathLength = 100, double MaximumStep = .1,
    double MinimumStep = 1e-12, double PositionTolerance = 1e-10, double DirectionTolerance = 1e-11,
    double OpticalPathTolerance = 1e-10, double RelativeTolerance = 1e-11, double SurfaceTolerance = 1e-10,
    int MaximumAttempts = 100000);
public sealed record GradientIndexMaterialEditDto(string Name, string Profile,
    IReadOnlyList<GradientIndexCoefficientEditDto> Coefficients,
    GradientIndexDispersionEditDto? Dispersion, GradientIndexIntegrationEditDto Integration);
