using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("CAD-URDF Maker Setup")]
[assembly: AssemblyVersion("1.3.4.0")]
[assembly: AssemblyFileVersion("1.3.4.0")]

namespace CadUrdfMaker.Setup
{
    public sealed class PackageFile { public string path; public string sha256; }
    public sealed class PackageManifest { public string version; public PackageFile[] files; }

    // Both the graphical and command-line paths use the packaged registration scripts.
    public static class Package
    {
        public const string ManifestName = "package-manifest.json";
        public static bool Embedded { get { return Assembly.GetExecutingAssembly().GetManifestResourceInfo("payload.zip") != null; } }
        public static string Home { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        public static PackageManifest ReadManifest(string json)
        {
            var manifest = new JavaScriptSerializer().Deserialize<PackageManifest>(json);
            Version version;
            if (manifest == null || !Version.TryParse(manifest.version, out version) || manifest.files == null || manifest.files.Length == 0)
                throw new InvalidDataException("Invalid package manifest.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.files) {
                SafePath(Path.GetTempPath(), file.path);
                if (!paths.Add(file.path) || file.path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) ||
                    file.sha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(file.sha256, "^[A-Fa-f0-9]{64}$"))
                    throw new InvalidDataException("Invalid or duplicate manifest file.");
            }
            foreach (var required in new[] { "CAD-URDF Maker.dll", "CadUrdfMaker.Core.dll", "Install.ps1", "Uninstall.ps1", "Install.exe", "Uninstall.exe", "web/index.html", "web/app.js" })
                if (!paths.Contains(required)) throw new InvalidDataException("Missing package file: " + required);
            return manifest;
        }
        public static string SafePath(string directory, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith("/") ||
                relative.Split('/').Any(p => p == "" || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                throw new InvalidDataException("Unsafe package path: " + relative);
            if (relative.Split('/').Any(p => p.Equals("testcases", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Private testcases cannot be installed.");
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes the install folder.");
            return path;
        }
        static string Hash(Stream stream)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static PackageManifest VerifyArchive(ZipArchive archive)
        {
            var entry = archive.GetEntry(ManifestName);
            if (entry == null) throw new InvalidDataException("The package manifest is missing.");
            PackageManifest manifest;
            using (var reader = new StreamReader(entry.Open())) manifest = ReadManifest(reader.ReadToEnd());
            var expected = manifest.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in archive.Entries) {
                SafePath(Path.GetTempPath(), item.FullName);
                if (!seen.Add(item.FullName)) throw new InvalidDataException("Duplicate archive entry: " + item.FullName);
                if (item.FullName == ManifestName) continue;
                PackageFile file;
                if (!expected.TryGetValue(item.FullName, out file)) throw new InvalidDataException("Unexpected package file: " + item.FullName);
                using (var input = item.Open())
                    if (!Hash(input).Equals(file.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package checksum failed: " + item.FullName);
            }
            if (seen.Count != expected.Count + 1) throw new InvalidDataException("The package is incomplete.");
            return manifest;
        }
        public static PackageManifest VerifyFolder(string directory)
        {
            var manifest = ReadManifest(File.ReadAllText(Path.Combine(directory, ManifestName)));
            foreach (var file in manifest.files) {
                using (var input = File.OpenRead(SafePath(directory, file.path)))
                    if (!Hash(input).Equals(file.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package checksum failed: " + file.path + ". Extract a fresh copy of the package.");
            }
            return manifest;
        }
        public static PackageManifest Manifest()
        {
            if (!Embedded) return ReadManifest(File.ReadAllText(Path.Combine(Home, ManifestName)));
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read)) return VerifyArchive(archive);
        }
        public static void Extract(ZipArchive archive, string destination)
        {
            VerifyArchive(archive);
            string directory = Path.GetFullPath(destination);
            // Never merge into an existing nonempty directory, including a previous install.
            // This prevents replacing personal files or an installation needed for rollback.
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new IOException("Choose a new, empty folder. To reuse an installation, run Install.exe from that folder.");
            // Do not install through directory junctions/symlinks into an unrelated location.
            for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a folder without directory junctions or symbolic links.");
            Directory.CreateDirectory(directory);
            foreach (var entry in archive.Entries) {
                string path = SafePath(directory, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.Open())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
            }
            VerifyFolder(directory);
        }
        public static void CheckPrerequisites()
        {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Windows x64 is required.");
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                if (key == null || Convert.ToInt32(key.GetValue("Release", 0)) < 528040)
                    throw new InvalidOperationException("Install Microsoft .NET Framework 4.8 or later, then run Setup again.");
            if (Process.GetProcessesByName("SLDWORKS").Length != 0)
                throw new InvalidOperationException("Save your work and close SolidWorks before installing or unregistering the plugin.");
        }
        public static void RunScript(string directory, bool uninstall, bool startup)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            string script = Path.Combine(directory, uninstall ? "Uninstall.ps1" : "Install.ps1");
            if (script.Contains('"')) throw new InvalidOperationException("Invalid installation folder.");
            var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"" + (!uninstall && startup ? " -StartWithSolidWorks" : "")) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
            };
            using (var process = Process.Start(start)) {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Registration did not complete.\r\n\r\n" + output.Result + error.Result);
            }
        }
    }

    public sealed class SetupForm : Form
    {
        readonly TextBox folder;
        readonly CheckBox startup;
        readonly Button action;
        readonly Label status;
        readonly bool uninstall;
        bool working;

        public SetupForm(bool remove)
        {
            uninstall = remove;
            var manifest = Package.Manifest();
            Text = "CAD-URDF Maker " + manifest.version + (remove ? " · Unregister" : " · Setup");
            ClientSize = new Size(620, 348);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(new Label { Text = remove ? "Unregister CAD-URDF Maker" : "Install CAD-URDF Maker " + manifest.version, Font = new Font("Segoe UI", 18, FontStyle.Bold), Bounds = new Rectangle(24, 22, 572, 42) });
            Controls.Add(new Label { Text = remove ? "Close SolidWorks, then remove the plugin registration.\r\nYour CAD files, saved projects and exports are retained." : "SolidWorks 2024 · Windows x64 · .NET Framework 4.8\r\nMicrosoft Edge WebView2 Runtime is required for the editor.", Bounds = new Rectangle(26, 76, 568, 54) });
            Controls.Add(new Label { Text = Package.Embedded ? "Install to a new, empty folder:" : "Plugin folder (keep this folder in place):", Bounds = new Rectangle(26, 145, 560, 24) });
            folder = new TextBox { Text = Package.Embedded ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CAD-URDF Maker", manifest.version) : Package.Home, ReadOnly = true, Bounds = new Rectangle(26, 175, 460, 29) };
            Controls.Add(folder);
            if (Package.Embedded) {
                var browse = new Button { Text = "Browse…", Bounds = new Rectangle(496, 173, 96, 31) };
                browse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = "Choose a new, empty installation folder", SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
                Controls.Add(browse);
            }
            startup = new CheckBox { Text = "Load automatically when SolidWorks starts", Checked = false, Visible = !remove, Bounds = new Rectangle(26, 217, 560, 30) };
            Controls.Add(startup);
            status = new Label { Text = "Save your work and close SolidWorks before continuing.", Bounds = new Rectangle(26, 262, 402, 66) };
            Controls.Add(status);
            action = new Button { Text = remove ? "Unregister" : "Install", Bounds = new Rectangle(462, 278, 130, 38) };
            action.Click += async (s, e) => await Install();
            Controls.Add(action);
            AcceptButton = action;
            FormClosing += (s, e) => { if (working) e.Cancel = true; };
        }
        async Task Install()
        {
            working = true;
            foreach (Control control in Controls) control.Enabled = false;
            status.Text = uninstall ? "Removing registration…" : "Checking package and installing…";
            string destination = folder.Text;
            bool load = startup.Checked;
            try {
                await Task.Run(() => {
                    Package.CheckPrerequisites();
                    if (Package.Embedded) {
                        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                        using (var archive = new ZipArchive(input, ZipArchiveMode.Read)) Package.Extract(archive, destination);
                    }
                    Package.VerifyFolder(destination);
                    Package.RunScript(destination, uninstall, load);
                });
                status.Text = uninstall ? "Registration removed. You may now delete the plugin folder." : "Installed. Open SolidWorks → Tools → Add-Ins → CAD-URDF Maker.";
                action.Text = "Close";
                action.Enabled = true;
                // Replace the async action with Close after success.
                AcceptButton = null;
                action.Hide();
                var close = new Button { Text = "Close", Bounds = action.Bounds };
                close.Click += (s, e) => Close();
                Controls.Add(close);
                AcceptButton = close;
            } catch (Exception error) {
                status.Text = "No success was reported. Review the error and try again.";
                MessageBox.Show(this, error.Message + (Package.Embedded ? "\r\n\r\nIf files were extracted, keep that folder and run its Install.exe to retry registration." : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                foreach (Control control in Controls) control.Enabled = true;
            } finally { working = false; }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try {
                // Read-only smoke check for build automation; never registers the plugin.
                if (args.Length == 1 && args[0] == "--verify-package") {
                    if (Package.Embedded) Package.Manifest(); else Package.VerifyFolder(Package.Home);
                    return 0;
                }
                if (args.Length != 0) throw new ArgumentException("For command-line installation, use Install.ps1 or Uninstall.ps1.");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var identity = WindowsIdentity.GetCurrent()) {
                    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) {
                        try { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = true, Verb = "runas" }); }
                        catch (System.ComponentModel.Win32Exception error) { if (error.NativeErrorCode != 1223) throw; }
                        return 0;
                    }
                }
                Application.Run(new SetupForm(Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location).Equals("Uninstall", StringComparison.OrdinalIgnoreCase)));
                return 0;
            } catch (Exception error) {
                if (args.Contains("--verify-package")) return 1;
                MessageBox.Show(error.Message, "CAD-URDF Maker Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
