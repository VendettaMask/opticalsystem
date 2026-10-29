using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PupilMapDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var rows = new List<object>();
        foreach (var config in new[] { 1, 2 })
        {
            using var stream = zip.GetEntry($"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/003-binocular_system-c{config}.json")!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            foreach (var hy in new[] { 0d, .5 })
            {
                var pupils = new[] { (0d,0d),(.001,0d),(0d,.001),(-.001,0d),(0d,-.001) };
                var pencil = WavefrontEngine.GenerateChiefRaySamples(optic, (0,hy), optic.Wavelengths[1], pupils, aimAtStop: optic.RayAimingEnabled, referenceWavelength: optic.Wavelengths[1]);
                Vector3D P(int i) => new(pencil.Samples[i].PupilX,pencil.Samples[i].PupilY,pencil.Samples[i].PupilZ);
                var x = (P(1)-P(0)).Length; var y=(P(2)-P(0)).Length;
                var sx=(P(1)-P(3)).Length; var sy=(P(2)-P(4)).Length;
                object Stretch(double a,double b) => new { x=Math.Max(a,b)/a,y=Math.Max(a,b)/b,aspect=a/b };
                var chief=optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(pencil.Samples[0].ImageDirection!.Value);
                rows.Add(new { config, hy, positive=Stretch(x,y), symmetric=Stretch(sx,sy), chief,
                    directionProjectionOnly = new { x=1/Math.Sqrt(1-chief.X*chief.X),y=1/Math.Sqrt(1-chief.Y*chief.Y) } });
            }
        }
        JsonFiles.Write(output,rows);
        Console.WriteLine(JsonSerializer.Serialize(rows,JsonFiles.Options));
        return 0;
    }
}
