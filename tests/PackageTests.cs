using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CadUrdfMaker.Setup;

public static class PackageTests
{
    static int passed;
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; }
        throw new Exception("Unsafe or incomplete package was accepted.");
    }
    static MemoryStream Alter(string original, string remove = null, string add = null, string corrupt = null)
    {
        var result = new MemoryStream();
        using (var input = ZipFile.OpenRead(original))
        using (var output = new ZipArchive(result, ZipArchiveMode.Create, true)) {
            foreach (var entry in input.Entries) {
                if (entry.FullName == remove) continue;
                using (var source = entry.Open())
                using (var destination = output.CreateEntry(entry.FullName).Open()) {
                    if (entry.FullName == corrupt) destination.WriteByte(42); else source.CopyTo(destination);
                }
            }
            if (add != null) using (var extra = output.CreateEntry(add).Open()) extra.WriteByte(42);
        }
        result.Position = 0;
        return result;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try {
            string zip = args[0], scratch = args[1];
            Test("embedded setup manifest and runtime hashes", () => { if (!Package.Embedded || Package.Manifest().version != args[2]) throw new Exception("Wrong embedded version."); });
            Test("portable ZIP integrity", () => { using (var archive = ZipFile.OpenRead(zip)) Package.VerifyArchive(archive); });
            Test("fresh extraction matches all manifest hashes", () => { using (var archive = ZipFile.OpenRead(zip)) Package.Extract(archive, Path.Combine(scratch, "fresh")); Package.VerifyFolder(Path.Combine(scratch, "fresh")); });
            Test("refuse nonempty destination and preserve its contents", () => {
                string directory = Path.Combine(scratch, "personal"); Directory.CreateDirectory(directory);
                string sentinel = Path.Combine(directory, "keep.txt"); File.WriteAllText(sentinel, "keep");
                using (var archive = ZipFile.OpenRead(zip)) Reject(() => Package.Extract(archive, directory));
                if (File.ReadAllText(sentinel) != "keep" || Directory.GetFileSystemEntries(directory).Length != 1) throw new Exception("Existing directory changed.");
            });
            Test("reject traversal, absolute, alternate-stream and private paths", () => {
                foreach (var path in new[] { "../model.sldasm", "web/../../secret", "C:/model", "/model", "web\\model", "file:stream", "web//app.js", "testcases/model.sldasm", "web/TESTCASES/model.stl", "web./app.js" })
                    Reject(() => Package.SafePath(scratch, path));
            });
            Test("reject unexpected CAD file before creating destination", () => {
                string destination = Path.Combine(scratch, "must-not-exist");
                using (var stream = Alter(zip, add: "testcases/private.sldasm"))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) Reject(() => Package.Extract(archive, destination));
                if (Directory.Exists(destination)) throw new Exception("Invalid archive caused extraction.");
            });
            Test("reject unlisted file", () => { using (var stream = Alter(zip, add: "debug.log")) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) Reject(() => Package.VerifyArchive(archive)); });
            Test("reject missing runtime dependency", () => { using (var stream = Alter(zip, remove: "WebView2Loader.dll")) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) Reject(() => Package.VerifyArchive(archive)); });
            Test("reject corrupted script", () => { using (var stream = Alter(zip, corrupt: "Install.ps1")) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) Reject(() => Package.VerifyArchive(archive)); });
            Test("reject duplicate archive entry", () => { using (var stream = Alter(zip, add: "Install.ps1")) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) Reject(() => Package.VerifyArchive(archive)); });
            Test("detect modified extracted runtime", () => {
                string directory = Path.Combine(scratch, "fresh");
                File.AppendAllText(Path.Combine(directory, "web", "app.js"), "corruption");
                Reject(() => Package.VerifyFolder(directory));
            });
            Test("graphical actions use x64 PowerShell and preserve startup option", () => {
                string directory = Path.Combine(scratch, "script path with spaces"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "Install.ps1"), "param([switch]$StartWithSolidWorks)\r\n[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'result.txt'),([Environment]::Is64BitProcess.ToString() + ':' + $StartWithSolidWorks.IsPresent.ToString()))");
                File.WriteAllText(Path.Combine(directory, "Uninstall.ps1"), "[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'result.txt'),'unregistered')");
                Package.RunScript(directory, false, true);
                if (File.ReadAllText(Path.Combine(directory, "result.txt")) != "True:True") throw new Exception("Startup installation arguments changed.");
                Package.RunScript(directory, false, false);
                if (File.ReadAllText(Path.Combine(directory, "result.txt")) != "True:False") throw new Exception("Default installation arguments changed.");
                Package.RunScript(directory, true, false);
                if (File.ReadAllText(Path.Combine(directory, "result.txt")) != "unregistered") throw new Exception("Wrong removal script.");
            });
            Test("registration errors propagate to the graphical installer", () => {
                string directory = Path.Combine(scratch, "script failure"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "Install.ps1"), "$ErrorActionPreference='Stop'; throw 'Expected registration failure'");
                try { Package.RunScript(directory, false, false); }
                catch (InvalidOperationException error) { if (error.Message.Contains("Expected registration failure")) return; throw; }
                throw new Exception("Failed registration was reported as success.");
            });
            Test("standalone, portable install and unregister forms render", () => {
                Application.EnableVisualStyles();
                var portable = System.Reflection.Assembly.LoadFrom(Path.Combine(scratch, "fresh", "Install.exe"));
                for (int mode = 0; mode < 3; mode++) {
                    bool uninstall = mode == 2;
                    using (var form = mode == 0 ? new SetupForm(false) : (Form)Activator.CreateInstance(portable.GetType("CadUrdfMaker.Setup.SetupForm"), new object[] { uninstall })) {
                    if (!form.Controls.OfType<Button>().Any(b => b.Text == (uninstall ? "Unregister" : "Install"))) throw new Exception("Missing action button.");
                    // Create native child handles for an actual WinForms layout render,
                    // off-screen so the test never interrupts the user's desktop.
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-10000, -10000);
                    form.Show();
                    Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(scratch, mode == 0 ? "setup.png" : uninstall ? "unregister.png" : "portable-install.png"));
                    }
                    form.Hide();
                    }
                }
            });
            Console.WriteLine(passed + " package tests passed. No registration or CAD access performed.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
