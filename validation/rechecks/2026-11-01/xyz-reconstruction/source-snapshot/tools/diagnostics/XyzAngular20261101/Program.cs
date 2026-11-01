using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

var root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var repo=Path.GetFullPath(args.Length>2?args[2]:".");
if(args[0]=="--fit"){PositiveFits.Run(root,repo);return;}
if(args[0]=="--mat")
{
    foreach(var name in new[]{"acryl_blue","mystique","cayman"})
    {
        var source=name=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm"):name=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm"):Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured/acryl_blue.astm");
        MatFitReview.Run(Path.Combine(root,name),Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-angular/full-precision",name+".mat"),source,repo);
    }
    return;
}
if(args[0]=="--chain"){await MeasuredAngularChain.Run(root,repo);return;}
var results=new List<object>();
foreach(var material in new[]{"garnet_red","krylon_blue","acryl_blue","mystique","cayman"})
{
    var path=material=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm"):material=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm"):Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured",material+".astm");
    try
    {
        var data=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550,AstmIncidentThetaDegrees:20));
        results.Add(new{Material=material,Success=true,data.Points.Count,data.ComputationProtocol,data.SourceSha256,data.DataSha256,Incidences=data.Points.Select(p=>p.IncidentThetaDegrees).Distinct(),data.ImportNotes});
    }
    catch(Exception ex){results.Add(new{Material=material,Success=false,Error=ex.ToString()});}
}
foreach(var material in new[]{"EDC50","EDS40","HILAM"})
{
    var dir=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-angular/measured-btdf",material);var path=Directory.GetFiles(dir,"*.bsdf").Single();
    try
    {
        var data=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,material=="HILAM"?632:550,true));
        results.Add(new{Material=material,Success=true,data.Points.Count,data.ComputationProtocol,data.SourceSha256,data.DataSha256});
    }
    catch(Exception ex){results.Add(new{Material=material,Success=false,Error=ex.ToString()});}
}
File.WriteAllText(Path.Combine(root,"import-audit.json"),JsonSerializer.Serialize(new{CoreSha256=PositiveFits.Sha(typeof(MeasuredBsdfData).Assembly.Location),Cases=results},PositiveFits.Json));
Console.WriteLine(JsonSerializer.Serialize(results));
