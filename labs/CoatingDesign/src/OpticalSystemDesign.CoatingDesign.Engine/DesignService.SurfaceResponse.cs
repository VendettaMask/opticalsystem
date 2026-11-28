using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public static (double Start, double End, double Angle, double Center) ReadSurfaceSettings(string path)
    {
        var json = BoundedFile.ReadAllText(path, BoundedFile.MaximumOpticalDocumentBytes, "镀膜实验室设计");
        if (CoatingResponseTable.Read(json) is { } table)
        {
            var first = table.Rows[0].WavelengthNm;
            var last = table.Rows[^1].WavelengthNm;
            return (first, last, table.AngleDegrees, (first + last) / 2);
        }
        var t = ExperimentStore.DecodeInput(json).Target;
        return (t.MinimumNm, t.MaximumNm, t.AngleDegrees, t.CenterNm);
    }

    public static SurfaceCoatingResponse ReadSurfaceResponse(string path, double minimumNm, double maximumNm)
        => ReadResponse(path, minimumNm, maximumNm, forImaging: false);

    public static SurfaceCoatingResponse ReadImageSurfaceResponse(string path, double minimumNm, double maximumNm)
        => ReadResponse(path, minimumNm, maximumNm, forImaging: true);

    private static SurfaceCoatingResponse ReadResponse(string path, double minimumNm, double maximumNm, bool forImaging)
    {
        var json = BoundedFile.ReadAllText(path, BoundedFile.MaximumOpticalDocumentBytes, "镀膜实验室设计");
        if (CoatingResponseTable.Read(json) is { } table)
        {
            if (!forImaging || !table.ZeroPhase) return new(table.Name, table.Power);
            // A single angle in a Zemax TABLE supplies the endpoint response for every ray.
            ThinFilmPower Power(double nm, double angle, ThinFilmPolarization pol) =>
                table.Power(nm, table.AngleDegrees, pol);
            ThinFilmAmplitude Amplitude(double nm, double angle, ThinFilmPolarization pol)
            {
                var power = Power(nm, angle, pol);
                return new(Math.Sqrt(power.Reflectance), Math.Sqrt(power.Transmittance), power, 0);
            }
            return new(table.Name, Power, Amplitude) { IsTabulated = true };
        }
        var design = ExperimentStore.DecodeInput(json);
        var materials = new DesignService().Materials(design, new(minimumNm, maximumNm));
        var power = PowerEvaluator(design, materials, display: true);
        if (design.Duplex?.Evaluate == CoatingEvaluationSurface.WholePlate
            || design.Duplex is null && design.StackGeometry?.IncludeBackSurface == true)
            return new(design.Name, power);
        var back = design.Duplex?.Evaluate == CoatingEvaluationSurface.Back;
        var layers = (back ? design.EffectiveBackLayers : design.Layers)
            .Select(layer => new CoherentFilm(materials[layer.MaterialId], layer.ThicknessNm)).ToArray();
        var coherent = new CoherentThinFilmSolver(materials[back ? design.StackGeometry!.ExitMediumId : design.IncidentId],
            materials[design.SubstrateId], layers);
        return new(design.Name, coherent.Evaluate, coherent.EvaluateAmplitude)
        {
            TransmissionStack = forImaging ? new CoherentMultilayerCoating(layers) : null
        };
    }
}
