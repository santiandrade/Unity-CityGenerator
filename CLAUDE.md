# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository. This file is the index: it states what must not be broken and points at the detail. **Read the linked document before touching the area it covers.**

## Project overview

Unity project (`City Generator`, Unity `6000.5.8f1`, URP) whose **only reason to exist is to develop, test and distribute the City Generator tool** — an Editor window that procedurally generates a city (roads, sidewalks, markings, buildings, plazas, street furniture, traffic lights, autonomous traffic, pedestrians and a minimap HUD) into a new or existing scene.

The tool lives in the embedded package `Packages/com.santiandrade.citygenerator/` and that package **is** the deliverable: what a user installs via **Package Manager > Install package from git URL**. This project consumes it the same way — as an installed package, not as loose code in `Assets/` — so a portability break shows up here first, not in the user's project. User-facing docs (install URL, update procedure, full manual) live in the root `README.md` / `README.es.md`.

Everything else in the repo supports the package, in one of three roles:

- **Demo content** — `Packages/com.santiandrade.citygenerator/DefaultAssets/`, ships inside the package.
- **Orphan models** — `Assets/Models/`, FBX/glb library entries from the same asset packs that no demo prefab uses. Kept for future use, deliberately **not** in the package.
- **Test scene** — `Assets/Scenes/City.unity`, disposable output kept only to eyeball the result.

The tool's behaviour is considered **done and correct**: reproduce it, don't redesign it.

Historical note: the layout the tool reproduces was originally a hand-built ProBuilder city, removed in commit `ff13e28`. Its numbers survive as `CityGeneratorConstants`. ProBuilder itself was removed from the project in commit `89ffaf4` — the demo floor/prop meshes were baked out into `DefaultAssets/Meshes/` and nothing depends on the package any more.

## Architecture — where the detail lives

| Area | Document |
| --- | --- |
| Editor window, UI Toolkit, validation, pipeline, builders, defaults | [`docs/architecture/editor-tool.md`](docs/architecture/editor-tool.md) |
| Runtime components: player, camera, traffic graph/agents/manager, collider policy | [`docs/architecture/runtime-and-traffic.md`](docs/architecture/runtime-and-traffic.md) |
| Pedestrian network, agents, manager, layers, removed POI machinery | [`docs/architecture/pedestrians.md`](docs/architecture/pedestrians.md) |
| Custom Places | [`docs/architecture/custom-places.md`](docs/architecture/custom-places.md) |
| Demo content (`DefaultAssets/`) | [`docs/architecture/demo-content.md`](docs/architecture/demo-content.md) |
| Test suite, test scene, versioning/release | [`docs/architecture/tests-scene-and-release.md`](docs/architecture/tests-scene-and-release.md) |

### Package layout

Embedded Unity package (`name` `com.santiandrade.citygenerator`, `unity` `6000.0`, dependencies `com.unity.inputsystem` and `com.unity.cloud.gltfast`), two assemblies:

- `Runtime/CityGenerator.Runtime.asmdef` — namespace `CityGenerator.Runtime`, references `Unity.InputSystem`.
- `Editor/CityGenerator.Editor.asmdef` — namespace `CityGenerator.Editor`, `includePlatforms: [Editor]`, references the runtime asmdef and `Unity.InputSystem`.

### Generation pipeline

`CityGeneratorValidator` → `CityGeneratorSceneBuilder` → `CityGeneratorContentAssembler` → `Grid` → `GroundBuilder` → `CustomPlaceBuilder` → `BuildingBuilder` → `PlazaBuilder` → `StreetPropsBuilder` → `TrafficBuilder`.

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

- [`docs/technical-review.md`](docs/technical-review.md) — standing technical review (performance, code quality, ECS analysis) with the pending findings.
- [`docs/technical-review-2026-08-25.md`](docs/technical-review-2026-08-25.md) — external review; its critical/architectural findings were addressed by SPEC 04 and its performance findings (items 6-9) by SPEC 05. Remaining medium/low-priority items (demo content, docs, `CityGeneratorWindow` splitting) stay open.
- [`docs/pedestrian-network-plan.md`](docs/pedestrian-network-plan.md) — superseded planning document, kept for history; `specs/03` is the authority.

## Invariants — do not break these

Each links to the document explaining why.

