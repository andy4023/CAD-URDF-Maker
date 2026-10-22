# CAD-URDF Maker 1.3.4 project format

The editable `.urdfmaker.json` project is UTF-8 JSON with `SchemaVersion: 1`. Unsupported schema versions are rejected. `RobotProject` in `src/Core/Models.cs` defines the persisted structure; `web/types.ts` mirrors editor messages. Stored dimensional values use SI units.

## Records and names

| Record | Contents |
| --- | --- |
| Project | Robot name, assembly path/configuration, capture timestamp, CAD stamp, root, links, joints, components, alignment, mesh settings, suggestions, CAD issues |
| Component | Persistent instance ID/reference, source path/configuration, captured placement, CAD and unit-density properties, override/provenance, material acknowledgment, exclusion, visibility, resolution |
| Link | Unique ID/name, component membership, captured zero frame, origin definition, frame visibility |
| Joint | Unique ID/name, parent/child IDs, type, axis, limits, confirmation, persistent axis/face reference and local geometry |
| Physical properties | Positive mass, three-component CoG, symmetric row-major 3×3 tensor about CoG, source note |

Robot name controls the export folder, URDF filename, and XML robot name. Link and joint names must each be unique. Names use letters/digits/underscores, starting with a letter or underscore; robot names also reject Windows device names. Stable IDs preserve topology and bindings during renaming.

## Frames and properties

Rigid transforms are row-major 4×4 matrices using column vectors and translation at indices 3, 7, and 11. The native adapter converts SolidWorks transform ordering by evaluating transformed basis points. Component CoG/tensors remain in component axes; tensors are about component CoG.

`Alignment` contains `Enabled` and radian `Rpy`. The CAD-to-robot rotation is `Rz(yaw) Ry(pitch) Rx(roll)`. Alignment retains the captured root origin. `ChildFramesFollowRoot` defaults to true: children retain their positions and use root orientation at zero. Generated axes are re-expressed from stored child axes, and meshes/properties use the effective frames. With the policy enabled, exported joint-origin rotations are zero.

Every included component belongs to one link; excluded components belong to none. The root has no incoming joint, every other link has one, and all links form a connected tree. Preview positions do not redefine captured zero placements.

`Component.Excluded` retains CAD identity and snapshots while removing the component from preview/export. Refresh preserves the flag for matched components. Restoration returns an unassigned record and can require mesh recapture.

## Origins and references

`Link.Origin` records mode (`draft`, `numeric`, `cad-point`), confirmation/reference validity, associated joint, point reference/component/local coordinates, source point in captured CAD coordinates, axis anchor/direction, and signed offset in metres. Missing metadata produces a draft.

Sketch-point coordinates are converted from sketch space into model coordinates and through the captured component placement. Refresh re-resolves persistent points and axes and invalidates changed/missing references. Numeric child input uses the generated parent frame; root input uses captured CAD axes. Moving origins use `a + d * (dot(q-a,d) + offset)` with unit direction `d`. Numeric moving points must be within 1 µm of the axis; CAD points are projected explicitly.

`OriginWorkflow` controls projection, confirmation, readiness, and combined link properties. Draft origins hide combined CoG/inertia; assigned mass remains available. Origin/joint geometry edits require renewed confirmation, while frame visibility is independent. Refresh updates CAD snapshots and opening marks a project stale. CAD refresh retains the original captured zero placements for matching components; recapture explicitly changes them.

## Editor bridge

Project commands include `ready`, `new`, `capture`, `refresh`, `save`, `open`, `export`, `validate`, `selection`, `cancel`, `update`, `axis`, and `remap`. Scope commands are `remove-unassigned`, `remove-unassigned-cad`, and `restore-components`; invalid/mixed batch removal is rejected atomically.

Origin commands operate on link IDs: `origin-begin`, `origin-axis`, `origin-point`, `origin-numeric-axis`, `origin-position`, `origin-offset`, and `origin-confirm`. Host messages include `state`, `progress`, `notice`, `error`, `validation`, and `selection`. State includes properties only for ready origins, plus origin errors and assigned mass.

The host owns CAD identities, geometry references, and physical snapshots. Editor updates can modify robot definitions and documented overrides, not source snapshots. Only the bundled `https://cad-urdf.local/` document may issue bridge commands. Meshes use a separate local virtual hostname. No remote API or account is required.
