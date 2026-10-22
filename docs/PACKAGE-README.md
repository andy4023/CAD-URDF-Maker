# CAD-URDF Maker 1.3.4

**Current release: 1.3.4**

A SolidWorks 2024 add-in for native assembly capture, rigid link grouping, joint/origin setup, mass and inertia tracking, embedded motion preview, and URDF/STL export.

## Install

Save your work and close SolidWorks. Windows x64, licensed SolidWorks 2024, .NET Framework 4.8 or later, and Microsoft Edge WebView2 Runtime are required. System prerequisites are installed separately.

- **Standalone setup:** double-click **CAD-URDF Maker-1.3.4-Setup.exe**, accept the administrator prompt, choose a new empty permanent folder, and click **Install**.
- **ZIP:** extract the entire archive into a permanent folder, then double-click **Install.exe**. Do not run installers inside the ZIP.
- **Prepared runtime folder:** move the complete folder to a permanent location, then double-click **Install.exe**.
- **PowerShell:** from elevated 64-bit PowerShell in this runtime folder, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
# Optional automatic startup loading:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -StartWithSolidWorks
```

Reopen SolidWorks and enable **CAD-URDF Maker** under **Tools → Add-Ins**. Keep this entire folder in place after registration. Normal use needs no build tools, account, or web server.

## Uninstall or disable

Close SolidWorks and run **Uninstall.exe** in the registered folder, or run this from elevated 64-bit PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

After unregistration, the folder can be deleted manually. CAD documents, saved projects, and exported robots are retained. For temporary disabling, clear the active and startup checkboxes under **Tools → Add-Ins**.

## Create or continue a robot

Open a saved native assembly/configuration and **Capture zero pose**. Name the robot, group rigid components, choose parent joints, define and confirm link origins, review physical properties and limits, then **Save → Validate → Export robot**. To continue, **Open project** with its `.urdfmaker.json` file, activate the same CAD source/configuration, and **Refresh CAD** before export.

- [Installation and troubleshooting](INSTALLATION.md)
- [Assembly preparation and complete workflows](WORKFLOWS.md)
- [Key functions](KEY-FUNCTIONS.md)
- [Inertia checking](INERTIA-VALIDATION.md)
- [Project format](PROJECT-FORMAT.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

The EXEs are not publisher-signed. `package-manifest.json` lists runtime files and SHA-256 hashes for detecting incomplete or corrupted extraction. Keep all DLLs, editor assets, scripts, licenses, and the manifest together.
