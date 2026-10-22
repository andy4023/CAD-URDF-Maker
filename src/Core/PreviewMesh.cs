using System;
using System.Collections.Generic;
using System.Linq;

namespace CadUrdfMaker.Core
{
    public static class PreviewMesh
    {
        // Vertex clustering retains triangle connectivity; physical properties never
        // use this approximation. Full CAD tessellation is retained for export.
        public static double[] Reduce(double[] mesh,int maxTriangles)
        {
            if(mesh.Length/9<=maxTriangles)return mesh;
            var min=new[]{double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity};var max=new[]{double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            for(int i=0;i<mesh.Length;i++) { int k=i%3;min[k]=Math.Min(min[k],mesh[i]);max[k]=Math.Max(max[k],mesh[i]); }
            double step=Math.Max(1e-8,Math3.Sub(max,min).Max()/200);double[] result=mesh;
            for(int attempt=0;attempt<16;attempt++){
                var cells=new Dictionary<string,double[]>();Func<int,string> cell=i=>string.Join(":",new[]{0,1,2}.Select(k=>Math.Floor((mesh[i+k]-min[k])/step).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                for(int i=0;i<mesh.Length;i+=3){string key=cell(i);double[] v;if(!cells.TryGetValue(key,out v)){v=new double[4];cells[key]=v;}for(int k=0;k<3;k++)v[k]+=mesh[i+k];v[3]++;}
                foreach(var v in cells.Values)for(int k=0;k<3;k++)v[k]/=v[3];var output=new List<double>();var seen=new HashSet<string>();
                for(int i=0;i<mesh.Length;i+=9){string a=cell(i),b=cell(i+3),c=cell(i+6);if(a==b||b==c||a==c)continue;var sorted=new[]{a,b,c};Array.Sort(sorted,StringComparer.Ordinal);if(!seen.Add(string.Join("|",sorted)))continue;output.AddRange(cells[a].Take(3));output.AddRange(cells[b].Take(3));output.AddRange(cells[c].Take(3));}
                result=output.ToArray();if(result.Length/9<=maxTriangles)break;step*=1.6;
            }
            // Never replace a valid tiny component with an empty preview.
            return result.Length==0?mesh:result;
        }
    }
}
