using System;
using System.Linq;

namespace CadUrdfMaker.Core
{
    public static class OriginWorkflow
    {
        public const double AxisTolerance=0.000001;
        public static bool VectorValid(double[] v) { return v!=null&&v.Length==3&&v.All(Math3.Finite); }
        public static double[] Position(LinkRecord l) { return new[]{l.ZeroFrame[3],l.ZeroFrame[7],l.ZeroFrame[11]}; }
        public static JointRecord Joint(RobotProject p,LinkRecord l) { return p.Joints.FirstOrDefault(j=>j.Child==l.Id); }
        public static bool Moving(RobotProject p,LinkRecord l) { var j=Joint(p,l);return l.Id!=p.RootLink&&j!=null&&j.Type!="fixed"; }
        public static double[] InputFrame(RobotProject p,LinkRecord l)
        {
            if(l.Id==p.RootLink)return Math3.Identity();
            var j=Joint(p,l);if(j==null)throw new InvalidOperationException("Choose the parent joint before defining the child origin.");
            return Math3.LinkFrame(p,p.Links.Single(x=>x.Id==j.Parent));
        }
        public static double[] Project(double[] point,double[] anchor,double[] direction,double offset=0)
        {
            if(!VectorValid(point)||!VectorValid(anchor)||!VectorValid(direction)||!Math3.Finite(offset))throw new ArgumentException("Origin and axis need finite coordinates and offset.");
            var d=Math3.Normalize(direction);double distance=Math3.Dot(Math3.Sub(point,anchor),d)+offset;
            return new[]{anchor[0]+distance*d[0],anchor[1]+distance*d[1],anchor[2]+distance*d[2]};
        }
        public static double DistanceToAxis(double[] point,double[] anchor,double[] direction) { var difference=Math3.Sub(point,Project(point,anchor,direction));return Math.Sqrt(Math3.Dot(difference,difference)); }
        public static void Invalidate(RobotProject p,LinkRecord l)
        {
            l.Origin.Confirmed=false;var j=Joint(p,l);if(j!=null)j.Confirmed=false;
        }
        public static void SetAxis(RobotProject p,LinkRecord l,double[] anchor,double[] direction)
        {
            var j=Joint(p,l);if(l.Id==p.RootLink||j==null||j.Type=="fixed")throw new InvalidOperationException("Select a moving parent joint before defining its axis.");
            if(!VectorValid(anchor)||!VectorValid(direction))throw new ArgumentException("Axis requires finite XYZ coordinates.");
            var d=Math3.Normalize(direction);l.Origin.AxisPoint=(double[])anchor.Clone();l.Origin.AxisDirection=d;l.Origin.JointId=j.Id;
            j.Axis=Math3.Normalize(Math3.Vector(Math3.Inverse(l.ZeroFrame),d));Invalidate(p,l);
        }
        public static void SetPosition(RobotProject p,LinkRecord l,double[] cadPoint,bool projectOntoAxis)
        {
            if(!VectorValid(cadPoint))throw new ArgumentException("Origin requires three finite coordinates.");
            if(l.Id!=p.RootLink&&Joint(p,l)==null)throw new InvalidOperationException("Choose the parent joint first.");
            var o=l.Origin;double[] target=(double[])cadPoint.Clone();
            if(Moving(p,l)){
                if(!VectorValid(o.AxisPoint)||!VectorValid(o.AxisDirection))throw new InvalidOperationException("Define the joint axis first.");
                if(!projectOntoAxis&&DistanceToAxis(cadPoint,o.AxisPoint,o.AxisDirection)>AxisTolerance)throw new InvalidOperationException("Origin is more than 1 µm away from the joint axis. Choose an on-axis point or use CAD point projection.");
                target=Project(cadPoint,o.AxisPoint,o.AxisDirection,o.OffsetMetres);
            }else{o.OffsetMetres=0;}
            o.SourcePoint=(double[])cadPoint.Clone();o.Mode=projectOntoAxis?"cad-point":"numeric";o.ReferenceValid=true;o.JointId=l.Id==p.RootLink?"":Joint(p,l).Id;
            l.ZeroFrame[3]=target[0];l.ZeroFrame[7]=target[1];l.ZeroFrame[11]=target[2];Invalidate(p,l);
        }
        public static void SetOffset(RobotProject p,LinkRecord l,double offset)
        {
            if(!Moving(p,l)||!VectorValid(l.Origin.SourcePoint))throw new InvalidOperationException("Choose a joint axis and origin point before adjusting its offset.");
            if(!Math3.Finite(offset))throw new ArgumentException("Offset must be finite.");
            var target=Project(l.Origin.SourcePoint,l.Origin.AxisPoint,l.Origin.AxisDirection,offset);
            l.Origin.OffsetMetres=offset;l.ZeroFrame[3]=target[0];l.ZeroFrame[7]=target[1];l.ZeroFrame[11]=target[2];Invalidate(p,l);
        }
        public static string Error(RobotProject p,LinkRecord l,bool requireConfirmed=true)
        {
            var o=l.Origin;if(o==null||o.Mode=="draft")return "Define and confirm this link's origin first.";
            if(o.Mode!="numeric"&&o.Mode!="cad-point")return "Unsupported origin definition.";
            if(!Math3.Rigid(l.ZeroFrame)||!VectorValid(o.SourcePoint)||!Math3.Finite(o.OffsetMetres))return "Origin definition contains invalid coordinates.";
            if(!o.ReferenceValid)return "The origin's CAD reference changed or disappeared. Select it again.";
            if(o.Mode=="cad-point"&&(string.IsNullOrEmpty(o.PointReference)||!VectorValid(o.LocalPoint)))return "Select the CAD vertex, sketch point, or reference point again.";
            var j=Joint(p,l);if(l.Id!=p.RootLink&&(j==null||o.JointId!=j.Id))return "Define the origin for this link's current parent joint.";
            double[] expected=o.SourcePoint;
            if(Moving(p,l)){
                if(!j.ReferenceValid)return "The joint axis reference needs review.";
                if(!VectorValid(o.AxisPoint)||!VectorValid(o.AxisDirection)||Math.Abs(Math.Sqrt(Math3.Dot(o.AxisDirection,o.AxisDirection))-1)>1e-8)return "Define a valid joint axis first.";
                if(DistanceToAxis(Position(l),o.AxisPoint,o.AxisDirection)>AxisTolerance)return "The link origin must lie on its joint axis (within 1 µm).";
                if(!VectorValid(j.Axis))return "Define a valid joint direction first.";
                var physical=Math3.Vector(l.ZeroFrame,j.Axis);
                if(Math.Abs(Math3.Dot(Math3.Normalize(physical),o.AxisDirection))<1-1e-8)return "The joint direction no longer matches the origin's axis. Define the axis again.";
                if(o.Mode=="numeric"&&DistanceToAxis(o.SourcePoint,o.AxisPoint,o.AxisDirection)>AxisTolerance)return "The numeric origin must lie on its joint axis.";
                expected=Project(o.SourcePoint,o.AxisPoint,o.AxisDirection,o.OffsetMetres);
            }else if(o.OffsetMetres!=0)return "Root and fixed-joint origins do not use an axial offset.";
            var delta=Math3.Sub(expected,Position(l));if(Math.Sqrt(Math3.Dot(delta,delta))>AxisTolerance)return "Origin position changed. Define and confirm it again.";
            if(requireConfirmed&&!o.Confirmed)return "Confirm this link's origin to calculate CoG and inertia.";
            return null;
        }
        public static void Confirm(RobotProject p,LinkRecord l) { string error=Error(p,l,false);if(error!=null)throw new InvalidOperationException(error);l.Origin.Confirmed=true; }
        public static PhysicalProperties Properties(RobotProject p,LinkRecord l) { string error=Error(p,l);if(error!=null)throw new InvalidOperationException(error);return Math3.LinkProperties(p,l); }
    }
}
