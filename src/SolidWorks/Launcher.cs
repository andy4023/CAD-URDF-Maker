using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadUrdfMaker.Core;

namespace CadUrdfMaker.SolidWorks
{
    public static class Launcher
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);if(args.Length==0)MessageBox.Show("Start SolidWorks and open a native assembly first.\r\n\r\n"+e.Message,"CAD-URDF Maker");return 1;}
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var app=CadSession.Attach();
                if(args.Length>0&&args[0]=="--probe"){Console.WriteLine(ProjectIO.Json(new CadSession(app).Probe()));return 0;}
                if(args.Length>0&&args[0]=="--diagnose-point"){Console.WriteLine(ProjectIO.Json(new CadSession(app).DiagnosePoint()));return 0;}
                if(args.Length>0&&args[0]=="--verify-origins"){Console.WriteLine(ProjectIO.Json(new CadSession(app).VerifyOrigins()));return 0;}
                if(args.Length>0&&args[0]=="--diagnose-mass"){Console.WriteLine(ProjectIO.Json(new CadSession(app).DiagnoseMass()));return 0;}
                if(args.Length>0&&args[0]=="--diagnose-inertia"){Console.WriteLine(ProjectIO.Json(new CadSession(app).DiagnoseInertia(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(args,1)))));return 0;}
                if(args.Length>0&&args[0]=="--verify-inertia-box"){Console.WriteLine(ProjectIO.Json(new CadSession(app).VerifyInertiaBox()));return 0;}
                if(args.Length>1&&args[0]=="--verify-mesh"){
                    Exception failure=null;var form=new Form{Width=1,Height=1,ShowInTaskbar=false,Opacity=0};form.Shown+=async(s,e)=>{try{Console.WriteLine(ProjectIO.Json(await new CadSession(app).VerifyMeshAsync(args[1])));}catch(Exception ex){failure=ex;}finally{form.Close();}};Application.Run(form);if(failure!=null)throw failure;return 0;
                }
                if(args.Length>1&&(args[0]=="--snapshot"||args[0]=="--snapshot-properties")){
                    Exception failure=null;var form=new Form{Text="CAD-URDF Maker — validating CAD capture",Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
                    bool stop=false;Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;stop=true;};
                    form.Shown+=async (s,e)=>{try{var snapshot=await new CadSession(app).CaptureAsync(null,true,(name,n,count)=>Console.WriteLine(n+" / "+count+"  "+name),()=>stop,args[0]!="--snapshot-properties");ProjectIO.Save(args[1],snapshot.Project);Console.WriteLine(ProjectIO.Json(new{Components=snapshot.Project.Components.Count,Resolved=snapshot.Project.Components.FindAll(c=>c.Resolved).Count,Meshes=snapshot.Meshes.Count,Issues=snapshot.Project.CadIssues}));}catch(Exception ex){failure=ex;}finally{form.Close();}};Application.Run(form);if(failure!=null)throw failure;return 0;
                }
                var window=new Form{Text="CAD-URDF Maker",Width=1400,Height=900,MinimumSize=new System.Drawing.Size(700,500)};var editor=new EditorControl();window.Controls.Add(editor);window.Shown+=(s,e)=>editor.Initialize(app);Application.Run(window);return 0;
        }
    }
}
