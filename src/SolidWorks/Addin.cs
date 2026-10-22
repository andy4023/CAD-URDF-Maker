using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

namespace CadUrdfMaker.SolidWorks
{
    // SolidWorks marshals AddinCallbacks as IDispatch. ISwAddin alone is
    // an IUnknown interface, so a dispatch class interface is also required.
    [ComVisible(true),Guid("136E0D98-8518-4EBC-8BFE-6CA96F4850D8"),ProgId("CadUrdfMaker.Addin"),ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class Addin : ISwAddin
    {
        ISldWorks app;
        ITaskpaneView pane;
        EditorControl editor;
        public bool ConnectToSW(object thisSw,int cookie)
        {
            string stage="Connect to SolidWorks";
            try{
                app=(ISldWorks)thisSw;
                stage="Register add-in callbacks";
                if(!app.SetAddinCallbackInfo2(0,this,cookie))throw new InvalidOperationException("SolidWorks rejected the add-in callback registration.");
                stage="Create task pane";
                string icon=Path.Combine(Path.GetDirectoryName(typeof(Addin).Assembly.Location),"web","icon.bmp");pane=app.CreateTaskpaneView2(icon,"CAD-URDF Maker");
                if(pane==null)throw new InvalidOperationException("SolidWorks could not create the CAD-URDF Maker task pane.");
                stage="Host editor window";
                // Keep the original managed instance. AddControl can return a COM
                // wrapper that cannot be cast back to EditorControl in the host CLR.
                editor=new EditorControl();
                if(!pane.DisplayWindowFromHandlex64(editor.Handle.ToInt64()))throw new InvalidOperationException("SolidWorks could not host the CAD-URDF Maker editor window.");
                editor.Show();
                stage="Initialize editor";editor.Initialize(app);return true;
            }catch(Exception e){
                string diagnostic=StartupFailure(stage,e);
                ReleasePane();
                MessageBox.Show("Startup failed at: "+stage+"\r\n\r\n"+e.Message+diagnostic,"CAD-URDF Maker",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;
            }
        }
        public bool DisconnectFromSW() { ReleasePane();return true; }
        void ReleasePane()
        {
            // Cleanup must also work when startup only created part of the UI.
            try{if(pane!=null)pane.DeleteView();}catch{}finally{pane=null;}
            try{if(editor!=null)editor.Dispose();}catch{}finally{editor=null;app=null;}
        }
        static string StartupFailure(string stage,Exception error)
        {
            try{
                string directory=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"CAD-URDF Maker","Logs");Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"startup.log");
                File.AppendAllText(path,DateTimeOffset.Now.ToString("o")+" | "+stage+"\r\nAssembly: "+typeof(Addin).Assembly.Location+"\r\n"+error+"\r\n\r\n");
                return "\r\n\r\nDiagnostic log: "+path;
            }catch{return "";}
        }
        [ComRegisterFunction]
        public static void Register(Type type)
        {
            using(var key=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\{"+type.GUID.ToString().ToUpperInvariant()+"}")){key.SetValue("",0,RegistryValueKind.DWord);key.SetValue("Title","CAD-URDF Maker");key.SetValue("Description","CAD mass properties, guided joints, live URDF preview, and STL export");}
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\{"+type.GUID.ToString().ToUpperInvariant()+"}"))key.SetValue("",0,RegistryValueKind.DWord);
        }
        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\Addins\{"+type.GUID.ToString().ToUpperInvariant()+"}",false);
            Registry.CurrentUser.DeleteSubKey(@"Software\SolidWorks\AddInsStartup\{"+type.GUID.ToString().ToUpperInvariant()+"}",false);
        }
    }
}
