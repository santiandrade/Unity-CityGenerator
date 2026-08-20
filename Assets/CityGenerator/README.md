# City Generator

An Editor tool that procedurally generates a city — roads, sidewalks, road markings,
buildings, plazas, street furniture, traffic lights and autonomous traffic — into a new or
existing scene. Open it from **Tools > City Generator**.

This folder (`Assets/CityGenerator/`) is the whole deliverable: copy it into another Unity
project and it works on its own, with the exceptions noted below.

## Requirements

- **Unity's new Input System** (`com.unity.inputsystem`). Both `.asmdef`s in this folder
  reference `Unity.InputSystem`; the classic `UnityEngine.Input` API is not used anywhere.
- A layer named **`Vehicle`** if you want traffic. The tool does not create this layer for
  you — it warns in the console and falls back to the Default layer instead of failing, but
  vehicles on the Default layer will not detect each other with their forward sensor, so
  traffic will drive through itself. Add the layer in *Project Settings > Tags and Layers*
  before generating.
- A `TrafficLight` prefab with a `CityGenerator.Runtime.TrafficLight` component if
  **Include Traffic** is enabled — the tool validates this and blocks generation otherwise.

## `CityGeneratorDefaultAssets.cs` — the one non-portable file

Every other script in `Assets/CityGenerator/` is self-contained and asset-agnostic. This one
file is the exception: it fills a freshly opened tool window with *this* repository's demo
prefabs (`Assets/Prefabs/...`) by hardcoded path, purely so the window has something to
generate on first use.

When you copy the tool to another project, either:

- **Rewrite** the paths in `ApplyTo` to point at that project's own prefabs, or
- **Delete the call** to `CityGeneratorDefaultAssets.ApplyTo` in `CityGeneratorWindow.OnEnable`
  (and the file itself) and leave the settings fields empty — the window still opens and
  works, you just assign every prefab by hand the first time.

Every path lookup in this file is defensive (`AssetDatabase.LoadAssetAtPath` returns `null`
silently on a missing asset), so a half-copied demo project will not throw — it will just
leave some fields empty for you to fill in.

## Requirements for your own prefabs

The tool never mutates a prefab asset — everything it changes is done on the *scene
instances* it generates — but it does expect a few things from what you assign:

- **Pivot at the base**, for every prefab category (buildings, props, vegetation, floors).
  The tool positions everything by placing the pivot at the target point on the ground.
- **Buildings sized to the 22 m corner slot** (`CityGeneratorConstants.BuildingSlotPitch`).
  Buildings are the one category the tool does **not** overlap-check, against each other or
  against the block edge — an oversized building prefab will visibly clip into its neighbour.
  This is deliberate: sizing your own prefabs to the slot is your responsibility, the same as
  with any other user-supplied asset.
- **Vehicles**: a single `BoxCollider` on the root, and **no `Rigidbody`** — vehicles move by
  transform every frame (`CarAgent`), the collider only exists so they can detect each other
  with a forward `SphereCast` on the `Vehicle` layer.
- **Every other prefab** (props, vegetation, floors, plaza content) just needs a `Renderer`
  somewhere in its hierarchy — the tool measures its footprint from combined renderer bounds
  (`CityGeneratorBoundsUtility`) to place it and to check it against other placed objects.

## What the tool does *not* do — your responsibility per scene

Everything below applies to the specific scene you generated into, not to the tool. The tool's
job ends at leaving the geometry ready for these steps to be a single button:

- **Bake lightmaps and occlusion culling.** Every generated group except `Vehicles` (which
  `CarAgent` moves by transform every frame, incompatible with static batching) is already
  marked `Batching Static | Occluder Static | Occludee Static`, so both bakes are ready to run
  with no manual setup — the tool just doesn't run them for you.
- **Add `LODGroup`s** to your own prefabs if you're generating a large city. The tool has no
  opinion on LOD; it only places whatever prefab you gave it.
- **Adjust lighting** — the generated scene ships with a single directional light and no
  `Global Volume` (removed on purpose, to stay render-pipeline-agnostic).

## Recommended project settings

Not applied by the tool (they're global to a Unity project, not something a generator can
carry with it), but worth reproducing in the target project for the same performance profile
this repository ships with:

| Setting | Value | Why |
|---|---|---|
| `Main Light Shadow Resolution` (URP asset) | 2048 | 8192 costs ~134–268 MB of VRAM with no visible gain on city-scale geometry |
| `Shadow Cascades` | 2 | 4 cascades re-render all shadow-casting geometry four times per frame |
| `Shadow Distance` | 70 m | Comfortably covers a 3×3 generated city (±90 m) without wasting draw distance |
| `Soft Shadow Quality` | Medium/Low | High shadow filtering doesn't read as different at this scale |
| `Opaque Texture` (URP asset) | Off | Only turn on if a shader you add reads `_CameraOpaqueTexture` |
| `Depth Texture` (URP asset) | On | Needed if you use SSAO or another Renderer Feature that reads scene depth |
| GPU Resident Drawer | Instanced Drawing | Requires static-flagged geometry (already applied by the tool) and Forward+ rendering |

`targetFrameRate`/`vSyncCount` are **not** in this table on purpose — they're set at runtime by
`CityGenerator.Runtime.PerformanceBootstrap`, which ships inside this folder, so a generated
city behaves the same in any project without extra configuration.

## Scaling traffic

`CarAgent` has no route planning or congestion avoidance: past roughly 40% of a grid's spawn
nodes occupied, traffic tends to gridlock rather than flow (the tool warns in the console when
you exceed this). If you need denser traffic than that, a `CityGenerator.Runtime.TrafficManager`
is generated automatically whenever **Include Traffic** is enabled — it ticks every `CarAgent`
from one central `Update` and, once more than ~60 cars are registered, staggers the forward
sensor for cars far from the camera. That buys some headroom, but the real ceiling is the lack
of route planning, not per-car update cost.