- **The tool must stay portable to any Unity project**: no dependency on this project's prefabs, materials, layers or assets outside the package's own `DefaultAssets/`, and no mutation of the user's assets. Anything project-specific belongs in the demo content, in `CityGeneratorDefaultAssets`, or in `Assets/Editor/` (`CityGeneratorReleaseWindow.cs`, `CityGeneratorSetDefaultsWindow.cs`) — never elsewhere in the package.
- **Fixes belong in the tool, not in the scene.** Hand-editing `City.unity` fixes exactly one city and is lost on the next generation.
- **`obstacles` is the single source of truth for overlap avoidance.** New categories append to it; `CityGeneratorSpatialHash` is a pure index over it, never a second source. ([editor-tool](docs/architecture/editor-tool.md))
- **`CarAgent`'s unsignalled-crossing reservation must keep all three of its pieces** — it once deadlocked every car for five minutes and its current shape *is* the fix. Likewise, forward-sensor hits are discarded by identity, never by distance. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Don't reintroduce rotation smoothing on `ThirdPersonCamera`** — it was tried and caused visible motion sickness. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Don't lower `TrafficManager.staggerMinAgentCount`** without re-verifying the default 80-car demo behaves the same. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Vehicle/pedestrian masks and layers are written per generated instance**, never trusted from the prefab's baked serialized data. ([editor-tool](docs/architecture/editor-tool.md), [pedestrians](docs/architecture/pedestrians.md))
- **A collider deeper in a user prefab's hierarchy is left completely untouched**; only the root proxy collider gets the `Vehicle`/`Pedestrian` layer. ([runtime-and-traffic](docs/architecture/runtime-and-traffic.md))
- **Never use `PropertyField` for a UI row created after the window's one-time `Bind()`** — it never binds and renders empty. ([editor-tool](docs/architecture/editor-tool.md))
- **Don't reintroduce a pedestrian-side POI stop** without a new spec; `PedestrianNetwork` has exactly three node kinds. ([pedestrians](docs/architecture/pedestrians.md))
- **Every vehicle prefab must keep its baked `CarAgent`**, or it silently falls back to identical default tuning. ([demo-content](docs/architecture/demo-content.md))
- **Layout numbers live in `CityGeneratorConstants`, never inline**; player/camera/pedestrian/crowd tuning lives in `CityGeneratorSettings`, never back in constants. ([editor-tool](docs/architecture/editor-tool.md))
- **Treat a Performance test failure as a correctness signal, not noise.** ([tests](docs/architecture/tests-scene-and-release.md))
- **The Minimap snapshot excludes vehicles/pedestrians by deactivating their groups, never `Camera.cullingMask`** — the `Vehicle`/`Pedestrian` layer sits only on each instance's root proxy collider, never its child mesh `Renderer`s, so a culling mask silently renders them anyway. ([editor-tool](docs/architecture/editor-tool.md))

## Working in this project

- There is no CLI build/lint pipeline and no CI — everything runs through the Unity Editor (or Rider/Visual Studio via the generated `.sln`). The `Unity.*.csproj` files and `.sln` at the root are IDE-generated and gitignored; never hand-edit or commit them. The test suite runs manually from the Unity Test Runner.
- New code goes in `Packages/com.santiandrade.citygenerator/Runtime` (namespace `CityGenerator.Runtime`) or `.../Editor` (namespace `CityGenerator.Editor`) — extend those, don't introduce a new namespace, and remember both are behind `.asmdef`s, so anything new they reference must be added to the asmdef's `references`.
- Structural, hard-to-hand-author assets (AnimatorControllers, ModelImporter reconfiguration, prefab creation) are built via one-off editor scripts run through the Unity MCP tooling, not by writing `.controller`/`.meta`/`.prefab` YAML directly. Create prefabs with `PrefabUtility.SaveAsPrefabAsset`, place instances with `PrefabUtility.InstantiatePrefab`, and edit existing prefabs with `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`.
- Prefer `BoxCollider`s over `MeshCollider`s on new props and floors; leave purely decorative geometry (markings, glass, small details) with no collider.
- Unity here does not pick up new/edited scripts on its own while the Editor is in the background: creating a `.cs` and calling `AssetDatabase.Refresh()` leaves the assembly stale (the type is missing from `AppDomain`, and running a command fails to compile against it). Force it with `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`, then wait — the domain reload makes the MCP briefly report "Unity not detected", and only after it do the new types resolve. Check the Unity console when a type still fails to appear.
- `Object.GetInstanceID()` is obsolete in this Unity version (`GetEntityId()`).
- Nothing in C# refers to the scene, the city root or the materials folder by name; material references are serialized by GUID. Editor scripts that need the city root should look it up as `GameObject.Find("City")`.
