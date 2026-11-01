using System.IO.Compression;

/// <summary>Corpus-only reader for these signed-engine exports. It is not a product XMP importer.
/// The complete 64-square merged table is independently checked against native COM ExportTXT.</summary>
internal static class NativeXmpControl
{
    internal static float[][] ReadLayers(string path,int resolution,int layers)
    {
        var bytes=File.ReadAllBytes(path);using var decoded=new MemoryStream();
        for(int i=8;i<bytes.Length-2;i++)
        {
            if(bytes[i]!=0x78 || (bytes[i+1]!=0xda && bytes[i+1]!=0x9c))continue;
            var framed=BitConverter.ToInt32(bytes,i-8);var size=BitConverter.ToInt32(bytes,i-4);
            if(framed<10||framed-4>bytes.Length-i||size<0||size>16*1024*1024)continue;
            try
            {
                using var input=new MemoryStream(bytes,i,framed-4);using var z=new ZLibStream(input,CompressionMode.Decompress);using var block=new MemoryStream();
                var buffer=new byte[16384];int count;while((count=z.Read(buffer))>0){if(block.Length+count>size)throw new InvalidDataException();block.Write(buffer,0,count);}
                if(block.Length!=size)throw new InvalidDataException();block.Position=0;block.CopyTo(decoded);i+=framed-5;
            }
            catch(InvalidDataException) { }
        }
        var data=decoded.ToArray();var result=new List<float[]>();var expected=checked(resolution*resolution*4);
        for(int i=0;i<data.Length-16-expected&&result.Count<layers;i++)
        {
            if(BitConverter.ToInt32(data,i)!=resolution||BitConverter.ToInt32(data,i+4)!=resolution||BitConverter.ToInt32(data,i+8)!=expected||BitConverter.ToInt32(data,i+12)!=0)continue;
            var values=new float[resolution*resolution];for(int j=0;j<values.Length;j++){values[j]=BitConverter.ToSingle(data,i+16+j*4);if(!float.IsFinite(values[j])||values[j]<0)throw new InvalidDataException("Invalid native map value");}
            result.Add(values);i+=15+expected;
        }
        if(result.Count!=layers)throw new InvalidDataException("Unsupported native XMP control layout: "+path);
        return result.ToArray();
    }
    internal static double Integral(float[] values,int resolution,int cosinePower=0)
    {
        double integral=0;var step=Math.PI/resolution;
        for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++)
        {
            var a=-Math.PI/2+(x+.5)*step;var b=-Math.PI/2+(y+.5)*step;var theta=Math.Sqrt(a*a+b*b);
            if(theta<Math.PI/2)integral+=values[y*resolution+x]*(theta==0?1:Math.Sin(theta)/theta)*step*step*Math.Pow(Math.Cos(theta),cosinePower);
        }
        return integral;
    }
}
