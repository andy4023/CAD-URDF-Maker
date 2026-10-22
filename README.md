# CAD-URDF Maker 1.3.4

**Current release: 1.3.4**

A SolidWorks 2024 add-in for creating URDF robots from native assemblies. Group rigid parts into links, define and confirm joint origins, inspect mass/CoG/inertia, preview motion, and export URDF with STL meshes.
**Only tested in SolidWorks 2024**

## Start here

| Installation choice | Open or run |
| --- | --- |
| Standalone setup | [CAD-URDF Maker-1.3.4-Setup.exe](dist/CAD-URDF%20Maker-1.3.4-Setup.exe) |
| ZIP with graphical or PowerShell installation | [CAD-URDF Maker-1.3.4.zip](dist/CAD-URDF%20Maker-1.3.4.zip) — extract completely first |
| Extracted files in the local release folder | `portable/Install.exe` — keep the whole folder in a permanent location |
| Build from source | [Development guide](docs/DEVELOPMENT.md) |

All prebuilt options contain the same 1.3.4 plugin. Compare downloaded EXE/ZIP hashes with [SHA256SUMS](dist/CAD-URDF%20Maker-1.3.4-SHA256SUMS.txt) if needed.

**Requirements:** Windows x64, licensed SolidWorks 2024, .NET Framework 4.8 or later, and Microsoft Edge WebView2 Runtime. Save your work and close SolidWorks before installing or uninstalling. Administrator rights are required for registration. Normal operation is local and requires no build tools or web server.

1. Install with one of the methods above.
2. Reopen SolidWorks and enable **CAD-URDF Maker** under **Tools → Add-Ins**.
3. Open and prepare a saved native assembly, then **Capture zero pose**.
4. Group components, define parent joints, confirm origins, review limits/properties, then **Save → Validate → Export robot**.

## Guides

- [Install, uninstall, disable, and troubleshoot](docs/INSTALLATION.md)
- [Assembly preparation, new export, and saved-project workflows](docs/WORKFLOWS.md)
- [Key plugin functions](docs/KEY-FUNCTIONS.md)
- [Check mass and inertia](docs/INERTIA-VALIDATION.md)
- [Build and package from source](docs/DEVELOPMENT.md)
- [Project format and editor bridge](docs/PROJECT-FORMAT.md)
- [Verification coverage](docs/VALIDATION.md)

## Folder layout

```text
CAD-URDF-Maker-1.3.4/
  README.md
  dist/          Standalone setup, portable ZIP, and download checksums
  portable/      Complete extracted runtime with Install/Uninstall tools
  docs/          User guides and developer reference
  src/           Core, SolidWorks add-in, and installer source
  web/           Embedded editor source and compiled entry points
  scripts/       Build, test, packaging, and registration scripts
  tests/         Analytical regression tests and generated preview fixture
  package.json
  THIRD-PARTY-NOTICES.md
```

The `portable/` files must stay together after registration. The setup EXE embeds its runtime files. System prerequisites are installed separately. Executables are not publisher-signed; hashes detect corruption.

## Export and scope

An export contains `<robot_name>.urdf`, `meshes/`, and a validation report inside a robot-named folder. Save the separate `.urdfmaker.json` project and retain its source assembly for continued editing. Keep the exported folder together for downstream use.

Supported joints are fixed, revolute, continuous, and prismatic. Native assemblies and whole component instances are the CAD source. STEP import, splitting one component across links, closed-chain constraints, physics simulation, ROS scaffolding, and separate collision decomposition are not supported in this release.

## Uploading the folder

Upload this folder's contents to your GitHub repository. Include the hidden `.gitignore` and `.gitattributes` files. All guide links use local paths; no repository URL is required. Dependency caches, generated build outputs, and signing keys are excluded from source tracking. See [third-party notices](THIRD-PARTY-NOTICES.md) for bundled component licenses.
