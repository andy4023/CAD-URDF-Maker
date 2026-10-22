# Build CAD-URDF Maker 1.3.4

Run PowerShell from the source folder containing `src/`, `web/`, and `scripts/`.

## Build prerequisites

- Windows x64 with SolidWorks 2024 and its API interop libraries installed.
- .NET Framework 4.8, including the x64 C# compiler at the standard Windows framework path.
- Node.js and Python available on PATH.
- Network access for the initial dependency download.

Runtime prerequisites and registration are described in [INSTALLATION.md](INSTALLATION.md).

## Dependencies and build

```powershell
.\scripts\Get-Dependencies.ps1
.\scripts\Build.ps1 -OutputDirectory build
```

Dependency versions are pinned: WebView2 SDK 1.0.2903.40, TypeScript 5.7.2, and Three.js 0.170.0. Downloads populate ignored dependency directories. The build generates a local strong-name key if needed; keep it out of source control.

The default SolidWorks path is `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS`. Override it when necessary:

```powershell
.\scripts\Build.ps1 -SolidWorksRoot 'D:\SOLIDWORKS' -OutputDirectory build
```

The output includes the add-in, runtime dependencies, embedded editor, documentation, Install/Uninstall tools, and a package manifest. To register this build, close SolidWorks and use its Install.exe or Install.ps1 as described in the installation guide. Keep the build output in place while registered.

## Tests and release packages

```powershell
.\scripts\Test.ps1 -OutputDirectory build-validation
.\scripts\Release.ps1 -OutputDirectory build-release-1.3.4
.\scripts\Test-Package.ps1 -ReleaseVersion 1.3.4
```

Release reads the compiled add-in version and writes the setup EXE, runtime ZIP, and SHA-256 list to `dist/`. It packages an explicit runtime file allowlist and verifies the embedded payload before writing distribution files. Package tests do not register the plugin.

To prepare the extracted `portable/` option, extract the complete generated runtime ZIP into that folder. Refresh it whenever distribution packages are rebuilt. Keep source files and generated runtime files separate.

## Editor preview

```powershell
.\scripts\Serve-Preview.ps1
```

Open `http://127.0.0.1:8765/build/viewer-harness.html`. The helper generates a box-based robot with analytical physical properties and a simulated native bridge. It does not require CAD documents and does not replace an in-process SolidWorks deployment check.

## Repository contents

Include source, scripts, analytical tests, documentation, and the current `dist/` files. The prepared `portable/` folder is an installation convenience and is ignored by Git because its files are already in the runtime ZIP. Dependency caches, build/test scratch outputs, local signing keys, CAD inputs, and exported robot projects are also ignored. See [VALIDATION.md](VALIDATION.md) for verification coverage.
