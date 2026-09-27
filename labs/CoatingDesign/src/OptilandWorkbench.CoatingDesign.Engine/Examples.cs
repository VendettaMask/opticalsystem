namespace OptilandWorkbench.CoatingDesign.Engine;

public static class Examples
{
    public static Experiment Create(DesignKind kind)
    {
        var library = new MaterialLibrary();
        var low = library.Films.Single(m => m.Name.StartsWith("SiO2", StringComparison.Ordinal));
        var high = library.Films.Single(m => m.Name.StartsWith("Ta2O5", StringComparison.Ordinal));
        var incident = library.Select("Air");
        var substrate = library.Select("N-BK7");
        var target = kind switch
        {
            DesignKind.Antireflection => new DesignTarget(MaximumLayers: 8, Reflectance: 0.02),
            DesignKind.HighReflector => new DesignTarget(kind, MinimumNm: 520, MaximumNm: 580, Reflectance: 0.98, MaximumLayers: 32),
            _ => new DesignTarget(kind, MinimumNm: 480, MaximumNm: 620, MaximumLayers: 39, FwhmNm: 15,
                PeakTransmittance: 0.75, BlockingOd: 1, StopBands: [new(490, 510), new(590, 610)])
        };
        return new(Experiment.CurrentSchema, Guid.NewGuid(), kind switch
        {
            DesignKind.Antireflection => "示例一 · 可见光减反膜",
            DesignKind.HighReflector => "示例二 · 550 nm 高反膜",
            _ => "示例三 · 550 nm 窄带滤光片"
        }, target, new CalculationSettings(), incident.Id, substrate.Id, low.Id, high.Id,
            [incident, substrate, low, high], []);
    }
}
