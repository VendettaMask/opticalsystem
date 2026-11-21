using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Analysis;
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "validation/rechecks/2026-11-21/afocal-color-focus-shift/independent");
Directory.CreateDirectory(output);
var optic = OpticalFormatCatalog.Import(File.ReadAllText("src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX"), ".zmx");
Console.WriteLine($"Afocal {optic.ImageSpaceAfocal}, waves " + string.Join(", ",optic.Wavelengths.Select(w=>$"{w.Micrometers} primary={w.IsPrimary}")));
var samples = Enumerable.Range(0,101).Select(i=>.486+i*.0017).Select(w=>
{
 var r=optic.Paraxial.MarginalRay(w); var y=r.Heights[^1][0]; var u=r.Slopes[^1][0];
 var v=-1000*u/y;
 return new { w,y,u,focus=-y/u, v };
}).ToArray();
foreach(var w in new[]{.486,.52,.53,.55,.587,.62,.64,.656})
{
 var r=optic.Paraxial.MarginalRay(w);var y=r.Heights[^1][0];var u=r.Slopes[^1][0];
 var real=FieldCurvatureMetrics.AfocalVergences(optic,0,0,w,1e-4);
 Console.WriteLine($"{w:F6} y={y:G12} u={u:G12} focus={-y/u:G12} v={-1000*u/y:G12} realST={real}");
}
Console.WriteLine($"Power min {samples.Min(x=>x.v):G12} max {samples.Max(x=>x.v):G12} range {samples.Max(x=>x.v)-samples.Min(x=>x.v):G12}");
File.WriteAllText(Path.Combine(output, "results.json"),System.Text.Json.JsonSerializer.Serialize(samples,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));

foreach(var zone in new[]{0.0,.5}) { var d=new ColorFocusShiftAnalysis(OpticalSystemDesign.Core.Optic.CreateCookeTriplet(),pupilZone:zone).GenerateData(); Console.WriteLine("COOKE "+zone+" "+System.Text.Json.JsonSerializer.Serialize(new{points=d.PlotSeries[0].Points.Where((p,i)=>i==0||i==50||i==100),values=d.Values})); }

foreach(var w in new[]{.486,.53,.587,.656}) { var vals=new[]{-.5,.5}.Select(p=>optic.TraceGenericFinalSample(0,0,0,p,w,aimAtStop:optic.RayAimingEnabled)!).Select(s=>-1000*s.Direction.Y/(s.Position.Y*s.Direction.Z)); Console.WriteLine($"ZONE .5 {w} {vals.Average():G17} delta={vals.Average()-.0881561849085:G17}"); }
