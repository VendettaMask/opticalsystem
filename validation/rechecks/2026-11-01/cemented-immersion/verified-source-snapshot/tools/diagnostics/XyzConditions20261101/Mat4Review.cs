using System.Buffers.Binary;
using System.Text.Json;

internal static class Mat4Review
{
    internal sealed record Variable(string Name,int Rows,int Columns,double[] Values);
    internal static Variable[] Read(string path)
    {
        var bytes=File.ReadAllBytes(path);var result=new List<Variable>();int at=0;
        int Int(){if(at+4>bytes.Length)throw new InvalidDataException();var n=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at,4));at+=4;return n;}
        while(at<bytes.Length)
        {
            var type=Int();var rows=Int();var columns=Int();var imaginary=Int();var length=Int();
            if(type!=1000||rows<1||columns<1||(long)rows*columns>1000000||imaginary!=0||length<1||length>100||at+length>bytes.Length)throw new InvalidDataException("Unsupported controlled MATLAB v4 record");
            var name=System.Text.Encoding.ASCII.GetString(bytes,at,length).TrimEnd('\0');at+=length;var values=new double[rows*columns];
            for(int i=0;i<values.Length;i++){if(at+8>bytes.Length)throw new InvalidDataException();values[i]=BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(at,8)));at+=8;}
            result.Add(new(name,rows,columns,values));
        }
        return result.ToArray();
    }
    internal static void Inspect(string path,string output)
    {
        var variables=Read(path);var report=variables.Select(v=>new{v.Name,v.Rows,v.Columns,Minimum=v.Values.Min(),Maximum=v.Values.Max(),Negative=v.Values.Count(x=>x<0),First=v.Values.Take(12).ToArray()}).ToArray();
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
