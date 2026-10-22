# Assembly preparation and export workflows

For CAD-URDF Maker **1.3.4**. See [installation](INSTALLATION.md) and the [function guide](KEY-FUNCTIONS.md).

## Prepare the SolidWorks assembly

1. **Choose a stable assembly/configuration.** Open the top-level native assembly, activate the intended configuration, resolve lightweight components, rebuild, and save. Keep source paths stable: saved projects match the assembly path and configuration.
2. **Set the intended mechanical zero.** Position every movable component at joint zero before capture. Bounded revolute/prismatic limits must include zero. Keep this pose stable while setting up the robot.
3. **Plan rigid links.** List names such as `base_link`, `shoulder_link`, and `forearm_link`. Parts with no relative motion, including their fasteners, belong together. Each included component instance belongs to exactly one link. Subassemblies help organization but are not automatically URDF links. A single multibody component cannot be split across links; separate independently moving geometry into component instances first.
4. **Plan one connected tree.** Choose one root and one parent for every other link. Decide how to represent a closed mechanism as a tree; closed-chain constraints are outside this version. Use fixed joints for separate links that remain rigidly attached.
5. **Prepare selectable references.** For each moving joint, create a named reference axis or identify a cylindrical face. Provide an accessible vertex, reference point, or individual 2D/3D sketch point at the intended axial station. Off-axis points can be projected onto the axis. Root/fixed origins need a point without a moving axis. Clear other selections before each pick.
6. **Use meaningful names and mates.** Distinct component/reference names help identify repeated instances. Lock mates can suggest rigid groups; concentric, hinge, and slider mates can suggest joints. Review the suggestions against the intended mechanism.
7. **Verify materials and mass properties.** Assign correct materials/densities in the active part configurations and review native mass-property overrides. Resolve assembly/subassembly overrides that cannot be allocated to leaf components. Acknowledge default density only after verifying it; documented plugin overrides are available for measured properties.
8. **Plan exclusions and valid geometry.** Identify skeletons, contact helpers, construction geometry, suppressed parts, or unwanted hardware for explicit exclusion in the plugin. **Hiding is insufficient**: hidden components are included in capture. Correct scaled/mirrored instance transforms; the plugin rejects them.

Use **Align model** after capture to establish +Z up; the native assembly does not need remodeling just to change the exported global orientation.

## Workflow: completely new URDF export

1. **Start and capture.** Activate the prepared assembly/configuration. Click **New** if another setup is present, then **Capture zero pose**. Wait for component, property, and mesh capture to finish.
2. **Name and align.** In **Robot settings**, enter a name such as `demo_robot` and click **Apply robot name**. Names use letters/digits/underscores, starting with a letter or underscore; robot names also reject Windows device names. In **Align model**, map CAD directions to **+Z up, +X forward, +Y left**, or apply a rotation in degrees. Keep **Child axes match root at zero pose** enabled for parallel zero-pose link axes.
3. **Group components into links.** Click **+ Link**, name each link, and mark the base as **Root link**. Select a link, select its parts in SolidWorks, and click **Add selected CAD components**. Alternatively use **Add from captured assembly** or each component's **Assigned link** control. Review any CAD grouping/joint suggestions before using them.
4. **Account for every component.** Assign every wanted instance once. Under **Unassigned**, check unwanted components and click **Remove selected from URDF**, or select them in CAD and use **Remove CAD selection from URDF**. Batch removal requires unassigned parts. **Removed / excluded** retains records for restoration.
5. **Confirm the root origin.** Select the root, click **Define origin / show frame**, select exactly one CAD vertex/sketch point/reference point, and click **Use selected CAD vertex / sketch point / reference point**. Numeric position is also available. Review the frame and **Confirm origin**.
6. **Choose each child's parent joint.** In **2. Parent joint**, select **Parent link**, choose `fixed`, `revolute`, `continuous`, or `prismatic`, then click **Create parent joint**. Edit **Joint name** and click **Apply joint name** if needed.
7. **Define child origins.** For a moving joint, select exactly one reference axis or cylindrical face and click **1. Use selected CAD axis / cylinder**. Select exactly one vertex/sketch point/reference point and click **2. Project selected CAD point onto axis**. Optionally set **Offset along selected axis · mm** and **Preview offset**. Review the origin and positive axis direction, then **Confirm origin**. Fixed children use a selected point or numeric origin directly. Numeric child origins/axis anchors use the parent frame and metres; a moving numeric origin must lie on its axis within 1 µm.
8. **Set limits and check movement.** Open **Edit joint limits / direction**, enter limits/effort/velocity, and **Apply limits**. Ensure the joint confirmation checkbox is checked after reviewing its frame, axis, and direction. Exercise **Joint movement** sliders/numeric positions, then use **Zero pose** to reset preview motion. Axis/direction edits require renewed confirmation.
9. **Review physics.** Confirmed origins expose combined link CoG/inertia automatically. Check component properties and sources. Correct CAD materials and **Refresh CAD**, or apply a density/complete override with a source note. CoG is relative to each link's own origin; root-frame comparison uses a different origin.
10. **Save, validate, export.** **Save** a `.urdfmaker.json` project. **Validate**, resolve every diagnostic, and save the corrected setup. Click **Export robot ↗** and select a **parent output folder**. The plugin creates the robot-named subfolder below.

