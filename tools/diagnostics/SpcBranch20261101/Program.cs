using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;

var model = new DesignTopology { Name = "SPC existing authored singlet", Stop = new("stop",1,2), Image = new("image",55),
    Fields = [new("Axis",0,0),new("Edge",0,3)], Elements = [new() { Id="crown", FrontSurfaceId="front",BackSurfaceId="back",FrontZMillimeters=3,ThicknessMillimeters=3,
        Front=new(){ClearSemiDiameterMillimeters=5,Profile=new(){CurvaturePerMillimeter=1d/40}},
        Back=new(){ClearSemiDiameterMillimeters=5,Profile=new(){CurvaturePerMillimeter=-1d/30}} }] };
var source=TopologyCompiler.Compile(model); var transition=SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(source,"crown","null"));
if (args.Length > 0 && args[0] is "freeze" or "case" or "originals" or "reopen") { await Formal.Run(args, model); return; }
if (args.Length > 0 && args[0] == "search")
{
 var plan=new AutonomousSearchPlan{Seeds=[model],Options=new(){Seed=42,InsertionMaterials=["N-BK7","N-F2"],
  Expansion=new(){EnableInsertion=true,EnableMaterial=true},Local=new(){ComputeMode=DesignComputeMode.Cpu,GateCompensationVersion=4,PerformFinalAssessment=false,
   Sampling=new(){FieldsPerDirection=3,PupilRings=2,PupilArms=8,IncludeBoundaryClearance=true,IncludeBoundaryMaximum=true}}}};
 var budget=new ParaxialSeedSearchOptions{MaximumFunctionEvaluations=600,MaximumTracedRays=2000000,TimeLimit=TimeSpan.FromSeconds(120)};
 var result=SpcBranchSearch.Run(plan,budget,new(),progress:(stage,f)=>{if(f%25==0)Console.WriteLine($"{stage} {f}");});
 Console.WriteLine($"END {result.StopReason} fn={result.Work.FunctionEvaluations} grad={result.Work.GradientEvaluations} rays={result.Work.TracedRays}");
 foreach(var p in result.Parents)Console.WriteLine($"P {p.Outcome} grad={string.Join(',',p.ParentStationarity?.Gradient??[])} eigen={string.Join(',',p.Saddle?.Fine.Eigenvalues??[])} uncertainty={p.Saddle?.CurvatureUncertainty}");
 foreach(var b in result.Branches)Console.WriteLine($"B {b.Sign} {b.Outcome} {b.InitialObjective}->{b.OptimizedObjective} stages={b.Stages.Length}");
 var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,WriteIndented=true,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
 File.WriteAllText(args.Length>1?args[1]:"validation/rechecks/2026-11-01/spc-branches/search-probe.json",JsonSerializer.Serialize(new{plan,budget,result},json));
 return;
}
OpticalDesignParameter[] pars=[new(transition.OriginalBackIndex,OpticalDesignParameterKind.Curvature,.01),new(transition.NullFrontIndex,OpticalDesignParameterKind.Curvature,.01),new(transition.NullBackIndex,OpticalDesignParameterKind.Curvature,.01)];
var optic=transition.CreateOptic();var coords=OpticalDesignParameters.Read(optic,pars);var pupils=ApertureSampler.GenerateGaussianQuadrature(2,8).ToArray();
foreach(var d in new[]{0d,.0001,-.0001})
{
 var x=coords.ToArray();x[1]+=d/.01;x[2]-=d/.01;
 using var rays=SequentialTraceMeasurement.Begin();
 try {var value=DifferentiableOpticalMetrics.EvaluateNullElementContinuation(OpticalDesignParameters.Apply(optic.ToSnapshot(),pars,x),pars,[(0d,0d),(0d,1d)],pupils,transition.NullFrontIndex,transition.NullBackIndex);
 Console.WriteLine($"d={d:R} RMS={string.Join(',',value.Fields.Select(f=>f.RmsRadiusMillimeters.Value))} grad={string.Join(',',value.Fields[0].RmsRadiusMillimeters.Gradient())} rays={rays.RayCount}");}
 catch(Exception e){Console.WriteLine(e.ToString());}
}
