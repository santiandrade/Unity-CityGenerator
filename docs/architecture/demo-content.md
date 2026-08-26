# Demo content — `Packages/com.santiandrade.citygenerator/DefaultAssets/`

Detail behind the "Demo content" bullet of the root `CLAUDE.md`. Ships inside the package (moved there by SPEC 02 so it's portable to any project that installs it), loaded by `CityGeneratorDefaultAssets` from hardcoded `DefaultAssets/...` paths. Unity treats package content as read-only in the consuming project — to edit a demo prefab, copy it into your own `Assets/` first.

## `DefaultAssets/Prefabs/`

- **`Buildings/`** — 17 modelled prefabs: `Building-A` through `Building-M`, `Building-Skyscraper-A` through `-E`, plus `Building-Hospital`. Root scale 10, pivot at base, own collider; widest footprint ~13.9 m, clearing the 22 m slot. `Building-Hospital` is **not** part of the random building rotation — it's used exclusively as the demo's whole-block Custom Place entry, wired in by `CityGeneratorDefaultAssets.ApplyTo` rather than `buildingPrefabs` (see `custom-places.md`).
- **`Characters/`** — 12 selectable Player Prefab candidates, doubling as the default pedestrian list: `Character-Male-A` through `-F`, `Character-Female-A` through `-F`. Each a clean model+Animator prefab with **no movement component baked in**; the default Player Prefab is `Character-Male-D`.
- **`Floors/`** — `RoadBase`, `RoadSidewalk`, `RoadDash`, `RoadZebra`, `Lawn`. Plain `MeshFilter`/`MeshRenderer` prefabs pointing at the extracted mesh assets in `DefaultAssets/Meshes/`. (They were originally authored with ProBuilder; ProBuilder was removed from the project in commit `89ffaf4` and the meshes baked out — nothing in the repo depends on it any more.)
- **`Props/`** — `Bench`, `Bin`, `Lamp`, `TrafficLight`, and `Fountain` (the only imported-model prop, built on `DefaultAssets/Models/Props/Fountain by Poly.glb` via glTFast).
- **`Vegetation/Tree.prefab`**.
- **`Vehicles/`** — 15 prefabs: `Ambulance`, `Delivery-Flat`, `DeliveryCar`, `Firetruck`, `Garbage-Truck`, `Hatchback-Sports`, `PoliceCar`, `Sedan`, `SedanSportCar`, `Suv`, `Suv-Luxury`, `TaxiCar`, `Truck`, `Truck-Flat`, `Van`.

## Colliders

Deliberately simple: `BoxCollider`s on floors, benches, lamps and traffic lights; `MeshCollider`s only on `Tree` (trunk + crown); **no collider at all** on road markings. Vehicles carry a single root `BoxCollider` (no `Rigidbody` — they move by transform, and the collider exists only so they can detect each other). See the collider policy in `runtime-and-traffic.md` for what happens at generation time when a vehicle or pedestrian prefab has none.

## Baked `CarAgent` tuning

Each of the 15 vehicle prefabs carries its own baked `CarAgent` component with distinct tuning (`maxSpeed`, `acceleration`, `braking`, `turnSpeed`, `cornerSpeedFactor`) so mixed traffic doesn't drive identically: heavy vehicles (`Garbage-Truck`, `Truck`, `Truck-Flat`, the delivery vans) are slowest with the widest turns, emergency vehicles (`Ambulance`, `Firetruck`, `PoliceCar`) are tuned for snappy acceleration, and sports/luxury cars (`Hatchback-Sports`, `SedanSportCar`, `Suv-Luxury`) are fastest and hardest-accelerating.

`CityGeneratorTrafficBuilder.BuildVehicles` only `AddComponent`s a fresh `CarAgent` (with the script's own defaults) when a prefab doesn't already carry one — **every vehicle prefab must keep its baked `CarAgent`**, or it silently falls back to identical default tuning at generation time. The generator adds a ±6% `maxSpeed` jitter per instance on top.

## Other folders

- **`Materials/`** — the flat-colour URP/Lit palette (14 materials: `Asphalt`, `Sidewalk`, `RoadLine`, `Crosswalk`, `Grass`, `GlassBlue`, `TreeTrunk`/`TreeLeaves`, `MetalDark`, and the emissive `LightRed`/`LightAmber`/`LightGreen`/`LightOff`/`LampWarm`). Created by editor script, not by hand. The fountain is the only geometry that doesn't use this palette — it keeps the materials embedded in its `.glb`.
- **`Meshes/`** — the extracted mesh assets backing the floor/prop prefabs.
- **`Animations/CharacterAnimator.controller`** — created via `UnityEditor.Animations.AnimatorController` (editor script), not hand-authored YAML. Base layer: `Locomotion` (1D blend tree on `Speed`: idle→walk→sprint), `Jump`, `Fall`. Shared by all 12 `Characters/` prefabs (each just points its `Animator.m_Controller` at it), rather than each carrying its own copy.
- **`Models/`** — only the FBX/glb models a demo prefab actually references (17 buildings, 15 cars, 12 character FBX — `character-male-a` through `-f`, `character-female-a` through `-f` — the fountain `.glb`), decided by `AssetDatabase.GetDependencies` over the demo prefabs and `CharacterAnimator.controller`, not by inspection. Everything else from the same asset packs stays in `Assets/Models/` in this repo and is **not** part of the package. There are no `Characters/` orphans left: the 12 referenced characters moved into the package, and the remaining 14 (10 mobility-aid models, 4 wheelchairs) were removed from the repo entirely. `Buildings/` and `Cars/` each still carry a `Textures/colormap.png` — a texture atlas shared with the orphan models of the same category still in `Assets/Models/`, so it's **copied** into the package (not moved) to avoid breaking those orphans; the same file exists in both places on purpose. Each of the 12 character FBX's importer was reconfigured by editor script to `animationType: Generic` with an Avatar created from the model, and `idle`/`walk`/`sprint`/`fall`/`crouch`/`static` set to loop.
- **`Input/InputSystem_Actions.inputactions`** — the only input asset (`generateWrapperCode: 0`; scripts look up the `Player` map and its actions by name through a serialized `InputActionAsset` reference). The project uses the new Input System exclusively (`activeInputHandler: 1`) — `UnityEngine.Input` throws at runtime. The tool takes this asset as a *setting* (`general.inputActions`), never by hardcoded path.

## Not part of the package

`Assets/Settings/` holds the URP pipeline assets (separate PC and Mobile renderer/pipeline configs) and volume profiles. Project configuration, not deliverable; see `../technical-review.md` for the recommended values.
