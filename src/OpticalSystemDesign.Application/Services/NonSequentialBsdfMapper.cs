using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Scattering;
using CoreModel = OpticalSystemDesign.Core.NonSequential.NonSequentialSurfaceBsdf;
using CoreKind = OpticalSystemDesign.Core.NonSequential.NonSequentialBsdfKind;

namespace OpticalSystemDesign.Application.Services;

internal static class NonSequentialBsdfMapper
{
    public static CoreModel? ToCore(NonSequentialSurfaceBsdf? model)
    {
        if (model is null) return null;
        var d = model.MeasuredData;
        if (d?.ZemaxDefinition is { } zemax && (zemax.Integrals is null || zemax.Integrals.Count > 6400 || zemax.Integrals.Any(t => t is null)))
            throw new InvalidDataException("Zemax BSDF 的 TIS 载荷无效。");
        if (d is not null && (d.Points is null || d.Points.Count > MeasuredBsdfData.MaximumPoints || d.Points.Any(p => p is null)))
            throw new InvalidDataException("实测 BSDF 载荷数量无效。");
        var data = d is null ? null : new MeasuredBsdfData(d.Name, d.SourceFileName, d.SourceSha256,
            Array.AsReadOnly(d.Points.Select(p => new MeasuredBsdfPoint((BsdfSide)p.Side, p.WavelengthNanometers,
                p.IncidentThetaDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees, p.BrdfPerSteradian, p.BtdfPerSteradian, p.IncidentPhiDegrees)).ToArray()), d.DataSha256,
            (BsdfCoordinates)d.Coordinates, d.SourceFormat, d.ImportNotes, d.ZemaxDefinition is { } z
                ? new(z.Transmission, z.IncidentIndex, z.TransmittedIndex, z.Integrals.Select(t => new ZemaxBsdfTis((BsdfSide)t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees, t.Fraction)).ToArray()) : null, (BsdfStoredQuantity)d.StoredQuantity, d.SpeosPeak, d.ZemaxXyz, d.SpeosSpectral);
        var result = new CoreModel((CoreKind)(int)model.Kind, model.Reflectance, data, model.MaterialAxis is { } a ? new(a.X, a.Y, a.Z) : null); _ = result.Create(); return result;
    }
    public static NonSequentialSurfaceBsdf? ToDto(CoreModel? model)
    {
        if (model is null) return null;
        var d = model.MeasuredData;
        return new((NonSequentialBsdfKind)(int)model.Kind, model.Reflectance, d is null ? null : new(d.Name, d.SourceFileName,
            d.SourceSha256, d.DataSha256, Array.AsReadOnly(d.Points.Select(p => new MeasuredBsdfPointDto((int)p.Side,
                p.WavelengthNanometers, p.IncidentThetaDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees,
                p.BrdfPerSteradian, p.BtdfPerSteradian, p.IncidentPhiDegrees)).ToArray()), (int)d.Coordinates, d.SourceFormat, d.ImportNotes,
                d.ZemaxDefinition is { } z ? new(z.Transmission, z.IncidentIndex, z.TransmittedIndex, Array.AsReadOnly(z.Integrals.Select(t => new ZemaxBsdfTisDto((int)t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees, t.Fraction)).ToArray())) : null, (int)d.StoredQuantity, d.SpeosPeak, d.ZemaxXyz, d.SpeosSpectral),
            model.MaterialAxis is { } axis ? new(axis.X, axis.Y, axis.Z) : null);
    }
}
