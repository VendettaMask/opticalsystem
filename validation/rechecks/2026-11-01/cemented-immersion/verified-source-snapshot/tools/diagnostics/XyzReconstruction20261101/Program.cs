using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

var root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var repo=Path.GetFullPath(args.Length>2?args[2]:".");
if(args[0]=="--chain"){await MeasuredLogChain.Run(root,repo);return;}
if(args[0]=="--calibrated-chain"){await AbsoluteRbfChain.Run(root,repo,Path.GetFullPath(args[3]),Path.GetFullPath(args[4]));return;}
if(args[0]=="--fit"){RbfFitting.Run(root,repo);return;}
if(args[0]=="--import")
{
    var cases=new List<object>();
    foreach(var name in new[]{"garnet_red","krylon_blue","acryl_blue","mystique","cayman"})
    {
        var path=Source(name,repo);var data=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550,AstmLogInterpolationScalePerSteradian:1e-6));
        var model=new MeasuredBsdf(data);double worst=0;var powers=new List<object>();
        foreach(var incidence in data.Points.Select(p=>p.IncidentThetaDegrees).Distinct())
        {var theta=incidence*Math.PI/180;var c=new BsdfContext(new(Math.Sin(theta),0,Math.Cos(theta)),new(0,0,1),550);powers.Add(new{IncidenceDegrees=incidence,Power=model.Integrate(c).Total});}
        foreach(var row in PositiveFits.Rows(path))
        {
            var c=new BsdfContext(new(Math.Sin(row[0]),0,Math.Cos(row[0])),new(0,0,1),550);
            var d=new OpticalSystemDesign.Core.Backend.Vector3D(Math.Sin(row[2])*Math.Cos(row[3]),Math.Sin(row[2])*Math.Sin(row[3]),Math.Cos(row[2]));
            worst=Math.Max(worst,Math.Abs(model.Evaluate(c,d)-row[19])/Math.Max(1,row[19]));
        }
        if(worst>2e-11)throw new InvalidDataException("Original measured knot mismatch");
        cases.Add(new{Material=name,data.ComputationProtocol,data.Points.Count,data.SourceSha256,data.DataSha256,MaximumScaledOriginalKnotError=worst,Powers=powers});Console.WriteLine(name+": all eight incidences imported");
    }
    File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{CoreSha256=PositiveFits.Sha(typeof(MeasuredBsdf).Assembly.Location),Cases=cases},PositiveFits.Json));return;
}
throw new ArgumentException("Unknown diagnostic command");

static string Source(string name,string repo)=>name=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm"):name=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm"):Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured",name+".astm");
