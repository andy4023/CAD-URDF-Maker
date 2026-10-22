using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CadUrdfMaker.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadUrdfMaker.SolidWorks
{
    public sealed class CadSnapshot
    {
        public RobotProject Project { get; set; }
        public Dictionary<string,double[]> Meshes { get; set; }
    }
    public sealed class AxisSelection
    {
        public double[] Point { get; set; }
        public double[] Direction { get; set; }
        public string GeometryReference { get; set; }
        public string ComponentId { get; set; }
        public double[] LocalPoint { get; set; }
        public double[] LocalDirection { get; set; }
    }
    public sealed class PointSelection
    {
        public double[] Point { get; set; }
        public double[] LocalPoint { get; set; }
        public string ComponentId { get; set; }
        public string GeometryReference { get; set; }
    }
    public sealed class CadSession
    {
        readonly ISldWorks app;
        readonly int thread;
        public CadSession(ISldWorks application) { app=application;thread=Thread.CurrentThread.ManagedThreadId; }
        void AssertThread() { if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("SolidWorks access must remain on the owning STA thread."); }
        public static ISldWorks Attach() { return (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        public IModelDoc2 Document()
        {
            AssertThread();var doc=(IModelDoc2)app.ActiveDoc;if(doc==null||doc.GetType()!=2)throw new InvalidOperationException("Open and activate a native SolidWorks assembly first.");
            if(string.IsNullOrEmpty(doc.GetPathName()))throw new InvalidOperationException("Save the assembly before capturing a project.");return doc;
        }
        public object Probe()
        {
            var doc=Document();var components=Flatten(doc).ToList();return new{Title=doc.GetTitle(),Path=doc.GetPathName(),Configuration=doc.ConfigurationManager.ActiveConfiguration.Name,Stamp=doc.GetUpdateStamp(),ComponentCount=components.Count,Components=components.Take(5).Select(c=>new{c.Name2,c.ReferencedConfiguration,Suppressed=c.IsSuppressed()}).ToArray()};
        }
        public object DiagnosePoint()
        {
            // Read the user's existing selection only; never select, edit or save CAD.
            var doc=Document();int stamp=doc.GetUpdateStamp();var selection=(ISelectionMgr)doc.SelectionManager;int count=selection.GetSelectedObjectCount2(-1);
            var types=Enumerable.Range(1,count).Select(i=>((swSelectType_e)selection.GetSelectedObjectType3(i,-1)).ToString()).ToArray();
            var project=new RobotProject{AssemblyPath=doc.GetPathName(),Configuration=doc.ConfigurationManager.ActiveConfiguration.Name};
            PointSelection result=null;string error=null;bool roundTrip=false;
            try{
                if(count==1){var component=(IComponent2)selection.GetSelectedObjectsComponent4(1,-1);if(component!=null)project.Components.Add(new ComponentRecord{Id=Reference(doc,component),ZeroTransform=Transform(component.Transform2)});}
                result=SelectedPoint(project);var restored=ReadPoint(doc,Resolve(doc,result.GeometryReference),result.ComponentId,project);
                roundTrip=Math3.Sub(restored.Point,result.Point).Select(Math.Abs).Max()<1e-10;
                if(!roundTrip)throw new InvalidOperationException("Persistent point reference changed its coordinates.");
            }catch(Exception e){error=e.Message;}
            if(selection.GetSelectedObjectCount2(-1)!=count||doc.GetUpdateStamp()!=stamp)throw new InvalidOperationException("Point diagnostic changed the selection or document state.");
            return new{SelectionCount=count,SelectionTypes=types,Point=result,ReferenceRoundTrip=roundTrip,Error=error,DocumentAndSelectionUnchanged=true};
        }
        public object DiagnoseMass()
        {
            var doc=Document();var component=Flatten(doc).First(c=>!c.IsSuppressed()&&c.GetModelDoc2()!=null);var log=new List<object>();IMassProperty2 mass=null;
            Action<string,Action> step=(name,action)=>{try{action();log.Add(new{name,ok=true});}catch(Exception e){log.Add(new{name,ok=false,error=e.Message});}};
            step("create",()=>mass=(IMassProperty2)doc.Extension.CreateMassProperty2());
            step("selected-items",()=>mass.SelectedItems=new object[]{component});
            step("system-units",()=>mass.UseSystemUnits=true);
            step("accuracy",()=>mass.AccuracyLevel=2);
            step("hidden",()=>mass.IncludeHiddenBodiesOrComponents=true);
            step("coordinate-system",()=>{var utility=(IMathUtility)app.GetMathUtility();mass.SetCoordinateSystem(utility.CreateTransform(new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0}));});
            step("recalculate",()=>mass.Recalculate());
            step("get-mass",()=>log.Add(new{value=mass.Mass}));
            step("get-cog",()=>log.Add(new{cog=Doubles(mass.CenterOfMass)}));
            step("get-inertia",()=>log.Add(new{inertia=Doubles(mass.GetMomentOfInertia(0))}));
            step("get-overrides",()=>mass.GetOverrideOptions());
            step("unit-density",()=>log.Add(new{unit=UnitDensity(component)}));
            step("transform",()=>log.Add(new{transform=Transform(component.Transform2)}));
            return new{component=component.Name2,steps=log};
        }
        public object DiagnoseInertia(string[] names)
        {
            var doc=Document();int stamp=doc.GetUpdateStamp();var selection=(ISelectionMgr)doc.SelectionManager;int count=selection.GetSelectedObjectCount2(-1);var results=new List<object>();
            var components=Flatten(doc).Where(c=>!c.IsSuppressed()&&c.GetModelDoc2()!=null&&(names.Length==0||names.Contains(c.Name2))).ToList();
            foreach(var component in components){var captured=ReadMass(doc,component,Transform(component.Transform2));selection.SuspendSelectionList();try{
                if(!component.Select4(false,null,false))throw new InvalidOperationException("Could not select "+component.Name2);
                var mass=(IMassProperty2)doc.Extension.CreateMassProperty2();mass.UseSystemUnits=true;mass.AccuracyLevel=2;mass.IncludeHiddenBodiesOrComponents=true;
                var utility=(IMathUtility)app.GetMathUtility();if(!mass.SetCoordinateSystem(utility.CreateTransform(new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0}))||!mass.Recalculate())throw new InvalidOperationException("Mass calculation failed.");
                var raw=Doubles(mass.GetMomentOfInertia(0));var moments=Doubles(mass.PrincipalMomentsOfInertia);var axes=new[]{Doubles(mass.get_PrincipalAxesOfInertia(0)),Doubles(mass.get_PrincipalAxesOfInertia(1)),Doubles(mass.get_PrincipalAxesOfInertia(2))};
                var tensor=new double[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)tensor[i*3+j]+=moments[k]*axes[k][i]*axes[k][j];
                var negated=raw.Select((v,i)=>i==0||i==4||i==8?v:-v).ToArray();
                results.Add(new{Component=component.Name2,Mass=mass.Mass,Raw=raw,PrincipalMoments=moments,PrincipalAxes=axes,Reconstructed=tensor,Captured=captured,UnitDensity=UnitDensity(component),Placement=Transform(component.Transform2),RawDifference=raw.Zip(tensor,(a,b)=>Math.Abs(a-b)).Max(),NegatedDifference=negated.Zip(tensor,(a,b)=>Math.Abs(a-b)).Max(),RawEigen= Math3.Eigenvalues(raw),NegatedEigen=Math3.Eigenvalues(negated)});
            }finally{selection.ResumeSelectionList2(false);}}
            if(doc.GetUpdateStamp()!=stamp||selection.GetSelectedObjectCount2(-1)!=count)throw new InvalidOperationException("Inertia diagnostic CAD state changed: stamp "+stamp+" -> "+doc.GetUpdateStamp()+", selection count "+count+" -> "+selection.GetSelectedObjectCount2(-1)+".");
            return new{Results=results,DocumentAndSelectionUnchanged=true};
        }
        public object VerifyInertiaBox()
        {
            var modeler=(IModeler)app.GetModeler();var utility=(IMathUtility)app.GetMathUtility();
            var body=(IBody2)modeler.CreateBodyFromBox(new double[]{0,0,0,0,0,1,.1,.2,.3});if(body==null)throw new InvalidOperationException("Analytic box unavailable.");
            var before=Doubles(body.GetMassProperties(1000));var r=Math3.FromRpy(new[]{.4,.7,-.3});
            var native=(IMathTransform)utility.CreateTransform(new[]{r[0],r[4],r[8],r[1],r[5],r[9],r[2],r[6],r[10],0,0,0,1,0,0,0});
            if(!body.ApplyTransform((MathTransform)native))throw new InvalidOperationException("Could not rotate the temporary box.");
            var after=Doubles(body.GetMassProperties(1000));var expected=Math3.RotateTensor(r,new[]{before[6],0,0,0,before[7],0,0,0,before[8]});
            var positive=new[]{after[6],after[9],after[10],after[9],after[7],after[11],after[10],after[11],after[8]};var negative=positive.Select((v,i)=>i==0||i==4||i==8?v:-v).ToArray();
            return new{Before=before,After=after,Expected=expected,PositiveDifference=positive.Zip(expected,(a,b)=>Math.Abs(a-b)).Max(),NegativeDifference=negative.Zip(expected,(a,b)=>Math.Abs(a-b)).Max()};
        }
        public async Task<object> VerifyMeshAsync(string output)
        {
            var doc=Document();var component=Flatten(doc).Last(c=>!c.IsSuppressed()&&c.GetModelDoc2()!=null);
            var placement=Transform(component.Transform2);var physics=ReadMass(doc,component,placement);var unit=UnitDensity(component);
            var triangles=await TessellateAsync(component,new ExportSettings(),()=>false);MeshIO.WriteStl(output,triangles);
            var min=new[]{double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity};var max=new[]{double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};for(int i=0;i<triangles.Length;i++){int k=i%3;min[k]=Math.Min(min[k],triangles[i]);max[k]=Math.Max(max[k],triangles[i]);}
            for(int i=0;i<3;i++)if(physics.Cog[i]<min[i]-1e-4||physics.Cog[i]>max[i]+1e-4)throw new InvalidOperationException("Native mesh is not in the component-local mass-property coordinate frame.");
            var math=(IMathUtility)app.GetMathUtility();var modeler=(IModeler)app.GetModeler();
            // An asymmetric analytic box validates the native API's SI units and
            // product-of-inertia sign convention without creating a CAD document.
            var box=(IBody2)modeler.CreateBodyFromBox(new double[]{0,0,0,0,0,1,.1,.2,.3});
            if(box==null)throw new InvalidOperationException("Could not create an analytic native test body.");var raw=Doubles(box.GetMassProperties(1000));
            var diagonal=new[]{raw[6],raw[7],raw[8]};Array.Sort(diagonal);
            if(Math.Abs(raw[5]-6)>1e-8||Math.Abs(diagonal[0]-.025)>1e-8||Math.Abs(diagonal[1]-.05)>1e-8||Math.Abs(diagonal[2]-.065)>1e-8)throw new InvalidOperationException("Native geometry mass units/order do not match the tested SI adapter.");
            double cosine=Math.Sqrt(.5);var swRotation=(IMathTransform)math.CreateTransform(new double[]{cosine,cosine,0,-cosine,cosine,0,0,0,1,0,0,0,1,0,0,0});var rotation=Transform(swRotation);var unrotated=new PhysicalProperties{Mass=raw[5],Cog=raw.Take(3).ToArray(),Inertia=new[]{raw[6],-raw[9],-raw[10],-raw[9],raw[7],-raw[11],-raw[10],-raw[11],raw[8]}};
            if(!box.ApplyTransform((MathTransform)swRotation))throw new InvalidOperationException("Could not rotate the native analytic test body.");var rotated=Doubles(box.GetMassProperties(1000));var expected=Math3.Transform(unrotated,rotation);if(Math.Abs(-rotated[9]-expected.Inertia[1])>1e-8)throw new InvalidOperationException("Native products of inertia do not match the tested tensor sign convention.");
            return new{Component=component.Name2,Triangles=triangles.Length/9,BoundsMin=min,BoundsMax=max,Physics=physics,UnitDensity=unit,AnalyticBoxMassKg=raw[5],AnalyticBoxInertia=raw.Skip(6).Take(6).ToArray(),Output=output};
        }
        public bool Changed(RobotProject p)
        {
            AssertThread();try{var doc=Document();return !SameDocument(doc,p)||doc.GetUpdateStamp()!=p.CadStamp;}catch{return true;}
        }
        public object VerifyOrigins()
        {
            var doc=Document();int stamp=doc.GetUpdateStamp();var selection=(ISelectionMgr)doc.SelectionManager;int count=selection.GetSelectedObjectCount2(-1);
            IComponent2 component=null;IFace2 face=null;IVertex vertex=null;var vertices=new List<IVertex>();
            foreach(var candidate in Flatten(doc).Where(c=>!c.IsSuppressed()&&c.GetModelDoc2()!=null).OrderBy(c=>c.Name2.StartsWith("EC-")?0:1)){
                var part=candidate.GetModelDoc2() as IPartDoc;if(part==null)continue;
                foreach(var body in Objects(part.GetBodies2(0,false)).OfType<IBody2>()){
                    var localFace=Objects(body.GetFaces()).OfType<IFace2>().FirstOrDefault(f=>((ISurface)f.GetSurface()).IsCylinder());face=localFace==null?null:candidate.GetCorrespondingEntity(localFace) as IFace2;
                    vertices=Objects(body.GetVertices()).OfType<IVertex>().ToList();vertex=vertices.FirstOrDefault();if(face!=null&&vertex!=null){component=candidate;break;}
                }if(component!=null)break;
            }
            if(component==null)throw new InvalidOperationException("Native origin verification needs a resolved solid with a cylindrical face and a vertex.");
            string id=Reference(doc,component);var placement=Transform(component.Transform2);
            var p=new RobotProject{AssemblyPath=doc.GetPathName(),Configuration=doc.ConfigurationManager.ActiveConfiguration.Name,RootLink="root",ZeroCaptured=true,Stale=false};
            p.Components.Add(new ComponentRecord{Id=id,Name=component.Name2,ZeroTransform=placement,Cad=ReadMass(doc,component,placement),Resolved=true,MaterialKnown=true});
            var root=new LinkRecord{Id="root",Name="base"};var child=new LinkRecord{Id="child",Name="child",ZeroFrame=(double[])placement.Clone(),Components=new List<string>{id}};p.Links.Add(root);p.Links.Add(child);
            var joint=new JointRecord{Id="joint",Name="joint",Parent="root",Child="child"};p.Joints.Add(joint);OriginWorkflow.SetPosition(p,root,new double[3],false);OriginWorkflow.Confirm(p,root);
            AxisSelection axis;PointSelection point;
            selection.SuspendSelectionList();
            try{
                if(!((IEntity)face).Select4(false,null))throw new InvalidOperationException("Could not select the native cylindrical face for acceptance verification.");axis=SelectedAxis(p);
                selection.SuspendSelectionList();
                try{
                    bool selectedVertex=false;foreach(var candidate in vertices.Take(16)){
                        var corresponding=(component.GetCorrespondingEntity((IEntity)candidate)??component.GetCorresponding((IEntity)candidate)) as IVertex;
                        if(corresponding!=null&&selection.AddSelectionListObject(corresponding,selection.CreateSelectData())){vertex=candidate;selectedVertex=true;break;}
                        var location=Math3.Point(placement,Doubles(candidate.GetPoint()));
                        if(doc.Extension.SelectByID2("","VERTEX",location[0],location[1],location[2],false,0,null,0)){vertex=candidate;selectedVertex=true;break;}
                    }
                    if(!selectedVertex)throw new InvalidOperationException("Could not add an assembly-context native vertex to the temporary selection list: "+component.Name2);point=SelectedPoint(p);
                }finally{selection.ResumeSelectionList2(false);}
            }finally{selection.ResumeSelectionList2(false);}
            var expectedPoint=Math3.Point(placement,Doubles(vertex.GetPoint()));if(Math3.Sub(expectedPoint,point.Point).Select(Math.Abs).Max()>1e-9)throw new InvalidOperationException("Selected CAD point used an unexpected component coordinate frame.");
            OriginWorkflow.SetAxis(p,child,axis.Point,axis.Direction);joint.GeometryReference=axis.GeometryReference;joint.GeometryComponent=axis.ComponentId;joint.ReferencePoint=axis.LocalPoint;joint.ReferenceDirection=axis.LocalDirection;
            OriginWorkflow.SetPosition(p,child,point.Point,true);child.Origin.PointReference=point.GeometryReference;child.Origin.PointComponent=point.ComponentId;child.Origin.LocalPoint=point.LocalPoint;OriginWorkflow.Confirm(p,child);
            var before=OriginWorkflow.Properties(p,child);var oldPosition=OriginWorkflow.Position(child);OriginWorkflow.SetOffset(p,child,.005);OriginWorkflow.Confirm(p,child);var after=OriginWorkflow.Properties(p,child);
            double tensorDifference=before.Inertia.Zip(after.Inertia,(a,b)=>Math.Abs(a-b)).Max();if(tensorDifference>1e-9||Math.Abs(before.Mass-after.Mass)>1e-12)throw new InvalidOperationException("Origin offset changed native mass or inertia about CoG.");
            for(int i=0;i<3;i++)if(Math.Abs(OriginWorkflow.Position(child)[i]-oldPosition[i]-.005*axis.Direction[i])>1e-9)throw new InvalidOperationException("Native origin offset moved off its selected axis.");
            if(selection.GetSelectedObjectCount2(-1)!=count||doc.GetUpdateStamp()!=stamp)throw new InvalidOperationException("Native acceptance did not preserve the CAD selection/document state.");
            return new{Component=component.Name2,NativePoint=point.Point,AxisPoint=axis.Point,AxisDirection=axis.Direction,Origin=OriginWorkflow.Position(child),MassKg=after.Mass,CogInLink=after.Cog,MaximumTensorChange=tensorDifference,SelectionRestored=true,DocumentUnchanged=true,Mode="external COM diagnostic; in-process task-pane acceptance still required"};
        }
        bool SameDocument(IModelDoc2 doc,RobotProject p) { return string.Equals(doc.GetPathName(),p.AssemblyPath,StringComparison.OrdinalIgnoreCase)&&doc.ConfigurationManager.ActiveConfiguration.Name==p.Configuration; }
        static IEnumerable<IComponent2> Flatten(IModelDoc2 doc)
        {
            var root=(IComponent2)doc.ConfigurationManager.ActiveConfiguration.GetRootComponent3(false);if(root==null)throw new InvalidOperationException("Assembly root is unavailable. Resolve the assembly in SolidWorks.");
            var queue=new Queue<IComponent2>();foreach(var o in Objects(root.GetChildren()))queue.Enqueue((IComponent2)o);
            while(queue.Count>0){var c=queue.Dequeue();var children=Objects(c.GetChildren()).OfType<IComponent2>().ToList();if(children.Count==0)yield return c;else foreach(var child in children)queue.Enqueue(child);}
        }
        static IEnumerable<object> Objects(object value) { if(value==null)return Enumerable.Empty<object>();return ((Array)value).Cast<object>(); }
        static double[] Doubles(object value) { return Objects(value).Select(Convert.ToDouble).ToArray(); }
        string Reference(IModelDoc2 doc,object value) { var bytes=doc.Extension.GetPersistReference3(value) as byte[];if(bytes==null||bytes.Length==0)throw new InvalidOperationException("CAD object has no persistent reference.");return Convert.ToBase64String(bytes); }
        object Resolve(IModelDoc2 doc,string reference) { int error;return doc.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference),out error); }
        public double[] Transform(IMathTransform transform)
        {
            if(transform==null)return Math3.Identity();var math=(IMathUtility)app.GetMathUtility();
            Func<double[],double[]> point=p=>Doubles(((IMathPoint)((IMathPoint)math.CreatePoint(p)).MultiplyTransform(transform)).ArrayData);
            var origin=point(new double[3]);var result=Math3.Identity();for(int i=0;i<3;i++){var basis=new double[3];basis[i]=1;var v=Math3.Sub(point(basis),origin);for(int j=0;j<3;j++)result[j*4+i]=v[j];result[i*4+3]=origin[i];}return result;
        }
        PhysicalProperties ReadMass(IModelDoc2 doc,IComponent2 component,double[] placement)
        {
            // SolidWorks 2024 SP5 can fault on IMassProperty2.SelectedItems assignment.
            // Create from a temporary native selection instead; preserve the user's list.
            var selection=(ISelectionMgr)doc.SelectionManager;selection.SuspendSelectionList();
            try {
            if(!component.Select4(false,null,false))throw new InvalidOperationException("Could not select the component for native mass calculation.");
            var mass=(IMassProperty2)doc.Extension.CreateMassProperty2();if(mass==null)throw new InvalidOperationException("Solid CAD bodies are required for mass properties.");
            mass.UseSystemUnits=true;mass.AccuracyLevel=2;mass.IncludeHiddenBodiesOrComponents=true;
            var math=(IMathUtility)app.GetMathUtility();var identity=math.CreateTransform(new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0});
            if(!mass.SetCoordinateSystem(identity)||!mass.Recalculate())throw new InvalidOperationException("SolidWorks could not calculate the selected component mass properties.");
            var raw=InertiaAdapter.MassPropertyTensor(Doubles(mass.GetMomentOfInertia(0)));
            var principal=Doubles(mass.PrincipalMomentsOfInertia);Array.Sort(principal);var eigen=Math3.Eigenvalues(raw);
            double tolerance=Math.Max(1e-14,raw.Select(Math.Abs).Max()*1e-7);
            if(principal.Length!=3||principal.Any(v=>!Math3.Finite(v))||principal.Zip(eigen,(a,b)=>Math.Abs(a-b)).Max()>tolerance)throw new InvalidOperationException("Native inertia does not match SolidWorks principal moments. Review mass-property overrides and refresh CAD.");
            var overrides=(IMassPropertyOverrideOptions)mass.GetOverrideOptions();string source="CAD material/geometry";
            if(overrides!=null&&(overrides.OverrideMass||overrides.OverrideCenterOfMass||overrides.OverrideMomentsOfInertia))source="CAD effective mass-property override";
            var world=new PhysicalProperties{Mass=mass.Mass,Cog=Doubles(mass.CenterOfMass),Inertia=raw,Source=source};return Math3.Transform(world,Math3.Inverse(placement));
            } finally { selection.ResumeSelectionList2(false); }
        }
        PhysicalProperties UnitDensity(IComponent2 c)
        {
            object bodyInfo;var bodies=Objects(c.GetBodies3(0,out bodyInfo)).OfType<IBody2>().ToList();var properties=new List<PhysicalProperties>();
            foreach(var body in bodies){var v=Doubles(body.GetMassProperties(1));if(v.Length<12)throw new InvalidOperationException("CAD solid geometry properties unavailable.");properties.Add(new PhysicalProperties{Mass=v[5],Cog=new[]{v[0],v[1],v[2]},Inertia=InertiaAdapter.BodyTensor(v),Source="CAD geometry at 1 kg/m³"});}
            if(properties.Count==0)throw new InvalidOperationException("No solid bodies; resolve this component or exclude it.");return Math3.Combine(properties);
        }
        public async Task<CadSnapshot> CaptureAsync(RobotProject previous,bool captureZero,Action<string,int,int> progress,Func<bool> cancelled,bool includeMeshes=true)
        {
            var doc=Document();if(previous!=null&&!SameDocument(doc,previous))throw new InvalidOperationException("Activate the saved project assembly/configuration, or create a New project for a different assembly.");
            int stamp=doc.GetUpdateStamp();var components=Flatten(doc).ToList();var project=previous==null?new RobotProject():ProjectIO.Parse(ProjectIO.Json(previous));
            var previousZero=project.Components.ToDictionary(c=>c.Id,c=>(double[])c.ZeroTransform.Clone());
            if(captureZero){project.CapturedUtc=DateTime.UtcNow.ToString("o");project.ZeroCaptured=true;}
            project.AssemblyPath=doc.GetPathName();project.AssemblyTitle=doc.GetTitle();project.Configuration=doc.ConfigurationManager.ActiveConfiguration.Name;project.CadStamp=stamp;project.Stale=true;project.CadIssues.Clear();project.Suggestions.Clear();
            var old=project.Components.ToDictionary(c=>c.Id);var seen=new HashSet<string>();var meshes=new Dictionary<string,double[]>();var componentMap=new Dictionary<string,IComponent2>();
            for(int n=0;n<components.Count;n++){
                if(cancelled())throw new OperationCanceledException("CAD capture cancelled; the previous project is retained.");
                var c=components[n];progress(c.Name2,n,components.Count);await Task.Yield();AssertThread();
                if((IModelDoc2)app.ActiveDoc!=doc||doc.GetUpdateStamp()!=stamp)throw new InvalidOperationException("Assembly changed during capture. Refresh again after the rebuild finishes.");
                string reference;try{reference=Reference(doc,c);}catch(Exception e){project.CadIssues.Add(c.Name2+": "+e.Message);continue;}
                string id=reference;ComponentRecord record;if(!old.TryGetValue(id,out record)){record=new ComponentRecord{Id=id,PersistentReference=reference};project.Components.Add(record);}seen.Add(id);componentMap[id]=c;
                record.Name=c.Name2;record.Path=c.GetPathName();record.Configuration=c.ReferencedConfiguration;record.Hidden=c.IsHidden(false);record.Resolved=false;record.Issue="";
                try{
                    if(c.IsSuppressed())throw new InvalidOperationException("Component is suppressed.");if(c.GetModelDoc2()==null)throw new InvalidOperationException("Component is lightweight or unloaded. Resolve it in SolidWorks, then Refresh.");
                    double[] placement=Transform(c.Transform2);if(!Math3.Rigid(placement))throw new InvalidOperationException("Scaled or mirrored component placement is unsupported; correct the CAD instance.");
                    if(captureZero||!old.ContainsKey(id))record.ZeroTransform=placement;
                    record.Cad=ReadMass(doc,c,placement);record.UnitDensity=UnitDensity(c);
                    var model=(IModelDoc2)c.GetModelDoc2();string database;record.MaterialKnown=false;var part=model as IPartDoc;if(part!=null)record.MaterialKnown=!string.IsNullOrEmpty(part.GetMaterialPropertyName2(c.ReferencedConfiguration,out database));
                    if(record.Cad.Source.Contains("override"))record.MaterialKnown=true;
                    record.Resolved=true;if(!record.Excluded&&includeMeshes)meshes[id]=await TessellateAsync(c,project.Export,cancelled);
                }catch(OperationCanceledException){throw;}catch(Exception e){record.Issue=e.Message;record.Resolved=false;}
            }
            foreach(var record in project.Components.Where(c=>!seen.Contains(c.Id))){record.Resolved=false;record.Issue="Persistent CAD reference no longer appears in this configuration. Remap it or explicitly exclude it.";}
            if(captureZero&&previous!=null){foreach(var link in project.Links){string anchor=link.Components.FirstOrDefault(id=>seen.Contains(id)&&previousZero.ContainsKey(id));if(anchor!=null){var c=project.Components.Single(x=>x.Id==anchor);link.ZeroFrame=Math3.Multiply(Math3.Multiply(c.ZeroTransform,Math3.Inverse(previousZero[anchor])),link.ZeroFrame);}}foreach(var joint in project.Joints)joint.Confirmed=false;}
            if(cancelled())throw new OperationCanceledException();CheckAggregateOverrides(doc,project);Suggestions(doc,componentMap,project);
            foreach(var j in project.Joints.Where(j=>!string.IsNullOrEmpty(j.GeometryReference))){try{var entity=Resolve(doc,j.GeometryReference);var axis=ReadAxis(doc,entity,j.GeometryComponent,project);if(Math3.Sub(axis.LocalPoint,j.ReferencePoint).Select(Math.Abs).Max()>1e-7||Math3.Dot(axis.LocalDirection,j.ReferenceDirection)<1-1e-8){j.ReferenceValid=false;j.Confirmed=false;}}catch{j.ReferenceValid=false;j.Confirmed=false;}}
            foreach(var link in project.Links){
                if(captureZero&&previous!=null){link.Origin.Confirmed=false;link.Origin.ReferenceValid=false;}
                var joint=OriginWorkflow.Joint(project,link);if(joint!=null&&!joint.ReferenceValid){link.Origin.Confirmed=false;link.Origin.ReferenceValid=false;}
                if(!string.IsNullOrEmpty(link.Origin.PointReference)){
                    try{var point=ReadPoint(doc,Resolve(doc,link.Origin.PointReference),link.Origin.PointComponent,project);if(!OriginWorkflow.VectorValid(link.Origin.LocalPoint)||Math3.Sub(point.LocalPoint,link.Origin.LocalPoint).Select(Math.Abs).Max()>1e-7)throw new InvalidOperationException("Origin reference moved.");}
                    catch{link.Origin.ReferenceValid=false;link.Origin.Confirmed=false;}
                }
            }
            if((IModelDoc2)app.ActiveDoc!=doc||doc.GetUpdateStamp()!=stamp)throw new InvalidOperationException("Assembly changed during capture; no snapshot was committed.");project.Stale=false;progress("CAD snapshot ready",components.Count,components.Count);
            return new CadSnapshot{Project=project,Meshes=meshes};
        }
        void CheckAggregateOverrides(IModelDoc2 doc,RobotProject p)
        {
            var root=(IComponent2)doc.ConfigurationManager.ActiveConfiguration.GetRootComponent3(false);var selection=(ISelectionMgr)doc.SelectionManager;
            Action<IComponent2> check=null;check=c=>{var children=Objects(c.GetChildren()).OfType<IComponent2>().ToList();if(children.Count==0||c.IsSuppressed())return;selection.SuspendSelectionList();try{
                doc.ClearSelection2(true);if(!c.IsRoot()&&!c.Select4(false,null,false))throw new InvalidOperationException("Could not select subassembly for override inspection.");
                var mass=(IMassProperty2)doc.Extension.CreateMassProperty2();var overrides=(IMassPropertyOverrideOptions)mass.GetOverrideOptions();
                if(overrides!=null&&(overrides.OverrideMass||overrides.OverrideCenterOfMass||overrides.OverrideMomentsOfInertia))p.CadIssues.Add(c.IsRoot()?"Top-level assembly mass overrides cannot be attributed to individual links. Remove the aggregate override or correct individual components.":c.Name2+": subassembly mass override cannot be distributed across leaf components. Resolve the override in CAD.");
            }catch(Exception e){p.CadIssues.Add(c.Name2+": could not verify assembly overrides: "+e.Message);}finally{selection.ResumeSelectionList2(false);}foreach(var child in children)check(child);};check(root);
        }
        async Task<double[]> TessellateAsync(IComponent2 c,ExportSettings settings,Func<bool> cancelled)
        {
            object info;var output=new List<double>();foreach(var body in Objects(c.GetBodies3(0,out info)).OfType<IBody2>()){
                var tess=(ITessellation)body.GetTessellation(null);tess.NeedVertexNormal=true;tess.NeedFaceFacetMap=false;tess.ImprovedQuality=true;tess.MatchType=1;tess.SurfacePlaneTolerance=settings.ChordMetres;tess.CurveChordTolerance=settings.ChordMetres;tess.SurfacePlaneAngleTolerance=settings.AngleRadians;tess.CurveChordAngleTolerance=settings.AngleRadians;
                if(!tess.Tessellate())throw new InvalidOperationException("CAD tessellation failed.");
                var points=new Dictionary<int,double[]>();var normals=new Dictionary<int,double[]>();Func<int,double[]> getPoint=id=>{double[] value;if(!points.TryGetValue(id,out value)){value=Doubles(tess.GetVertexPoint(id));points[id]=value;}return value;};Func<int,double[]> getNormal=id=>{double[] value;if(!normals.TryGetValue(id,out value)){value=Doubles(tess.GetVertexNormal(id));normals[id]=value;}return value;};
                int count=tess.GetFacetCount();for(int facet=0;facet<count;facet++){
                    if(facet%2048==0){await Task.Yield();AssertThread();if(cancelled())throw new OperationCanceledException();}var fins=Objects(tess.GetFacetFins(facet)).Select(Convert.ToInt32).ToArray();if(fins.Length!=3)throw new InvalidOperationException("Non-triangular CAD tessellation is unsupported.");
                    var ids=new List<int>();foreach(int fin in fins)foreach(int vertex in Objects(tess.GetFinVertices(fin)).Select(Convert.ToInt32))if(!ids.Contains(vertex))ids.Add(vertex);if(ids.Count!=3)throw new InvalidOperationException("Invalid tessellation triangle.");
                    var a=getPoint(ids[0]);var b=getPoint(ids[1]);var d=getPoint(ids[2]);var normal=getNormal(ids[0]);
                    if(normal.Length!=3)throw new InvalidOperationException("CAD vertex normals unavailable.");if(Math3.Dot(Math3.Cross(Math3.Sub(b,a),Math3.Sub(d,a)),normal)<0){var temp=b;b=d;d=temp;}output.AddRange(a);output.AddRange(b);output.AddRange(d);
                }
            }if(output.Count==0)throw new InvalidOperationException("No CAD mesh triangles were produced.");return output.ToArray();
        }
        void Suggestions(IModelDoc2 doc,Dictionary<string,IComponent2> components,RobotProject project)
        {
            var pairs=new HashSet<string>();foreach(var pair in components){try{foreach(var mate in Objects(pair.Value.GetMates()).OfType<IMate2>()){
                if(mate.Type!=1&&mate.Type!=16&&mate.Type!=22&&mate.Type!=23)continue;if(mate.GetMateEntityCount()<2)continue;var a=mate.MateEntity(0);var b=mate.MateEntity(1);if(a.ReferenceComponent==null||b.ReferenceComponent==null)continue;
                string aid=Reference(doc,a.ReferenceComponent),bid=Reference(doc,b.ReferenceComponent);if(aid==bid||!components.ContainsKey(aid)||!components.ContainsKey(bid))continue;string key=string.CompareOrdinal(aid,bid)<0?aid+bid+mate.Type:bid+aid+mate.Type;if(!pairs.Add(key))continue;
                AxisSelection axis=null;try{axis=ReadAxis(doc,a.Reference,aid,project);}catch{}
                project.Suggestions.Add(new JointSuggestion{ComponentA=aid,ComponentB=bid,Type=mate.Type==16?"fixed":mate.Type==23?"prismatic":"revolute",Reason=mate.Type==16?"Lock mate: candidate rigid group":mate.Type==1?"Concentric mate: candidate axis; remaining motion needs confirmation":"Hinge/slider mate: confirm permitted motion and direction",Point=axis==null?null:axis.Point,Direction=axis==null?null:axis.Direction});
            }}catch(Exception e){project.CadIssues.Add(pair.Value.Name2+": mate inspection failed: "+e.Message);}}
        }
        AxisSelection ReadAxis(IModelDoc2 doc,object selected,string componentId,RobotProject p)
        {
            double[] point,direction;var face=selected as IFace2;var axis=selected as IRefAxis;var feature=selected as IFeature;if(feature!=null)axis=feature.GetSpecificFeature2() as IRefAxis;
            if(face!=null){var surface=(ISurface)face.GetSurface();if(!surface.IsCylinder())throw new InvalidOperationException("Select a cylindrical face or reference axis.");var values=Doubles(surface.CylinderParams);point=values.Take(3).ToArray();direction=Math3.Normalize(values.Skip(3).Take(3).ToArray());}
            else if(axis!=null){var v=Doubles(axis.GetRefAxisParams());if(v.Length!=6)throw new InvalidOperationException("Unexpected reference axis data.");point=v.Take(3).ToArray();direction=Math3.Normalize(Math3.Sub(v.Skip(3).Take(3).ToArray(),point));}
            else throw new InvalidOperationException("Select a cylindrical face or reference axis.");
            var zero=Math3.Identity();if(!string.IsNullOrEmpty(componentId)){var c=p.Components.Single(x=>x.Id==componentId);zero=c.ZeroTransform;}
            return new AxisSelection{Point=Math3.Point(zero,point),Direction=Math3.Normalize(Math3.Vector(zero,direction)),LocalPoint=point,LocalDirection=direction,ComponentId=componentId,GeometryReference=Reference(doc,selected)};
        }
        public AxisSelection SelectedAxis(RobotProject p)
        {
            var doc=Document();if(!SameDocument(doc,p))throw new InvalidOperationException("Activate the project assembly/configuration first.");var selection=(ISelectionMgr)doc.SelectionManager;if(selection.GetSelectedObjectCount2(-1)!=1)throw new InvalidOperationException("Select exactly one CAD cylindrical face or reference axis.");var selected=selection.GetSelectedObject6(1,-1);var comp=(IComponent2)selection.GetSelectedObjectsComponent4(1,-1);return ReadAxis(doc,selected,comp==null?"":Reference(doc,comp),p);
        }
        PointSelection ReadPoint(IModelDoc2 doc,object selected,string componentId,RobotProject p)
        {
            var vertex=selected as IVertex;var point=selected as IRefPoint;var sketchPoint=selected as ISketchPoint;var feature=selected as IFeature;if(feature!=null)point=feature.GetSpecificFeature2() as IRefPoint;
            double[] local;
            if(vertex!=null)local=Doubles(vertex.GetPoint());
            else if(point!=null)local=Doubles(((IMathPoint)point.GetRefPoint()).ArrayData);
            else if(sketchPoint!=null){
                var sketch=(ISketch)sketchPoint.GetSketch();if(sketch==null)throw new InvalidOperationException("The selected sketch point has no owning sketch.");
                // Sketch points use sketch coordinates. 3D sketches have an identity
                // transform; planar sketches need the inverse model-to-sketch transform.
                var modelToSketch=sketch.ModelToSketchTransform;if(modelToSketch==null)throw new InvalidOperationException("The selected sketch's coordinate transform is unavailable.");
                local=Math3.Point(Math3.Inverse(Transform(modelToSketch)),new[]{sketchPoint.X,sketchPoint.Y,sketchPoint.Z});
            }
            else throw new InvalidOperationException("The selected object is not a solid vertex, sketch point, or reference point. Select the point itself, rather than a sketch line or sketch feature.");
            if(!OriginWorkflow.VectorValid(local))throw new InvalidOperationException("CAD point did not return finite XYZ coordinates.");
            var placement=Math3.Identity();if(!string.IsNullOrEmpty(componentId)){var component=p.Components.SingleOrDefault(c=>c.Id==componentId);if(component==null)throw new InvalidOperationException("This point's component is not in the captured assembly. Refresh CAD first.");placement=component.ZeroTransform;}
            return new PointSelection{Point=Math3.Point(placement,local),LocalPoint=local,ComponentId=componentId,GeometryReference=Reference(doc,selected)};
        }
        public PointSelection SelectedPoint(RobotProject p)
        {
            var doc=Document();if(!SameDocument(doc,p))throw new InvalidOperationException("Activate the project assembly/configuration first.");
            var selection=(ISelectionMgr)doc.SelectionManager;int count=selection.GetSelectedObjectCount2(-1);if(count!=1)throw new InvalidOperationException("SolidWorks reports "+count+" selected objects. Clear the selection, then select exactly one solid vertex, sketch point, or reference point.");
            var component=(IComponent2)selection.GetSelectedObjectsComponent4(1,-1);
            return ReadPoint(doc,selection.GetSelectedObject6(1,-1),component==null?"":Reference(doc,component),p);
        }
        public List<string> SelectedComponents(RobotProject p)
        {
            var doc=Document();if(!SameDocument(doc,p))throw new InvalidOperationException("Activate the project assembly/configuration first.");var selection=(ISelectionMgr)doc.SelectionManager;var ids=new List<string>();for(int i=1;i<=selection.GetSelectedObjectCount2(-1);i++){var c=selection.GetSelectedObject6(i,-1) as IComponent2??selection.GetSelectedObjectsComponent4(i,-1) as IComponent2;if(c!=null){string id=Reference(doc,c);if(p.Components.Any(x=>x.Id==id)&&!ids.Contains(id))ids.Add(id);}}return ids;
        }
    }
}
