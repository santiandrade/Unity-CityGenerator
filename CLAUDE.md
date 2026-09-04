# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository. This file is the index: it states what must not be broken and points at the detail. **Read the linked document before touching the area it covers.**

## Project overview

Unity project (`City Generator`, Unity `6000.5.8f1`, URP) whose **only reason to exist is to develop, test and distribute the City Generator tool** — an Editor window that procedurally generates a city (roads, sidewalks, markings, buildings, plazas, street furniture, traffic lights, autonomous traffic, pedestrians and a minimap HUD) into a new or existing scene.

The tool lives in the embedded package `Packages/com.santiandrade.citygenerator/` and that package **is** the deliverable: what a user installs via **Package Manager > Install package from git URL**. This project consumes it the same way — as an installed package, not as loose code in `Assets/` — so a portability break shows up here first, not in the user's project. User-facing docs (install URL, update procedure, full manual) live in the root `README.md` / `README.es.md`.

Everything else in the repo supports the package, in one of three roles:

- **Demo content** — `Packages/com.santiandrade.citygenerator/DefaultAssets/`, ships inside the package. Only the models a demo prefab actually references live there; unreferenced orphans from the same asset packs were deleted rather than kept (`Assets/Models/` no longer exists).
- **Test scene** — `Assets/Scenes/City.unity`, disposable output kept only to eyeball the result.

The tool's behaviour is considered **done and correct**: reproduce it, don't redesign it.

Historical note: the layout the tool reproduces was originally a hand-built ProBuilder city, removed in commit `ff13e28`. Its numbers survive as `CityGeneratorConstants`. ProBuilder itself was removed from the project in commit `89ffaf4` — the demo floor/prop meshes were baked out into `DefaultAssets/Meshes/` and nothing depends on the package any more.

## Architecture — where the detail lives

| Area | Document |
| --- | --- |
| Editor window, UI Toolkit, validation, pipeline, builders, defaults, minimap, day/night, audio | [`docs/architecture/editor-tool.md`](docs/architecture/editor-tool.md) |
| Runtime components: player, camera, traffic graph/agents/manager, collider policy | [`docs/architecture/runtime-and-traffic.md`](docs/architecture/runtime-and-traffic.md) |
| Pedestrian network, agents, manager, layers, removed POI machinery | [`docs/architecture/pedestrians.md`](docs/architecture/pedestrians.md) |
| Custom Places | [`docs/architecture/custom-places.md`](docs/architecture/custom-places.md) |
| Demo content (`DefaultAssets/`) | [`docs/architecture/demo-content.md`](docs/architecture/demo-content.md) |
| Test suite, test scene, versioning/release | [`docs/architecture/tests-scene-and-release.md`](docs/architecture/tests-scene-and-release.md) |

### Package layout

Embedded Unity package (`name` `com.santiandrade.citygenerator`, `unity` `6000.0`, dependencies `com.unity.inputsystem`, `com.unity.cloud.gltfast` and `com.unity.ugui`), two assemblies:

- `Runtime/CityGenerator.Runtime.asmdef` — namespace `CityGenerator.Runtime`, references `Unity.InputSystem` and `UnityEngine.UI` (the Minimap HUD is UGUI).
- `Editor/CityGenerator.Editor.asmdef` — namespace `CityGenerator.Editor`, `includePlatforms: [Editor]`, references the runtime asmdef and `Unity.InputSystem`.

### Generation pipeline

`CityGeneratorValidator` → `CityGeneratorSceneBuilder` → `CityGeneratorContentAssembler` → `Grid` → `GroundBuilder` → `CustomPlaceBuilder` → `BuildingBuilder` → `PlazaBuilder` → `StreetPropsBuilder` → `TrafficBuilder` → `PedestrianBuilder` → `CustomPedestrianBuilder` → `MinimapBuilder` → `AudioBuilder`.

## Specs and reviews

Specs are in `specs/` (Spanish, driven by a `/spec-*` workflow configured by `specs/.spec-config.yml`). They record the decisions taken **and explicitly discarded**, several found during manual QA — read the relevant one before changing generation logic.

