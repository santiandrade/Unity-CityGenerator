🇪🇸 [Leer en español](api-reference.es.md)

# Runtime API Reference

`CityGeneratorAPI` (`CityGenerator.Runtime` namespace) is the resolution entry point for a
generated city's data at runtime — both in the Editor's Play Mode and in a finished player build.
It resolves an explicit handle, `CityGeneratorCity`, against a registry that every generated
city's `CityGeneratorInfo` component maintains by registering itself while active. No setup, no
assembly reference beyond the package itself.

```csharp
using CityGenerator.Runtime;

CityGeneratorCity? city = CityGeneratorAPI.Default;
if (city.HasValue)
{
    Vector2Int size = city.Value.City.GridSize;
    int vehicles = city.Value.Traffic.VehicleCount;
}

// or, chaining through Nullable<T> without a `.Value`:
int buildingCount = CityGeneratorAPI.Default?.City.BuildingCount ?? 0;
```

## How it works

- `CityGeneratorInfo` — added to the city's root object by
  `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler` on every Build and Re-Build —
  registers itself with `CityGeneratorAPI` in its own `OnEnable` and unregisters in `OnDisable`,
  the same lifecycle pattern `TrafficManager`/`PedestrianManager` use for their agents. There is no
  cached lookup and no global search (`FindFirstObjectByType`/`FindAnyObjectByType`/
  `FindObjectsByType`) anywhere in `CityGeneratorAPI`: a scene unload or a domain reload clears
  stale entries by construction, because the component that would report them is itself gone.
- `CityGeneratorAPI.Default` resolves to the one registered city, or `null` when there are zero or
  more than one. With more than one it's genuinely ambiguous which city the caller wants, so it
  returns `null` and logs **one warning per session** (not per call) rather than guessing — use
  `All`, `InScene`, or `For` instead when more than one city can be registered at once.
- `CityGeneratorAPI.All` lists every currently registered (active) city, in registration order.
  `CityGeneratorAPI.InScene(scene)` resolves the one registered in a specific `Scene`.
  `CityGeneratorAPI.For(info)` resolves a handle for a `CityGeneratorInfo` you already have a
  reference to — the only one of the four that also resolves a **deactivated** city, since a
  deactivated root is (deliberately) absent from the registry and therefore from `All`/`Default`/
  `InScene`.
- `CityGeneratorCity` is an immutable handle — a `readonly struct` wrapping the `CityGeneratorInfo`
  reference, not a copy of its data — so it can never go stale relative to the city while
  `IsValid`. `IsValid` is `false` once the underlying `CityGeneratorInfo` has been destroyed.
  `IsActive` is `false` also when the city's root is currently deactivated. Every module getter
  returns a safe default (`0`, `false`, `Vector2Int.zero`, `Vector3.zero`, or `null`) once
  `IsValid` is `false`, and every setter is a silent no-op — none of them throw. The one operation
  that *does* throw on an invalid handle is `.Value` on a `null` `CityGeneratorCity?` — use `?.`
  with `??`, not `.Value`, when you haven't already checked `HasValue`.
- The API is pull-only: read a property whenever you need the value. There's no event/callback
  system (no `OnCityRegistered`, no `OnHourChanged`) — poll from your own `Update`, coroutine, or
  UI refresh as needed.
- Almost everything is read-only, exposed as a property (`city.City.BuildingCount`). The handful of
  exceptions are mutations over behaviour the tool itself already performs safely at runtime (the
  Day/Night Cycle preview, the Minimap HUD) — always a **method** (`city.City.SetHour(12f)`), never
  a settable property: the modules are `readonly struct`s returned by value, so
  `city.Minimap.ViewRadiusMeters = 120f` would not compile (CS1612). Nothing in this API spawns,
  despawns, or resizes anything: vehicle/pedestrian counts, building layout, and the grid itself
  are fixed at generation time.
- **Querying too early can miss a city that exists.** Registration happens in `CityGeneratorInfo`'s
  own `OnEnable`, and Unity does not guarantee any ordering between different objects' `OnEnable`
  calls. A script that calls `CityGeneratorAPI.Default` from its own `Awake`/`OnEnable` can get
  `null` even though the city is about to exist a moment later. Querying from `Start` onward is
  safe; the previous (v2.10) static API hid this because it resolved lazily with
  `FindFirstObjectByType`, which finds an object regardless of whether its own `OnEnable` has run
  yet.
