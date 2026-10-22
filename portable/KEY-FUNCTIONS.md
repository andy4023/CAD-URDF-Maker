# CAD-URDF Maker: key functions

This note describes plugin **1.3.4**. See [README.md](README.md) for installation and complete new/saved-project workflows.

## Projects and CAD snapshots

| Control | Function |
| --- | --- |
| **New** | Starts an empty setup. Save the current project first if needed. |
| **Capture zero pose** | Captures component instance identities, placements, physical properties, meshes, and mate suggestions from the active saved native assembly/configuration. Recapture changes the zero reference and requires origin review. |
| **Refresh CAD** | Updates geometry, physical data, and reference validity while retaining original zero placements and matched setup. New/missing components need review. |
| **Save** | Writes editable `.urdfmaker.json` data: names, grouping, origins, references, overrides, exclusions, limits, alignment, and frame visibility. CAD files remain external. |
| **Open project** | Loads the setup, marks CAD stale, and clears preview meshes. Refresh before export. It does not import an existing `.urdf`. |
| **Robot settings** | Sets the XML robot name, export folder, and URDF filename with **Apply robot name** or Enter. Renaming preserves setup. |
| **Cancel capture** | Cancels the running capture and retains the previous project. |

Captured CAD zero, saved setup, and preview motion are separate. Preview positions do not persist as a different mechanical zero. A saved project requires the recorded assembly path/configuration and referenced CAD files.

## Links, membership, and exclusions

**+ Link** creates a rigid body definition. Each link needs a unique valid name and at least one included component. **Root link** chooses the single root. Every other link requires one parent joint, and all links must form a connected tree without cycles.

**Add selected CAD components** assigns CAD instances to the inspected link, moving their membership from other links if necessary. **Add from captured assembly** and a component's **Assigned link** dropdown provide alternatives. One link can contain many rigid parts. Repeated instances have separate identities; subassemblies are not automatically rigid links.

**CAD suggestions** proposes grouping from lock mates and joints from supported motion mates. Review the intended motion, parent/child choice, type, axis, origin, and limits; suggestions are not proof of a valid robot mechanism.

**Create one link per component** in project settings makes an editable starting link for each included unassigned component. Group rigid parts and define parent joints afterward. **Delete link** removes that link and its connected joints; reassess membership and connectivity before export.

**Remove selected from URDF** acts on checked unassigned records; **Remove CAD selection from URDF** reads selected captured instances. Invalid/mixed assigned batches are rejected before changes. The component inspector also offers explicit exclusion. Exclusion affects the robot definition without deleting or suppressing CAD entities.

**Removed / excluded** retains records and supports restoration as unassigned. Removal persists through matching CAD refresh and save/reopen. Restored records may need mesh recapture. Active unassigned records block export. Hidden CAD components still contribute unless explicitly excluded.

**Remap from CAD selection** replaces a missing part with a refreshed, unassigned component selected in CAD. It transfers membership, override/acknowledgment/exclusion, and captured placement, while invalidating affected point/axis references. Refresh and review those references afterward, especially if replacement local coordinates differ.

## Joints and origin confirmation

In a child link's **2. Parent joint** section, select the parent and joint type. **Apply joint name** or Enter changes its unique name while retaining IDs, geometry, confirmations, and preview positions. Default joint names derive from child link names.

| Type | Motion | Required settings |
| --- | --- | --- |
| `fixed` | No relative motion | Confirmed origin/joint; no moving axis or motion limits |
| `revolute` | Bounded rotation | Axis, lower/upper angles, effort, velocity |
| `continuous` | Unbounded rotation | Axis, effort, velocity; no bounded lower/upper limits |
| `prismatic` | Bounded translation | Axis, lower/upper positions, effort, velocity |

The child frame is its joint frame at zero. Define the root first, then work outward. **Define origin / show frame** or **Edit origin** starts a draft for review.

For a moving link, select exactly one CAD reference axis/cylindrical face and **Use selected CAD axis / cylinder**. Select exactly one vertex/sketch point/reference point and **Project selected CAD point onto axis**. This removes perpendicular displacement while retaining the axial station. Optionally use a signed millimetre offset and **Preview offset**, then **Confirm origin** after reviewing direction and position.

Root/fixed links use a point or numeric position directly. Individual 2D/3D sketch points are supported; whole sketches and sketch segments are not point selections. Sketch-local coordinates are converted into model coordinates and then the captured component placement.

Numeric origin/axis anchors for child links use the generated **parent frame**; root numeric positions use captured CAD axes. Positions use metres. A moving numeric origin more than **1 µm** off-axis is rejected. CAD-selected points are explicitly projected and may be off-axis.

For axis anchor `a`, unit direction `d`, selected point `q`, and offset `s` in metres:

```text
origin = a + d * (dot(q - a, d) + s)
```

