using System;
using System.Linq;
using CadUrdfMaker.Core;

// All inputs are analytical examples generated here, independent of CAD files.
public static class InertiaTests
{
    sealed class Example
    {
        public double Mass;
        public double[] Tensor, Moments, Placement;
        public double[][] Axes;
    }
    static void Assert(bool ok,string text){if(!ok)throw new Exception(text);}
    static void Near(double a,double b){Assert(Math.Abs(a-b)<1e-12,"Expected "+b+", got "+a);}
    static int passed;
    static void Test(string name,Action test){test();passed++;Console.WriteLine("PASS "+name);}
    static Example[] Examples()
    {
        return Enumerable.Range(0,6).Select(n=>{
            var rotation=Math3.FromRpy(new[]{.37+.09*n,-.61+.06*n,.83-.08*n});
            var moments=new[]{.001*(n+1),.004*(n+1),.0045*(n+1)};
            var axes=Enumerable.Range(0,3).Select(k=>new[]{rotation[k],rotation[4+k],rotation[8+k]}).ToArray();
            var tensor=new double[9];
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)tensor[i*3+j]+=moments[k]*axes[k][i]*axes[k][j];
            var placement=Math3.FromRpy(new[]{-.23+.04*n,.51,-.19});placement[3]=.1*n;placement[7]=-.2;placement[11]=.3;
            return new Example{Mass=1+n,Tensor=tensor,Moments=moments,Axes=axes,Placement=placement};
        }).ToArray();
    }
    public static int Main()
    {
        try{
            var records=Examples();
            Test("analytical tensors retain signs and match constructed principal moments and axes",()=>{
                foreach(var record in records){
                    var tensor=InertiaAdapter.MassPropertyTensor(record.Tensor);
                    for(int i=0;i<9;i++)Near(tensor[i],record.Tensor[i]);
                    var eigen=Math3.Eigenvalues(tensor);for(int i=0;i<3;i++)Near(eigen[i],record.Moments[i]);
                    Assert(Validation.PhysicsError(new PhysicalProperties{Mass=record.Mass,Inertia=tensor})==null,"Analytical tensor rejected");
                }
            });
            Test("local frame conversion preserves principal moments and detects an off-diagonal sign regression",()=>{
                int reproduced=0;
                foreach(var record in records){
                    var tensor=InertiaAdapter.MassPropertyTensor(record.Tensor);
                    var local=Math3.Transform(new PhysicalProperties{Mass=record.Mass,Inertia=tensor},Math3.Inverse(record.Placement));
                    var eigen=Math3.Eigenvalues(local.Inertia);for(int i=0;i<3;i++)Near(eigen[i],record.Moments[i]);
                    Assert(Validation.PhysicsError(local)==null,"Rotation invalidated tensor");
                    var broken=record.Tensor.Select((v,i)=>i==0||i==4||i==8?v:-v).ToArray();
                    var error=Validation.PhysicsError(new PhysicalProperties{Mass=record.Mass,Inertia=broken});
                    if(error!=null&&error.Contains("triangle inequality")&&error.Contains("excess="))reproduced++;
                }
                Assert(reproduced>0,"Examples did not exercise the off-diagonal inversion failure");
            });
            Test("body API products use the opposite off-diagonal convention for an analytical box",()=>{
                // Box: 6 kg, sides 0.1, 0.2, 0.3 m. Moments follow m*(b*b+c*c)/12.
                var diagonal=new[]{.065,0,0,0,.05,0,0,0,.025};
                var expected=Math3.RotateTensor(Math3.FromRpy(new[]{.4,.7,-.3}),diagonal);
                var values=new double[12];values[5]=6;values[6]=expected[0];values[7]=expected[4];values[8]=expected[8];
                values[9]=-expected[1];values[10]=-expected[2];values[11]=-expected[5];
                var tensor=InertiaAdapter.BodyTensor(values);for(int i=0;i<9;i++)Near(tensor[i],expected[i]);
            });
            Test("inertia adapters reject malformed data and retain source arrays",()=>{
                var source=records[0].Tensor;var copy=InertiaAdapter.MassPropertyTensor(source);copy[0]=0;Assert(source[0]!=0,"Adapter mutated input");
                foreach(var malformed in new[]{new double[8],new[]{double.NaN,0,0,0,0,0,0,0,0}}){bool rejected=false;try{InertiaAdapter.MassPropertyTensor(malformed);}catch(ArgumentException){rejected=true;}Assert(rejected,"Malformed tensor accepted");}
            });
            Console.WriteLine(passed+" analytical inertia regression tests passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
