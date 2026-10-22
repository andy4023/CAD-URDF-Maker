using System;
using System.Collections.Generic;
using System.Linq;

namespace CadUrdfMaker.Core
{
    public static class Math3
    {
        public static double[] Identity() { return new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 }; }
        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        public static double Dot(double[] a, double[] b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
        public static double[] Sub(double[] a, double[] b) { return new double[] { a[0]-b[0],a[1]-b[1],a[2]-b[2] }; }
        public static double[] Cross(double[] a, double[] b) { return new double[] { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] }; }
        public static double[] Normalize(double[] a) { double n=Math.Sqrt(Dot(a,a)); if (!Finite(n) || n<1e-12) throw new ArgumentException("Axis must have nonzero finite length."); return a.Select(x=>x/n).ToArray(); }
        public static double[] Multiply(double[] a, double[] b)
        {
            var c=new double[16]; for(int i=0;i<4;i++) for(int j=0;j<4;j++) for(int k=0;k<4;k++) c[i*4+j]+=a[i*4+k]*b[k*4+j]; return c;
        }
        public static double[] Inverse(double[] a)
        {
            var b=Identity(); for(int i=0;i<3;i++) for(int j=0;j<3;j++) b[i*4+j]=a[j*4+i]; for(int i=0;i<3;i++) b[i*4+3]=-(b[i*4]*a[3]+b[i*4+1]*a[7]+b[i*4+2]*a[11]); return b;
        }
        public static double[] Point(double[] t, double[] p) { var q=Vector(t,p); q[0]+=t[3];q[1]+=t[7];q[2]+=t[11];return q; }
        public static double[] Vector(double[] t,double[] p) { return new double[] { t[0]*p[0]+t[1]*p[1]+t[2]*p[2],t[4]*p[0]+t[5]*p[1]+t[6]*p[2],t[8]*p[0]+t[9]*p[1]+t[10]*p[2] }; }
        public static double[] RotateTensor(double[] t, double[] inertia)
        {
            var output=new double[9]; for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)for(int l=0;l<3;l++)output[i*3+j]+=t[i*4+k]*inertia[k*3+l]*t[j*4+l];return output;
        }
        public static PhysicalProperties Transform(PhysicalProperties p,double[] t) { return new PhysicalProperties { Mass=p.Mass, Cog=Point(t,p.Cog), Inertia=RotateTensor(t,p.Inertia), Source=p.Source }; }
        public static PhysicalProperties Scale(PhysicalProperties p,double s,string source) { return new PhysicalProperties { Mass=p.Mass*s, Cog=(double[])p.Cog.Clone(), Inertia=p.Inertia.Select(x=>x*s).ToArray(), Source=source }; }
        public static PhysicalProperties Combine(IEnumerable<PhysicalProperties> items)
        {
            var parts=items.ToList(); var p=new PhysicalProperties { Source="Combined components" }; p.Mass=parts.Sum(x=>x.Mass);
            if (!(p.Mass>0)) throw new ArgumentException("Combined mass must be positive.");
            for(int i=0;i<3;i++) p.Cog[i]=parts.Sum(x=>x.Mass*x.Cog[i])/p.Mass;
            foreach(var part in parts) { var d=Sub(part.Cog,p.Cog);double d2=Dot(d,d);for(int i=0;i<3;i++)for(int j=0;j<3;j++)p.Inertia[i*3+j]+=part.Inertia[i*3+j]+part.Mass*((i==j?d2:0)-d[i]*d[j]); }
            return p;
        }
        public static PhysicalProperties Effective(ComponentRecord c)
        {
            if(c.Override.Mode=="complete") { var p=c.Override.Complete; return new PhysicalProperties { Mass=p.Mass,Cog=p.Cog,Inertia=p.Inertia,Source="Tool: complete override — "+c.Override.Note }; }
            if(c.Override.Mode=="density") { if(c.UnitDensity==null)throw new ArgumentException("Unit-density CAD geometry unavailable: "+c.Name);return Scale(c.UnitDensity,c.Override.Density,"Tool: density override — "+c.Override.Note); }
            return c.Cad;
        }
        public static PhysicalProperties LinkProperties(RobotProject p,LinkRecord link)
        {
            var inverse=Inverse(LinkFrame(p,link));
            return Combine(link.Components.Select(id=>p.Components.Single(c=>c.Id==id)).Select(c=>Transform(Effective(c),Multiply(inverse,c.ZeroTransform))));
        }
        public static double[] FromRpy(double[] angles)
        {
            double cr=Math.Cos(angles[0]),sr=Math.Sin(angles[0]),cp=Math.Cos(angles[1]),sp=Math.Sin(angles[1]),cy=Math.Cos(angles[2]),sy=Math.Sin(angles[2]);
            return new[]{cy*cp,cy*sp*sr-sy*cr,cy*sp*cr+sy*sr,0,sy*cp,sy*sp*sr+cy*cr,sy*sp*cr-cy*sr,0,-sp,cp*sr,cp*cr,0,0,0,0,1};
        }
        public static double[] LinkFrame(RobotProject p,LinkRecord link)
        {
            if(p.ChildFramesFollowRoot&&link.Id!=p.RootLink){
                var root=p.Links.FirstOrDefault(l=>l.Id==p.RootLink);
                if(root!=null){var aligned=(double[])LinkFrame(p,root).Clone();aligned[3]=link.ZeroFrame[3];aligned[7]=link.ZeroFrame[7];aligned[11]=link.ZeroFrame[11];return aligned;}
            }
            if(!p.Alignment.Enabled||link.Id!=p.RootLink)return link.ZeroFrame;
            // URDF has no root origin element. Re-express the physical root link
            // in aligned axes, including its STL, CoG, and tensor. Rotating every
            // CAD frame equally would cancel out during export and do nothing.
            var frame=Inverse(FromRpy(p.Alignment.Rpy));
            frame[3]=link.ZeroFrame[3];frame[7]=link.ZeroFrame[7];frame[11]=link.ZeroFrame[11];return frame;
        }
        public static double[] JointAxis(RobotProject p,JointRecord joint)
        {
            var child=p.Links.Single(l=>l.Id==joint.Child);
            // Axis is persisted in the original child ZeroFrame, when using a stored basis. Re-express it in the generated frame without changing
            // its physical direction when root alignment or frame policy changes.
            return Normalize(Vector(Inverse(LinkFrame(p,child)),Vector(child.ZeroFrame,joint.Axis)));
        }
        public static double[] PreviewAlignment(RobotProject p)
        {
            if(!p.Alignment.Enabled)return Identity();
            var rotation=FromRpy(p.Alignment.Rpy);var root=p.Links.FirstOrDefault(l=>l.Id==p.RootLink);
            if(root!=null){var origin=Vector(rotation,new[]{root.ZeroFrame[3],root.ZeroFrame[7],root.ZeroFrame[11]});rotation[3]=-origin[0];rotation[7]=-origin[1];rotation[11]=-origin[2];}
            return rotation;
        }
        public static double[] Rpy(double[] t)
        {
            double pitch=Math.Asin(Math.Max(-1,Math.Min(1,-t[8])));
            if(Math.Abs(Math.Cos(pitch))>1e-9)return new double[] { Math.Atan2(t[9],t[10]),pitch,Math.Atan2(t[4],t[0]) };
            return new double[] { 0,pitch,Math.Atan2(-t[1],t[5]) };
        }
        public static bool Rigid(double[] t)
        {
            if(t==null||t.Length!=16||t.Any(x=>!Finite(x)))return false;
            for(int i=0;i<3;i++)for(int j=0;j<3;j++){double dot=0;for(int k=0;k<3;k++)dot+=t[k*4+i]*t[k*4+j];if(Math.Abs(dot-(i==j?1:0))>1e-7)return false;}
            double determinant=t[0]*(t[5]*t[10]-t[6]*t[9])-t[1]*(t[4]*t[10]-t[6]*t[8])+t[2]*(t[4]*t[9]-t[5]*t[8]);
            return Math.Abs(determinant-1)<1e-7 && Math.Abs(t[12])+Math.Abs(t[13])+Math.Abs(t[14])+Math.Abs(t[15]-1)<1e-8;
        }
        public static double[] Eigenvalues(double[] tensor)
        {
            var a=(double[])tensor.Clone();for(int sweep=0;sweep<32;sweep++){int p=0,q=1;if(Math.Abs(a[2])>Math.Abs(a[1]))q=2;if(Math.Abs(a[5])>Math.Abs(a[p*3+q])){p=1;q=2;}if(Math.Abs(a[p*3+q])<1e-16)break;double angle=0.5*Math.Atan2(2*a[p*3+q],a[q*3+q]-a[p*3+p]);double c=Math.Cos(angle),s=Math.Sin(angle);var r=new double[]{1,0,0,0,1,0,0,0,1};r[p*3+p]=c;r[q*3+q]=c;r[p*3+q]=s;r[q*3+p]=-s;var b=new double[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)for(int l=0;l<3;l++)b[i*3+j]+=r[k*3+i]*a[k*3+l]*r[l*3+j];a=b;}
            var e=new double[]{a[0],a[4],a[8]};Array.Sort(e);return e;
        }
    }
}
