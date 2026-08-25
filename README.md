🇪🇸 [Leer en español](README.es.md)

# City Generator

<img src="Packages/com.santiandrade.citygenerator/Editor/ToolThumbnail.png" alt="City Generator thumbnail" width="100%">

An Editor tool for Unity that procedurally generates a city — roads, sidewalks, road
markings, buildings, plazas, street furniture, traffic lights, autonomous traffic and
pedestrians — into a new or existing scene. Open it from **Tools > City Generator > Open**.

The window is split into three tabs: **City** (grid, ground, buildings, plazas, vehicles,
props), **Player** (Player Prefab, movement, `CharacterController` and camera tuning) and
**Pedestrians** (the pedestrian prefab list, plus their walk/idle behaviour and crowd
tuning) — everything is editable from the window, nothing requires touching the package's
code.

It ships as the embedded package `com.santiandrade.citygenerator`, installable directly
from a git URL, with a full set of demo prefabs included so the window is ready to
generate a city the moment you install it.

## Installation

In your Unity project, open **Window > Package Manager**, click the **+** button, choose
**Install package from git URL**, and paste:

```
https://github.com/santiandrade/Unity-CityGenerator.git?path=/Packages/com.santiandrade.citygenerator
```

The `?path=` segment points at the package inside this repository (the repository root is
not itself a package). This form tracks the tip of the default branch.

For a reproducible install pinned to a specific release, append `#vX.Y.Z` with a tag from
the [Releases page](https://github.com/santiandrade/Unity-CityGenerator/releases) — for
example, `...citygenerator#v1.0.1` for that exact version.

## Updating

Unity does **not** offer an "Update" button for packages installed from a git URL — that
button only exists for packages that come from a registry (a scoped registry / OpenUPM),
which this package is not published to. When you install by git URL, the Package Manager
resolves and locks the current commit in `Packages/packages-lock.json`; it never checks
the remote again on its own.

To update, reinstall the package: **Package Manager > your installed "City Generator"
entry > remove it**, then install again from git URL. If you installed with a `#vX.Y.Z`
tag, use the new tag from the [Releases page](https://github.com/santiandrade/Unity-CityGenerator/releases) to pin the new version. If you installed
without a tag, reinstalling the same untagged URL re-resolves to whatever is on the
default branch now. Either way, this replaces the commit locked in
`Packages/packages-lock.json`.

## Requirements

- **Unity 6000.0** or newer.
- **Unity's new Input System** (`com.unity.inputsystem`, declared as a package
  dependency). The tool's `.asmdef`s reference `Unity.InputSystem`; the classic
  `UnityEngine.Input` API is not used anywhere.
- **glTFast** (`com.unity.cloud.gltfast`, declared as a package dependency). Needed to
  import the demo fountain prop, which is a `.glb` model.
- A layer named **`Vehicle`**, used by traffic so vehicles can detect each other with
  their forward sensor. You don't need to create it yourself — the tool creates it the
  first time it generates traffic with one, using the first free layer slot, and logs
  that it did so. Only if every layer slot is already taken does it fall back to
  warning instead — vehicles then won't detect each other at all (they still stop for
  lights and unsignalled-crossing priority) until you free one up.
- A layer named **`Pedestrian`**, same idea: created automatically the first time you
  generate pedestrians, so `CarAgent`'s pedestrian sensor can detect them. Same
  fail-closed fallback if every slot is taken — vehicles just won't detect pedestrians
  until you free one up.
- A `TrafficLight` prefab with a `CityGenerator.Runtime.TrafficLight` component if
  **Include Traffic** is enabled — the tool validates this and blocks generation
  otherwise.

## Demo content

The package includes a full set of demo assets under its `DefaultAssets/` folder —
buildings, vehicles, vegetation, street furniture, floor pieces, materials and the
player prefab — so `Tools > City Generator > Open` opens with every required field already
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
- **Vehicles and pedestrians**: no `Rigidbody` — both move by transform every frame
  (`CarAgent`/`PedestrianAgent`). You don't need to add a collider yourself: if your
  prefab already has one or more `Collider`s anywhere in its hierarchy, the tool keeps
  them as-is (just forcing `isTrigger` off); if it has none, the tool adds a
  non-trigger `BoxCollider` to the generated instance, sized from the prefab's own
  combined renderer bounds. Either way it's what lets vehicles detect each other with a
  forward `SphereCast` on the `Vehicle` layer, lets vehicles detect pedestrians (and the
  player) the same way, and lets the player's `CharacterController` physically collide
  with both. Pedestrians additionally want, if you want walk/idle animation, an
  `Animator` driving `CharacterAnimator.controller`'s `Speed`/`Grounded` parameters (or
  your own controller with the same names) — otherwise they still walk, just without
  animation.
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

## Scaling pedestrians

Pedestrians only spawn on the sidewalk ring nodes (8 per block), so the tool warns
(non-blocking) once a **Pedestrian Count** exceeds ~70% of that total — past that point
the crowd reads as overcrowded, even though `PedestrianAgent` has no gridlock mechanics
of its own (it only pushes apart from very close neighbours, it never gets permanently
stuck like a car can).

A **1×N or N×1 grid** has no interior intersections, so it has no zebra crossings or
traffic lights either — every block's pedestrians stay confined to their own sidewalk
ring, unable to cross to a neighbouring block. The tool warns about this too when
**Include Pedestrians** is on.

The pedestrian graph auto-repairs itself against a moved/added obstacle every time you
enter Play (and via `Tools > City Generator > Rebuild Pedestrian Network` without
entering Play), using a small physics probe per sidewalk node — but a building or prop
prefab with **no `Collider`** anywhere in its hierarchy isn't detected by that check.
It's still avoided at generation time, via the same shared obstacle list every other
category (lamps, bins, vegetation) is checked against.

## Render pipeline

The 14 demo materials are authored as **URP/Lit** and will render magenta under
Built-in or HDRP. The tool's own code has no render-pipeline dependency — it doesn't
require or configure URP, and no `Global Volume` is generated — so it works with any
pipeline as long as you supply materials that pipeline understands. Only the bundled
demo content is URP-specific.

## License

MIT — see [LICENSE.md](LICENSE.md).
