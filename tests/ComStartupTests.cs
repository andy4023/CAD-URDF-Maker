using System;
using System.Runtime.InteropServices;
using CadUrdfMaker.SolidWorks;
using SolidWorks.Interop.swpublished;

public static class ComStartupTests
{
    [STAThread]
    public static int Main()
    {
        try {
            var addin=new Addin();
            // SetAddinCallbackInfo2 marshals its callback object as IDispatch.
            // This reproduces that conversion without touching a CAD document
            // or requiring the add-in to be registered in the machine registry.
            IntPtr dispatch=Marshal.GetIDispatchForObject(addin);
            try{if(dispatch==IntPtr.Zero)throw new Exception("Callback IDispatch is missing.");}
            finally{if(dispatch!=IntPtr.Zero)Marshal.Release(dispatch);}
            Console.WriteLine("PASS callback object marshals as IDispatch");
            IntPtr unknown=Marshal.GetIUnknownForObject(addin),swInterface=IntPtr.Zero;
            try {
                Guid iid=typeof(ISwAddin).GUID;
                int hr=Marshal.QueryInterface(unknown,ref iid,out swInterface);
                if(hr!=0||swInterface==IntPtr.Zero)Marshal.ThrowExceptionForHR(hr!=0?hr:unchecked((int)0x80004002));
            } finally{if(swInterface!=IntPtr.Zero)Marshal.Release(swInterface);Marshal.Release(unknown);}
            Console.WriteLine("PASS native SolidWorks ISwAddin interface remains available");
            return 0;
        }catch(Exception e){Console.Error.WriteLine("FAIL COM startup: "+e.GetType().Name+": "+e.Message);return 1;}
    }
}
