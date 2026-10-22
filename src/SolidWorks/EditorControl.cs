using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadUrdfMaker.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SolidWorks.Interop.sldworks;

namespace CadUrdfMaker.SolidWorks
{
    [ComVisible(true),Guid("D3BB6DD6-2FD5-4BF6-BEA7-C42FD1848A79"),ProgId("CadUrdfMaker.EditorControl"),ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class EditorControl : UserControl
    {
        WebView2 browser;
        CadSession cad;
        RobotProject project=new RobotProject();
        Dictionary<string,double[]> meshes=new Dictionary<string,double[]>();
        Dictionary<string,string> meshUrls=new Dictionary<string,string>();
        readonly string cache=Path.Combine(Path.GetTempPath(),"CadUrdfMaker",Guid.NewGuid().ToString("N"));
        readonly Timer timer=new Timer{Interval=1500};
        bool busy,cancelled,ready;
        string projectPath="";
        public EditorControl() { Dock=DockStyle.Fill; }
        public async void Initialize(ISldWorks application)
        {
            try {
                cad=new CadSession(application);Directory.CreateDirectory(cache);browser=new WebView2{Dock=DockStyle.Fill};Controls.Add(browser);
                string userData=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"CAD-URDF Maker","WebView2");
                var environment=await CoreWebView2Environment.CreateAsync(null,userData);await browser.EnsureCoreWebView2Async(environment);
                string web=Path.Combine(Path.GetDirectoryName(typeof(EditorControl).Assembly.Location),"web");
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("cad-urdf.local",web,CoreWebView2HostResourceAccessKind.DenyCors);
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("cad-urdf-cache.local",cache,CoreWebView2HostResourceAccessKind.Allow);
                browser.CoreWebView2.Settings.AreDevToolsEnabled=false;browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                browser.CoreWebView2.NavigationStarting+=(s,e)=>{if(!e.Uri.StartsWith("https://cad-urdf.local/",StringComparison.Ordinal))e.Cancel=true;};
                browser.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
                browser.CoreWebView2.WebMessageReceived+=Receive;browser.Source=new Uri("https://cad-urdf.local/index.html");
                timer.Tick+=(s,e)=>{if(!busy&&ready&&!project.Stale&&project.ZeroCaptured&&cad.Changed(project)){project.Stale=true;SendState();}};timer.Start();
            } catch(Exception e){Controls.Clear();Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,Text="CAD-URDF Maker could not initialize WebView2.\r\n"+e.Message+"\r\nInstall the Microsoft Edge WebView2 Runtime and reopen the add-in."});}
        }
        void Send(object value) { if(browser!=null&&browser.CoreWebView2!=null&&!IsDisposed)browser.CoreWebView2.PostWebMessageAsJson(ProjectIO.Json(value)); }
        void SendState()
        {
            var properties=new Dictionary<string,PhysicalProperties>();var originErrors=new Dictionary<string,string>();
            foreach(var l in project.Links){try{originErrors[l.Id]=OriginWorkflow.Error(project,l,false);if(OriginWorkflow.Error(project,l)==null)properties[l.Id]=OriginWorkflow.Properties(project,l);}catch(Exception e){originErrors[l.Id]=e.Message;}}
            double assignedMassKg=0;foreach(var c in project.Components.Where(c=>!c.Excluded&&project.Links.Any(l=>l.Components.Contains(c.Id))))try{assignedMassKg+=Math3.Effective(c).Mass;}catch{}
            Send(new{type="state",project,properties,originErrors,assignedMassKg,meshUrls,projectPath,busy});
        }
        async void Receive(object sender,CoreWebView2WebMessageReceivedEventArgs args)
        {
            if(!args.Source.StartsWith("https://cad-urdf.local/",StringComparison.Ordinal))return;
            try {
                var message=ProjectIO.Serializer().Deserialize<Dictionary<string,object>>(args.WebMessageAsJson);string type=(string)message["type"];
                if(type=="ready"){ready=true;SendState();return;}if(type=="cancel"){cancelled=true;return;}if(busy)throw new InvalidOperationException("Wait for the current operation or cancel it first.");
                if(type=="new"){project=new RobotProject();projectPath="";meshes.Clear();meshUrls.Clear();SendState();return;}
                if(type=="update"){
                    var candidate=ProjectIO.Parse(ProjectIO.Json(message["project"]));ApplyEditorChanges(candidate);SendState();return;
                }
                if(type.StartsWith("origin-",StringComparison.Ordinal)){HandleOrigin(type,message);SendState();return;}
                if(type=="capture"||type=="refresh"){
                    if(type=="refresh"&&!project.ZeroCaptured)throw new InvalidOperationException("Capture the zero pose first.");
                    busy=true;cancelled=false;SendState();try{var snapshot=await cad.CaptureAsync(type=="capture"&&!project.ZeroCaptured?null:project,type=="capture",(name,n,count)=>Send(new{type="progress",name,n,count}),()=>cancelled);project=snapshot.Project;meshes=snapshot.Meshes;await WritePreview();}finally{busy=false;}SendState();return;
                }
                if(type=="save"){
                    using(var dialog=new SaveFileDialog{Title="Save CAD-URDF Maker project",Filter="CAD-URDF Maker project|*.urdfmaker.json",FileName=string.IsNullOrEmpty(projectPath)?project.Name+".urdfmaker.json":projectPath}){if(dialog.ShowDialog()==DialogResult.OK){ProjectIO.Save(dialog.FileName,project);projectPath=dialog.FileName;Send(new{type="notice",message="Project saved."});}}SendState();return;
                }
                if(type=="open"){
                    using(var dialog=new OpenFileDialog{Title="Open CAD-URDF Maker project",Filter="CAD-URDF Maker project|*.urdfmaker.json"}){if(dialog.ShowDialog()==DialogResult.OK){project=ProjectIO.Load(dialog.FileName);project.Stale=true;projectPath=dialog.FileName;meshes.Clear();meshUrls.Clear();SendState();}}return;
                }
                if(type=="axis"){
                    var j=project.Joints.Single(x=>x.Id==(string)message["id"]);message["id"]=j.Child;HandleOrigin("origin-axis",message);SendState();return;
                }
                if(type=="selection"){Send(new{type="selection",ids=cad.SelectedComponents(project)});return;}
                if(type=="remove-unassigned"||type=="remove-unassigned-cad"||type=="restore-components"){
                    if(!project.ZeroCaptured)throw new InvalidOperationException("Capture the assembly first.");
                    var ids=type=="remove-unassigned-cad"?cad.SelectedComponents(project).ToArray():ProjectIO.Serializer().Deserialize<string[]>(ProjectIO.Json(message["ids"]));
                    bool removed=type!="restore-components";int count=ComponentScope.SetRemoved(project,ids,removed);
                    if(!removed&&ids.Any(id=>!meshes.ContainsKey(id)))project.Stale=true;
                    SendState();Send(new{type="notice",message=count+(removed?" component(s) removed from the URDF. CAD is unchanged.":" component(s) restored as unassigned. Assign them to links before export.")});return;
                }
                if(type=="remap"){
                    string oldId=(string)message["id"];var selected=cad.SelectedComponents(project);if(selected.Count!=1)throw new InvalidOperationException("Select exactly one replacement CAD component, and refresh first so it appears in the component list.");string newId=selected[0];if(oldId==newId)return;var old=project.Components.Single(x=>x.Id==oldId);var replacement=project.Components.Single(x=>x.Id==newId);if(project.Links.Any(l=>l.Components.Contains(newId)))throw new InvalidOperationException("Unassign the replacement component before remapping.");
                    replacement.Override=old.Override;replacement.MaterialAcknowledged=old.MaterialAcknowledged;replacement.Excluded=old.Excluded;replacement.ZeroTransform=old.ZeroTransform;foreach(var l in project.Links){for(int i=0;i<l.Components.Count;i++)if(l.Components[i]==oldId)l.Components[i]=newId;if(l.Origin.PointComponent==oldId){l.Origin.ReferenceValid=false;l.Origin.Confirmed=false;}if(project.Joints.Any(j=>j.Child==l.Id&&j.GeometryComponent==oldId)){l.Origin.ReferenceValid=false;l.Origin.Confirmed=false;}}foreach(var j in project.Joints.Where(j=>j.GeometryComponent==oldId)){j.GeometryComponent=newId;j.ReferenceValid=false;j.Confirmed=false;}project.Components.Remove(old);project.Stale=true;SendState();return;
                }
                if(type=="export"){
                    if(cad.Changed(project)){project.Stale=true;SendState();}var diagnostics=Validation.Check(project);if(diagnostics.Count>0){Send(new{type="validation",diagnostics});return;}
                    using(var dialog=new FolderBrowserDialog{Description="Choose a parent folder. CAD-URDF Maker will create a robot-named output subfolder."}){if(dialog.ShowDialog()!=DialogResult.OK)return;string destination=Path.Combine(dialog.SelectedPath,project.Name);if(Directory.Exists(destination)&&MessageBox.Show("Replace the previous CAD-URDF Maker export? A recoverable copy will be kept beside it.","CAD-URDF Maker",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                        busy=true;SendState();try{string json=ProjectIO.Json(project);var frozen=ProjectIO.Parse(json);await Task.Run(()=>UrdfExport.Package(destination,frozen,meshes));Send(new{type="notice",message="Export complete: "+destination});}finally{busy=false;SendState();}}
                    return;
                }
                if(type=="validate"){Send(new{type="validation",diagnostics=Validation.Check(project)});return;}
                throw new InvalidOperationException("Unknown editor command.");
            }catch(OperationCanceledException){Send(new{type="notice",message="Operation cancelled. Previous project retained."});SendState();}
            catch(Exception e){busy=false;Send(new{type="error",message=e.Message});SendState();}
        }
        static double[] MessageVector(Dictionary<string,object> message,string key)
        {
            object value;if(!message.TryGetValue(key,out value))throw new InvalidDataException("Missing "+key+" coordinates.");
            var values=ProjectIO.Serializer().Deserialize<double[]>(ProjectIO.Json(value));if(!OriginWorkflow.VectorValid(values))throw new InvalidDataException("Enter three finite "+key+" coordinates.");return values;
        }
        void HandleOrigin(string type,Dictionary<string,object> message)
        {
            if(!project.ZeroCaptured)throw new InvalidOperationException("Capture the assembly first.");
            if((type=="origin-axis"||type=="origin-point"||type=="origin-confirm")&&(project.Stale||cad.Changed(project)))throw new InvalidOperationException("Refresh CAD before selecting or confirming origin geometry.");
            var next=ProjectIO.Parse(ProjectIO.Json(project));var link=next.Links.Single(l=>l.Id==(string)message["id"]);var o=link.Origin;var joint=OriginWorkflow.Joint(next,link);
            if(type=="origin-begin"){
                OriginWorkflow.Invalidate(next,link);link.ShowFrame=true;
                if(joint!=null&&joint.ReferenceValid&&!string.IsNullOrEmpty(joint.GeometryReference)&&OriginWorkflow.VectorValid(joint.ReferencePoint)&&OriginWorkflow.VectorValid(joint.ReferenceDirection)){
                    var placement=string.IsNullOrEmpty(joint.GeometryComponent)?Math3.Identity():next.Components.Single(c=>c.Id==joint.GeometryComponent).ZeroTransform;
                    o.AxisPoint=Math3.Point(placement,joint.ReferencePoint);o.AxisDirection=Math3.Normalize(Math3.Vector(placement,joint.ReferenceDirection));o.JointId=joint.Id;
                }
            }else if(type=="origin-axis"){
                var axis=cad.SelectedAxis(next);OriginWorkflow.SetAxis(next,link,axis.Point,axis.Direction);
                joint.GeometryReference=axis.GeometryReference;joint.GeometryComponent=axis.ComponentId;joint.ReferencePoint=axis.LocalPoint;joint.ReferenceDirection=axis.LocalDirection;joint.ReferenceValid=true;
                if(o.Mode=="cad-point"&&OriginWorkflow.VectorValid(o.SourcePoint)){bool pointValid=o.ReferenceValid;OriginWorkflow.SetPosition(next,link,o.SourcePoint,true);o.ReferenceValid=pointValid;}
                link.ShowFrame=true;
            }else if(type=="origin-numeric-axis"){
                var frame=OriginWorkflow.InputFrame(next,link);OriginWorkflow.SetAxis(next,link,Math3.Point(frame,MessageVector(message,"point")),Math3.Vector(frame,MessageVector(message,"direction")));
                joint.GeometryReference="";joint.GeometryComponent="";joint.ReferencePoint=null;joint.ReferenceDirection=null;joint.ReferenceValid=true;
                if(o.Mode=="cad-point"&&OriginWorkflow.VectorValid(o.SourcePoint)){bool pointValid=o.ReferenceValid;OriginWorkflow.SetPosition(next,link,o.SourcePoint,true);o.ReferenceValid=pointValid;}
                link.ShowFrame=true;
            }else if(type=="origin-point"){
                var point=cad.SelectedPoint(next);OriginWorkflow.SetPosition(next,link,point.Point,true);o.PointReference=point.GeometryReference;o.PointComponent=point.ComponentId;o.LocalPoint=point.LocalPoint;link.ShowFrame=true;
            }else if(type=="origin-position"){
                o.OffsetMetres=0;var frame=OriginWorkflow.InputFrame(next,link);OriginWorkflow.SetPosition(next,link,Math3.Point(frame,MessageVector(message,"point")),false);
                o.PointReference="";o.PointComponent="";o.LocalPoint=null;link.ShowFrame=true;
            }else if(type=="origin-offset"){
                OriginWorkflow.SetOffset(next,link,Convert.ToDouble(message["offsetMetres"]));
            }else if(type=="origin-confirm"){
                OriginWorkflow.Confirm(next,link);
            }else throw new InvalidOperationException("Unknown origin command.");
            project=next;
        }
        void ApplyEditorChanges(RobotProject candidate)
        {
            // The browser may edit robot definitions, never CAD snapshots or source identity.
            if(candidate.Components.Count!=project.Components.Count||candidate.Components.Select(c=>c.Id).OrderBy(x=>x).SequenceEqual(project.Components.Select(c=>c.Id).OrderBy(x=>x))==false)throw new InvalidDataException("Component identity cannot be edited in the browser.");
            var merged=ProjectIO.Parse(ProjectIO.Json(project));
            bool settingsChanged=candidate.Export.ChordMetres!=project.Export.ChordMetres||candidate.Export.AngleRadians!=project.Export.AngleRadians;
            foreach(var c in merged.Components){var edit=candidate.Components.Single(x=>x.Id==c.Id);if(c.Excluded&&!edit.Excluded&&!meshes.ContainsKey(c.Id))settingsChanged=true;c.Override=edit.Override;c.Excluded=edit.Excluded;c.MaterialAcknowledged=edit.MaterialAcknowledged;}
            var oldJoints=project.Joints.ToDictionary(j=>j.Id);foreach(var j in candidate.Joints){JointRecord original;if(oldJoints.TryGetValue(j.Id,out original)){
                bool geometryChanged=j.GeometryReference!=original.GeometryReference||j.GeometryComponent!=original.GeometryComponent;
                if(geometryChanged&&!(string.IsNullOrEmpty(j.GeometryReference)&&string.IsNullOrEmpty(j.GeometryComponent)))throw new InvalidDataException("Geometry references may only be changed through CAD selection or cleared for numeric setup.");
                if(!geometryChanged){j.ReferencePoint=original.ReferencePoint;j.ReferenceDirection=original.ReferenceDirection;j.ReferenceValid=original.ReferenceValid;}
            }}
            foreach(var link in candidate.Links){
                var original=project.Links.FirstOrDefault(l=>l.Id==link.Id);link.Origin=original==null?new OriginDefinition():ProjectIO.Serializer().Deserialize<OriginDefinition>(ProjectIO.Json(original.Origin));
                var joint=candidate.Joints.FirstOrDefault(j=>j.Child==link.Id);var oldJoint=project.Joints.FirstOrDefault(j=>j.Child==link.Id);
                bool changed=original!=null&&!link.ZeroFrame.SequenceEqual(original.ZeroFrame);
                changed|=(joint==null)!=(oldJoint==null);
                if(joint!=null&&oldJoint!=null)changed|=joint.Id!=oldJoint.Id||joint.Type!=oldJoint.Type||joint.Parent!=oldJoint.Parent||!joint.Axis.SequenceEqual(oldJoint.Axis)||joint.GeometryReference!=oldJoint.GeometryReference;
                if(candidate.RootLink!=project.RootLink&&(link.Id==candidate.RootLink||link.Id==project.RootLink))changed=true;
                if(changed){link.Origin.Confirmed=false;if(joint!=null)joint.Confirmed=false;}
            }
            merged.Name=candidate.Name;merged.RootLink=candidate.RootLink;merged.Links=candidate.Links;merged.Joints=candidate.Joints;merged.Export=candidate.Export;merged.Alignment=candidate.Alignment;merged.ChildFramesFollowRoot=candidate.ChildFramesFollowRoot;if(settingsChanged)merged.Stale=true;project=merged;
        }
        async Task WritePreview()
        {
            var copy=meshes.ToArray();int budget=Math.Max(64,500000/Math.Max(1,copy.Length));var urls=await Task.Run(()=>{var result=new Dictionary<string,string>();int n=0;foreach(var pair in copy){string filename="mesh-"+(n++)+"-"+Guid.NewGuid().ToString("N")+".stl";MeshIO.WriteStl(Path.Combine(cache,filename),PreviewMesh.Reduce(pair.Value,budget));result[pair.Key]="https://cad-urdf-cache.local/"+filename;}return result;});meshUrls=urls;
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){cancelled=true;timer.Stop();timer.Dispose();if(browser!=null)browser.Dispose();/* Session cache remains recoverable and is cleared by the user's normal temp cleanup. */}base.Dispose(disposing);
        }
    }
}
