# Custom flag motion and embedded dependency research

Report language: English. Date: 22 October 2026. Investigation ongoing.

**Portable map-only animated custom flags remain unresolved.** The native flag's
separate Item, Prefab, DynaObject and Mesh archives can be reproduced and loaded
from an embedded ZIP, but that does not initialize the renderer's structured
vertex buffers. Whole-flag circular motion is demonstrated separately from cloth
waving. Their simultaneous operation is not yet demonstrated.

## Evidence and correction

[Download the kinematic rotation recording](../artifacts/flag-motion-embedded-20261022/kinematic/video-slow/kinematic-rotation-cloth-frozen.mp4).
It shows the stationary control, then the moving flag. Captions explicitly identify
the frozen cloth and the required game-directory installation. The recording is
1920 × 1080, 31.4833 seconds, 1,858 captured frames, approximately 59.0 fps.
No interpolation, generated frames or speed changes were applied. SHA-256:
`9EFB6C51EA0F7C11E2C7E59B09114C2555CD5D9B2013C619B4D27476ED8A3FFC`.

An initial interpretation of this experiment incorrectly claimed rotation and
waving together. The stationary kinematic control disproved that interpretation:
its cloth silhouette stays frozen. The prematurely labelled video is quarantined
under `rejected-premature-label/` and must not be used as success evidence.
Distinct compressed video frames are insufficient evidence of cloth deformation;
lighting and compression can change pixels without moving the cloth.

## Reproducing the native dependency files

The split-archive experiment preserves the accepted swallowtail geometry, all
five visual nodes and their 86-frame animation tables. Its dependency chain is:

`Item.Gbx → Prefab.Gbx → DynaObject.Gbx → Mesh.Gbx`.

References between the custom archives are ordinary relative references with
ancestor level zero. The mesh retains the late native MaterialUserInst link.
The generator is `Tests/FlagProbe/FlagExternalGraph.cs`; accepted generated files
are under `artifacts/flag-motion-embedded-20261022/external-graph/files-v2/`.

| Test | Observed result |
| --- | --- |
| Loose user-folder split graph | All dependencies load; 0/5 structured visual flags |
| Embedded split graph in the initial session | All dependencies load; 0/5 |
| Cold process PID 6416, loose custom graph removed | Embedded item loads; 0/5 |
| Explicit embedded Mesh preload | Returns the same mesh pointer used by the embedded item |

The embedded FID prefix is
`fake:MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/TMIS_ExternalGraph/`.
The disposable map deliberately has zero anchors. Failed candidates were inspected
before drawing and were not placed. This establishes dependency transport and
native loading, not successful embedded rendering. Receipts are in
`external-graph/live3/` and `cold2/` under the experiment's evidence directory.

The earlier game-directory installation control is still valid: custom long,
swallowtail and round outlines wave after restart without Openplanet when their
three accepted Item.Gbx files are installed. Every client needs those files.
See [the installation research](custom-flag-file-followup-2026-10-22.md).

## Whole-flag motion tests

`Tests/FlagProbe/FlagMotionResearch.cs` constructs two-body controls from the
accepted installed swallowtail: a pole body and a cloth body. Geometry is shifted
to a shared pivot with a six-metre orbit radius, including every animation frame
and LOD. These are installation-context diagnostic fixtures, not portable items
or production exports. Their tiny diagnostic collision surfaces do not establish
vanilla collision parity.

The standard kinematic graph contains two bodies and two world-relative
constraints. Its rotation period is 12 seconds. Both bodies use identical timing.

| Fixture | Whole-flag motion | Cloth deformation |
| --- | --- | --- |
| Standard kinematic constraints, phase bounds 0/0 | Circular motion and rotation observed | Frozen in the stationary control |
| Same graph, phase bounds 0/1 | Fixture accepted | Stationary control still frozen |
| Non-kinematic bodies, LocAnim version 2 | Requested rotation ignored | Waving observed |
| LocAnim version 3, 360°, function 0, visual | Requested rotation ignored | Waving observed |
| Same version 3, physical motion enabled | Requested rotation ignored | Waving observed |
| LocAnim version 3, native-example function 1, visual | Requested rotation ignored | Waving observed |
| Same function 1, physical motion enabled | Requested rotation ignored | Waving observed |
| Original constraints retained, only body IsKinematic changed to false | Requested rotation ignored | Waving observed |

The function-1 tests ran in fresh PID 13192 with no dedicated flag helper plugin.
Both models automatically initialized 5/5 structured visuals before drawing.
Fixed-camera twelve-second recordings cover a complete requested rotation period.
The foreground pole remains fixed while the cloth changes shape. Evidence:
`local-motion-v3-func1/cold1/{stationary-control,physical-control}.mp4`.

The final one-field control ran in fresh PID 6012. It retains the complete
four-entry constraint graph and phase bounds 0/1, changing only the body
`IsKinematic` flag relative to the phase-range control (plus fixture identity).
Both models initialize 5/5 structured visuals. The stationary control waves, and
the second flag also waves while its pole stays fixed throughout the 15-second
recording. Thus switching that flag is not a combined-motion fix. Evidence:
`nonkinematic-constraints/cold1/`, including `orbit-motion-samples.jpg` and
[the labelled video](../artifacts/flag-motion-embedded-20261022/nonkinematic-constraints/cold1/nonkinematic-cloth-waves-motion-ignored.mp4).
That video is 1920 × 1080, 15 seconds, 882 captured frames, approximately
58.8 captured fps. Every captured presentation timestamp is preserved exactly
in the captioned export. SHA-256:
`1257BB5D489BF65AA2B6A082A0243B7ADE44A22AE86675921800E7A81D281F21`.

