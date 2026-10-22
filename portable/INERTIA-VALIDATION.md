# Checking inertia in CAD-URDF Maker

## Compare the same source and frame

1. Activate the assembly configuration used by the saved project and select the component instance under review.
2. In SolidWorks, open **Evaluate → Mass Properties** and use kilograms, metres, and kg·m² with high accuracy and sufficient displayed precision.
3. Check material assignments and **Override Mass Properties**. In the plugin, review whether the effective source is CAD, a density override, or a complete override.
4. Click **Refresh CAD** before comparing a saved snapshot with current CAD. Installing an update does not recalculate saved physical data.
5. Compare principal moments at the centre of mass. General-axis diagonal entries are not necessarily principal moments. Coordinate rotations preserve the principal moments while changing tensor entries.

## Physical checks

Mass must be positive and finite. The tensor about CoG must be finite, symmetric, and positive definite. Sort its principal moments as `Imin <= Imid <= Imax`; physical inertia requires `Imax <= Imin + Imid`, allowing numerical roundoff. Validation reports values and excess for triangle-inequality failures.

Component properties are expressed in component axes; combined link properties use generated link axes and the link CoG. Use the same reference frame and reference point when comparing tensors. A tensor about another point includes a parallel-axis contribution.

## Native API conventions

The mass-property tensor API and body product-of-inertia API use distinct off-diagonal conventions. The plugin's adapters handle those conventions separately. Enter signed tensor off-diagonals for a complete plugin override, with CoG in component coordinates and inertia about that CoG.

## Analytical regression inputs

`tests/InertiaTests.cs` constructs six asymmetric tensors from explicit principal moments and rotations, then checks sign retention, principal values, frame invariance, and an incorrect off-diagonal sign conversion. A separate analytical box checks the body-product adapter. These examples contain no measured component records.
