using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using CadUrdfMaker.Core;

public static class CoreTests
{
    static int passed;
    static void ConfirmOrigins(RobotProject p)
    {
        foreach(var l in p.Links){var j=p.Joints.FirstOrDefault(x=>x.Child==l.Id);l.Origin=new OriginDefinition{Mode="numeric",SourcePoint=OriginWorkflow.Position(l),JointId=j==null?"":j.Id};if(l.Id!=p.RootLink&&j!=null&&j.Type!="fixed"){l.Origin.AxisPoint=OriginWorkflow.Position(l);l.Origin.AxisDirection=Math3.Normalize(Math3.Vector(l.ZeroFrame,j.Axis));}OriginWorkflow.Confirm(p,l);}
    }
    static void Near(double actual,double expected,double tolerance=1e-10){if(double.IsNaN(actual)||Math.Abs(actual-expected)>tolerance)throw new Exception("Expected "+expected+", got "+actual);}
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static PhysicalProperties Box(double mass,double x,double y,double z,double[] cog=null){return new PhysicalProperties{Mass=mass,Cog=cog??new double[3],Inertia=new[]{mass*(y*y+z*z)/12,0,0,0,mass*(x*x+z*z)/12,0,0,0,mass*(x*x+y*y)/12}};}
    static void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    static RobotProject Single()
    {
        var c=new ComponentRecord{Id="component",Name="box",Resolved=true,MaterialKnown=true,Cad=Box(12,1,2,3),UnitDensity=Box(6,1,2,3)};
        var l=new LinkRecord{Id="base",Name="base_link",Components=new List<string>{c.Id}};
        l.Origin=new OriginDefinition{Mode="numeric",Confirmed=true,SourcePoint=new double[3]};
        return new RobotProject{Name="test_robot",ZeroCaptured=true,Stale=false,Components=new List<ComponentRecord>{c},Links=new List<LinkRecord>{l},RootLink=l.Id};
    }
    public static int Main(string[] args)
    {
        try{
            Test("analytic rectangular box",()=>{var p=Box(12,1,2,3);Near(p.Inertia[0],13);Near(p.Inertia[4],10);Near(p.Inertia[8],5);Assert(Validation.PhysicsError(p)==null,"Valid box rejected");});
            Test("analytic cylinder",()=>{double m=3,r=.2,h=.7;var p=new PhysicalProperties{Mass=m,Inertia=new[]{m*(3*r*r+h*h)/12,0,0,0,m*(3*r*r+h*h)/12,0,0,0,m*r*r/2}};Assert(Validation.PhysicsError(p)==null,"Valid cylinder rejected");Near(p.Inertia[8],.06);});
            Test("parallel-axis theorem and repeated instances",()=>{var a=Box(2,1,1,1,new[]{-1.0,0,0});var b=Box(2,1,1,1,new[]{1.0,0,0});var p=Math3.Combine(new[]{a,b});Near(p.Mass,4);Near(p.Cog[0],0);Near(p.Inertia[0],2.0/3);Near(p.Inertia[4],4+2.0/3);Near(p.Inertia[8],4+2.0/3);});
            Test("mass-weighted mixed-density CoG",()=>{var a=Box(1,1,1,1);var b=Box(3,1,1,1,new[]{2.0,0,0});var p=Math3.Combine(new[]{a,b});Near(p.Cog[0],1.5);Near(p.Inertia[4],2.0/3+3);});
            Test("rotation and off-diagonal signs",()=>{double c=Math.Sqrt(.5);var t=new[]{c,-c,0,2,c,c,0,3,0,0,1,4,0,0,0,1};var p=Math3.Transform(Box(12,1,2,3),t);Near(p.Cog[0],2);Near(p.Inertia[0],11.5);Near(p.Inertia[1],1.5);Near(p.Inertia[3],1.5);Near(p.Inertia[4],11.5);var local=Math3.Transform(p,Math3.Inverse(t));Near(local.Inertia[0],13);Near(local.Inertia[1],0);Near(local.Cog[2],0);});
            Test("eigenvalues and physical validity",()=>{var e=Math3.Eigenvalues(new[]{2.0,1,0,1,3,0,0,0,4});Near(e[0],(5-Math.Sqrt(5))/2);Near(e[1],(5+Math.Sqrt(5))/2);Near(e[2],4);var p=Box(1,1,1,1);p.Inertia[8]=9;Assert(Validation.PhysicsError(p)!=null,"Nonphysical inertia accepted");p.Inertia[8]=-1;Assert(Validation.PhysicsError(p)!=null,"Negative inertia accepted");});
            Test("rigid frame validation and reflection rejection",()=>{Assert(Math3.Rigid(Math3.Identity()),"Identity rejected");var t=Math3.Identity();t[0]=-1;Assert(!Math3.Rigid(t),"Mirror accepted");t=Math3.Identity();t[0]=2;Assert(!Math3.Rigid(t),"Scale accepted");});
            Test("density and complete override precedence",()=>{var c=Single().Components[0];c.Override.Mode="density";c.Override.Density=2;Near(Math3.Effective(c).Mass,12);Near(Math3.Effective(c).Inertia[0],13);c.Override.Mode="complete";c.Override.Complete=Box(20,1,2,3);Near(Math3.Effective(c).Mass,20);});
            Test("tree, limits, material, stale and reference checks",()=>{var p=Single();Assert(Validation.Check(p).Count==0,"Single link rejected");p.Components[0].MaterialKnown=false;Assert(Validation.Check(p).Any(d=>d.Code=="material"),"Missing material accepted");p.Components[0].MaterialAcknowledged=true;Assert(Validation.Check(p).Count==0,"Acknowledgment rejected");p.Stale=true;Assert(Validation.Check(p).Any(d=>d.Code=="stale"),"Stale accepted");p.Stale=false;p.Components[0].Resolved=false;Assert(Validation.Check(p).Any(d=>d.Code=="unresolved"),"Missing ref accepted");});
            Test("duplicate assignment and disconnected robot",()=>{var p=Single();p.Links.Add(new LinkRecord{Id="other",Name="other",Components=new List<string>{"component"}});Assert(Validation.Check(p).Any(d=>d.Code=="membership"),"Duplicate membership accepted");Assert(Validation.Check(p).Any(d=>d.Code=="disconnected"),"Disconnected tree accepted");});
            Test("joint cycle and invalid effort",()=>{var p=Single();var c=new ComponentRecord{Id="second",Name="second",Resolved=true,MaterialKnown=true,Cad=Box(1,1,1,1)};p.Components.Add(c);p.Links.Add(new LinkRecord{Id="child",Name="child_link",Components=new List<string>{c.Id}});var j=new JointRecord{Id="joint",Name="hinge",Parent="base",Child="child",Confirmed=true};p.Joints.Add(j);Assert(Validation.Check(p).Any(d=>d.Code=="limits"),"Missing effort accepted");j.Effort=3;j.Velocity=2;ConfirmOrigins(p);Assert(Validation.Check(p).Count==0,"Valid revolute rejected");j.ReferenceValid=false;Assert(Validation.Check(p).Any(d=>d.Code=="reference"),"Changed geometry accepted");j.ReferenceValid=true;p.Joints.Add(new JointRecord{Id="back",Name="back",Type="fixed",Parent="child",Child="base",Confirmed=true});Assert(Validation.Check(p).Any(d=>d.Code=="cycle"),"Loop accepted");});
            string temp=Path.Combine(args.Length>0?args[0]:Path.GetTempPath(),"cad-urdf-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            Test("robot and joint rename persist and export matching filenames without changing frames",()=>{
                var p=Single();p.Components.Add(new ComponentRecord{Id="second",Name="second",Resolved=true,MaterialKnown=true,Cad=Box(2,1,2,3)});
                p.Links.Add(new LinkRecord{Id="child",Name="child_link",Components=new List<string>{"second"}});p.Joints.Add(new JointRecord{Id="j",Name="joint_1",Parent="base",Child="child",Type="fixed",Confirmed=true});ConfirmOrigins(p);
                string linksBefore=ProjectIO.Json(p.Links);p.Name="example_robot";p.Joints[0].Name="example_joint";
                string saved=Path.Combine(temp,"named_project.urdfmaker.json");ProjectIO.Save(saved,p);p=ProjectIO.Load(saved);
                Assert(p.Name=="example_robot"&&p.Joints[0].Name=="example_joint"&&p.Joints[0].Id=="j","Names or joint identity lost on reopen");
                Assert(ProjectIO.Json(p.Links)==linksBefore&&p.Joints[0].Confirmed,"Renaming changed origins or confirmations");
                string target=Path.Combine(temp,"named_robot");var triangle=new[]{0.0,0,0,1,0,0,0,1,0};var meshes=new Dictionary<string,double[]>{{"component",triangle},{"second",triangle}};
                UrdfExport.Package(target,p,meshes);Assert(File.Exists(Path.Combine(target,"example_robot.urdf"))&&!File.Exists(Path.Combine(target,"robot.urdf")),"Export name did not match robot");
                var xml=new XmlDocument();xml.Load(Path.Combine(target,"example_robot.urdf"));Assert(xml.DocumentElement.GetAttribute("name")=="example_robot"&&xml.SelectSingleNode("/robot/joint").Attributes["name"].Value=="example_joint","URDF names differ from saved names");
                p.Name="example_v2";UrdfExport.Package(target,p,meshes);Assert(File.Exists(Path.Combine(target,"example_v2.urdf"))&&!File.Exists(Path.Combine(target,"example_robot.urdf")),"Replacement retained a stale URDF filename");
                var backup=Directory.GetDirectories(temp,"named_robot.previous-*").Single();Assert(File.Exists(Path.Combine(backup,"example_robot.urdf")),"Renamed export lost recoverable old file");
            });
            Test("robot filenames reject unsafe and Windows-reserved names before export",()=>{
                var p=Single();foreach(var name in new[]{"","../robot","my robot","1robot","CON","prn","AUX","NUL","COM1","lpt9"}){
                    p.Name=name;Assert(Validation.Check(p).Any(d=>d.Code=="name"),"Unsafe robot name accepted: "+name);bool rejected=false;try{UrdfExport.FileName(p);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Unsafe export filename accepted");
                }
                p.Name="robot";Assert(UrdfExport.FileName(p)=="robot.urdf","Default filename changed unexpectedly");
            });
            Test("selected unassigned removal keeps other unassigned components blocking export",()=>{
                var p=Single();p.Components.Add(new ComponentRecord{Id="unused_a",Name="unused_a"});p.Components.Add(new ComponentRecord{Id="unused_b",Name="unused_b"});
                Assert(ComponentScope.SetRemoved(p,new[]{"unused_a"},true)==1,"Removal count incorrect");
                var diagnostics=Validation.Check(p);Assert(!diagnostics.Any(d=>d.Target=="unused_a"),"Removed component still blocks export");
                Assert(diagnostics.Any(d=>d.Target=="unused_b"&&d.Code=="membership"),"Remaining unassigned component accepted");
                string target=Path.Combine(temp,"blocked_subset");bool blocked=false;try{UrdfExport.Package(target,p,new Dictionary<string,double[]>());}catch(InvalidDataException){blocked=true;}
                Assert(blocked&&!Directory.Exists(target),"Export wrote a package with an unassigned component");
            });
            Test("component removal rejects mixed assigned or missing selections atomically",()=>{
                var p=Single();p.Components.Add(new ComponentRecord{Id="unused",Name="unused"});string before=ProjectIO.Json(p);
                foreach(var ids in new[]{new[]{"unused","component"},new[]{"unused","missing"},new string[0]}){
                    bool rejected=false;try{ComponentScope.SetRemoved(p,ids,true);}catch(InvalidOperationException){rejected=true;}
                    Assert(rejected&&ProjectIO.Json(p)==before,"Invalid batch partially changed component scope");
                }
                Assert(ComponentScope.SetRemoved(p,new[]{"unused","unused"},true)==1,"Duplicate selection double counted");
                Assert(ComponentScope.SetRemoved(p,new[]{"unused"},true)==0,"Repeated removal was not idempotent");
            });
            Test("subset export preserves link physics and omits removed component meshes",()=>{
                var p=Single();p.Components.Add(new ComponentRecord{Id="unused",Name="unused",Issue="Unresolved optional hardware"});
                string original=ProjectIO.Json(p.Components[0]),origin=ProjectIO.Json(p.Links[0].Origin),physics=ProjectIO.Json(OriginWorkflow.Properties(p,p.Links[0]));
                ComponentScope.SetRemoved(p,new[]{"unused"},true);Assert(Validation.Check(p).Count==0,"Removed unresolved hardware blocks valid subset");
                Assert(ProjectIO.Json(OriginWorkflow.Properties(p,p.Links[0]))==physics,"Removal changed assigned physics");
                Assert(ProjectIO.Json(p.Components[0])==original&&ProjectIO.Json(p.Links[0].Origin)==origin,"Removal changed assigned CAD or origin data");
                string target=Path.Combine(temp,"valid_subset");UrdfExport.Package(target,p,new Dictionary<string,double[]>{{"component",new[]{0.0,0,0,1,0,0,0,1,0}}});
                Assert(Directory.GetFiles(Path.Combine(target,"meshes"),"*.stl").Length==1,"Removed components received meshes");
                var xml=new XmlDocument();xml.Load(Path.Combine(target,p.Name+".urdf"));Assert(xml.SelectNodes("/robot/link").Count==1,"Subset export added unwanted links");
            });
            Test("removed component records persist and restore as unassigned",()=>{
                var p=Single();var extra=new ComponentRecord{Id="unused",Name="unused",Cad=Box(4,1,2,3),Resolved=true,MaterialKnown=true};extra.ZeroTransform[3]=.123;p.Components.Add(extra);
                string data=ProjectIO.Json(extra.Cad);ComponentScope.SetRemoved(p,new[]{extra.Id},true);
                string path=Path.Combine(temp,"subset.urdfmaker.json");ProjectIO.Save(path,p);var reopened=ProjectIO.Load(path);
                Assert(reopened.Components.Single(c=>c.Id==extra.Id).Excluded,"Removal lost on reopen");
                ComponentScope.SetRemoved(reopened,new[]{extra.Id},false);var restored=reopened.Components.Single(c=>c.Id==extra.Id);
                Assert(!restored.Excluded&&ProjectIO.Json(restored.Cad)==data&&restored.ZeroTransform[3]==.123,"Restore changed captured properties or placement");
                Assert(Validation.Check(reopened).Any(d=>d.Code=="membership"&&d.Target==extra.Name),"Restored unassigned component did not block export");
            });
            Test("project save/reopen preserves overrides and zero pose",()=>{var p=Single();p.Components[0].Override.Mode="complete";p.Components[0].Override.Complete=Box(3,1,1,1);p.Components[0].Override.Note="measured";p.Components[0].ZeroTransform[3]=.25;string path=Path.Combine(temp,"project.urdfmaker.json");ProjectIO.Save(path,p);var reopened=ProjectIO.Load(path);Near(reopened.Components[0].ZeroTransform[3],.25);Assert(reopened.Components[0].Override.Note=="measured","Provenance lost");ProjectIO.Save(path,p);});
            Test("Z-up alignment rotates exported root mesh, CoG and inertia",()=>{
                var p=Single();var root=p.Links[0];root.ZeroFrame=Math3.FromRpy(new[]{0.0,0,.7});root.ZeroFrame[3]=2;root.ZeroFrame[7]=3;root.ZeroFrame[11]=4;
                var c=p.Components[0];c.ZeroTransform[3]=2;c.ZeroTransform[7]=3;c.ZeroTransform[11]=4;c.Cad.Cog=new[]{.1,.2,.3};
                string originalCad=ProjectIO.Json(c),originalFrame=ProjectIO.Json(root.ZeroFrame);
                p.Alignment=new AlignmentSettings{Enabled=true,Rpy=new[]{Math.PI/2,0.0,0}};
                var physics=Math3.LinkProperties(p,root);Near(physics.Mass,12);Near(physics.Cog[0],.1);Near(physics.Cog[1],-.3);Near(physics.Cog[2],.2);Near(physics.Inertia[0],13);Near(physics.Inertia[4],5);Near(physics.Inertia[8],10);
                string target=Path.Combine(temp,"aligned_single");ConfirmOrigins(p);UrdfExport.Package(target,p,new Dictionary<string,double[]>{{"component",new[]{0.0,1,0,1,0,0,0,0,1}}});
                var vertices=MeshIO.ReadStl(Path.Combine(target,"meshes/base_link.stl"));Near(vertices[0],0);Near(vertices[1],0,1e-7);Near(vertices[2],1,1e-7);
                var xml=new XmlDocument();xml.Load(Path.Combine(target,p.Name+".urdf"));Assert(xml.SelectNodes("/robot/link").Count==1&&xml.SelectNodes("/robot/joint").Count==0,"Alignment added an artificial world link");
                var inertia=xml.SelectSingleNode("/robot/link/inertial/inertia");Near(double.Parse(inertia.Attributes["iyy"].Value,System.Globalization.CultureInfo.InvariantCulture),5);
                var worldRoot=Math3.Multiply(Math3.PreviewAlignment(p),Math3.LinkFrame(p,root));for(int i=0;i<16;i++)Near(worldRoot[i],Math3.Identity()[i]);
                Assert(ProjectIO.Json(c)==originalCad&&ProjectIO.Json(root.ZeroFrame)==originalFrame,"Alignment changed native snapshots");Assert(!p.Stale,"Alignment forced CAD refresh");
            });
            Test("aligned child joint and STL reconstruct the same Cartesian geometry",()=>{
                var p=Single();p.ChildFramesFollowRoot=false;p.Alignment.Enabled=true;p.Alignment.Rpy=new[]{Math.PI/2,0.0,0};p.Links[0].ZeroFrame[7]=2;p.Components[0].ZeroTransform[7]=2;
                var c=new ComponentRecord{Id="second",Name="second",Resolved=true,MaterialKnown=true,Cad=Box(2,1,2,3)};c.ZeroTransform[7]=3;p.Components.Add(c);
                var child=new LinkRecord{Id="child",Name="child_link",Components=new List<string>{c.Id}};child.ZeroFrame[7]=3;p.Links.Add(child);
                p.Joints.Add(new JointRecord{Id="j",Name="hinge",Parent="base",Child="child",Axis=new[]{0.0,0,1},Confirmed=true,Effort=10,Velocity=2});
                string target=Path.Combine(temp,"aligned_child");var triangle=new[]{0.0,0,.3,1,0,.3,0,1,.3};ConfirmOrigins(p);UrdfExport.Package(target,p,new Dictionary<string,double[]>{{"component",triangle},{"second",triangle}});
                var xml=new XmlDocument();xml.Load(Path.Combine(target,p.Name+".urdf"));var origin=xml.SelectSingleNode("/robot/joint/origin");Func<string,double[]> parse=s=>s.Split(' ').Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var t=Math3.FromRpy(parse(origin.Attributes["rpy"].Value));var xyz=parse(origin.Attributes["xyz"].Value);t[3]=xyz[0];t[7]=xyz[1];t[11]=xyz[2];Near(t[3],0);Near(t[7],0);Near(t[11],1);
                var stl=MeshIO.ReadStl(Path.Combine(target,"meshes/child_link.stl"));var point=Math3.Point(t,stl.Take(3).ToArray());Near(point[0],0,1e-7);Near(point[1],-.3,1e-7);Near(point[2],1,1e-7);
                var axis=Math3.Vector(t,new[]{0.0,0,1});Near(axis[0],0);Near(axis[1],-1);Near(axis[2],0);Near(Math3.LinkProperties(p,child).Inertia[0],13.0/6);
                Assert(p.Joints[0].Confirmed&&p.Joints[0].Axis[2]==1,"Physical joint definition changed");
            });
            Test("alignment persists, accepts omitted optional fields, validates angles and resets",()=>{
                var p=Single();string minimal=ProjectIO.Json(p);var dictionary=ProjectIO.Serializer().Deserialize<Dictionary<string,object>>(minimal);dictionary.Remove("Alignment");Assert(!ProjectIO.Parse(ProjectIO.Json(dictionary)).Alignment.Enabled,"Minimal project alignment changed");
                p.Alignment.Enabled=true;p.Alignment.Rpy=new[]{.31,-.47,.82};var before=Math3.LinkProperties(p,p.Links[0]);var values=Math3.Eigenvalues(before.Inertia);Near(before.Mass,12);Near(values[0],5);Near(values[1],10);Near(values[2],13);
                string path=Path.Combine(temp,"alignment.urdfmaker.json");ProjectIO.Save(path,p);var loaded=ProjectIO.Load(path);Assert(loaded.Alignment.Enabled,"Alignment was lost");Near(loaded.Alignment.Rpy[1],-.47);var refreshCopy=ProjectIO.Parse(ProjectIO.Json(loaded));Near(refreshCopy.Alignment.Rpy[2],.82);
                loaded.Alignment.Enabled=false;Near(Math3.LinkProperties(loaded,loaded.Links[0]).Inertia[4],10);loaded.Alignment.Rpy=new[]{0.0,0};Assert(Validation.Check(loaded).Any(d=>d.Code=="alignment"),"Malformed alignment accepted");
            });
            Test("child root orientation fixes cyclic CoG axes and exports physical joint axis",()=>{
                var p=Single();var cyclic=new[]{0.0,0,1,0,1,0,0,0,0,1,0,0,0,0,0,1};
                var c=new ComponentRecord{Id="hip_part",Name="hip_part",Resolved=true,MaterialKnown=true,Cad=Box(12,1,2,3,new[]{.0715834,-.05777685,-.04149639}),ZeroTransform=cyclic};p.Components.Add(c);
                var child=new LinkRecord{Id="hip",Name="hip_link",Components=new List<string>{c.Id},ZeroFrame=(double[])cyclic.Clone()};p.Links.Add(child);
                var j=new JointRecord{Id="hip_joint",Name="hip_joint",Parent="base",Child="hip",Axis=new[]{0.0,0,1},Confirmed=true,Effort=10,Velocity=3};p.Joints.Add(j);
                var props=Math3.LinkProperties(p,child);Near(props.Cog[0],-.04149639);Near(props.Cog[1],.0715834);Near(props.Cog[2],-.05777685);Near(props.Inertia[0],5);Near(props.Inertia[4],13);Near(props.Inertia[8],10);
                var axis=Math3.JointAxis(p,j);Near(axis[0],1);Near(axis[1],0);Near(axis[2],0);Near(j.Axis[2],1);
                var triangle=new[]{1.0,0,0,0,1,0,0,0,1};string path=Path.Combine(temp,"root_oriented");ConfirmOrigins(p);UrdfExport.Package(path,p,new Dictionary<string,double[]>{{"component",triangle},{c.Id,triangle}});
                var xml=new XmlDocument();xml.Load(Path.Combine(path,p.Name+".urdf"));Assert(xml.SelectSingleNode("/robot/joint/origin").Attributes["rpy"].Value=="0 0 0","Nonzero joint frame rotation exported");Assert(xml.SelectSingleNode("/robot/joint/axis").Attributes["xyz"].Value=="1 0 0","Physical joint axis changed");
                var vertices=MeshIO.ReadStl(Path.Combine(path,"meshes/hip_link.stl"));Near(vertices[0],0);Near(vertices[1],1);Near(vertices[2],0);Assert(j.Confirmed&&j.ReferenceValid&&!p.Stale,"Reframing invalidated unchanged CAD or joint geometry");
                p.ChildFramesFollowRoot=false;var restored=Math3.LinkProperties(p,child);Near(restored.Cog[0],.0715834);Near(restored.Inertia[0],13);Near(Math3.JointAxis(p,j)[2],1);
            });
            Test("all child orientations track the root through alignment and root changes",()=>{
                var p=Single();var root=p.Links[0];root.ZeroFrame=Math3.FromRpy(new[]{.31,-.27,.42});root.ZeroFrame[3]=1.2;root.ZeroFrame[7]=2.3;root.ZeroFrame[11]=3.4;
                var child=new LinkRecord{Id="child",Name="child_link",ZeroFrame=Math3.FromRpy(new[]{-.6,.2,.8})};child.ZeroFrame[3]=4;child.ZeroFrame[7]=5;child.ZeroFrame[11]=6;p.Links.Add(child);
                var grandchild=new LinkRecord{Id="grand",Name="grand_link",ZeroFrame=Math3.FromRpy(new[]{.9,-.4,-.7})};grandchild.ZeroFrame[3]=-1;grandchild.ZeroFrame[7]=-2;grandchild.ZeroFrame[11]=-3;p.Links.Add(grandchild);
                var joint=new JointRecord{Child=child.Id,Axis=Math3.Normalize(new[]{.2,.4,.8})};string raw=ProjectIO.Json(child.ZeroFrame),rawAxis=ProjectIO.Json(joint.Axis);
                for(int mode=0;mode<3;mode++){
                    if(mode==1){p.Alignment.Enabled=true;p.Alignment.Rpy=new[]{-.23,.37,-.61};}if(mode==2)p.RootLink=grandchild.Id;
                    var frame=Math3.LinkFrame(p,p.Links.Single(l=>l.Id==p.RootLink));foreach(var link in p.Links){var generated=Math3.LinkFrame(p,link);foreach(int i in new[]{0,1,2,4,5,6,8,9,10})Near(generated[i],frame[i]);foreach(int i in new[]{3,7,11})Near(generated[i],link.ZeroFrame[i]);}
                    var physical=Math3.Vector(Math3.LinkFrame(p,child),Math3.JointAxis(p,joint));var expected=Math3.Vector(child.ZeroFrame,joint.Axis);for(int i=0;i<3;i++)Near(physical[i],expected[i]);
                }
                Assert(ProjectIO.Json(child.ZeroFrame)==raw&&ProjectIO.Json(joint.Axis)==rawAxis,"Original frame or axis was overwritten");
            });
            Test("root child-frame policy persists and defaults on when the policy is omitted",()=>{
                var p=Single();Assert(p.ChildFramesFollowRoot,"New project does not follow root");var dictionary=ProjectIO.Serializer().Deserialize<Dictionary<string,object>>(ProjectIO.Json(p));dictionary.Remove("ChildFramesFollowRoot");Assert(ProjectIO.Parse(ProjectIO.Json(dictionary)).ChildFramesFollowRoot,"Minimal project did not adopt root axes");
                p.ChildFramesFollowRoot=false;string path=Path.Combine(temp,"frame-policy.urdfmaker.json");ProjectIO.Save(path,p);Assert(!ProjectIO.Load(path).ChildFramesFollowRoot,"Explicit custom-frame policy was lost");p.ChildFramesFollowRoot=true;Assert(ProjectIO.Parse(ProjectIO.Json(p)).ChildFramesFollowRoot,"Refresh clone lost root frame policy");
            });
            Test("URDF, metre STL, deterministic XML and recoverable export",()=>{var p=Single();p.Components[0].ZeroTransform[3]=.1;var mesh=new Dictionary<string,double[]>{{"component",new[]{0.0,0,0,1,0,0,0,1,0}}};string target=Path.Combine(temp,"robot");ConfirmOrigins(p);UrdfExport.Package(target,p,mesh);var xml=new XmlDocument();xml.Load(Path.Combine(target,p.Name+".urdf"));Near(double.Parse(xml.SelectSingleNode("/robot/link/inertial/origin").Attributes["xyz"].Value.Split(' ')[0],System.Globalization.CultureInfo.InvariantCulture),.1);var v=MeshIO.ReadStl(Path.Combine(target,"meshes/base_link.stl"));Near(v[0],.1,1e-7);byte[] first=File.ReadAllBytes(Path.Combine(target,p.Name+".urdf"));ConfirmOrigins(p);UrdfExport.Package(target,p,mesh);Assert(first.SequenceEqual(File.ReadAllBytes(Path.Combine(target,p.Name+".urdf"))),"XML nondeterministic");Assert(Directory.GetDirectories(temp,"robot.previous-*").Length==1,"Previous export lost");});
            Test("export protects unrelated destination",()=>{var p=Single();string target=Path.Combine(temp,"unrelated");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"keep.txt"),"keep");bool rejected=false;try{ConfirmOrigins(p);UrdfExport.Package(target,p,new Dictionary<string,double[]>());}catch(IOException){rejected=true;}Assert(rejected&&File.ReadAllText(Path.Combine(target,"keep.txt"))=="keep","Unrelated data changed");});
            Test("preview reduction retains valid connected triangles",()=>{var v=new List<double>();for(int x=0;x<50;x++)for(int y=0;y<50;y++){v.AddRange(new[]{x*.01,y*.01,0,(x+1)*.01,y*.01,0,x*.01,(y+1)*.01,0});v.AddRange(new[]{(x+1)*.01,y*.01,0,(x+1)*.01,(y+1)*.01,0,x*.01,(y+1)*.01,0});}var reduced=PreviewMesh.Reduce(v.ToArray(),400);Assert(reduced.Length>0&&reduced.Length/9<=400,"Preview budget not met");Assert(reduced.All(Math3.Finite),"Preview has invalid values");});
            Console.WriteLine(passed+" core tests passed. Artifacts: "+temp);return 0;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
