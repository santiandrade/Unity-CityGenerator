🇪🇸 [Leer en español](README.es.md)

# City Generator

<img src="Packages/com.santiandrade.citygenerator/Editor/ToolThumbnail.png" alt="City Generator thumbnail" width="100%">

An Editor tool for Unity that procedurally generates a city — roads, sidewalks, road
markings, buildings, plazas, street furniture, traffic lights, autonomous traffic,
pedestrians, an optional day/night cycle and a minimap HUD — into a new or existing
scene. Open it from
**Tools > City Generator > Open**.

The window is split into four tabs: **City** (grid, ground, buildings, plazas, vehicles,
props, an optional Day/Night Cycle for the generated directional light — start hour, speed
multiplier, and a colour gradient/intensity curve over the 24 h — and Custom Places:
manually-placed entries with a title, a prefab, a block/corner picked from a grid preview,
a fixed orientation, and an optional "Is Point Of Interest" flag that surfaces the entry on
the minimap, instantiated instead of a random building at that spot), **Player** (Player Prefab, movement, `CharacterController` and camera tuning),
**Pedestrians** (the pedestrian prefab list, plus their walk/idle behaviour and crowd
tuning), and **Minimap** (on by default; texture resolution and view radius for the
in-game minimap HUD) — everything is editable from the window, nothing requires touching
the package's code.

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

If you installed **without** a `#vX.Y.Z` tag (tracking the tip of the default branch),
Package Manager can detect and apply updates for you: open **Package Manager > your
installed "City Generator" entry > Manage**, and click **Update** if it's offered. This
re-resolves the git URL and replaces the commit locked in `Packages/packages-lock.json`
with whatever is currently on the default branch — no need to remove and reinstall.

If you installed **with** a `#vX.Y.Z` tag pinned to a specific release, Package Manager
does not offer an automatic update to a newer tag — the **Update** button only tracks the
same ref you installed from. To move to a new release, reinstall the package: **Package
Manager > your installed "City Generator" entry > remove it**, then install again from
git URL using the new tag from the [Releases page](https://github.com/santiandrade/Unity-CityGenerator/releases).

## Requirements

- **Unity 6000.0** or newer.
- **Unity's new Input System** (`com.unity.inputsystem`, declared as a package
  dependency). The tool's `.asmdef`s reference `Unity.InputSystem`; the classic
  `UnityEngine.Input` API is not used anywhere.
- **glTFast** (`com.unity.cloud.gltfast`, declared as a package dependency). Needed to
  import the demo fountain prop, which is a `.glb` model.
- **uGUI** (`com.unity.ugui`, declared as a package dependency). Needed by the minimap
  HUD, which is a UGUI `Canvas` + `RawImage`.
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
- A `TrafficLight` prefab with a `CityGenerator.Runtime.TrafficLight` component
  whenever the grid has at least one interior intersection (grid width and height
  both greater than 1) — the tool validates this and blocks generation otherwise,
  regardless of whether **Include Traffic** is on: the traffic network and its lights
  are always generated so crossings stay wired to a real light, even when no vehicles
  are spawned.

## Demo content

The package includes a full set of demo assets under its `DefaultAssets/` folder —
buildings, vehicles, vegetation, street furniture, floor pieces, materials, the
player prefab, and the minimap HUD prefab/sprites — so `Tools > City Generator > Open`
opens with every required field already filled in and a city is one click away.

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
  (`CarAgent`/`PedestrianAgent`). You don't need to add a collider yourself: the
  generated instance's root always ends up with one dedicated, non-trigger collider
  used for sensor detection — an existing collider on the root is reused, or a
  `BoxCollider` sized from the prefab's own combined renderer bounds is added if the
  root has none. A collider that only exists deeper in your prefab's hierarchy is left
  completely untouched (its own layer and `isTrigger` are free for you to use for
  anything else) — only the root proxy is what lets vehicles detect each other with a
  forward `SphereCast` on the `Vehicle` layer and lets vehicles detect pedestrians (and
  the player) the same way; the player's `CharacterController` can still physically
  collide with any collider anywhere in the prefab. Pedestrians additionally want, if you want walk/idle animation, an
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

- **Bake lightmaps and occlusion culling.** Every generated group except `Vehicles` and
  `Pedestrians` (which `CarAgent`/`PedestrianAgent` move by transform every frame,
  incompatible with static batching) is already marked `Batching Static | Occluder Static | Occludee Static`, so both bakes
  are ready to run with no manual setup — the tool just doesn't run them for you.
- **Add `LODGroup`s** to your own prefabs if you're generating a large city. The tool
  has no opinion on LOD; it only places whatever prefab you gave it.
- **Adjust lighting** — the generated scene ships with a single directional light and no
  `Global Volume` (removed on purpose, to stay render-pipeline-agnostic). That light is
  always created facing roughly east-west (yaw -110°, so the sun rises towards the
  minimap's right) and
  carries the Day/Night Cycle component; with the cycle disabled it simply stays fixed at
  the configured Start Hour.

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

`targetFrameRate`/`vSyncCount` are **not** in this table on purpose — as of v2.0.0 the
package no longer sets them for you (it previously did, at runtime, via
`CityGenerator.Runtime.PerformanceBootstrap`). Set your own frame rate/VSync preference
for your project; there's no package-level opt-in replacement.

## Scaling traffic

`CarAgent` has no route planning or congestion avoidance: past roughly 40% of a grid's
spawn nodes occupied, traffic tends to gridlock rather than flow (the window shows a
warning next to **Vehicle Count** as soon as you exceed this, before you generate). If you need denser traffic than that, a
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
