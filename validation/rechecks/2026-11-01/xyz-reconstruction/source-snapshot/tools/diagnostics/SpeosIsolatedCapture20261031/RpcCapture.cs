using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Diagnostic-only transport for the installed official grammar. No product dependency.
internal static class RpcCapture
{
    internal static byte[] Field(int id, object value)
    {
        using var m=new MemoryStream(); using var w=new BinaryWriter(m);
        void V(ulong v){while(v>127){w.Write((byte)(v|128));v>>=7;}w.Write((byte)v);}
        if(value is double d){V((ulong)(id*8+1));w.Write(d);}
        else if(value is float f){V((ulong)(id*8+5));w.Write(f);}
        else if(value is int i){V((ulong)(id*8));V((ulong)i);}
        else {var b=value is string s?Encoding.UTF8.GetBytes(s):(byte[])value;V((ulong)(id*8+2));V((ulong)b.Length);w.Write(b);}
        return m.ToArray();
    }
    internal static byte[] Message(params byte[][] fields)=>fields.SelectMany(f=>f).ToArray();
    internal static async Task<int> Run(string[] args)
    {
        var root=Path.GetFullPath(args[1]);var source=Path.GetFullPath(args[2]);
        var executable=Path.Combine(root,"native-runtime/Optical Products/SPEOS_RPC/SpeosRPC_Server.exe");
        var outputRoot=args[0]=="trace"&&args.Length>3?Path.Combine(root,args[3]):args[0] is "prepare" or "inspect"?Path.GetDirectoryName(Path.GetFullPath(args[3]))!:root;
        Directory.CreateDirectory(outputRoot);
        var log=Path.Combine(outputRoot,"rpc-server.log");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable)!};
        foreach(var a in new[]{"--host","127.0.0.1","--port","50187","--transport_insecure","--log_file",log})start.ArgumentList.Add(a);
        using var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();start.ArgumentList[3]=port.ToString();
        using var server=Process.Start(start)!;
        try{
            using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(15),DefaultRequestVersion=HttpVersion.Version20,DefaultVersionPolicy=HttpVersionPolicy.RequestVersionExact};
            async Task<byte[]> Call(string method,byte[] body){
                var frame=new byte[5+body.Length];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1),body.Length);body.CopyTo(frame,5);
                var route=method.Contains('/')?"/"+method:"/ansys.api.speos.bsdf.v1.AnisotropicSpeosBsdf.AnisotropicBsdfService/"+method;
                using var req=new HttpRequestMessage(HttpMethod.Post,route){Version=HttpVersion.Version20,VersionPolicy=HttpVersionPolicy.RequestVersionExact,Content=new ByteArrayContent(frame)};
                req.Content.Headers.ContentType=new MediaTypeHeaderValue("application/grpc");req.Headers.Add("TE","trailers");
                using var response=await http.SendAsync(req);var data=await response.Content.ReadAsByteArrayAsync();
                var status=response.TrailingHeaders.TryGetValues("grpc-status",out var st)?st.Single():response.Headers.TryGetValues("grpc-status",out st)?st.Single():"unknown";
                if(!response.IsSuccessStatusCode||status!="0")throw new InvalidDataException(method+": "+response.StatusCode+" grpc="+status+" "+response.TrailingHeaders.ToString()+response.Headers.ToString());
                if(method.EndsWith("/GetRayPaths"))return data;
                if(data.Length<5||data[0]!=0||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1))!=data.Length-5)throw new InvalidDataException("Invalid response frame");
                return data[5..];
            }
            Exception? last=null;for(int i=0;i<60;i++){if(server.HasExited)throw new InvalidOperationException("Owned RPC server exited: "+server.ExitCode);try{await Call("Load",Field(1,source));last=null;break;}catch(HttpRequestException ex){last=ex;await Task.Delay(250);}}
            if(last is not null)throw last;
            if(args[0]=="inspect"){NativePowerStateProbe.Capture(server,source,Path.GetFullPath(args[3]));return 0;}
            if(args[0]=="prepare"){
                if(args.Length==5&&args[4]=="freeze"){
                    await Call("DisableWhiteSpecular",[]);
                    await Call("FreezeAbsorptionReflectionTransmissionCoefficients",[]);
                    await Call("Save",Field(1,Path.GetFullPath(args[3])));
                    return 0;
                }
                var angle=double.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture)*Math.PI/180;
                var height=double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture);
                var white=double.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture);
                var cone=Message(Field(1,angle),Field(2,height));
                var row=Message(Enumerable.Range(0,3).Select(_=>Field(1,cone)).ToArray());
                await Call("SetSpecularInterpolationEnhancementData",Message(Field(1,Field(1,row)),Field(3,1d),Field(4,1d)));
                await Call("EnableWhiteSpecular",Field(1,white));
                await Call("Save",Field(1,Path.GetFullPath(args[3])));
                File.WriteAllBytes(Path.ChangeExtension(args[3],".enhancement.pb"),await Call("GetSpecularInterpolationEnhancementData",[]));
                File.WriteAllText(Path.ChangeExtension(args[3],".native.json"),JsonSerializer.Serialize(new{Version=FileVersionInfo.GetVersionInfo(executable).FileVersion,ExecutableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant(),SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),SavedSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[3]))).ToLowerInvariant(),HalfAngleDegrees=args[4],Height=height,WhiteBase=white},new JsonSerializerOptions{WriteIndented=true}));
                return 0;
            }
            if(args[0]=="trace"){
                var traceDirectory=args.Length>3?Path.Combine(root,args[3]):root;Directory.CreateDirectory(traceDirectory);
                File.WriteAllText(Path.Combine(traceDirectory,"native-trace-engine.json"),JsonSerializer.Serialize(new{Version=FileVersionInfo.GetVersionInfo(executable).FileVersion,ExecutableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant(),SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),OwnedServerPid=server.Id,LoopbackOnly=true,PopupDisplayEnabled=false},new JsonSerializerOptions{WriteIndented=true}));
                var wave=args.Length>5?double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture):400;
                if(!double.IsFinite(wave)||wave<200||wave>20000)throw new ArgumentOutOfRangeException(nameof(wave));
                var incidence=args.Length>6?double.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture):0;
                if(!double.IsFinite(incidence)||incidence<0||incidence>=90)throw new ArgumentOutOfRangeException(nameof(incidence));
                return await RpcTrace.Run(traceDirectory,source,Call,args.Length>4&&args[4]=="direct",wave,incidence);
            }
            var prefix=Path.Combine(root,Path.GetFileNameWithoutExtension(source));
            if(args.Length>3&&args[3]=="set-control-cones"){
                var control=File.ReadAllBytes(Path.Combine(root,"synthetic-v11-rpc-enhancement.pb"));
                await Call("SetSpecularInterpolationEnhancementData",Message(control,Field(3,1d),Field(4,1d)));
                await Call("EnableWhiteSpecular",Field(1,550d));
            }
            await Call("ExportFile",Field(1,prefix+"-rpc-export.native"));
            File.WriteAllBytes(prefix+"-rpc-export.pb",await Call("Export",[]));
            var enhancement=await Call("GetSpecularInterpolationEnhancementData",[]);File.WriteAllBytes(prefix+"-rpc-enhancement.pb",enhancement);
            foreach(var mode in new[]{"loaded","white-disabled","white-enabled","reapplied-specular","generated-specular"}){
                if(mode=="white-disabled")await Call("DisableWhiteSpecular",[]);
                if(mode=="white-enabled")await Call("EnableWhiteSpecular",Field(1,550d));
                if(mode=="reapplied-specular")await Call("SetSpecularInterpolationEnhancementData",enhancement);
                if(mode=="generated-specular")await Call("GenerateSpecularInterpolationEnhancementData",Message(Field(1,1d),Field(2,1d)));
                File.WriteAllBytes(prefix+"-"+mode+"-enhancement.pb",await Call("GetSpecularInterpolationEnhancementData",[]));
                await Call("Save",Field(1,prefix+"-"+mode+".anisotropicbsdf"));
                foreach(var resolution in new[]{64,128,256,512})
                    await Call("ExportToConoscopicMap",Message(Field(1,prefix+"-"+mode+"-"+resolution+".xmp"),Field(2,400d),Field(3,0d),Field(4,1),Field(5,resolution)));
            }
            File.WriteAllText(prefix+"-rpc-capture.json",JsonSerializer.Serialize(new{Engine="Speos RPC",Version=FileVersionInfo.GetVersionInfo(executable).FileVersion,ExecutableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant(),Source=source,SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),OwnedServerPid=server.Id,LoopbackOnly=true,PopupDisplayEnabled=false},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        } catch(Exception ex){File.WriteAllText(Path.Combine(outputRoot,"native-rpc-failure.json"),JsonSerializer.Serialize(new{Source=source,Error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 2;}
        finally{if(!server.HasExited){server.Kill(true);server.WaitForExit();}}
    }
}