| Spec | Scope |
| --- | --- |
| [`01-city-generator-tool.md`](specs/01-city-generator-tool.md) | The original tool. Read before touching generation logic. |
| [`02-unity-package-distribution.md`](specs/02-unity-package-distribution.md) | Embedding, demo content movement, README, versioning, release tooling. No generation changes. |
| [`03-pedestrian-network.md`](specs/03-pedestrian-network.md) | Autonomous pedestrian system. |
| [`04-critical-architecture-fixes.md`](specs/04-critical-architecture-fixes.md) | v2.0.0: transactional rebuild, hierarchical collider detection, `Include Traffic` validation, non-singleton managers, single Input System authority. |
| [`05-performance-and-tests.md`](specs/05-performance-and-tests.md) | v2.1.0: the `Assets/Tests/` suite, `CityGeneratorSpatialHash`, `TrafficLaneOccupancy`, `PedestrianRoadProximityGrid`, connected components / BFS caching, `PedestrianPathBufferPool`. Records the measured baseline/delta for each change. |
| [`06-custom-places.md`](specs/06-custom-places.md) | Custom Places, plus full removal of the pedestrian POI machinery. |
| [`07-minimap-hud.md`](specs/07-minimap-hud.md) | v2.3.0: the Minimap HUD, `CityGeneratorMinimapBuilder`'s snapshot capture, and wiring `isPointOfInterest` on Custom Places to it. |
| [`08-day-night-cycle.md`](specs/08-day-night-cycle.md) | The optional Day/Night Cycle: `Runtime/DayNightCycle.cs`, and the first case where "Rebuild City in Current Scene" reconfigures the Directional Light instead of leaving it untouched. |
| [`09-city-audio.md`](specs/09-city-audio.md) | The Audio tab: looping 2D Ambience and one positional 3D source per generated plaza, both applied on Build and Re-Build. |
| [`10-pedestrian-interior-routes.md`](specs/10-pedestrian-interior-routes.md) | The `Interior` pedestrian node kind (a cross through the gap between a normal block's 4 building slots) and how `PlanNewDestination` reaches it. |
| [`11-custom-grid.md`](specs/11-custom-grid.md) | Custom Grid: the "Customize" mode replacing the rectangular footprint with an arbitrary poliomino, the 3-arm traffic light rule (applied to both grid modes), and the perimeter sidewalk band + walkway that make a city end in sidewalk rather than asphalt. |
| [`12-custom-pedestrians.md`](specs/12-custom-pedestrians.md) | Custom Pedestrians: a separate budget of pedestrians confined to a hand-traced subgraph, its node-graph picker, and the `Pets/` demo prefabs (rigid-rig Animator culling). |
| [`13-free-camera.md`](specs/13-free-camera.md) | Free Camera: `FreeCameraController` alongside `ThirdPersonCamera` on the Main Camera, and the `Free View` action map. |
| [`16-multiple-cities.md`](specs/16-multiple-cities.md) | Multiple cities coexisting in one scene: root-relative traffic/pedestrian graphs, hierarchy-scoped signal matching, `MinimapData.localCenter`/`localSize`, and the `Rebuild Minimap` menu. |

- [`docs/user-manual.md`](docs/user-manual.md) / [`docs/user-manual.es.md`](docs/user-manual.es.md) — end-user manual for the window (every tab, card and parameter), linked from the READMEs. Screenshots live in `docs/images/manual/`; keep both language versions in sync when the UI changes.
- [`docs/technical-review.md`](docs/technical-review.md) — standing technical review (performance, code quality, ECS analysis) with the pending findings.
- [`docs/technical-review-2026-08-25.md`](docs/technical-review-2026-08-25.md) — external review; its critical/architectural findings were addressed by SPEC 04 and its performance findings (items 6-9) by SPEC 05. Remaining medium/low-priority items (demo content, docs, `CityGeneratorWindow` splitting) stay open.
- [`docs/pedestrian-network-plan.md`](docs/pedestrian-network-plan.md) — superseded planning document, kept for history; `specs/03` is the authority.

## Invariants — do not break these

Each links to the document explaining why.

- **The tool must stay portable to any Unity project**: no dependency on this project's prefabs, materials, layers or assets outside the package's own `DefaultAssets/`, and no mutation of the user's assets. Anything project-specific belongs in the demo content, in `CityGeneratorDefaultAssets`, or in `Assets/Editor/` (`CityGeneratorReleaseWindow.cs`, `CityGeneratorSetDefaultsWindow.cs`) — never elsewhere in the package.
- **Fixes belong in the tool, not in the scene.** Hand-editing `City.unity` fixes exactly one city and is lost on the next generation.
- **A generated city always ends in sidewalk, never in bare asphalt**, on both grid modes, and that sidewalk is walkable. Both the ground band and the perimeter sidewalk band are tiled by `CityGeneratorGroundBuilder.EnumerateBand` as an exact dilation difference worked per *missing* cell — the two obvious alternatives (per-real-cell strips, or per-missing-cell strips with a square corner fill) leave z-fighting overlaps and a bare convex corner respectively. ([editor-tool](docs/architecture/editor-tool.md), [pedestrians](docs/architecture/pedestrians.md))
- **A Custom Grid city ends up as the plain rectangle of its own bounding box**: every gap is filled by `CityGeneratorGroundBuilder.BuildEmptyBlocks` with the Ground card's `Empty Block Prefab`, tiled by `EnumerateEmptyFill` as the bounding rectangle (grown by `RoadBaseMargin`) minus the shape's paved dilation — so the fill stops at the outer edge of the perimeter sidewalk instead of hiding it. ([editor-tool](docs/architecture/editor-tool.md))
- **`obstacles` is the single source of truth for overlap avoidance.** New categories append to it; `CityGeneratorSpatialHash` is a pure index over it, never a second source. ([editor-tool](docs/architecture/editor-tool.md))
- **`CarAgent`'s unsignalled-crossing reservation must keep all three of its pieces** — it once deadlocked every car for five minutes and its current shape *is* the fix. Likewise, forward-sensor hits are discarded by identity, never by distance. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Don't reintroduce rotation smoothing on `ThirdPersonCamera`** — it was tried and caused visible motion sickness. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Don't lower `TrafficManager.staggerMinAgentCount`** without re-verifying that a default-settings demo city behaves the same. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Vehicle/pedestrian masks and layers are written per generated instance**, never trusted from the prefab's baked serialized data. ([editor-tool](docs/architecture/editor-tool.md), [pedestrians](docs/architecture/pedestrians.md))
- **`TrafficNetwork`/`PedestrianNetwork` build every node position/direction via `transform.TransformPoint`/`TransformDirection`, never in absolute world space.** SPEC 16: this is what lets a `CityGeneratorRoot` be copied to a new position (translation only — rotation/scale are unsupported, see the user manual) and still drive a correct graph. A single point built without going through the root's transform strands that subset of nodes at the origin while the rest of the graph moves with the root — a bug invisible at the default `(0,0,0)` position that almost all QA runs at. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md), [pedestrians](docs/architecture/pedestrians.md))
- **Neither network searches the whole scene for its `TrafficLight`/`TrafficLightIntersection` matches.** `TrafficNetwork.AssignTrafficLights` and `PedestrianNetwork.Build` resolve their own `CityGeneratorRoot` ancestor (`GetComponentInParent`) and search only `GetComponentsInChildren` under it, so two cities in the same scene never cross-match each other's signals — falling back to a scene-wide `FindObjectsByType` only when there is no `CityGeneratorRoot` ancestor at all (a synthetic network in a test, or standalone use outside the generation pipeline). ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md), [pedestrians](docs/architecture/pedestrians.md))
- **A pedestrian prefab whose model isn't skinned (no `SkinnedMeshRenderer`, e.g. rigid per-limb `MeshRenderer`s) must get `Animator.cullingMode = Always Animate`, never `Cull Completely`** — `CityGeneratorPedestrianBuilder.ApplyAnimatorCullingMode` decides this per instance; hardcoding `Cull Completely` again silently freezes that rig's Animator forever (parameters keep updating, state time never advances, no console warning). A new FBX for such a prefab also needs its Locomotion clips' `Loop Time` explicitly enabled (`ModelImporter.clipAnimations`) — auto-split take clips default to it off, which freezes the pose on the last frame after under a second in a visually identical way but needs the opposite fix. ([pedestrians](docs/architecture/pedestrians.md))
- **A collider deeper in a user prefab's hierarchy is left completely untouched**; only the root proxy collider gets the `Vehicle`/`Pedestrian` layer. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Pedestrian obstacle avoidance is purely physics-based, on `Collider` alone**: `PedestrianNetwork.PrunePlacedObstacles`'s `Physics.CheckSphere` is the only mechanism that blocks a node. An obstacle (a Custom Place, a custom pedestrian prefab, any demo asset) with no `Collider` anywhere in its hierarchy is never treated as blocking, by design — a rect-based fallback for that case existed and was removed after it wrongly blocked ~35% of a generated city's nodes (see [pedestrians](docs/architecture/pedestrians.md)'s "Obstacle pruning"). Don't reintroduce a non-physics obstacle check; the fix for a `Collider`-less asset that should block pedestrians is to give it a `Collider`.
- **Never use `PropertyField` for a UI row created after the window's one-time `Bind()`** — it never binds and renders empty. ([editor-tool](docs/architecture/editor-tool.md))
- **Don't reintroduce a pedestrian-side POI stop** without a new spec; `PedestrianNetwork` has exactly four node kinds (`Ring`, `Curb`, `Crossing`, `Interior`). ([pedestrians](docs/architecture/pedestrians.md))
- **The validator asks the traffic builder whether a grid has a signalled intersection; it never re-derives the rule.** `CityGeneratorTrafficBuilder.HasSignalledIntersection` (>= `SignalledIntersectionMinArms` real arms, the same count `BuildTrafficLights` loops on) is the single predicate, and `SignalledIntersectionAgreementTests` pins the two to each other. The two once disagreed — the validator only demanded a Traffic Light prefab on a grid larger than 1x1, or a Custom shape containing a full 2x2 of cells, while the builder already signalled the T-intersections of a 1xN/Nx1 grid or an L-shaped one — so such a city passed validation with no prefab and then instantiated a null one mid-generation. Note the lights are built even with `Include Traffic` off, so the prefab requirement is deliberately independent of it. ([editor-tool](docs/architecture/editor-tool.md))
- **Every vehicle prefab must keep its baked `CarAgent`**, or it silently falls back to identical default tuning. ([demo-content](docs/architecture/demo-content.md))
- **Layout numbers live in `CityGeneratorConstants`, never inline**; player/camera/pedestrian/crowd tuning lives in `CityGeneratorSettings`, never back in constants. ([editor-tool](docs/architecture/editor-tool.md))
- **Treat a Performance test failure as a correctness signal, not noise.** ([tests](docs/architecture/tests-scene-and-release.md))
- **The Directional Light's yaw is always forced to -110°** on both Build and Re-Build (`CityGeneratorSceneBuilder.DirectionalLightYaw`, pushed into `DayNightCycle.SetBaseRotation` so an old baked yaw is corrected too), so the sun rises east-north-east and sets west-south-west, roughly matching the minimap's orientation (minimap-right is East) without putting shadows exactly along a street. `CityGeneratorMinimapBuilder`'s neutral snapshot light reads the same constant — don't hardcode a second copy. ([editor-tool](docs/architecture/editor-tool.md))
- **The Minimap snapshot renders under its own neutral daytime light**: every enabled directional light in memory is disabled and a temporary white one added for the capture, so the snapshot never bakes in the Day/Night Cycle's current hour. Unlike hiding a preexisting GameObject, toggling a preexisting `Light` *does* take effect on a same-call `Camera.Render()`. ([editor-tool](docs/architecture/editor-tool.md))
- **The minimap's map `RawImage` must keep its `MinimapWindow` material.** `MinimapHUD` never clamps its `uvRect` to [0, 1] (the player stays exactly centred), so near a city edge the window reaches past the snapshot; that shader is what paints the remainder with the capture camera's background colour. The stock UI material instead smears the PNG's `Clamp`-wrapped border pixels across the whole minimap. ([editor-tool](docs/architecture/editor-tool.md))
- **The Minimap snapshot excludes vehicles/pedestrians by deactivating their groups, never `Camera.cullingMask`** — the `Vehicle`/`Pedestrian` layer sits only on each instance's root proxy collider, never its child mesh `Renderer`s, so a culling mask silently renders them anyway.
- **The Minimap snapshot isolates itself by moving `cityRoot` to a far-away offset before capturing, never by hiding/moving whatever else is loaded** (never `Camera.scene`, which silently fails to filter an unsaved scene). A manual `Camera.Render()` doesn't reflect a same-call `SetActive`/layer/position change on a GameObject Unity has already rendered before — confirmed directly with a minimal repro — so hiding another scene's root or a Re-Build's still-alive previous `CityGeneratorRoot` doesn't reliably work. `cityRoot` itself is always freshly created for the call, so moving *it* (never anything preexisting) is what actually works. ([editor-tool](docs/architecture/editor-tool.md))

