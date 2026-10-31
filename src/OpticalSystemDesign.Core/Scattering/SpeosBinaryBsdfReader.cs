using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Archive-3 layouts checked against installed v8/v11 and official v9 fixtures.</summary>
internal static class SpeosBinaryBsdfReader
{
    internal static bool IsBinary(byte[] raw)
    {
        if (!raw.AsSpan().StartsWith("OPTIS"u8)) return false;
        var lf = Array.IndexOf(raw, (byte)10); return lf is >= 0 and < 256 && lf + 1 < raw.Length && raw[lf + 1] == (byte)'1';
    }
    internal static MeasuredBsdfData Read(byte[] raw, string path, BsdfFileImportOptions options, CancellationToken token)
    {
        try
        {
            var lf = Array.IndexOf(raw, (byte)10); var header = Encoding.ASCII.GetString(raw, 0, lf).TrimEnd('\r');
            var anisotropic = header.Contains("Anisotropic BSDF", StringComparison.OrdinalIgnoreCase);
            var version = anisotropic && header.EndsWith("v7.0", StringComparison.OrdinalIgnoreCase) ? 7
                : anisotropic && header.EndsWith("v9.0", StringComparison.OrdinalIgnoreCase) ? 9
                : anisotropic && (header.EndsWith("v11.0", StringComparison.OrdinalIgnoreCase) || header.EndsWith("v11", StringComparison.OrdinalIgnoreCase)) ? 11
                : header.Equals("OPTIS - brdf surface file v8.0", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
            if (version == 0) throw new InvalidDataException("尚不支持此 Speos 二进制版本/布局；支持已核验的 .brdf v8 与 .anisotropicbsdf v7/v9/v11 布局。");
            using var stream = new MemoryStream(raw, false); stream.Position = lf + 2; using var reader = new BinaryReader(stream);
            var archive = reader.ReadInt32(); var archiveFlag = reader.ReadByte(); var archiveReserved = reader.ReadInt32();
            if (archive != 3 || archiveFlag > 1 || archiveFlag != 1 && version != 11 || archiveReserved != 0 || version == 11 && reader.ReadByte() != 0) throw new InvalidDataException("Speos 二进制存档标记未知；需要已核验的小端 archive-3 压缩布局。");
            long decoded = 0; int nodes = 0;
            byte[] Bytes(int length) { if (length < 0 || length > stream.Length - stream.Position) throw new InvalidDataException("Speos 压缩块截断。"); return reader.ReadBytes(length); }
            byte[] Packed(int expected)
            {
                token.ThrowIfCancellationRequested(); var length = version == 8 ? reader.ReadInt32() : reader.ReadInt64();
                if (length != expected || expected < 0 || (decoded += length) > 128 * 1024 * 1024) throw new InvalidDataException("Speos 压缩块长度不匹配或超过解压预算。");
                var result = new byte[expected];
                if (version == 8)
                {
                    var packed = Bytes(reader.ReadInt32()); using var input = new MemoryStream(packed, false); using var z = new ZLibStream(input, CompressionMode.Decompress); int at = 0;
                    while (at < result.Length) { token.ThrowIfCancellationRequested(); var n = z.Read(result, at, result.Length - at); if (n == 0) throw new InvalidDataException("Speos 压缩块截断。"); at += n; }
                    if (z.ReadByte() != -1) throw new InvalidDataException("Speos 解压长度与声明不符。");
                }
                else
                {
                    int at = 0;
                    while (at < expected)
                    {
                        token.ThrowIfCancellationRequested(); var chunk = reader.ReadInt32(); var packed = reader.ReadInt32();
                        if (chunk <= 0 || chunk > expected - at) throw new InvalidDataException("Speos LZ4 分块长度无效。");
                        DecodeLz4(Bytes(packed), result.AsSpan(at, chunk)); at += chunk;
                    }
                }
                return result;
            }
            string Text()
            {
                var count = reader.ReadInt32(); if (count is < 0 or > 2048) throw new InvalidDataException("Speos 测量说明超限。");
                return count == 0 && version != 8 ? "" : new UTF8Encoding(false, true).GetString(Packed(count));
            }
            double[] Values(int count)
            {
                var b = Packed(checked(count * 8)); var values = new double[count];
                for (var k = 0; k < count; k++) { values[k] = BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(k * 8, 8))); if (!double.IsFinite(values[k])) throw new InvalidDataException("Speos 二进制含非有限数值。"); }
                return values;
            }
            double Number() { var v = reader.ReadDouble(); return double.IsFinite(v) ? v : throw new InvalidDataException("Speos 二进制含非有限数值。"); }
            int Count(int max) { var n = reader.ReadInt32(); return n >= 1 && n <= max ? n : throw new InvalidDataException("Speos 二进制维数无效或超限。"); }
            var comment = Text(); var description = Text(); var vector = anisotropic ? new[] { Number(), Number(), Number() } : null;
            var reflection = reader.ReadByte(); var transmission = reader.ReadByte();
            double niReference = 1, ntReference = 1;
            if (version >= 9) { niReference = Number(); ntReference = Number(); if (niReference <= 0 || ntReference <= 0) throw new InvalidDataException("Speos 参考介质无效。"); }
            var proportional = reader.ReadByte(); var enhancement = reader.ReadByte();
            if (reflection > 1 || transmission > 1 || reflection + transmission == 0 || proportional > 1) throw new InvalidDataException("Speos 二进制 R/T/BSDF 标志无效。");
            if (enhancement > 1 || enhancement != 0 && version is not (7 or 11)) throw new InvalidDataException("此版本的活动镜面锥布局尚未核验，不能忽略该标志。");
            if (archiveFlag == 0 && enhancement != 0) throw new InvalidDataException("无辅助表的存档不能声明活动镜面锥。");
            var normalizationCache = version == 11 ? Number() : (double?)null;
            if (normalizationCache is <= 0) throw new InvalidDataException("Speos v11 归一化缓存无效。");
            if (version == 8 && (reader.ReadInt32() != 0 || reader.ReadInt32() != 0 || reader.ReadInt32() != 0)) throw new InvalidDataException("Speos 二进制包含未实现的附加选项。");
            var text = new StringBuilder(); string N(double v) => v.ToString("G17", CultureInfo.InvariantCulture); void Line(string s) => text.AppendLine(s);
            Line(anisotropic ? "OPTIS - Anisotropic BSDF surface file v8.0" : "OPTIS - BRDF surface file v8.0"); Line("0"); Line(comment.Replace('\r', ' ').Replace('\n', ' ')); Line(description.Length.ToString(CultureInfo.InvariantCulture)); Line(description);
            if (vector is not null) Line(string.Join(" ", vector.Select(N)));
            Line($"{reflection} {transmission}"); Line(proportional.ToString(CultureInfo.InvariantCulture));
            var active = Enumerable.Range(0, 2).Where(l => (l == 0 ? reflection : transmission) == 1).ToArray();
            var counts = new Dictionary<int, int>(); var fractions = new Dictionary<int, double[]>();
            var inputKeys = new Dictionary<int, List<(double Rotation, double Incidence, double Wave)>>();
            var maxima = new Dictionary<int, List<double>>();
            if (anisotropic)
            {
                foreach (var lobe in active)
                {
                    inputKeys[lobe] = new(); maxima[lobe] = new();
                    var nr = Count(65); var rotations = Values(nr); Line(nr.ToString(CultureInfo.InvariantCulture)); Line(string.Join(" ", rotations.Select(N))); int count = 0;
                    foreach (var a in rotations) { var ni = Count(64); var inc = Values(ni); count = checked(count + ni); Line(ni.ToString(CultureInfo.InvariantCulture)); Line(string.Join(" ", inc.Select(N))); foreach (var i in inc) inputKeys[lobe].Add((a, i, 0)); } counts.Add(lobe, count);
                }
                foreach (var lobe in active)
                {
                    Line(N(Number()) + " " + N(Number())); var label = Text(); Line(label.Replace('\r', ' ').Replace('\n', ' ')); var nw = Count(version == 11 ? 256 : 32); Line(nw.ToString(CultureInfo.InvariantCulture));
                    var waves = Enumerable.Range(0, nw).Select(_ => Number()).ToArray(); var spectrum = Enumerable.Range(0, nw).Select(_ => Number()).ToArray();
                    for (var w = 0; w < nw; w++) Line(N(waves[w]) + " " + N(spectrum[w]));
                }
            }
            else
            {
                var ni = Count(64); var nw = Count(32); var incidences = Values(ni); var waves = Values(nw);
                Line($"{ni} {nw}"); Line(string.Join(" ", incidences.Select(N))); Line(string.Join(" ", waves.Select(N)));
                foreach (var lobe in active) { fractions.Add(lobe, Values(checked(ni * nw))); counts.Add(lobe, checked(ni * nw)); }
            }
            foreach (var lobe in active) for (var k = 0; k < counts[lobe]; k++)
            {
                var nt = Count(129); var np = Count(257); nodes = checked(nodes + nt * np); if (nodes > MeasuredBsdfData.MaximumPoints) throw new InvalidDataException("Speos 原始节点超过预算；不自动降采样。");
                var theta = Values(nt); var phi = Values(np); var values = Values(checked(nt * np));
                if (anisotropic) maxima[lobe].Add(values.Max());
                if (!anisotropic) Line(N(fractions[lobe][k] * 100)); Line($"{nt} {np}"); Line(string.Join(" ", phi.Select(N)));
                for (var t = 0; t < nt; t++) Line(N(theta[t]) + " " + string.Join(" ", values.AsSpan(t * np, np).ToArray().Select(N)));
            }
            // Speos persists auxiliary sampling/peak tables. Recompute sampling from the retained angular values.
            // Retain cone parameters when enabled; sampling/normalization caches are recomputed locally.
            var auxiliaryStart = checked((int)stream.Position);
            var coneTables = new Dictionary<int, List<double[]>>();
            foreach (var lobe in active) for (var table = 0; table < (archiveFlag == 0 ? 0 : version == 9 ? 4 : 2); table++)
            {
                if (version == 8) _ = Values(counts[lobe]);
                else
                {
                    if (!coneTables.ContainsKey(lobe)) coneTables[lobe] = new();
                    coneTables[lobe].Add(Enumerable.Range(0, counts[lobe]).Select(_ => Number()).ToArray());
                }
            }
            double? whiteReference = null;
            bool white = false;
            if (anisotropic && archiveFlag == 1)
            {
                var flag = reader.ReadByte(); if (flag > 1) throw new InvalidDataException("Speos White Specular 标志无效。"); white = flag == 1;
                if (white && enhancement == 0 && options.SpeosPeak is null) throw new InvalidDataException("White Specular 需要已定义的增强锥。");
                whiteReference = Number(); if (whiteReference is < 200 or > 20000) throw new InvalidDataException("Speos White Specular 参考波长无效。");
            }
            else if (!anisotropic && Encoding.ASCII.GetString(Bytes(checked((int)(stream.Length - stream.Position)))).Trim() != "End of file") throw new InvalidDataException("Speos 二进制结束标记无效。");
            if (stream.Position != stream.Length) throw new InvalidDataException("Speos 二进制尾部含额外记录或未实现特性。");
            var auxiliaryDigest = Convert.ToHexString(SHA256.HashData(raw.AsSpan(auxiliaryStart))).ToLowerInvariant();
            var sha = Convert.ToHexString(SHA256.HashData(raw)); var parsed = SpeosBsdfReader.Read(text.ToString(), path, sha, options, token, allowProjected: true, compactSpectra: version == 11);
            var result = new MeasuredBsdfData(parsed.Name, parsed.SourceFileName, parsed.SourceSha256, parsed.Points, coordinates: parsed.Coordinates,
                sourceFormat: $"Speos{(anisotropic ? "AnisotropicBsdf" : "Brdf")}Binary{version}Archive3/1",
                importNotes: $"Decoded archive-3/{(version == 8 ? "zlib" : "LZ4")} little-endian; archive flag={archiveFlag}; interpolation indices={N(niReference)}/{N(ntReference)}; stored values={(proportional == 0 ? "BSDF*abs(cos(theta))" : "BSDF")}; auxiliary SHA256={auxiliaryDigest}; enhancement={enhancement}; white={white}; archive scalar={normalizationCache?.ToString("G17", CultureInfo.InvariantCulture) ?? "none"} (normalization recomputed from retained tables); white reference nm={whiteReference?.ToString("G17", CultureInfo.InvariantCulture) ?? "none"}; " + parsed.ImportNotes,
                storedQuantity: parsed.StoredQuantity, speosSpectral: parsed.SpeosSpectral);
            if (enhancement == 1 || options.SpeosPeak is not null)
            {
                var cones = new List<SpeosCone>();
                if (enhancement == 1) foreach (var lobe in active) for (var k = 0; k < counts[lobe]; k++)
                {
                    var cosine = coneTables[lobe][0][k]; var height = coneTables[lobe][1][k]; var maximum = maxima[lobe][k]; var key = inputKeys[lobe][k];
                    if (cosine is < 0 or > 1 || height < 0 || maximum == 0 && height > 0) throw new InvalidDataException("Speos 镜面锥范围无效。");
                    cones.Add(new(lobe == 1, key.Rotation, key.Incidence, key.Wave, Math.Acos(cosine) * 180 / Math.PI, maximum > 0 ? height / maximum : 0));
                }
                result = SpeosPeakBuilder.Apply(result, niReference, ntReference, options.SpeosPeak?.WhiteReferenceNanometers ?? (white ? whiteReference : null), enhancement == 1 ? cones : null, options.SpeosPeak);
            }
            return result;
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Speos 二进制文件截断。", ex); }
        catch (OverflowException ex) { throw new InvalidDataException("Speos 二进制维数溢出。", ex); }
    }
    private static void DecodeLz4(byte[] source, Span<byte> destination)
    {
        int input = 0, output = 0;
        int Extended(int length)
        { if (length != 15) return length; int next; do { if (input >= source.Length) throw new InvalidDataException("LZ4 长度截断。"); next = source[input++]; length = checked(length + next); } while (next == 255); return length; }
        while (input < source.Length)
        {
            var token = source[input++]; var literals = Extended(token >> 4);
            if (literals > source.Length - input || literals > destination.Length - output) throw new InvalidDataException("LZ4 字面量越界。");
            source.AsSpan(input, literals).CopyTo(destination[output..]); input += literals; output += literals;
            if (input == source.Length) break;
            if (source.Length - input < 2) throw new InvalidDataException("LZ4 匹配偏移截断。"); var offset = source[input] | source[input + 1] << 8; input += 2;
            var count = checked(Extended(token & 15) + 4);
            if (offset <= 0 || offset > output || count > destination.Length - output) throw new InvalidDataException("LZ4 匹配越界。");
            for (var k = 0; k < count; k++) { destination[output] = destination[output - offset]; output++; }
        }
        if (output != destination.Length) throw new InvalidDataException("LZ4 解压长度不匹配。");
    }
}
