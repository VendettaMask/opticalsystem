using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record MeasuredBsdfPoint(BsdfSide Side, double WavelengthNanometers, double IncidentThetaDegrees,
    double OutgoingThetaDegrees, double OutgoingPhiDegrees, double BrdfPerSteradian, double BtdfPerSteradian,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double IncidentPhiDegrees = 0);

public enum BsdfCoordinates { IncidentPlane, FixedMaterialAxes }
public enum BsdfStoredQuantity { Bsdf, ProjectedBsdf }

/// <summary>Immutable power-BSDF grid with an explicit incident-plane or fixed-material-axis convention.</summary>
public sealed class MeasuredBsdfData
{
    public const string Protocol = "MeasuredBsdfGrid/1";
    public const string FixedAxisProtocol = "MeasuredBsdfGrid/2";
    public const string SpecularProtocol = "MeasuredBsdfGrid/3";
    public const string EnhancedProtocol = "MeasuredBsdfGrid/4";
    public const int MaximumPoints = 131072;
    public const int MaximumDocumentPoints = 262144;
    public string Name { get; }
    public string SourceFileName { get; }
    public string SourceSha256 { get; }
    public string DataSha256 { get; }
    public IReadOnlyList<MeasuredBsdfPoint> Points { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public BsdfCoordinates Coordinates { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceFormat { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ImportNotes { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZemaxBsdfDefinition? ZemaxDefinition { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public BsdfStoredQuantity StoredQuantity { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SpeosPeakDefinition? SpeosPeak { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZemaxXyzDefinition? ZemaxXyz { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SpeosSpectralDefinition? SpeosSpectral { get; }
    [JsonIgnore] public int PayloadPointCount => Points.Count + (SpeosPeak?.Points.Count ?? 0) + (ZemaxXyz?.XPoints.Count ?? 0) + (ZemaxXyz?.ZPoints.Count ?? 0);
    [JsonIgnore] public string ComputationProtocol => SpeosPeak is not null || ZemaxXyz is not null || SpeosSpectral is not null ? EnhancedProtocol : ZemaxDefinition is not null || StoredQuantity != BsdfStoredQuantity.Bsdf ? SpecularProtocol : Coordinates != BsdfCoordinates.IncidentPlane || SourceFormat is not null ? FixedAxisProtocol : Protocol;
    internal IMeasuredBsdfDistribution Grid { get; }
    [JsonIgnore] public bool HasTransmission => Grid.HasTransmission;
    [JsonIgnore] public bool HasBothSides => Grid.HasBothSides;

    [JsonConstructor]
    public MeasuredBsdfData(string name, string sourceFileName, string sourceSha256,
        IReadOnlyList<MeasuredBsdfPoint> points, string? dataSha256 = null, BsdfCoordinates coordinates = BsdfCoordinates.IncidentPlane,
        string? sourceFormat = null, string? importNotes = null, ZemaxBsdfDefinition? zemaxDefinition = null, BsdfStoredQuantity storedQuantity = BsdfStoredQuantity.Bsdf,
        SpeosPeakDefinition? speosPeak = null, ZemaxXyzDefinition? zemaxXyz = null, SpeosSpectralDefinition? speosSpectral = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 2048 || string.IsNullOrWhiteSpace(sourceFileName)
            || sourceFileName.Length > 2048 || sourceSha256 is null || sourceSha256.Length != 64 || sourceSha256.Any(c => !Uri.IsHexDigit(c))
            || points is null || points.Count is < 1 or > MaximumPoints || points.Any(p => p is null) || !Enum.IsDefined(coordinates)
            || sourceFormat?.Length > 256 || importNotes?.Length > 8192 || !Enum.IsDefined(storedQuantity) || zemaxDefinition is not null && storedQuantity != BsdfStoredQuantity.Bsdf)
            throw new InvalidDataException("实测 BSDF 的来源或数据数量无效。");
        Name = name; SourceFileName = sourceFileName; SourceSha256 = sourceSha256.ToLowerInvariant();
        if (speosPeak is not null && (zemaxDefinition is not null || zemaxXyz is not null) || zemaxXyz is not null && zemaxDefinition is null || speosSpectral is not null && zemaxDefinition is not null) throw new InvalidDataException("BSDF 增强模型冲突。");
        Coordinates = coordinates; SourceFormat = sourceFormat; ImportNotes = importNotes; ZemaxDefinition = zemaxDefinition?.Freeze(); StoredQuantity = storedQuantity;
        SpeosPeak = speosPeak?.Freeze(); ZemaxXyz = zemaxXyz?.Freeze(); SpeosSpectral = speosSpectral?.Freeze();
        var frozen = points.OrderBy(p => p.Side).ThenBy(p => p.WavelengthNanometers).ThenBy(p => p.IncidentThetaDegrees)
            .ThenBy(p => p.IncidentPhiDegrees).ThenBy(p => p.OutgoingThetaDegrees).ThenBy(p => p.OutgoingPhiDegrees).ToArray();
        Points = Array.AsReadOnly(frozen);
        if (PayloadPointCount > MaximumPoints) throw new InvalidDataException("BSDF 冻结载荷超过节点预算。");
        Grid = SpeosSpectral is not null ? new SpeosSpectralDistribution(frozen, coordinates, storedQuantity, SpeosSpectral, SpeosPeak)
            : SpeosPeak is not null ? new SpeosPeakDistribution(frozen, coordinates, storedQuantity, SpeosPeak)
            : ZemaxXyz is not null ? new ZemaxXyzDistribution(frozen, coordinates, ZemaxDefinition!, ZemaxXyz)
            : ZemaxDefinition is null ? new MeasuredBsdfGrid(frozen, coordinates, sourceFormat is not null, storedQuantity == BsdfStoredQuantity.ProjectedBsdf) : new ZemaxBsdfDistribution(frozen, coordinates, ZemaxDefinition);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(ComputationProtocol);
            if (ComputationProtocol != Protocol) { writer.Write((int)Coordinates); writer.Write(SourceFormat ?? ""); writer.Write(ImportNotes ?? ""); }
            if (ComputationProtocol is SpecularProtocol or EnhancedProtocol) { writer.Write((int)StoredQuantity); writer.Write(ZemaxDefinition is { } z ? System.Text.Json.JsonSerializer.Serialize(z) : ""); }
            if (ComputationProtocol == EnhancedProtocol) { writer.Write(SpeosPeak is null ? "" : System.Text.Json.JsonSerializer.Serialize(SpeosPeak)); writer.Write(ZemaxXyz is null ? "" : System.Text.Json.JsonSerializer.Serialize(ZemaxXyz)); writer.Write(SpeosSpectral is null ? "" : System.Text.Json.JsonSerializer.Serialize(SpeosSpectral)); }
            writer.Write(frozen.Length);
            foreach (var p in frozen)
            {
                writer.Write((int)p.Side); writer.Write(p.WavelengthNanometers); writer.Write(p.IncidentThetaDegrees);
                writer.Write(p.OutgoingThetaDegrees); writer.Write(p.OutgoingPhiDegrees); writer.Write(p.BrdfPerSteradian); writer.Write(p.BtdfPerSteradian);
                if (ComputationProtocol != Protocol) writer.Write(p.IncidentPhiDegrees);
            }
        }
        DataSha256 = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)))).ToLowerInvariant();
        if (dataSha256 is not null && !DataSha256.Equals(dataSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("实测 BSDF 数据摘要不一致。");
    }
}

public static class MeasuredBsdfCsv
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public const string Header = "side,wavelength_nm,theta_i_deg,theta_o_deg,phi_o_deg,brdf_sr_inv,btdf_sr_inv";

    public static MeasuredBsdfData Read(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 or > MaximumBytes) throw new InvalidDataException("实测 BSDF 文件必须在 1 字节至 32 MiB 之间。");
        using var bytes = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("实测 BSDF 文件超过 32 MiB。");
            bytes.Write(buffer, 0, count);
        }
        var raw = bytes.ToArray(); var text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF');
        using var reader = new StringReader(text);
        if (reader.ReadLine()?.Trim() != "# OpticalSystemDesign.MeasuredBsdfGrid/1")
            throw new InvalidDataException("需要声明 OpticalSystemDesign.MeasuredBsdfGrid/1 的 BSDF CSV，不能猜测商业文件格式或角度单位。");
        var name = Path.GetFileNameWithoutExtension(path); string? line; bool header = false; int row = 1;
        var points = new List<MeasuredBsdfPoint>();
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested(); row++; line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) { if (line.StartsWith("# name=", StringComparison.Ordinal)) name = line[7..].Trim(); continue; }
            if (!header) { if (line != Header) throw new InvalidDataException("BSDF CSV 列名或顺序无效。"); header = true; continue; }
            if (points.Count >= MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("实测 BSDF 超过 131072 行。");
            var cells = line.Split(',', 8);
            if (cells.Length != 7 || !Enum.TryParse<BsdfSide>(cells[0].Trim(), true, out var side) || !Enum.IsDefined(side)
                || !cells[0].Trim().Equals(side.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"BSDF CSV 第 {row} 行需要 Front/Back 和六个有限数值。");
            double Number(int i) => double.TryParse(cells[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value : throw new InvalidDataException($"BSDF CSV 第 {row} 行第 {i + 1} 列不是有限数值。");
            points.Add(new(side, Number(1), Number(2), Number(3), Number(4), Number(5), Number(6)));
        }
        if (!header) throw new InvalidDataException("BSDF CSV 缺少列名。");
        cancellationToken.ThrowIfCancellationRequested();
        var result = new MeasuredBsdfData(name, Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(raw)), points);
        cancellationToken.ThrowIfCancellationRequested(); return result;
    }
}