## Working in this project

- **Documentation records mechanisms and the reasons behind them, never current values.** Don't write the tool's default settings, package version, asset counts or prefab inventories into `README*.md`, `CLAUDE.md` or `docs/` — those change on any "Set Current Selection As Default" or content tweak, and a stale number in a doc is worse than no number. Point at the source instead (`CityGeneratorDefaultAssets.ApplyTo`, `CityGeneratorSettings`, `CityGeneratorConstants`, `package.json`, the asset folder). A constant's *value* is fine to cite where the value is itself the explanation (why a band is that wide, why a threshold sits where it does); a settings default is not.
- `specs/`, `docs/technical-review-2026-08-25.md` and `docs/pedestrian-network-plan.md` are **historical records, not living documents** — they say what was decided or found at a point in time. Don't rewrite them to match today's code; correct today's docs instead.
- There is no CLI build/lint pipeline and no CI — everything runs through the Unity Editor (or Rider/Visual Studio via the generated `.sln`). The `Unity.*.csproj` files and `.sln` at the root are IDE-generated and gitignored; never hand-edit or commit them. The test suite runs manually from the Unity Test Runner.
- New code goes in `Packages/com.santiandrade.citygenerator/Runtime` (namespace `CityGenerator.Runtime`) or `.../Editor` (namespace `CityGenerator.Editor`) — extend those, don't introduce a new namespace, and remember both are behind `.asmdef`s, so anything new they reference must be added to the asmdef's `references`.
- Structural, hard-to-hand-author assets (AnimatorControllers, ModelImporter reconfiguration, prefab creation) are built via one-off editor scripts run through the Unity MCP tooling, not by writing `.controller`/`.meta`/`.prefab` YAML directly. Create prefabs with `PrefabUtility.SaveAsPrefabAsset`, place instances with `PrefabUtility.InstantiatePrefab`, and edit existing prefabs with `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`.
- Prefer `BoxCollider`s over `MeshCollider`s on new props and floors; leave purely decorative geometry (markings, glass, small details) with no collider.
- Unity here does not pick up new/edited scripts on its own while the Editor is in the background: creating a `.cs` and calling `AssetDatabase.Refresh()` leaves the assembly stale (the type is missing from `AppDomain`, and running a command fails to compile against it). Force it with `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`, then wait — the domain reload makes the MCP briefly report "Unity not detected", and only after it do the new types resolve. Check the Unity console when a type still fails to appear.
- `Object.GetInstanceID()` is obsolete in this Unity version (`GetEntityId()`).
- Nothing in C# refers to the scene, the city root or the materials folder by name; material references are serialized by GUID. Editor scripts that need the city root should look it up as `GameObject.Find("City")`.