| Quantity | Editor units |
| --- | --- |
| Angular limits / angular preview positions | Degrees |
| Prismatic limits / positions | Metres |
| Effort | N·m for rotation; N for translation |
| Velocity | rad/s for rotation; m/s for translation |
| Numeric origin / axial offset | Metres / millimetres respectively |

```text
demo_robot/
  demo_robot.urdf
  meshes/
    base_link.stl
    ...
  validation-report.json
  .cad-urdf-maker-output
```

URDF uses kilograms, metres, radians, and kg·m². STL vertices are link-local and in metres; visual and collision elements share each link's mesh. Keep the whole export folder together. Replacing recognized output retains a recoverable `.previous-…` sibling; arbitrary existing folders are protected.

## Workflow: continue from a saved project

1. **Activate the original CAD source.** Open the same saved `.SLDASM` path and configuration used by the project. Resolve lightweight parts and rebuild. Retain the original zero pose unless intentionally redefining it.
2. **Open the editable setup.** Click **Open project** and choose the **`.urdfmaker.json`** file. An exported `.urdf` is a downstream robot description, not a resumable setup. Source CAD documents are not embedded in the project.
3. **Refresh CAD.** Opening always marks CAD stale and clears preview meshes. Refresh reloads meshes/properties while retaining captured zero placements, grouping, names, exclusions, overrides, alignment, and limits for matched components. It revalidates referenced axes and points.
4. **Resolve changes.** Assign/exclude new components. For a missing/replaced part, refresh so its replacement appears, leave the replacement unassigned, select exactly that replacement in CAD, select the old record in the plugin, and use **Remap from CAD selection**. Refresh again and review affected references. Alternatively explicitly exclude a component no longer needed. Reselect and confirm missing/changed joint geometry and draft origins.
5. **Continue editing.** Adjust grouping, names, limits, physical overrides, or alignment. Review origins/joints after geometry or topology edits. Confirm all draft origins before export.
6. **Save and regenerate.** **Save**, **Validate**, resolve diagnostics, and **Export robot ↗**. Save again after fixes. Retain the project and source CAD for future work, plus the export folder for downstream use.

Use **Refresh CAD** for ordinary continuation. **Capture zero pose** explicitly recaptures the current CAD pose and requires origin review. The preview's **Zero pose** button only resets motion sliders; preview positions do not persist as a new mechanical zero.

## Common blockers

| Symptom | Action |
| --- | --- |
| Plugin missing / editor fails to load | Check Tools → Add-Ins, installation path, WebView2 Runtime, and `%LOCALAPPDATA%\CAD-URDF Maker\Logs\startup.log`. See installation details. |
| CAD refresh needed / assembly mismatch | Activate the recorded assembly path and configuration, then Refresh CAD. Use New for another assembly. |
| DRAFT origin / no combined inertia | Resolve reference errors, define axis/point geometry as needed, then Confirm origin. |
| Unassigned / unresolved / suppressed component | Assign once, resolve/remap, or explicitly exclude it. Hiding does not exclude it. |
| Point pick fails | Select exactly one individual vertex/sketch point/reference point, rather than a whole sketch, line, or multiple objects. |
| Invalid material / inertia | Verify CAD materials and overrides, refresh, and review plugin overrides/source notes. |
| Invalid tree / limits | Use one root, one parent per child, no cycles, confirmed joints, positive effort/velocity, and bounded ranges containing zero. |

