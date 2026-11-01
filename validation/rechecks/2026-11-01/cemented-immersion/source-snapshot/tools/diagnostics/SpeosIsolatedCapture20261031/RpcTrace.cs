using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using static RpcCapture;

// Commercial-engine validation only. No product reference or engine fallback.
internal static class RpcTrace
{
    internal static List<(int Id,int Wire,byte[] Data)> Read(byte[] bytes)
    {
        var list=new List<(int,int,byte[])>();int at=0;
        ulong Var(){ulong v=0;for(int shift=0;shift<70;shift+=7){if(at>=bytes.Length)throw new InvalidDataException("Truncated protobuf");var b=bytes[at++];v|=(ulong)(b&127)<<shift;if(b<128)return v;}throw new InvalidDataException("Long protobuf integer");}
        while(at<bytes.Length){var tag=Var();var wire=(int)(tag&7);int n;byte[] b;if(wire==0)b=BitConverter.GetBytes(Var());else{n=wire==1?8:wire==5?4:wire==2?checked((int)Var()):throw new InvalidDataException("Unknown wire");if(n<0||n>bytes.Length-at)throw new InvalidDataException("Truncated protobuf field");b=bytes.AsSpan(at,n).ToArray();at+=n;}list.Add(((int)(tag>>3),wire,b));}return list;
    }
    private static string GuidValue(byte[] response)=>Encoding.UTF8.GetString(Read(response).Single(p=>p.Id==1).Data);
    private static byte[] PackedFloats(params float[] values){using var s=new MemoryStream();using(var w=new BinaryWriter(s,Encoding.UTF8,true))foreach(var v in values)w.Write(v);return s.ToArray();}
    private static byte[] Axis(params double[] values){using var s=new MemoryStream();using(var w=new BinaryWriter(s,Encoding.UTF8,true))foreach(var v in values)w.Write(v);return s.ToArray();}
    internal static async Task<int> Run(string root,string source,Func<string,byte[],Task<byte[]>> call,bool direct=false,double wavelength=400,double incidence=0)
    {
        var requests=new List<object>();var stages=new List<string>();var output=Path.Combine(root,"native-interactive-trace.json");
        async Task<byte[]> Rpc(string method,byte[] body){requests.Add(new{Method=method,BodyBase64=Convert.ToBase64String(body)});var result=await call(method,body);stages.Add(method);return result;}
        async Task<string> Create(string service,byte[] message,int field=1)=>GuidValue(await Rpc(service+"/Create",Field(field,message)));
        try
        {
            async Task<string> Face(string name,float z,float extent,float nz,bool tilted=false)
            {
                var vertices=PackedFloats(-extent,-extent,z,extent,-extent,z,extent,extent,z,-extent,extent,z);
                var normals=PackedFloats(0,0,nz,0,0,nz,0,0,nz,0,0,nz);
                if(tilted)
                {
                    var s=(float)Math.Sin(incidence*Math.PI/180);var c=(float)Math.Cos(incidence*Math.PI/180);
                    vertices=PackedFloats(-extent*c,-extent,extent*s,extent*c,-extent,-extent*s,extent*c,extent,-extent*s,-extent*c,extent,extent*s);
                    normals=PackedFloats(nz*s,0,nz*c,nz*s,0,nz*c,nz*s,0,nz*c,nz*s,0,nz*c);
                }
                var triangles=nz>0?new byte[]{0,1,2,0,2,3}:new byte[]{0,2,1,0,3,2};
                return await Create("ansys.api.speos.face.v1.FacesManager",Message(Field(1,name),Field(10,vertices),Field(11,triangles),Field(12,normals)));
            }
            var targetFace=await Face("target",0,100,1,true);var sourceFace=await Face("emitter",10,.01f,-1);
            var targetBody=await Create("ansys.api.speos.body.v1.BodiesManager",Message(Field(1,"targetBody"),Field(5,targetFace)));
            var sourceBody=await Create("ansys.api.speos.body.v1.BodiesManager",Message(Field(1,"sourceBody"),Field(5,sourceFace)));
            var part=await Create("ansys.api.speos.part.v1.PartsManager",Message(Field(10,targetBody),Field(10,sourceBody)));
            var debugFace=Read(Read(await Rpc("ansys.api.speos.face.v1.FacesManager/Read",Field(1,sourceFace))).Single(f=>f.Id==1).Data);
            var debugBody=Read(Read(await Rpc("ansys.api.speos.body.v1.BodiesManager/Read",Field(1,sourceBody))).Single(f=>f.Id==1).Data);
            var debugPart=Read(Read(await Rpc("ansys.api.speos.part.v1.PartsManager/Read",Field(1,part))).Single(f=>f.Id==1).Data);
            File.WriteAllText(Path.Combine(root,"native-interactive-geometry.json"),JsonSerializer.Serialize(new{Face=debugFace.Select(f=>new{f.Id,f.Wire,Bytes=f.Data.Length,Text=f.Id==1?Encoding.UTF8.GetString(f.Data):null}),Body=debugBody.Select(f=>new{f.Id,Text=Encoding.UTF8.GetString(f.Data)}),Part=debugPart.Select(f=>new{f.Id,Text=Encoding.UTF8.GetString(f.Data)})},new JsonSerializerOptions{WriteIndented=true}));
            var sop=await Create("ansys.api.speos.sop.v1.SOPTemplatesManager",Message(Field(1,"measured input control"),Field(5,Field(1,source))));
            var absorber=await Create("ansys.api.speos.sop.v1.SOPTemplatesManager",Message(Field(1,"black emitter backing"),Field(3,Field(1,0d))));
            var spectrum=await Create("ansys.api.speos.spectrum.v1.SpectrumsManager",Message(Field(1,wavelength+" nm"),Field(3,Field(1,wavelength))));
            var intensity=await Create("ansys.api.speos.intensity.v1.IntensityTemplatesManager",Message(Field(1,"near normal collimated"),Field(9,Message(Field(1,.00001d),Field(2,.00001d),Field(3,.00001d)))));
            var emission=Message(Field(3,Field(1,1d)),Field(5,intensity),Field(6,Array.Empty<byte>()),Field(9,spectrum));
            var sourceTemplate=await Create("ansys.api.speos.source.v1.SourceTemplatesManager",Message(Field(1,"control source"),Field(4,emission)));
            // Weight omitted deliberately: absorption frequency directly estimates total R.
            var simulation=await Create("ansys.api.speos.simulation.v1.templates.SimulationTemplatesManager",Message(Field(3,"single interaction MC"),
                direct?Field(1,Message(Field(2,.000001d),Field(3,3),Field(4,Field(1,1e-9)))):Field(5,Message(Field(1,.000001d),Field(2,3)))));
            string? sensor=null;
            if(direct)sensor=await Create("ansys.api.speos.sensor.v1.SensorTemplatesManager",Message(Field(4,"hemisphere radiometric control"),Field(7,Message(Field(4,Array.Empty<byte>()),Field(8,Field(1,Message(Field(1,90d),Field(2,64)))),Field(11,Array.Empty<byte>())))));
            var geoPath=Field(1,Message(Field(1,"sourceBody/emitter"),Field(2,0)));
            var properties=Field(1,geoPath);
            var sourceInstance=Message(Field(1,"source"),Field(10,sourceTemplate),Field(12,Message(properties,Field(3,Field(2,Array.Empty<byte>())))));
            var sceneData=Message(Field(10,part),Field(13,sourceInstance),
                Field(15,Message(Field(1,"simulation"),Field(10,simulation),Field(12,"source"),Field(13,Array.Empty<byte>()),direct?Field(11,"sensor"):Array.Empty<byte>())),
                Field(16,Message(Field(1,"targetMaterial"),Field(13,sop),Field(12,Field(1,"targetBody")))),
                Field(16,Message(Field(1,"emitterMaterial"),Field(13,absorber),Field(12,Field(1,"sourceBody")))));
            if(direct)sceneData=Message(sceneData,Field(14,Message(Field(1,"sensor"),Field(10,sensor!),Field(11,"native-direct-result"),Field(15,Message(Field(1,Axis(0,0,0,1,0,0,0,1,0,0,0,1)),Field(3,Array.Empty<byte>()))))));
            var scene=await Create("ansys.api.speos.scene.v2.ScenesManager",sceneData);
            const int rays=65536;
            var job=await Create("ansys.api.speos.job.v2.JobsManager",Message(Field(7,direct?"weighted radiometric BSDF control":"unweighted normal BSDF control"),Field(1,scene),Field(2,"simulation"),direct?Field(4,Message(Field(1,1048576),Field(2,40))):Field(6,Field(1,Message(Field(1,"source"),Field(3,rays))))),3);
            if(direct)await Rpc("ansys.api.speos.job.v2.JobActions/SaveFile",Message(Field(1,job),Field(2,Path.Combine(root,"native-physical-control.speos"))));
            await Rpc("ansys.api.speos.job.v2.JobActions/Start",Field(1,job));
            int state=0;var deadline=DateTime.UtcNow.AddSeconds(50);
            do{var response=await Rpc("ansys.api.speos.job.v2.JobActions/GetState",Field(1,job));state=(int)Read(response).Where(p=>p.Id==1).Select(p=>BitConverter.ToUInt64(p.Data)).DefaultIfEmpty(0UL).Single();if(state is 3 or 4 or 5)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
            if(state!=4){var error=Read(await Rpc("ansys.api.speos.job.v2.JobActions/GetError",Field(1,job))).Where(p=>p.Wire==2).Select(p=>new{Field=p.Id,Value=Encoding.UTF8.GetString(p.Data)}).ToArray();await Rpc("ansys.api.speos.job.v2.JobActions/Stop",Field(1,job));File.WriteAllText(output,JsonSerializer.Serialize(new{Status="Native physical trace not completed",State=state,Errors=error,Stages=stages},new JsonSerializerOptions{WriteIndented=true}));return 2;}
            if(direct){var results=Read(await Rpc("ansys.api.speos.job.v2.JobActions/GetResults",Field(1,job))).Where(p=>p.Id==1).Select(p=>Read(p.Data)).SelectMany(p=>p).Where(p=>p.Id==1&&p.Wire==2).Select(p=>Encoding.UTF8.GetString(p.Data)).ToArray();
                File.WriteAllBytes(Path.Combine(root,"native-direct-progress.pb"),await Rpc("ansys.api.speos.job.v2.JobActions/GetProgressStatus",Field(1,job)));
                File.WriteAllText(output,JsonSerializer.Serialize(new{Status="Native weighted direct trace completed",SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),RequestedRays=1048576,MaximumSeconds=40,SourceWatts=1,WavelengthNm=wavelength,TargetIncidenceDegrees=incidence,TargetNormal=new[]{(double)(float)Math.Sin(incidence*Math.PI/180),0d,(double)(float)Math.Cos(incidence*Math.PI/180)},SourcePatchHalfWidthMm=.01,SourceConeDegrees=.00001,Sensor="Radiometric conoscopic hemisphere, 64 pixels, far field",ResultPaths=results,Stages=stages},new JsonSerializerOptions{WriteIndented=true}));return 0;}
            var frames=await Rpc("ansys.api.speos.job.v2.JobActions/GetRayPaths",Message(Field(1,job),Field(4,1)));
            File.WriteAllBytes(Path.Combine(root,"native-interactive-raypaths.grpc"),frames);
            var examples=new List<object>();var count=0;int reflected=0,absorbed=0;double first=0,second=0;var bins=new int[18];
            for(int at=0;at<frames.Length;){if(frames[at]!=0)throw new InvalidDataException("Compressed native frame");var n=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(frames.AsSpan(at+1));var fields=Read(frames.AsSpan(at+5,n).ToArray());at+=5+n;count++;var status=fields.Where(p=>p.Id==5).SelectMany(p=>ReadVarints(p.Data)).Select(v=>unchecked((int)v)).ToArray();var coordinates=fields.Where(p=>p.Id==1).SelectMany(p=>Floats(p.Data)).ToArray();var direction=fields.Where(p=>p.Id==6).SelectMany(p=>Floats(p.Data)).ToArray();
                if(coordinates.Length<6||Math.Abs(coordinates[2]-10)>1e-4||Math.Abs(coordinates[5])>1e-4)throw new InvalidDataException("Native control did not hit the target first");
                double dx=direction.Length==3?direction[0]:0,dy=direction.Length==3?direction[1]:0,dz=direction.Length==3?direction[2]:0;
                if(coordinates.Length>=9){dx=coordinates[6]-coordinates[3];dy=coordinates[7]-coordinates[4];dz=coordinates[8]-coordinates[5];}
                if(dz>0){reflected++;var mu=dz/Math.Sqrt(dx*dx+dy*dy+dz*dz);first+=mu;second+=mu*mu;bins[Math.Min(17,(int)(Math.Acos(mu)*180/Math.PI/5))]++;}else if(dx==0&&dy==0&&dz==0)absorbed++;else throw new InvalidDataException("Unexpected outgoing hemisphere");
                if(examples.Count<20)examples.Add(new{status,coordinates,direction,Fields=fields.Select(p=>new{p.Id,p.Wire,Bytes=p.Data.Length})});}
            File.WriteAllText(output,JsonSerializer.Serialize(new{Status="Native interactive trace captured",SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),RequestedRays=rays,RayPaths=count,Reflected=reflected,Absorbed=absorbed,ReflectedFraction=reflected/(double)count,MeanCosine=first/Math.Max(1,reflected),MeanSquareCosine=second/Math.Max(1,reflected),BinsFiveDegrees=bins,Examples=examples,Stages=stages,NoWeight=true,NominalIncidenceDegrees=0,SourceConeDegrees=.00001,MeasuredWavelengthNm=wavelength},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){File.WriteAllText(output,JsonSerializer.Serialize(new{Status="Native physical trace probe failed",Error=ex.ToString(),Stages=stages},new JsonSerializerOptions{WriteIndented=true}));return 2;}
        finally{File.WriteAllText(Path.Combine(root,"native-interactive-requests.json"),JsonSerializer.Serialize(requests,new JsonSerializerOptions{WriteIndented=true}));}
    }
    private static IEnumerable<float> Floats(byte[] bytes){for(int i=0;i<bytes.Length;i+=4)yield return BitConverter.ToSingle(bytes,i);}
    private static IEnumerable<ulong> ReadVarints(byte[] bytes){int at=0;while(at<bytes.Length){ulong value=0;for(int shift=0;shift<70;shift+=7){var b=bytes[at++];value|=(ulong)(b&127)<<shift;if(b<128)break;}yield return value;}}
}
