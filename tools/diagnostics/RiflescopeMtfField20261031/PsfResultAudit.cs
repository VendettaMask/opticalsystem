using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class PsfResultAudit
{
    internal static void Run(string repository, string root)
    {
        var hash = JsonFiles.Read<JsonElement>(Path.Combine(root,"core-cohort.json")).GetProperty("coreSha256").GetString()!;
        var gate = ComparisonConfiguration.Load(Path.Combine(repository,"tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"))
            .Analyses["Huygens PSF"].Quantities["Irradiance"];
        var rows = new List<object>();
        foreach (var centered in new[] {false,true})
        {
            var id=centered ? "centroid" : "chief";
            var folder=Path.Combine(root,"core-"+hash[..12],"psf32");
            if(JsonFiles.Read<JsonElement>(Path.Combine(folder,"identity.json")).GetProperty("coreSha256").GetString()!=hash)throw new InvalidDataException("PSF cohort differs");
            var actual=JsonFiles.Read<AnalysisData>(Path.Combine(folder,id+".json")).PlotSeries.Single();
            var native=JsonFiles.Read<JsonElement>(Path.Combine(root,"native-huygens-split","psf-field2-"+id,"data.json")).GetProperty("dataGrids")[0];
            var values=native.GetProperty("values").EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(v=>v.GetDouble())).ToArray();
            var b=new Series1DResult("grid","Native",Enumerable.Range(0,values.Length).Select(i=>(double)i).ToArray(),values,new("Sample","Dimensionless"),new("Irradiance","Dimensionless"));
            var a=b with{Y=actual.Points.Select(p=>p.Value!.Value).ToArray()};
            var aligned=ComparisonMetrics.Align(a,b,[]);
            var metric=ComparisonMetrics.Calculate("AbsoluteRelativeIntensity", "Dimensionless", aligned,gate);
            rows.Add(new{id,metric,pointwiseViolations=aligned.Workbench.Zip(aligned.Zemax,(x,y)=>Math.Abs(x-y)>gate.Absolute+gate.Relative*Math.Abs(y)).Count(v=>v),fittedScaleUsed=false});
        }
        JsonFiles.Write(Path.Combine(root,"psf-result-audit.json"),new{coreSha256=hash,rows});
    }
}