- **Several cities are resolvable, but the tool does not yet support several cities coexisting at
  different physical positions.** `All`/`InScene`/`For` exist because nothing stops two
  `CityGeneratorInfo` components from being loaded into memory at once (e.g. two scenes loaded
  additively during a transition, or a deliberately pooled second city kept deactivated), and this
  API resolves each one correctly. But generation itself still produces absolute world coordinates
  — `TrafficNetwork.IntersectionPosition` builds its `Vector3` directly from grid axes, without
  `TransformPoint` — so moving a `CityGeneratorRoot` moves its geometry but not its traffic/
  pedestrian graph. Don't build on the assumption that two registered cities can be relocated to
  different parts of the same world; that is future work, not something this API already delivers.

## `CityGeneratorCity.City`

Grid shape, generated content counts, seed, and the Day/Night Cycle (folded in here rather than
its own module, since conceptually it's about the current city, not a separate system).

| Member | Returns / does |
| --- | --- |
| `IsCustomGrid` | `true` if this city used Custom Grid instead of a rectangular grid. |
| `GridSize` | Rectangular grid: `(gridWidth, gridHeight)`. Custom Grid: bounding box of the real cells. |
| `BlockCount` | Number of real blocks (plazas included). |
| `BuildingCount` | Number of generated building instances. |
| `PlazaCount` | Number of blocks that are plazas. |
| `CustomPlaceCount` | Number of generated Custom Place instances. |
| `LampCount` | Number of generated street lamps. |
| `BinCount` | Number of generated bins. |
| `StreetTreeCount` | Number of generated street trees. |
| `TrafficLightCount` | Number of generated traffic lights (generated at every 4-way intersection regardless of whether traffic itself is enabled). |
| `IsSeeded` | `true` if this city was generated with a Custom Seed. |
| `Seed` | The seed used, or `0` when `IsSeeded` is `false`. |
| `IsDayNightEnabled` | `true` if the Day/Night Cycle is enabled and actively advancing. |
| `SetDayNightEnabled(bool enabled)` | Enables/disables the cycle's `Update` (`DayNightCycle.enabled`) — disabling freezes the light at its current hour. |
| `CurrentHour` | Current simulated hour (0-24). |
| `SetHour(float hour)` | Repositions the light instantly to `hour`, exactly like the Editor's own preview. |

## `CityGeneratorCity.Player`

Read-only. `IsEnabled` is `false` (and every other getter returns its default) whenever the
Player was disabled at generation time.

| Member | Returns |
| --- | --- |
| `IsEnabled` | `true` if the Player is enabled and its instance exists. |
| `Position` | The Player's current world position, or `Vector3.zero` if disabled. |
| `IsFreeViewActive` | `true` if Free View is currently active. There is no setter — Free View is toggled only by its own input action (the V key by default), by design. |

## `CityGeneratorCity.Traffic`

Read-only.

| Member | Returns |
| --- | --- |
| `IsEnabled` | `true` if traffic (vehicles) is enabled. |
| `VehicleCount` | The **live** number of registered `CarAgent`s — not a count frozen at generation time. A car can self-disable (e.g. a Custom Grid dead end), so this can be lower than the configured Vehicle Count. |

## `CityGeneratorCity.Pedestrians`

Read-only.

| Member | Returns |
| --- | --- |
| `IsEnabled` | `true` if (general) pedestrians are enabled. |
| `Count` | The **live** number of registered `PedestrianAgent`s (general pedestrians and Custom Pedestrians combined) — not a count frozen at generation time. |
| `CustomCount` | The configured Custom Pedestrian entry count (a budget independent of `IsEnabled`), not a live agent count. |

## `CityGeneratorCity.Minimap`

| Member | Returns / does |
| --- | --- |
| `IsEnabled` | `true` if the Minimap HUD is enabled. |
| `PointOfInterestCount` | Number of Custom Places marked as Point of Interest. |
| `ViewRadiusMeters` | Current view radius, in meters. |
| `SetViewRadiusMeters(float meters)` | Changes the view radius; takes effect the next frame. |
| `IsVisible` | `true` if the HUD's Canvas GameObject is currently active. |
| `SetVisible(bool visible)` | Shows/hides the HUD by toggling its Canvas GameObject. |

## `CityGeneratorCity.Audio`

Read-only.

| Member | Returns |
| --- | --- |
| `IsAmbienceEnabled` | `true` if 2D ambience audio is enabled. |
| `AmbienceClipCount` | Number of active ambience AudioSources. |
| `IsPlazaAudioEnabled` | `true` if positional plaza audio is enabled. |
| `PlazaAudioSourceCount` | Number of active plaza AudioSources (one per plaza, per configured clip). |

## Resolving a city: `CityGeneratorAPI`

| Member | Returns / does |
| --- | --- |
| `Default` | `CityGeneratorCity?` — the one registered city, or `null` with zero or more than one (logs one warning per session when ambiguous). |
| `All` | `IReadOnlyList<CityGeneratorCity>` — every registered (active) city, in registration order. A live view, not a copy. |
| `Count` | Number of registered cities. |
| `InScene(Scene scene)` | `CityGeneratorCity?` — the city registered in that scene, or `null`. |
| `For(CityGeneratorInfo info)` | `CityGeneratorCity?` — resolves even a deactivated city; `null` only if `info` is `null` or destroyed. |

## Things this API deliberately does not do

- **No mutation that spawns, despawns, or resizes content** — vehicles, pedestrians, buildings, the
  grid itself. Generation is exclusively an Editor workflow.
- **No `SetFreeViewActive`** — Free View stays a pure input toggle (the V key).
- **No setters on Traffic/Pedestrians/Audio/Player** — those modules are read-only.
- **No events/callbacks.** Poll instead.
- **No support for multiple cities coexisting at different physical positions** — see the note
  above; resolving several registered cities is supported, relocating them is not.

A setter called on `Minimap` (or `City`'s Day/Night setters) acts directly on the live scene
instance; if the Editor then performs a Re-Build (only possible in the Editor, never in a build),
those runtime changes are lost along with everything else the previous city held.

## Migrating from v2.10

v2.10 shipped a static, cached, single-city API. It is **removed** as of this release (breaking
change) in favor of the explicit handle above, because the static cache never invalidated —
`FindFirstObjectByType`'s result was resolved once and reused forever, which silently answered
questions about a destroyed or wrong city once more than one existed in memory.

| v2.10 | This release |
| --- | --- |
| `CityGeneratorAPI.IsCityAvailable` | `CityGeneratorAPI.Default.HasValue` (or `CityGeneratorAPI.Default is { } city` and use `city` directly) |
| `CityGeneratorAPI.City.GetGridSize()` | `CityGeneratorAPI.Default?.City.GridSize` |
| `CityGeneratorAPI.City.GetBuildingCount()` | `CityGeneratorAPI.Default?.City.BuildingCount ?? 0` |
| `CityGeneratorAPI.City.SetHour(12f)` | `CityGeneratorAPI.Default?.City.SetHour(12f)` |
| `CityGeneratorAPI.Traffic.GetVehicleCount()` | `CityGeneratorAPI.Default?.Traffic.VehicleCount ?? 0` |
| `CityGeneratorAPI.Minimap.SetViewRadiusMeters(120f)` | `CityGeneratorAPI.Default?.Minimap.SetViewRadiusMeters(120f)` |
| every other static getter/setter | same name, `PascalCase` property instead of `Get*()`/`Is*()` method, reached through a resolved `CityGeneratorCity` | 

Every getter/setter above keeps the exact same data and mutation surface as v2.10 — this release
changes how the city is *resolved*, not what can be read or written. Two migration pitfalls worth
calling out explicitly:

- **Prefer `?.` with `??` over `.Value`.** `CityGeneratorAPI.Default` is `CityGeneratorCity?`; a
  mechanical `CityGeneratorAPI.Default.Value.City.BuildingCount` throws where v2.10 would have
  quietly returned `0`. The `?.`/`??` pattern above preserves the old "never throws" guarantee.
- **Move any lookup out of `Awake`/`OnEnable` into `Start` (or later).** See "Querying too early"
  above — this is the one behavior change capable of breaking code that worked under the old lazy
  `FindFirstObjectByType` cache.
