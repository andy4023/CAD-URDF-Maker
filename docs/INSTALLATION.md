# Install and uninstall CAD-URDF Maker 1.3.4

## Requirements

- Windows x64 and licensed SolidWorks 2024.
- .NET Framework 4.8 or later and Microsoft Edge WebView2 Runtime, installed separately.
- Administrator rights for plugin registration/unregistration.

Save your work and close SolidWorks first. Normal use needs no compiler, Node.js, Python, account, or separately hosted editor. The EXEs are not publisher-signed. Package manifests and download checksums detect damaged or incomplete files.

## Method 1: standalone setup

1. Double-click **CAD-URDF Maker-1.3.4-Setup.exe** from the release `dist/` folder.
2. Accept the administrator prompt.
3. Select a **new, empty permanent folder** and click **Install**. The complete runtime is embedded in the EXE. A nonempty destination is refused.
4. Optionally enable **Load automatically when SolidWorks starts**.
5. Reopen SolidWorks, go to **Tools → Add-Ins**, and enable **CAD-URDF Maker**.

If setup extracts files but registration fails, review the error and retry with **Install.exe** inside the extracted folder.

## Method 2: ZIP or prepared portable folder

1. Use **Extract All** on **CAD-URDF Maker-1.3.4.zip** into a permanent folder, or move the complete release `portable/` folder to a permanent location.
2. Double-click **Install.exe** in that folder, accept the administrator prompt, and click **Install**.
3. Reopen SolidWorks and enable the add-in under **Tools → Add-Ins**.

Do not install from inside the ZIP. Keep the full registered folder together: DLLs, `web/`, licenses, manifest, and registration tools are required. Moving it after registration requires registration from the new location.

## Method 3: PowerShell

Open **64-bit PowerShell as administrator** and change to the extracted/prepared runtime folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

For automatic loading at startup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -StartWithSolidWorks
```

The execution-policy setting applies only to the launched PowerShell process. The graphical installers use these same registration scripts. Startup loading can also be changed under **Tools → Add-Ins**.

## Method 4: build and register from source

Follow the development guide supplied with the source folder to obtain dependencies and build the plugin. Close SolidWorks, then double-click **Install.exe** in the chosen build output, or run its **Install.ps1** in elevated 64-bit PowerShell. Keep that output folder in place while registered. Development tools are needed to build; they are not required for ordinary plugin use.

## Uninstall

1. Save your work and close SolidWorks.
2. Double-click **Uninstall.exe** in the currently registered folder and complete **Unregister**, or run this from elevated 64-bit PowerShell in that folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

3. After unregistration, delete the installation folder manually if desired.

CAD documents, `.urdfmaker.json` projects, and exported robot folders are retained. Keep registration files until unregistration completes.

To disable temporarily, clear the active checkbox under **Tools → Add-Ins**. Clear the startup checkbox too if it should remain disabled next session. This leaves the plugin installed.

## Troubleshooting

| Problem | Action |
| --- | --- |
| Installer requests an empty folder | Choose a new folder, or use Install.exe inside a complete existing runtime folder. |
| SolidWorks is running | Save CAD work, close SolidWorks, and retry. |
| Registration denied | Use the administrator prompt or elevated 64-bit PowerShell. |
| Hash or manifest failure | Extract a fresh, complete ZIP or obtain a fresh setup EXE. |
| Add-in missing | Verify registration and its stable path, then check Tools → Add-Ins. |
| Editor initialization fails | Install/repair WebView2 Runtime and reopen the add-in. |
| Startup failure | Review `%LOCALAPPDATA%\CAD-URDF Maker\Logs\startup.log`. |

After enabling the add-in, follow [WORKFLOWS.md](WORKFLOWS.md) to prepare CAD and create or continue a robot project.
