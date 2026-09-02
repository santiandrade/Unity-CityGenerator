# Demo content — `Packages/com.santiandrade.citygenerator/DefaultAssets/`

Detail behind the "Demo content" bullet of the root `CLAUDE.md`. Ships inside the package (moved there by SPEC 02 so it's portable to any project that installs it), loaded by `CityGeneratorDefaultAssets` from hardcoded `DefaultAssets/...` paths. Unity treats package content as read-only in the consuming project — to edit a demo prefab, copy it into your own `Assets/` first.

## `DefaultAssets/Prefabs/`

Inventory (which prefabs exist, and which of them a fresh window starts with) deliberately isn't listed here — browse the folder, and read `CityGeneratorDefaultAssets.ApplyTo` for what is assigned by default. What follows is what's true about each category regardless of its contents.

- **`Buildings/`** — modelled prefabs, root scale 10, pivot at base, own collider, all comfortably narrower than the 22 m slot. `Building-Hospital` is **not** part of the random building rotation — it's used exclusively as the demo's whole-block Custom Place entry, wired in by `CityGeneratorDefaultAssets.ApplyTo` rather than `buildingPrefabs` (see `custom-places.md`).
- **`Characters/`** — the selectable Player Prefab candidates, doubling as the default pedestrian list. Each a clean model+Animator prefab with **no movement component baked in**.
- **`Pets/`** (SPEC 12) — Custom Pedestrian prefab candidates, confined to a hand-traced subgraph rather than roaming the whole city (see `pedestrians.md`'s Custom Pedestrians section). Unlike `Characters/`, each animal's rig is a set of plain rigid `MeshRenderer` limbs (no `SkinnedMeshRenderer`), animated by moving the limb transforms directly — so it needs `Animator.cullingMode = Always Animate` rather than the `Characters/` convention of `Cull Completely` (see `pedestrians.md`'s Animator culling section for why `Cull Completely` silently freezes this rig shape), and its FBX importers need the same explicit per-clip `Loop Time` fix `Characters/` already has below, or the walk cycle freezes on its last frame after under a second.
- **`Floors/`** — the road base, sidewalk, dash, zebra and lawn pieces. Plain `MeshFilter`/`MeshRenderer` prefabs pointing at the extracted mesh assets in `DefaultAssets/Meshes/`. (They were originally authored with ProBuilder; ProBuilder was removed from the project in commit `89ffaf4` and the meshes baked out — nothing in the repo depends on it any more.)
- **`Props/`** — `Bench`, `Bin`, `Lamp`, `TrafficLight`, and `Fountain` (the only imported-model prop, a `.glb` loaded via glTFast).
- **`Vegetation/Tree.prefab`**.
- **`Vehicles/`** — the demo traffic fleet; see "Baked `CarAgent` tuning" below.
- **`MinimapHUD.prefab`** (SPEC 07) — Canvas (Screen Space Overlay) + a circle-masked `RawImage`, player marker and a deactivated POI marker template, wired to a `Runtime/MinimapHUD` component. Built via an editor script (procedurally-generated sprites, see below), not hand-authored. Instantiated by `CityGeneratorSceneBuilder` when `minimap.enabled`.

## `DefaultAssets/Sprites/`

`Minimap_Circle.png` (the HUD's circular mask), `Minimap_PlayerArrow.png` (player marker), `Minimap_POIPin.png` (the single generic POI icon reused for every Point of Interest) — all three generated procedurally by a one-off editor script (flat shapes on a transparent background), not authored externally.

## Colliders

Deliberately simple: `BoxCollider`s on floors, benches, lamps and traffic lights; `MeshCollider`s only on `Tree` (trunk + crown); **no collider at all** on road markings. Vehicles carry a single root `BoxCollider` (no `Rigidbody` — they move by transform, and the collider exists only so they can detect each other). See the collider policy in `runtime-and-traffic.md` for what happens at generation time when a vehicle or pedestrian prefab has none.

## Baked `CarAgent` tuning

Every vehicle prefab carries its own baked `CarAgent` component with distinct tuning (`maxSpeed`, `acceleration`, `braking`, `turnSpeed`, `cornerSpeedFactor`) so mixed traffic doesn't drive identically: heavy vehicles (trucks, the delivery vans) are slowest with the widest turns, emergency vehicles are tuned for snappy acceleration, and sports/luxury cars are fastest and hardest-accelerating.

`CityGeneratorTrafficBuilder.BuildVehicles` only `AddComponent`s a fresh `CarAgent` (with the script's own defaults) when a prefab doesn't already carry one — **every vehicle prefab must keep its baked `CarAgent`**, or it silently falls back to identical default tuning at generation time. The generator adds a ±6% `maxSpeed` jitter per instance on top.

## Other folders

- **`Materials/`** — the flat-colour URP/Lit palette (ground/prop colours plus the emissive traffic-light and lamp materials), created by editor script rather than by hand, and `MinimapWindow.mat` (see `editor-tool.md`'s Minimap section — that one is load-bearing, not decorative). The fountain is the only geometry that doesn't use this palette — it keeps the materials embedded in its `.glb`.
- **`Meshes/`** — the extracted mesh assets backing the floor/prop prefabs.
- **`Animations/CharacterAnimator.controller`** — created via `UnityEditor.Animations.AnimatorController` (editor script), not hand-authored YAML. Base layer: `Locomotion` (1D blend tree on `Speed`: idle→walk→sprint), `Jump`, `Fall`. Shared by every `Characters/` prefab (each just points its `Animator.m_Controller` at it), rather than each carrying its own copy.
- **`Animations/PetAnimator.controller`** — same `Locomotion` (1D blend tree on `Speed`, thresholds matching `PedestrianAgent.normalizedSpeed`) plus a set of unused, unreachable states (attack/emote/wheelchair/etc.) carried over from the asset pack's own animator layout; only `Locomotion` (the default state, no incoming transitions elsewhere) is ever entered. Shared by the `Pets/` prefabs the same way `CharacterAnimator.controller` is shared by `Characters/` — but unlike them, sharing it across differently-shaped animal skeletons only works because the demo animals use matching limb transform names (Generic rig, no retargeting); a future pet whose rig doesn't match would need its own controller or an Animator Override Controller instead.
- **`Models/`** — only the FBX/glb models a demo prefab actually references, decided by `AssetDatabase.GetDependencies` over the demo prefabs and the two animator controllers, not by inspection. Each model folder additionally carries the shared `colormap.png` texture atlas those FBX sample. Everything from the same asset packs that no demo prefab references was **not** brought into the package and was deleted rather than kept (`Assets/Models/` no longer exists in the repo). Each character FBX's importer was reconfigured by editor script to `animationType: Generic` with an Avatar created from the model, and its locomotion clips set to loop; the pet FBX got the equivalent per-clip `Loop Time`/`Loop Pose` fix (`ModelImporter.clipAnimations`, seeded from `defaultClipAnimations` to keep the auto-detected frame ranges) after shipping once with it off — see `pedestrians.md`'s Animator culling section.
- **`Audio/`** (SPEC 09) — the demo clips the Audio tab starts with: one looping city ambience for the 2D source, plus the positional plaza clips. Assigned as *settings* (`audio.ambience.clips`/`audio.plazaAudio.clips`) by `CityGeneratorDefaultAssets`, never by hardcoded path from the builder — see `editor-tool.md`'s Audio section.
- **`Shaders/MinimapWindow.shader`** (SPEC 07) — the `UI/Default` copy backing `Materials/MinimapWindow.mat`; load-bearing, see `editor-tool.md`'s Minimap section.
- **`Input/InputSystem_Actions.inputactions`** — the only input asset (`generateWrapperCode: 0`; scripts look up the `Player` map — and, since SPEC 13, the `Free View` map — plus their actions by name through a serialized `InputActionAsset` reference). The project uses the new Input System exclusively (`activeInputHandler: 1`) — `UnityEngine.Input` throws at runtime. The tool takes this asset as a *setting* (`general.inputActions`), never by hardcoded path.

## Not part of the package

`Assets/Settings/` holds the URP pipeline assets (separate PC and Mobile renderer/pipeline configs) and volume profiles. Project configuration, not deliverable; see `../technical-review.md` for the recommended values.