Editing origins, axes, points, offsets, parents, or joint types can invalidate confirmation. Refresh re-resolves persistent references; changed/missing geometry requires review. Draft/unresolved origins hide combined link CoG/inertia and block export, while assigned mass remains available. Review the joint confirmation checkbox after frame/direction edits.

## Alignment and frames

**Align model → Cartesian alignment** maps signed CAD directions to **+Z up, +X forward, +Y left**. Up and forward must use different axes. Degree RPY input and ±90° buttons support arbitrary orientation; **Restore captured orientation** removes the adjustment. The rotation convention is `Rz(yaw) Ry(pitch) Rx(roll)`.

Alignment affects preview and exported frames, meshes, CoG, and tensors while retaining the root origin. It does not move native CAD or need retessellation. Save to preserve it through reopening/refresh.

**Child axes match root at zero pose** is enabled by default, Children retain joint locations and share root directions at zero, with zero exported joint-origin rotations. Axis vectors, meshes, CoG, and inertia are re-expressed consistently. Disable it for individual child orientations. Moving joints naturally change descendant orientations/positions during motion.

**Show origin frame** controls one triad; **Link frames** controls all and indicates mixed visibility. Visibility persists independently of physics. Main CoG values are relative to **that link's origin**; collapsed **Root-frame comparison** values use the root origin. Parallel axes do not imply identical CoG coordinates.

## Physical properties and overrides

Native capture uses `IMassProperty2`, highest available accuracy, system units, effective CAD mass-property overrides, and hidden-component inclusion. Component CoG and tensors are stored in component coordinates, with inertia about component CoG. Captured rigid transforms locate each instance at zero.

After origin confirmation, component tensors rotate into generated link axes and combine about link CoG with the parallel-axis theorem. Individual component values remain auditable. Exported inertia is about the **link CoG**, expressed in link axes, with inertial `rpy="0 0 0"`.

| Source | Meaning |
| --- | --- |
| **Effective CAD properties** | Uses native material/geometry and effective CAD overrides. Correct CAD materials and refresh to update values. |
| **Uniform density override** | Uniformly scales CAD unit-density solid-body properties by density in kg/m³. Requires a source note. |
| **Complete physical override** | Supplies kg mass, component-local CoG in metres, and all six independent tensor entries in kg·m² about that CoG. Requires a source note. |

Plugin overrides take precedence. A mass-only change cannot establish a complete reliable tensor. Manual off-diagonals are signed tensor entries; native API sign conventions are handled by the adapter. Unknown material/default density requires correction or explicit acknowledgment after verification. Unallocatable assembly/subassembly overrides block export.

Physical checks require positive finite mass, finite CoG, a symmetric positive-definite tensor, and principal moments satisfying the triangle inequality. Refresh CAD after native geometry or property changes to update captured values; review user overrides separately.

## Preview and limits

The embedded viewer offers orbit/zoom, **Fit model**, joint sliders/numeric positions, frame triads, **CoG**, and **Inertia** ellipsoids. **Zero pose** resets preview motion. It does not move the CAD assembly or run physics simulation.

Angular limits/positions display degrees; prismatic limits/positions use metres. Effort uses N·m or N, and velocity uses rad/s or m/s. Moving joints need positive effort/velocity. Bounded ranges must be ordered and include zero. Continuous sliders show a preview interval without imposing bounded URDF limits.

**Reverse axis** reverses direction, maps bounded limits from `[lower, upper]` to `[-upper, -lower]`, reverses the preview position, and invalidates confirmation. **Apply limits** commits entered limits. Preview meshes are independently reduced toward a 500,000-triangle scene budget; this does not alter physical properties or export meshes. Default CAD tessellation is 0.1 mm chord deviation and 5° angular tolerance.

**STL export quality → Apply mesh settings** changes chord deviation (mm) and angular tolerance (degrees). Refresh CAD afterward to regenerate meshes. Positive tolerances are required and the angle must be below 180°; finer tolerances can increase capture time and mesh size.

## Validation and export

**Validate** checks stale CAD, membership/exclusions, empty links, names, materials/overrides, tensors, rigid transforms, origins/joints, references, connectivity/cycles, and limits. Resolve all diagnostics before export.

**Export robot ↗** creates `<robot_name>/<robot_name>.urdf`, link-local STLs, `validation-report.json`, and an output marker. URDF uses SI units with up to 17 significant digits. Visual/collision elements share each link mesh; separate collision decomposition is not generated.

Export validates a staged package before committing it. Replacing recognized output retains a `.previous-…` sibling; arbitrary existing folders are protected. Keep the exported folder together and save the editable `.urdfmaker.json` separately.

## Boundaries

Native assemblies and whole component instances are the source. Independent STEP import, splitting one component across links, closed-chain constraints, physics simulation, ROS scaffolding, and simulator-specific settings are not supported in this release. Scaled/mirrored transforms are rejected. Accuracy depends on the selected configuration, materials, captured zero, and confirmed geometry.
