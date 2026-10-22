# CAD-URDF Maker 1.3.4 verification

## Automated checks

```powershell
.\scripts\Test.ps1 -OutputDirectory build-validation
.\scripts\Release.ps1 -OutputDirectory build-release-1.3.4
.\scripts\Test-Package.ps1 -ReleaseVersion 1.3.4
```

The release verification covers 27 core tests, 11 origin workflow tests, four analytical inertia regressions, two COM interface checks, browser math/motion checks, and 14 package tests plus the three installer verification entrypoints.

Core and origin coverage includes analytical mass/tensor calculations, rigid transforms, repeated instances, overrides, membership/exclusions, tree structure, names, joint limits, projection/confirmation, alignment, project persistence, URDF/STL coordinates, staged export, destination protection, and recoverable replacement.

Inertia cases generate asymmetric tensors from specified principal moments and rotations. A separate analytical box checks body-product sign conventions. The optional editor preview generates box geometry and properties procedurally.

Package tests verify embedded/portable manifests and hashes, extraction, protected destinations, unsafe paths, missing/extra/duplicate files, corruption, script dispatch, error propagation, graphical forms, and verification entrypoints. These checks do not register the plugin or access CAD documents.

## Native deployment checks

After installation, verify task-pane activation, CAD component assignment, axis/point selection, saved-project refresh, origin confirmation, and a complete export in SolidWorks 2024. Compare physical properties with the active native configuration. Confirm that preview motion leaves CAD unchanged. Automated verification does not establish these live deployment checks.