For the frozen kinematic control, a 390 × 340 cloth-region mask varied by only
48 pixels at most over the measured interval, with area 11,968–12,006. The
non-kinematic waving control varied by as many as 16,603 pixels, with area
6,736–18,063. These are controlled silhouette comparisons, supported by the
retained sample images, rather than a compressed-frame uniqueness claim.
Changing the phase maximum from zero to one did not restore waving.

Read-only reflection confirmed that the version-3 motion node is present in the
game and shared by the pole and cloth. Axis, periods, angle, function and the
physical flag arrive as authored. `NightOnly` is zero, so night-only gating does
not explain the ignored motion. The native LightRay local animation uses function
1; copying that value did not change the outcome. Its archive version 4 has a
field absent from the pinned GBX.NET reader; that reader failure is not a game
crash or evidence of motion success.

The exact engine cause of the combined-motion failure is not established. These
results do not prove that every possible kinematic/cloth graph is incompatible.

## An automatic D++ or E++ workaround

Earlier GPU evidence identifies the required rendering state: the VertexTween
vertex shader reads a structured vertex buffer at VS t0 using SV_VertexID.
Setting bit 27 (`0x08000000`) of the tested build's `CPlugVisual+0x24` enables the
correct buffer. The tested standard visual archives do not serialize that bit.
Native early material resolution can initialize it automatically in the working
game-directory route; the embedded user route still does not.

A runtime plugin could potentially apply the demonstrated adjustment to selected
models on each client. An editor-only plugin cannot turn that transient adjustment
into a portable saved item. Players without the runtime plugin would still take
the failing route. It therefore does not meet the no-install requirement.

This is a renderer-state adjustment, not an executable-code patch or a change to
vehicle physics. It is nevertheless dependent on private memory layout and load
timing. It is reasonable as a scoped development workaround, but a production
implementation would require exact build/layout checks, explicit item selection,
initialization before first render, node lifetime and map-unload handling, and
bounded discovery instead of broad per-frame memory scanning. No production
D++ or E++ changes have been made. Setting the renderer bit alone also does not
prove simultaneous kinematic motion and cloth animation.

## Skyward promo plane

The original `BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx`
was inspected without modification. It has one kinematic body and one constraint:
world-relative Y rotation from 0 to 360 degrees and Y translation from 40 to 0.
Its plane and current banners share the mesh. The source dump is
`artifacts/flag-skyward-structure-20261022.txt`.

An eventual implementation must separate the banners from rigid plane geometry,
preserve the original attachment transform through the flight path, and drive
cloth deformation while that transform changes. A distant view of a moving
frozen banner is insufficient. Acceptance requires a fixed-camera test of rigid
motion and independent cloth deformation, intact attachments, a cold reload, and
finally an embedded-only run with all loose custom dependencies removed.

## Scope and next investigation

Tested baseline: Trackmania 2026.2.2.1751, engine 2026-02-02 17:51:19;
ordinary diagnostic sessions use Openplanet 1.29.14 Next/Public. No dedicated
flag helper was used for these motion or embedded-graph tests.

The one-field constraint control is now complete and negative. The retained
VertexTween shader disassembly distinguishes an instanced branch that calculates
the tween frame from packed instance timing and a non-instanced branch that takes
explicit frame offsets and weight from the draw constant buffer. A useful next
measurement is to determine which branch the moving kinematic flag actually
takes, then compare those timing inputs over time. The shader code alone does
not establish that causal explanation. Further portable-file work still needs an
effective early material/shader representation in embedded context; changing the
number of external archives alone has now been tested and is insufficient.

## Restoration and verification

With the game stopped, all twelve installed motion-test Item.Gbx files and all
seven disposable maps from this experiment were hash-checked and archived under
`artifacts/flag-motion-embedded-20261022/cleanup/`. The loose split graph had
already been held outside the user Items tree. The original proof map is retained
unchanged; opening it still requires the separately documented accepted
installation package. Rejected premature video labels and derived images are
quarantined together rather than presented as evidence of combined motion.

Seven protected hashes match: the executable, original Openplanet injection
proxy, Editor.op, XertroV's source flag, the GPU-cache ZIP, the Extras ZIP and the
retained proof map. No production plugin was changed or new flag helper left
installed. `Tests/FlagProbe/archive_motion_tests.py` and
`cleanup/restoration.json` contain the exact paths and receipts.

The restored cold process PID 32644 is in the main menu with readiness true,
no blocking dialog, a responding E++ tool pack and no dedicated flag helpers.
The runtime receipt and fresh Openplanet log are retained in
`cleanup/{restored-runtime.json,Openplanet-restored.log}`.

The final FlagProbe build passes with zero warnings and zero errors.
`git diff --check` passes; its existing LF/CRLF normalization notices are retained
as notices rather than build failures. This round does not claim complete vanilla
parity, multiplayer compatibility or a portable map-only solution.
