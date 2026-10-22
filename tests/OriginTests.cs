using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using CadUrdfMaker.Core;

public static class OriginTests
{
    static int passed;
    static void Near(double a,double b,double tolerance=1e-10){if(!Math3.Finite(a)||Math.Abs(a-b)>tolerance)throw new Exception("Expected "+b+", got "+a);}
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){bool failed=false;try{action();}catch(ArgumentException){failed=true;}catch(InvalidOperationException){failed=true;}catch(InvalidDataException){failed=true;}Assert(failed,"Invalid origin operation was accepted");}
    static void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    static PhysicalProperties Physics(){return new PhysicalProperties{Mass=12,Cog=new[]{.1,.2,.3},Inertia=new[]{13.0,0,0,0,10,0,0,0,5}};}
    static RobotProject Robot()
    {
        var p=new RobotProject{Name="origins",RootLink="root",ZeroCaptured=true,Stale=false};
        p.Components.Add(new ComponentRecord{Id="root_part",Name="root",Resolved=true,MaterialKnown=true,Cad=Physics()});
        p.Components.Add(new ComponentRecord{Id="child_part",Name="child",Resolved=true,MaterialKnown=true,Cad=Physics()});
        p.Links.Add(new LinkRecord{Id="root",Name="base",Components=new List<string>{"root_part"}});
        p.Links.Add(new LinkRecord{Id="child",Name="child",Components=new List<string>{"child_part"}});
        p.Joints.Add(new JointRecord{Id="joint",Name="joint",Parent="root",Child="child",Effort=10,Velocity=2});
        return p;
    }
    static void Ready(RobotProject p)
    {
        OriginWorkflow.SetPosition(p,p.Links[0],new double[3],false);OriginWorkflow.Confirm(p,p.Links[0]);
        var child=p.Links[1];OriginWorkflow.SetAxis(p,child,new double[3],new[]{0.0,0,1});OriginWorkflow.SetPosition(p,child,new double[3],false);OriginWorkflow.Confirm(p,child);p.Joints[0].Confirmed=true;
    }
    public static int Main(string[] args)
    {
        try{
            Test("origin projection uses the selected axis and signed offset",()=>{var point=OriginWorkflow.Project(new[]{2.0,4,8},new[]{1.0,2,3},new[]{0.0,0,2},-.5);Near(point[0],1);Near(point[1],2);Near(point[2],7.5);Near(OriginWorkflow.DistanceToAxis(point,new[]{1.0,2,3},new[]{0.0,0,1}),0);Reject(()=>OriginWorkflow.Project(point,new double[3],new double[3]));Reject(()=>OriginWorkflow.Project(new[]{double.NaN,0,0},new double[3],new[]{0.0,0,1}));});
            Test("draft origins gate link physics and export while mass remains available",()=>{var p=Robot();Reject(()=>OriginWorkflow.Properties(p,p.Links[1]));Near(Math3.Effective(p.Components[1]).Mass,12);Assert(Validation.Check(p).Any(d=>d.Code=="origin"),"Draft origin not reported");string path=Path.Combine(args[0],"draft-rejected.urdf");Reject(()=>UrdfExport.WriteUrdf(path,p));Assert(!File.Exists(path),"Draft export wrote a URDF");});
            Test("numeric origins enforce the 1 micron axis tolerance atomically",()=>{var p=Robot();var l=p.Links[1];OriginWorkflow.SetAxis(p,l,new double[3],new[]{0.0,0,1});var before=(double[])l.ZeroFrame.Clone();Reject(()=>OriginWorkflow.SetPosition(p,l,new[]{.0000011,0,.2},false));Assert(l.ZeroFrame.SequenceEqual(before),"Rejected point moved the origin");OriginWorkflow.SetPosition(p,l,new[]{.0000009,0,.2},false);OriginWorkflow.Confirm(p,l);Near(l.ZeroFrame[3],0);Near(l.ZeroFrame[11],.2);});
            Test("CAD point projection must be confirmed and retains its reference",()=>{var p=Robot();var l=p.Links[1];OriginWorkflow.SetAxis(p,l,new double[3],new[]{0.0,0,1});OriginWorkflow.SetPosition(p,l,new[]{.3,.4,.2},true);Reject(()=>OriginWorkflow.Confirm(p,l));l.Origin.PointReference="native-point";l.Origin.PointComponent="child_part";l.Origin.LocalPoint=new[]{.3,.4,.2};OriginWorkflow.Confirm(p,l);Near(l.ZeroFrame[3],0);Near(l.ZeroFrame[11],.2);Near(OriginWorkflow.Properties(p,l).Cog[2],.1);});
            Test("origin translation changes local CoG and preserves inertia about CoG",()=>{var p=Robot();Ready(p);var l=p.Links[1];var before=OriginWorkflow.Properties(p,l);OriginWorkflow.SetOffset(p,l,.5);Reject(()=>OriginWorkflow.Properties(p,l));Assert(!p.Joints[0].Confirmed,"Joint review was not invalidated");OriginWorkflow.Confirm(p,l);var after=OriginWorkflow.Properties(p,l);Near(after.Cog[2],before.Cog[2]-.5);Near(after.Mass,before.Mass);for(int i=0;i<9;i++)Near(after.Inertia[i],before.Inertia[i]);Assert(!p.Stale,"Origin edit invalidated CAD capture");});
            Test("grouping and physical overrides recalculate after origin confirmation",()=>{var p=Robot();Ready(p);var l=p.Links[1];p.Components[1].Override.Mode="complete";p.Components[1].Override.Complete=Physics();p.Components[1].Override.Complete.Mass=6;Near(OriginWorkflow.Properties(p,l).Mass,6);l.Components.Add("root_part");Near(OriginWorkflow.Properties(p,l).Mass,18);Assert(l.Origin.Confirmed,"Grouping edit invalidated the independent origin");});
            Test("invalid point or axis references hide inertial data",()=>{var p=Robot();Ready(p);var l=p.Links[1];l.Origin.ReferenceValid=false;Reject(()=>OriginWorkflow.Properties(p,l));Assert(Validation.Check(p).Any(d=>d.Code=="origin"),"Bad point reference accepted");l.Origin.ReferenceValid=true;p.Joints[0].ReferenceValid=false;Reject(()=>OriginWorkflow.Properties(p,l));});
            Test("joint origins and local mesh coordinates reconstruct the captured geometry",()=>{var p=Robot();Ready(p);var l=p.Links[1];OriginWorkflow.SetOffset(p,l,.25);OriginWorkflow.Confirm(p,l);p.Joints[0].Confirmed=true;string target=Path.Combine(args[0],"origin-export-"+Guid.NewGuid().ToString("N"));var triangle=new[]{0.0,0,0,1,0,0,0,1,0};UrdfExport.Package(target,p,new Dictionary<string,double[]>{{"root_part",triangle},{"child_part",triangle}});var xml=new XmlDocument();xml.Load(Path.Combine(target,p.Name+".urdf"));var origin=xml.SelectSingleNode("/robot/joint/origin");Near(double.Parse(origin.Attributes["xyz"].Value.Split(' ')[2],System.Globalization.CultureInfo.InvariantCulture),.25);var mesh=MeshIO.ReadStl(Path.Combine(target,"meshes/child.stl"));Near(mesh[2],-.25);Near(mesh[2]+.25,0);});
            Test("root and fixed joints accept origins without a moving axis",()=>{var p=Robot();OriginWorkflow.SetPosition(p,p.Links[0],new[]{1.0,2,3},false);OriginWorkflow.Confirm(p,p.Links[0]);p.Joints[0].Type="fixed";OriginWorkflow.SetPosition(p,p.Links[1],new[]{4.0,5,6},false);OriginWorkflow.Confirm(p,p.Links[1]);Assert(OriginWorkflow.Error(p,p.Links[1])==null,"Fixed origin requires an axis");Reject(()=>OriginWorkflow.SetAxis(p,p.Links[1],new double[3],new[]{0.0,0,1}));});
            Test("missing origin metadata keeps positions and grouping as drafts",()=>{var p=Robot();Ready(p);p.Links[1].ZeroFrame[11]=.7;var data=ProjectIO.Serializer().Deserialize<Dictionary<string,object>>(ProjectIO.Json(p));var links=ProjectIO.Serializer().Deserialize<List<Dictionary<string,object>>>(ProjectIO.Json(data["Links"]));foreach(var link in links){link.Remove("Origin");link.Remove("ShowFrame");}data["Links"]=links;var migrated=ProjectIO.Parse(ProjectIO.Json(data));Near(migrated.Links[1].ZeroFrame[11],.7);Assert(!migrated.Links[1].Origin.Confirmed&&migrated.Links[1].Origin.Mode=="draft","Old origin was silently trusted");Assert(migrated.Links[1].Components[0]=="child_part"&&migrated.Joints[0].Effort==10,"Existing setup was lost");});
            Test("origin references, visibility and confirmations persist independently",()=>{var p=Robot();Ready(p);var l=p.Links[1];string physics=ProjectIO.Json(OriginWorkflow.Properties(p,l));l.ShowFrame=true;Assert(ProjectIO.Json(OriginWorkflow.Properties(p,l))==physics,"Visibility changed physics");var copy=ProjectIO.Parse(ProjectIO.Json(p));Assert(copy.Links[1].ShowFrame&&copy.Links[1].Origin.Confirmed&&!copy.Links[0].ShowFrame,"Per-link state was lost");copy.Links[1].Origin.JointId="different";Reject(()=>OriginWorkflow.Properties(copy,copy.Links[1]));});
            Console.WriteLine(passed+" origin workflow tests passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
    }
}
