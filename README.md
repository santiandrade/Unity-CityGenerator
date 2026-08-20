🇪🇸 [Leer en español](README.es.md)

# City Generator

An Editor tool for Unity that procedurally generates a city — roads, sidewalks, road
markings, buildings, plazas, street furniture, traffic lights and autonomous traffic —
into a new or existing scene. Open it from **Tools > City Generator**.

It ships as the embedded package `com.santiandrade.citygenerator`, installable directly
from a git URL, with a full set of demo prefabs included so the window is ready to
generate a city the moment you install it.

## Installation

In your Unity project, open **Window > Package Manager**, click the **+** button, choose
**Install package from git URL**, and paste:

```
https://github.com/santiandrade/Unity-CityGenerator.git?path=/Packages/com.santiandrade.citygenerator#v1.0.0
```

The `?path=` segment points at the package inside this repository (the repository root is
not itself a package); the `#v1.0.0` segment pins an exact released version. You can drop
the `#v1.0.0` suffix to install straight from the tip of the default branch instead of a
tagged release — useful for tracking development, at the cost of losing repeatable
installs.

## Updating

Unity does **not** offer an "Update" button for packages installed from a git URL — that
button only exists for packages that come from a registry (a scoped registry / OpenUPM),
which this package is not published to. When you install by git URL, the Package Manager
resolves and locks the current commit in `Packages/packages-lock.json`; it never checks
the remote again on its own.

To update to a new version, reinstall the package with the new tag: **Package Manager >
your installed "City Generator" entry > remove it**, then install again from git URL with
the new tag, e.g. `...#v1.1.0`. This replaces the locked commit with the one the new tag
points at.

## Requirements

- **Unity 6000.0** or newer.
- **Unity's new Input System** (`com.unity.inputsystem`, declared as a package
  dependency). The tool's `.asmdef`s reference `Unity.InputSystem`; the classic
  `UnityEngine.Input` API is not used anywhere.
- **glTFast** (`com.unity.cloud.gltfast`, declared as a package dependency). Needed to
  import the demo fountain prop, which is a `.glb` model.
- A layer named **`Vehicle`** if you want traffic. The tool does not create this layer for
  you — it warns in the console and falls back to the Default layer instead of failing,
  but vehicles on the Default layer will not detect each other with their forward sensor,
  so traffic will drive through itself. Add the layer in *Project Settings > Tags and
  Layers* before generating.
- A `TrafficLight` prefab with a `CityGenerator.Runtime.TrafficLight` component if
  **Include Traffic** is enabled — the tool validates this and blocks generation
  otherwise.

## Demo content

The package includes a full set of demo assets under its `DefaultAssets/` folder —
buildings, vehicles, vegetation, street furniture, floor pieces, materials and the
player prefab — so `Tools > City Generator` opens with every required field already
filled in and a city is one click away.

This demo content lives inside the package, which Unity treats as read-only in your
project. If you want to modify a demo prefab, copy it into your own `Assets/` folder
first and assign your copy in the tool's window instead of the package original.

## Requirements for your own prefabs

The tool never mutates a prefab asset — everything it changes is done on the *scene
instances* it generates — but it does expect a few things from what you assign:

- **Pivot at the base**, for every prefab category (buildings, props, vegetation,
  floors). The tool positions everything by placing the pivot at the target point on
  the ground.
- **Buildings sized to the 22 m corner slot** (`CityGeneratorConstants.BuildingSlotPitch`).
  Buildings are the one category the tool does **not** overlap-check, against each other
  or against the block edge — an oversized building prefab will visibly clip into its
  neighbour. This is deliberate: sizing your own prefabs to the slot is your
  responsibility, the same as with any other user-supplied asset.
- **Vehicles**: a single `BoxCollider` on the root, and **no `Rigidbody`** — vehicles
  move by transform every frame (`CarAgent`), the collider only exists so they can
  detect each other with a forward `SphereCast` on the `Vehicle` layer.
- **Every other prefab** (props, vegetation, floors, plaza content) just needs a
  `Renderer` somewhere in its hierarchy — the tool measures its footprint from combined
  renderer bounds (`CityGeneratorBoundsUtility`) to place it and to check it against
  other placed objects.

## What the tool does *not* do — your responsibility per scene

Everything below applies to the specific scene you generated into, not to the tool. The
tool's job ends at leaving the geometry ready for these steps to be a single button:

- **Bake lightmaps and occlusion culling.** Every generated group except `Vehicles`
  (which `CarAgent` moves by transform every frame, incompatible with static batching)
  is already marked `Batching Static | Occluder Static | Occludee Static`, so both bakes
  are ready to run with no manual setup — the tool just doesn't run them for you.
- **Add `LODGroup`s** to your own prefabs if you're generating a large city. The tool
  has no opinion on LOD; it only places whatever prefab you gave it.
- **Adjust lighting** — the generated scene ships with a single directional light and no
  `Global Volume` (removed on purpose, to stay render-pipeline-agnostic).

## Recommended project settings

Not applied by the tool (they're global to a Unity project, not something a generator
can carry with it), but worth setting in your project for the same performance profile
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

`targetFrameRate`/`vSyncCount` are **not** in this table on purpose — they're set at
runtime by `CityGenerator.Runtime.PerformanceBootstrap`, which ships inside the package,
so a generated city behaves the same in any project without extra configuration.

## Scaling traffic

`CarAgent` has no route planning or congestion avoidance: past roughly 40% of a grid's
spawn nodes occupied, traffic tends to gridlock rather than flow (the tool warns in the
console when you exceed this). If you need denser traffic than that, a
`CityGenerator.Runtime.TrafficManager` is generated automatically whenever **Include
Traffic** is enabled — it ticks every `CarAgent` from one central `Update` and, once
more than ~60 cars are registered, staggers the forward sensor for cars far from the
camera. That buys some headroom, but the real ceiling is the lack of route planning, not
per-car update cost.

## Render pipeline

The 14 demo materials are authored as **URP/Lit** and will render magenta under
Built-in or HDRP. The tool's own code has no render-pipeline dependency — it doesn't
require or configure URP, and no `Global Volume` is generated — so it works with any
pipeline as long as you supply materials that pipeline understands. Only the bundled
demo content is URP-specific.

## License

MIT — see [LICENSE.md](LICENSE.md).
