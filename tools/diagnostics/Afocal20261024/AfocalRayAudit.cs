using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// A scoped surface-correspondence audit. Ray propagation stays entirely in Core.
internal static class AfocalRayAudit
{
    private static double D(JsonElement e, string n) => e.GetProperty(n).GetDouble();
    private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
    private static Vector3D V(JsonElement e) => new(D(e,"x"),D(e,"y"),D(e,"z"));
    internal static void Run(string root, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        var models = JsonFiles.Read<JsonElement>(Path.Combine(root,"models.json")).EnumerateArray().ToArray();
        var rows = new List<object>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(root,"rays")).Order())
        {
            var id = Path.GetFileName(dir);
            var modelId = id[..id.LastIndexOf("-w",StringComparison.Ordinal)];
            var model = models.Single(m => Path.GetFileNameWithoutExtension(m.GetProperty("snapshot").GetString()) == modelId);
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(model.GetProperty("snapshot").GetString()!));
            var native = JsonFiles.Read<JsonElement>(Path.Combine(dir,"native/ray-audit.json"));
            var nativeModel = JsonFiles.Read<JsonElement>(Path.Combine(dir,"native/model.json"));
            var source = Path.Combine(root,"sources",model.GetProperty("id").GetString()!);
            var retained = File.ReadLines(source).Select(s=>s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
                .Where(t=>t.Length>1 && t[0] is "REAX" or "REAY" or "REAZ" or "REAR" or "REAA" or "REAB" or "REAC")
                .Select(t=>int.Parse(t[1],CultureInfo.InvariantCulture)).ToHashSet();
            var nativeRows = nativeModel.GetProperty("surfaces").EnumerateArray().ToArray();
            bool CoordinateBreak(int si) => nativeRows[si].GetProperty("Type").GetString() == "CoordinateBreak";
            var map = Enumerable.Range(0,nativeRows.Length).Where(si=>!CoordinateBreak(si)||retained.Contains(si))
                .Select((si,ci)=>(si,ci)).ToDictionary(p=>p.si,p=>p.ci);
            if (map.Count!=optic.SurfaceGroup.Items.Count) throw new InvalidDataException("Unverified native/Core row map.");
            var core = JsonFiles.Read<JsonElement>(Path.Combine(dir,"core.json")).EnumerateArray()
                .SelectMany(f=>f.GetProperty("rays").EnumerateArray()).ToArray();
            var inputs = JsonFiles.Read<JsonElement>(Path.Combine(dir,"inputs.json"));
            if (!Equivalent(inputs,native.GetProperty("inputs")))
                throw new InvalidDataException("Different physical ray inputs.");
            var histories = core.Select(r=>r.GetProperty("samples").EnumerateArray().ToDictionary(s=>I(s,"surfaceNumber"))).ToArray();
            var coordMax=0d; var dirMax=0d; var coordinateFailures=0; var directionFailures=0;
            var acceptanceDifferences=0; var firstClipDifferences=0; var count=0;
            var details=new List<object>();
            var final = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
            for(var i=0;i<core.Length;i++)
            {
                var last=histories[i].Values.Last(); var n=final[i];
                var accepted=I(n,"error")==0 && I(n,"vignette")==0;
                var own=I(last,"surfaceNumber")==optic.SurfaceGroup.Items.Count-1 && !last.GetProperty("vignetted").GetBoolean() && D(last,"intensity")>0;
                if(accepted!=own) acceptanceDifferences++;
                if(I(n,"vignette")>0 && last.GetProperty("vignetted").GetBoolean() && map.TryGetValue(I(n,"vignette"),out var clip) && clip!=I(last,"surfaceNumber")) firstClipDifferences++;
            }
            foreach(var surface in native.GetProperty("surfaces").EnumerateArray())
            {
                var si=I(surface,"surface"); if(CoordinateBreak(si)) continue;
                var ci=map[si]; var ns= nativeRows[si].GetProperty("Type").GetString()=="NonSequential";
                var mp=0d; var md=0d; var comparisons=0;
                for(var i=0;i<core.Length;i++)
                {
                    var n=surface.GetProperty("rays")[i];
                    if(I(n,"error")!=0||I(n,"vignette")!=0||!histories[i].TryGetValue(ci,out var own)||own.GetProperty("vignetted").GetBoolean()||!own.GetProperty("hit").GetBoolean()) continue;
                    var p=V(own.GetProperty("position")); var d=V(own.GetProperty("direction"));
                    if(ns)
                    {
                        // Zemax's target NSC row is its entrance port; Core's
                        // compound sample is its exit. Reconstruct the entrance
                        // on the incoming Core line, from the actual prior frame.
                        var previous=optic.SurfaceGroup.Items[ci-1];
                        var prior=histories[i][ci-1];
                        p=V(prior.GetProperty("position")); d=V(prior.GetProperty("direction"));
                        var t=(previous.Thickness-p.Z)/d.Z;
                        p=p+d*t-new Vector3D(0,0,previous.Thickness);
                    }
                    var expectedP=new[]{D(n,"x"),D(n,"y"),D(n,"z")};
                    var expectedD=new[]{D(n,"l"),D(n,"m"),D(n,"n")};
                    var actualP=new[]{p.X,p.Y,p.Z}; var actualD=new[]{d.X,d.Y,d.Z};
                    for(var k=0;k<3;k++)
                    {
                        var dp=Math.Abs(actualP[k]-expectedP[k]); var dd=Math.Abs(actualD[k]-expectedD[k]);
                        mp=Math.Max(mp,dp);md=Math.Max(md,dd);
                        if(dp>1e-6+1e-6*Math.Abs(expectedP[k])) coordinateFailures++;
                        if(dd>1e-8+1e-6*Math.Abs(expectedD[k])) directionFailures++;
                    }
                    comparisons++;count++;
                }
                coordMax=Math.Max(coordMax,mp);dirMax=Math.Max(dirMax,md);
                details.Add(new{nativeSurface=si,coreSurface=ci,semantics=ns?"NSC entrance on incoming line; exit independently checked at next physical row":"same physical surface",comparisons,coordinateMaximum=mp,directionMaximum=md});
            }
            var conclusion=coordinateFailures+directionFailures+acceptanceDifferences+firstClipDifferences==0&&count>0?"Pass":"Difference";
            rows.Add(new{id,conclusion,inputs=core.Length,physicalSurfaceComparisons=count,coordinateFailures,directionFailures,acceptanceDifferences,firstClipDifferences,coordMax,dirMax,
                map=map.Select(p=>new{native=p.Key,core=p.Value,transparentCoordinateBreak=CoordinateBreak(p.Key)}),details});
            Console.WriteLine($"{id}: {conclusion}; pos={coordMax:G9}; dir={dirMax:G9}");
            JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
        }
    }
    private static bool Equivalent(JsonElement a,JsonElement b) => a.ValueKind==b.ValueKind && (a.ValueKind switch
    {
        JsonValueKind.Number => a.GetDouble()==b.GetDouble(),
        JsonValueKind.Object => a.EnumerateObject().Count()==b.EnumerateObject().Count() && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&Equivalent(p.Value,v)),
        JsonValueKind.Array => a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Equivalent(p.First,p.Second)),
        _ => a.ToString()==b.ToString()
    });
}
