# Demo content — `Packages/com.santiandrade.citygenerator/DefaultAssets/`

Detail behind the "Demo content" bullet of the root `CLAUDE.md`. Ships inside the package (moved there by SPEC 02 so it's portable to any project that installs it), loaded by `CityGeneratorDefaultAssets` from hardcoded `DefaultAssets/...` paths. Unity treats package content as read-only in the consuming project — to edit a demo prefab, copy it into your own `Assets/` first.

## `DefaultAssets/Prefabs/`

- **`Buildings/`** — 17 modelled prefabs: `Building-A` through `-I` plus `-L`/`-M` (there is no `-J`/`-K`), `Building-Skyscraper-A` through `-E`, and `Building-Hospital`. Root scale 10, pivot at base, own collider; widest footprint ~13.9 m, clearing the 22 m slot. `Building-Hospital` is **not** part of the random building rotation — it's used exclusively as the demo's whole-block Custom Place entry, wired in by `CityGeneratorDefaultAssets.ApplyTo` rather than `buildingPrefabs` (see `custom-places.md`).
- **`Characters/`** — 12 selectable Player Prefab candidates, doubling as the default pedestrian list: `Character-Male-A` through `-F`, `Character-Female-A` through `-F`. Each a clean model+Animator prefab with **no movement component baked in**; the default Player Prefab is `Character-Male-D`.
- **`Floors/`** — `RoadBase`, `RoadSidewalk`, `RoadDash`, `RoadZebra`, `Lawn`. Plain `MeshFilter`/`MeshRenderer` prefabs pointing at the extracted mesh assets in `DefaultAssets/Meshes/`. (They were originally authored with ProBuilder; ProBuilder was removed from the project in commit `89ffaf4` and the meshes baked out — nothing in the repo depends on it any more.)
- **`Props/`** — `Bench`, `Bin`, `Lamp`, `TrafficLight`, and `Fountain` (the only imported-model prop, built on `DefaultAssets/Models/Props/Fountain by Poly.glb` via glTFast).
- **`Vegetation/Tree.prefab`**.
- **`Vehicles/`** — 15 prefabs: `Ambulance`, `Delivery-Flat`, `DeliveryCar`, `Firetruck`, `Garbage-Truck`, `Hatchback-Sports`, `PoliceCar`, `Sedan`, `SedanSportCar`, `Suv`, `Suv-Luxury`, `TaxiCar`, `Truck`, `Truck-Flat`, `Van`.
- **`MinimapHUD.prefab`** (SPEC 07) — Canvas (Screen Space Overlay) + a circle-masked `RawImage`, player marker and a deactivated POI marker template, wired to a `Runtime/MinimapHUD` component. Built via an editor script (procedurally-generated sprites, see below), not hand-authored. Instantiated by `CityGeneratorSceneBuilder` when `minimap.enabled`.

## `DefaultAssets/Sprites/`

`Minimap_Circle.png` (the HUD's circular mask), `Minimap_PlayerArrow.png` (player marker), `Minimap_POIPin.png` (the single generic POI icon reused for every Point of Interest) — all three generated procedurally by a one-off editor script (flat shapes on a transparent background), not authored externally.

## Colliders

Deliberately simple: `BoxCollider`s on floors, benches, lamps and traffic lights; `MeshCollider`s only on `Tree` (trunk + crown); **no collider at all** on road markings. Vehicles carry a single root `BoxCollider` (no `Rigidbody` — they move by transform, and the collider exists only so they can detect each other). See the collider policy in `runtime-and-traffic.md` for what happens at generation time when a vehicle or pedestrian prefab has none.

## Baked `CarAgent` tuning

Each of the 15 vehicle prefabs carries its own baked `CarAgent` component with distinct tuning (`maxSpeed`, `acceleration`, `braking`, `turnSpeed`, `cornerSpeedFactor`) so mixed traffic doesn't drive identically: heavy vehicles (`Garbage-Truck`, `Truck`, `Truck-Flat`, the delivery vans) are slowest with the widest turns, emergency vehicles (`Ambulance`, `Firetruck`, `PoliceCar`) are tuned for snappy acceleration, and sports/luxury cars (`Hatchback-Sports`, `SedanSportCar`, `Suv-Luxury`) are fastest and hardest-accelerating.

`CityGeneratorTrafficBuilder.BuildVehicles` only `AddComponent`s a fresh `CarAgent` (with the script's own defaults) when a prefab doesn't already carry one — **every vehicle prefab must keep its baked `CarAgent`**, or it silently falls back to identical default tuning at generation time. The generator adds a ±6% `maxSpeed` jitter per instance on top.

## Other folders

- **`Materials/`** — the flat-colour URP/Lit palette (14 materials: `Asphalt`, `Sidewalk`, `RoadLine`, `Crosswalk`, `Grass`, `GlassBlue`, `TreeTrunk`/`TreeLeaves`, `MetalDark`, and the emissive `LightRed`/`LightAmber`/`LightGreen`/`LightOff`/`LampWarm`). Created by editor script, not by hand. The fountain is the only geometry that doesn't use this palette — it keeps the materials embedded in its `.glb`.
- **`Meshes/`** — the extracted mesh assets backing the floor/prop prefabs.
- **`Animations/CharacterAnimator.controller`** — created via `UnityEditor.Animations.AnimatorController` (editor script), not hand-authored YAML. Base layer: `Locomotion` (1D blend tree on `Speed`: idle→walk→sprint), `Jump`, `Fall`. Shared by all 12 `Characters/` prefabs (each just points its `Animator.m_Controller` at it), rather than each carrying its own copy.
- **`Models/`** — only the FBX/glb models a demo prefab actually references (17 buildings, 15 cars, 12 character FBX — `character-male-a` through `-f`, `character-female-a` through `-f` — the fountain `.glb`), decided by `AssetDatabase.GetDependencies` over the demo prefabs and `CharacterAnimator.controller`, not by inspection. `Buildings/`, `Cars/` and `Characters/` each additionally carry a `Textures/colormap.png`, the shared texture atlas those FBX sample. Everything from the same asset packs that no demo prefab references was **not** brought into the package. The colormaps were originally *copied* rather than moved precisely because those orphans still lived in `Assets/Models/` and shared the atlas; the orphans have since been deleted from the repo, so `Assets/Models/` now holds only the unrelated `Pets/` set (24 animal FBX with their own `colormap.png`) and no category is duplicated across both trees any more. Each of the 12 character FBX's importer was reconfigured by editor script to `animationType: Generic` with an Avatar created from the model, and `idle`/`walk`/`sprint`/`fall`/`crouch`/`static` set to loop.
- **`Input/InputSystem_Actions.inputactions`** — the only input asset (`generateWrapperCode: 0`; scripts look up the `Player` map and its actions by name through a serialized `InputActionAsset` reference). The project uses the new Input System exclusively (`activeInputHandler: 1`) — `UnityEngine.Input` throws at runtime. The tool takes this asset as a *setting* (`general.inputActions`), never by hardcoded path.

## Not part of the package

`Assets/Settings/` holds the URP pipeline assets (separate PC and Mobile renderer/pipeline configs) and volume profiles. Project configuration, not deliverable; see `../technical-review.md` for the recommended values.
