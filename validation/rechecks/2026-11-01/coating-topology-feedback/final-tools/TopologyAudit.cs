// Offline topology/feedback experiment. All physics, refinement and formal gates use a frozen product engine.
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class TopologyAudit
{
    private sealed record State(string Operation, FilmLayer[] Layers, CoatingConstraintAssessment Assessment, string Parent, NarrowBandPrototype? Origin=null);
    public static async Task Main(string[] args)
    {
        var control=args[0]=="control";var prototype=args[0]=="prototype";var inputs=args[1];var output=args[2];Directory.CreateDirectory(output);
        var summaries=new List<object>();var service=new DesignService();
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var objectiveGrid=typeof(DesignService).GetMethod("ObjectiveGrid",flags)!;
        var assess=typeof(DesignService).GetMethod("AssessGeneral",flags)!;
        var refine=typeof(DesignService).GetMethod("RefineGeneralThickness",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var file in Directory.GetFiles(inputs,"*.coating.json").Order(StringComparer.Ordinal))
        {
            using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(25));var token=limit.Token;
            var source=await ExperimentStore.OpenAsync(file);var d=source with{Result=null,Candidates=null,Tolerance=null};
            var watch=System.Diagnostics.Stopwatch.StartNew();var initial=service.Evaluate(d,token:token);var best=initial;
            var mats=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));var t=d.Target;
            var baseGrid=(double[])objectiveGrid.Invoke(null,[d])!;
            var grid=baseGrid.Concat(ThinFilmSpectrum.Grid(t.MinimumNm,t.MaximumNm,512)).Concat(initial.Result!.Spectrum
                .Where((s,i)=>i==0||i==initial.Result.Spectrum.Length-1||i>0&&i<initial.Result.Spectrum.Length-1&&s.Power.Transmittance>=initial.Result.Spectrum[i-1].Power.Transmittance&&s.Power.Transmittance>=initial.Result.Spectrum[i+1].Power.Transmittance)
                .Select(s=>s.WavelengthNanometers)).Distinct().Order().ToArray();
            long coarsePoints=0;var proposals=0;var dense=1;var runs=new List<object>();var edits=new List<object>();var generationRows=new List<object>();
            var w=Math.Min(d.Settings.MaximumIterations,Math.Max(1,(int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations))));
            var starts=Math.Min(4,(d.Settings.Search??new()).Starts);var structural=(d.Settings.Search??new()).VaryStructure&&d.Layers.All(l=>l.Variable);
            State Describe(string operation,FilmLayer[] layers,string parent,NarrowBandPrototype? origin=null)
            {
                token.ThrowIfCancellationRequested();var solver=new CoherentThinFilmSolver(mats[d.IncidentId],mats[d.SubstrateId],layers.Select(l=>new CoherentFilm(mats[l.MaterialId],l.ThicknessNm)));
                var samples=ThinFilmSpectrum.Sample(solver,grid,t.AngleDegrees,t.Polarization);coarsePoints+=grid.Length;
                return new(operation,layers,(CoatingConstraintAssessment)assess.Invoke(null,[d,samples])!,parent,origin);
            }
            double[] Od(Experiment e)=>(t.StopBands??[]).Select(b=>e.Result!.Spectrum.Where(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).Min(s=>s.Power.OpticalDensity)).ToArray();
            CoatingConstraintAssessment Actual(Experiment e)=>(CoatingConstraintAssessment)assess.Invoke(null,[e,e.Result!.Spectrum])!;
            bool Joint(Experiment e,Experiment anchor)
            {
                if(anchor.Result!.SamplingConverged&&!e.Result!.SamplingConverged||anchor.Result.Passed&&!e.Result!.Passed)return false;
                if(t.Kind==DesignKind.HighReflector)return e.Result!.Metrics.MinimumReflectance>=anchor.Result.Metrics.MinimumReflectance;
                return e.Result!.Metrics.FwhmNanometers is {} width&&anchor.Result.Metrics.FwhmNanometers is {} oldWidth
                    &&e.Result.Metrics.PeakTransmittance>=anchor.Result.Metrics.PeakTransmittance&&Math.Abs(width-t.FwhmNm)<=Math.Abs(oldWidth-t.FwhmNm)
                    &&Od(e).Zip(Od(anchor)).All(x=>x.First>=x.Second)&&Actual(e).Violations.Zip(Actual(anchor).Violations).All(x=>x.First<=x.Second);
            }
            bool Better(Experiment a,Experiment b)
            {
                if(!Joint(a,b))return false;
                if(t.Kind==DesignKind.HighReflector)return a.Result!.Metrics.MinimumReflectance>b.Result!.Metrics.MinimumReflectance;
                return a.Result!.Metrics!=b.Result!.Metrics||!Od(a).SequenceEqual(Od(b));
            }
            FilmLayer[]? Canonical(IEnumerable<FilmLayer> input)
            {
                var layers=new List<FilmLayer>();foreach(var layer in input)
                {
                    if(layers.Count>0&&layers[^1].MaterialId==layer.MaterialId&&layers[^1].Variable&&layer.Variable)
                        layers[^1]=layers[^1] with{ThicknessNm=layers[^1].ThicknessNm+layer.ThicknessNm};
                    else layers.Add(layer);
                }
                return layers.Count>0&&layers.Count<=t.MaximumLayers&&layers.All(l=>l.ThicknessNm>=d.Settings.MinimumThicknessNm&&l.ThicknessNm<=d.Settings.MaximumThicknessNm)?layers.ToArray():null;
            }
            IEnumerable<(string Op,FilmLayer[] Layers)> Mutations(FilmLayer[] layers)
            {
                for(var i=0;i<layers.Length;i++)
                {
                    var deleted=Canonical(layers.Where((_,j)=>j!=i));if(deleted is not null)yield return("remove:"+i,deleted);
                    if(layers.Length+2>t.MaximumLayers||layers[i].ThicknessNm<2*d.Settings.MinimumThicknessNm)continue;
                    foreach(var id in new[]{d.LowId,d.HighId}.Distinct().Where(id=>id!=layers[i].MaterialId))
                    {
                        var unit=CoherentThinFilmSolver.QuarterWaveThickness(mats[id],mats[d.IncidentId],t.CenterNm,t.AngleDegrees);
                        foreach(var thickness in new[]{d.Settings.MinimumThicknessNm,Math.Max(d.Settings.MinimumThicknessNm,unit/w)}.Distinct())
                        {
                            var candidate=Canonical(layers.Take(i).Append(layers[i] with{ThicknessNm=layers[i].ThicknessNm/2}).Append(new(id,thickness)).Append(layers[i] with{ThicknessNm=layers[i].ThicknessNm/2}).Concat(layers.Skip(i+1)));
                            if(candidate is not null)yield return("split:"+i,candidate);
                        }
                    }
                }
                foreach(var id in new[]{d.LowId,d.HighId}.Distinct())foreach(var position in new[]{0,layers.Length})
                {
                    var candidate=Canonical(layers.Take(position).Append(new(id,d.Settings.MinimumThicknessNm)).Concat(layers.Skip(position)));
                    if(candidate is not null)yield return("cap:"+position,candidate);
                }
            }
            static string Identity(FilmLayer[] layers)=>string.Join(';',layers.Select(l=>$"{l.MaterialId}:{l.ThicknessNm:R}:{l.Variable}"));
            static IOrderedEnumerable<State> Rank(IEnumerable<State> states)=>states.OrderBy(x=>x.Assessment.MaximumViolation).ThenBy(x=>x.Assessment.Merit).ThenByDescending(x=>x.Assessment.PeakTransmittance);
            static (bool Cavity,int Segment,double Depth)[] Roles(NarrowBandPrototype p)
            {
                var roles=new List<(bool Cavity,int Segment,double Depth)>();
                for(var s=0;s<p.MirrorLayers.Length;s++)
                {for(var j=0;j<p.MirrorLayers[s];j++)roles.Add((false,s,(j+.5)/p.MirrorLayers[s]));if(s<p.CavityOrders.Length)roles.Add((true,s,0));}
                roles.Reverse();return roles.ToArray();
            }
            IEnumerable<State> PrototypeMutations(State parent)
            {
                if(parent.Origin is not {} p)yield break;
                FilmLayer[] oldBase;try{oldBase=service.BuildNarrowBandPrototype(d,p);}catch(ArgumentException){yield break;}
                var cap=parent.Layers.Length-oldBase.Length;
                if(cap<0||cap>2||oldBase.Where((l,i)=>l.MaterialId!=parent.Layers[cap+i].MaterialId).Any())yield break;
                var oldRoles=Roles(p);
                var neighbors=(IEnumerable<NarrowBandPrototype>)typeof(DesignService).GetMethod("GeneralNeighbors",flags)!.Invoke(null,[p])!;
                foreach(var neighbor in neighbors)
                {
                    FilmLayer[] basis;try{basis=service.BuildNarrowBandPrototype(d,neighbor);}catch(ArgumentException){continue;}
                    if(basis.Length+cap>t.MaximumLayers)continue;var roles=Roles(neighbor);var layers=parent.Layers.Take(cap).ToList();
                    for(var i=0;i<basis.Length;i++)
                    {
                        var role=roles[i];var sameRole=Enumerable.Range(0,oldRoles.Length).Where(j=>oldRoles[j].Cavity==role.Cavity&&oldRoles[j].Segment==role.Segment).ToArray();
                        var sameMaterial=sameRole.Where(j=>oldBase[j].MaterialId==basis[i].MaterialId).ToArray();
                        var j=(sameMaterial.Length>0?sameMaterial:sameRole).MinBy(j=>Math.Abs(oldRoles[j].Depth-role.Depth));
                        layers.Add(basis[i] with{ThicknessNm=basis[i].ThicknessNm*parent.Layers[cap+j].ThicknessNm/oldBase[j].ThicknessNm});
                    }
                    var array=layers.ToArray();if(array.Any(l=>l.ThicknessNm<d.Settings.MinimumThicknessNm||l.ThicknessNm>d.Settings.MaximumThicknessNm))continue;
                    yield return Describe("prototype-neighbor",array,parent.Operation,neighbor);
                }
            }
            var seen=new HashSet<string>{Identity(d.Layers)};var beam=new List<State>{Describe("incumbent",d.Layers,"input")};
            if(!control&&structural)
            {
                // Feed actual matched/refined prescriptions back, rather than rebuilding quarter-wave prototypes.
                var seeds=(source.Candidates??[]).Where(c=>c.Layers.All(l=>l.Variable)&&c.Layers.Length<=t.MaximumLayers&&(!prototype||c.OriginPrototype is not null)).Select(c=>Describe("matched-seed",c.Layers,c.Name,c.OriginPrototype)).ToArray();
                beam.AddRange(Rank(seeds).Take(prototype?starts:1));
            }
            var controlState=initial.Layers;
            for(var round=0;round<2;round++)
            {
                Console.WriteLine($"{(control?"control":"topology")} {d.Name} round {round+1}");
                var candidates=new List<State>();
                if(!control&&structural)
                {
                    foreach(var parent in beam)
                    {
                        if(prototype)
                        {
                            foreach(var s in PrototypeMutations(parent))if(seen.Add(Identity(s.Layers))){candidates.Add(s);proposals++;}
                            continue;
                        }
                        var local=new List<State>();
                        foreach(var (op,layers) in Mutations(parent.Layers))if(seen.Add(Identity(layers)))
                        {var s=Describe(op,layers,parent.Operation);local.Add(s);candidates.Add(s);proposals++;}
                        // At a layer cap, prune first and then insert; the fitted prescription survives the exchange.
                        if(parent.Layers.Length+2>t.MaximumLayers)
                            foreach(var removed in Rank(local.Where(s=>s.Operation.StartsWith("remove:"))).Take(starts))
                                foreach(var (op,layers) in Mutations(removed.Layers).Where(x=>x.Op.StartsWith("split:")))if(seen.Add(Identity(layers)))
                                {candidates.Add(Describe("exchange/"+op,layers,removed.Operation));proposals++;}
                    }
                    candidates=Rank(candidates).ToList();
                    edits.AddRange(candidates.Select(s=>(object)new{Round=round+1,s.Operation,s.Parent,s.Layers,s.Assessment}));
                }
                var shortlist=control||!structural?Enumerable.Range(0,starts).Select(_=>Describe("old-continuation",best.Layers,"input")).ToArray()
                    :Rank(candidates).DistinctBy(s=>prototype&&s.Origin is {} p?string.Join(',',p.MirrorLayers)+"/"+string.Join(',',p.CavityOrders)+"/"+p.SubstrateStartsHigh:s.Operation.Split(':')[0]+"/"+string.Join(',',s.Layers.Select(l=>l.MaterialId))).Take(starts).ToArray();
                var next=new List<State>();
                foreach(var seed in shortlist)
                {
                    var active=control?controlState:seed.Layers;var value=service.Evaluate(d with{Layers=active},token:token);dense++;
                    var refined=((FilmLayer[] Layers,double Merit,RunRecord Run))refine.Invoke(service,[d with{Layers=active},mats,baseGrid,w,null,token,value])!;
                    var candidate=service.Evaluate(d with{Layers=refined.Layers},refined.Run,token:token);dense++;
                    var retained=Better(candidate,best);if(retained)best=candidate;
                    if(control)controlState=candidate.Layers;
                    runs.Add(new{Stage="short",Round=round+1,seed.Operation,seed.Parent,InitialLayers=active,FinalLayers=candidate.Layers,candidate.Result!.Metrics,Od=Od(candidate),candidate.Result.Passed,JointInitial=Joint(candidate,initial),Retained=retained,Allowance=w,refined.Run});
                    next.Add(prototype?new(seed.Operation,candidate.Layers,Actual(candidate),seed.Parent,seed.Origin):Describe(seed.Operation,candidate.Layers,seed.Parent));
                }
                if(!control&&next.Count>0)beam=Rank(next).Take(1).Concat(Rank(beam.Concat(next)))
                    .DistinctBy(s=>Identity(s.Layers)).Take(2).ToList();
                generationRows.Add(new{Round=round+1,Proposals=proposals,Best=best.Result!.Metrics,BestOd=Od(best),Beam=beam.ToArray()});
                var fullState=Rank(next.Count>0?next:beam).First();var fullSeed=control?controlState:fullState.Layers;
                var full=service.Optimize(d with{Layers=fullSeed},token:token);dense+=full.Result!.Run.GeneralWork?.DenseValidations??0;
                var accepted=Better(full,best);if(accepted)best=full;
                if(control)controlState=full.Layers;
                runs.Add(new{Stage="full",Round=round+1,Operation="feedback-complete",Parent="round",InitialLayers=fullSeed,FinalLayers=full.Layers,full.Result.Metrics,Od=Od(full),full.Result.Passed,JointInitial=Joint(full,initial),Retained=accepted,Allowance=d.Settings.MaximumIterations,Run=full.Result.Run});
                if(!control)beam=new[]{prototype?new State("full-feedback",full.Layers,Actual(full),"round",fullState.Origin):Describe("full-feedback",full.Layers,"round")}.Concat(Rank(beam))
                    .DistinctBy(s=>Identity(s.Layers)).Take(2).ToList();
            }
            await ExperimentStore.SaveAsync(best,Path.Combine(output,Path.GetFileName(file)));
            summaries.Add(new{d.Name,Arm=control?"old-continuation":prototype?"matched-prototype-feedback":"matched-topology-feedback",InputFingerprint=d.Fingerprint(),Seconds=watch.Elapsed.TotalSeconds,Initial=initial.Result!.Metrics,InitialOd=Od(initial),Final=best.Result!.Metrics,FinalOd=Od(best),best.Result.Passed,Joint=Joint(best,initial),StructuralAllowed=structural,CoarseSpectrumPoints=coarsePoints,Proposals=proposals,DenseCalls=dense,ShortAllowance=w,Starts=starts,Runs=runs,Rounds=generationRows,Edits=edits});
            await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summaries,Experiment.JsonOptions));
            Console.WriteLine($"{d.Name}: R={best.Result.Metrics.MinimumReflectance:G10}; T={best.Result.Metrics.PeakTransmittance:G10}; W={best.Result.Metrics.FwhmNanometers:G10}; pass={best.Result.Passed}; proposals={proposals}; {watch.Elapsed.TotalSeconds:F1}s");
        }
    }
}
