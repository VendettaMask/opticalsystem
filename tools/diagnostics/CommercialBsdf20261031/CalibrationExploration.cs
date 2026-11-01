using System.Globalization;
using System.Text.Json;

// Independent diagnostic least squares only. Never exports a usable material or calibration.
internal static class CalibrationExploration
{
    internal static void Run(string root, string oldRoot, string name = "mystique")
    {
        static double[][] Rows(string path)=>File.ReadAllLines(path).Where(l=>l.Length>0&&(char.IsDigit(l[0])||l[0]=='-')).Select(l=>l.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        static string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
        var spectral=Rows(Path.Combine(oldRoot,name+".astm")); var xyz=Rows(Path.Combine(oldRoot,name+"_XYZ.astm")).ToDictionary(Key);
        var heldout=spectral.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
        var results=new List<object>();
        for(var band=0;band<31;band++)
        {
            var rows=spectral.Select(r=>(X:xyz[Key(r)][4..],Y:r[band+4],Hold:heldout.Contains(Key(r)))).ToArray();
            var m=new double[3,4];
            foreach(var r in rows.Where(r=>!r.Hold))for(var i=0;i<3;i++){for(var j=0;j<3;j++)m[i,j]+=r.X[i]*r.X[j];m[i,3]+=r.X[i]*r.Y;}
            for(var i=0;i<3;i++)
            {
                var pivot=Enumerable.Range(i,3-i).MaxBy(j=>Math.Abs(m[j,i]));
                for(var j=i;j<4;j++)(m[i,j],m[pivot,j])=(m[pivot,j],m[i,j]);
                var d=m[i,i];for(var j=i;j<4;j++)m[i,j]/=d;
                for(var k=0;k<3;k++)if(k!=i){var t=m[k,i];for(var j=i;j<4;j++)m[k,j]-=t*m[i,j];}
            }
            var c=new[]{m[0,3],m[1,3],m[2,3]};var hold=rows.Where(r=>r.Hold).ToArray();
            var errors=hold.Select(r=>c.Select((v,j)=>v*r.X[j]).Sum()-r.Y).ToArray();
            var nrmse=Math.Sqrt(errors.Sum(e=>e*e)/hold.Sum(r=>r.Y*r.Y));
            results.Add(new{Wavelength=400+band*10,Coefficients=c,ValidationNrmse=nrmse,MaximumAbsolute=errors.Max(Math.Abs),MinimumPrediction=rows.Min(r=>c.Select((v,j)=>v*r.X[j]).Sum()),Passed=nrmse<=.01&&errors.Max(Math.Abs)<=.01});
        }
        File.WriteAllText(Path.Combine(root,"signed-linear-diagnostic.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(results));
    }
}
