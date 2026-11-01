using OpticalSystemDesign.Core.Scattering;

// Independent three-variable constrained least squares diagnostic. No usable export.
internal static class GamutExploration
{
    internal static object Run(XyzRadiometricObservation[] rows)
    {
        var train=rows.Where(p=>!p.Validation).ToArray();
        static double[] V(XyzRadiometricObservation p)=>new[]{p.X,p.Y,p.Z};
        static double Dot(double[] a,double[] b)=>a.Zip(b).Sum(p=>p.First*p.Second);
        static double[] Cross(double[] a,double[] b)=>new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        static double[]? Solve(double[,] a,double[] b)
        {
            var n=b.Length; a=(double[,])a.Clone(); b=(double[])b.Clone();
            for(int i=0;i<n;i++)
            {
                var p=Enumerable.Range(i,n-i).MaxBy(k=>Math.Abs(a[k,i]));if(Math.Abs(a[p,i])<1e-26)return null;
                for(int j=i;j<n;j++)(a[i,j],a[p,j])=(a[p,j],a[i,j]);(b[i],b[p])=(b[p],b[i]);
                var d=a[i,i];for(int j=i;j<n;j++)a[i,j]/=d;b[i]/=d;
                for(int k=0;k<n;k++)if(k!=i){var t=a[k,i];for(int j=i;j<n;j++)a[k,j]-=t*a[i,j];b[k]-=t*b[i];}
            }
            return b;
        }
        object Fit(bool allFeatures)
        {
            var vertices=Hull((allFeatures?rows:train).Select(V).Where(a=>a.Sum()>0).Select(a=>new[]{a[0]/a.Sum(),a[1]/a.Sum(),a[2]/a.Sum()}).ToArray());
            var candidates=new List<double[]>{new double[3]};
            void Project(double[][] basis)
            {
                var matrix=new double[basis.Length,basis.Length];var target=new double[basis.Length];
                foreach(var row in train){var v=V(row);var a=basis.Select(b=>Dot(b,v)).ToArray();for(int i=0;i<a.Length;i++){target[i]+=a[i]*row.RadiometricBsdfPerSteradian;for(int j=0;j<a.Length;j++)matrix[i,j]+=a[i]*a[j];}}
                var solve=Solve(matrix,target);if(solve is null)return;
                var c=Enumerable.Range(0,3).Select(i=>basis.Select((b,j)=>b[i]*solve[j]).Sum()).ToArray();
                if(vertices.All(v=>Dot(v,c)>=-1e-12))candidates.Add(c);
            }
            Project(new[]{new[]{1d,0,0},new[]{0d,1,0},new[]{0d,0,1}});
            foreach(var normal in vertices)
            {
                var u=Cross(normal,Math.Abs(normal[0])<.8?new[]{1d,0,0}:new[]{0d,1,0});var un=Math.Sqrt(Dot(u,u));u=u.Select(v=>v/un).ToArray();var v=Cross(normal,u);var vn=Math.Sqrt(Dot(v,v));v=v.Select(x=>x/vn).ToArray();Project(new[]{u,v});
            }
            for(int i=0;i<vertices.Length;i++)for(int j=i+1;j<vertices.Length;j++){var u=Cross(vertices[i],vertices[j]);var norm=Math.Sqrt(Dot(u,u));if(norm>1e-12)Project(new[]{u.Select(v=>v/norm).ToArray()});}
            var best=candidates.MinBy(c=>train.Sum(p=>Math.Pow(Dot(V(p),c)-p.RadiometricBsdfPerSteradian,2)))!;
            var held=rows.Where(p=>p.Validation).ToArray();var errors=held.Select(p=>Dot(V(p),best)-p.RadiometricBsdfPerSteradian).ToArray();
            return new{UnlabelledDomainIncludesValidationColors=allFeatures,Vertices=vertices.Length,Coefficients=best,ValidationNrmse=Math.Sqrt(errors.Sum(e=>e*e)/held.Sum(p=>p.RadiometricBsdfPerSteradian*p.RadiometricBsdfPerSteradian)),MaximumAbsolute=errors.Max(Math.Abs),MinimumPrediction=rows.Min(p=>Dot(V(p),best))};
        }
        return new[]{Fit(false),Fit(true)};
    }
    private static double[][] Hull(double[][] p)
    {
        var points=p.OrderBy(v=>v[0]).ThenBy(v=>v[1]).DistinctBy(v=>(v[0],v[1])).ToArray();
        static double Turn(double[] a,double[] b,double[] c)=>(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);
        var a=new List<double[]>();foreach(var v in points){while(a.Count>1&&Turn(a[^2],a[^1],v)<=0)a.RemoveAt(a.Count-1);a.Add(v);}
        var b=new List<double[]>();foreach(var v in points.Reverse()){while(b.Count>1&&Turn(b[^2],b[^1],v)<=0)b.RemoveAt(b.Count-1);b.Add(v);}
        return a.SkipLast(1).Concat(b.SkipLast(1)).ToArray();
    }
}
