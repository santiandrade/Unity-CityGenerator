🇪🇸 [Leer en español](api-reference.es.md)

# Runtime API Reference

`CityGeneratorAPI` (`CityGenerator.Runtime` namespace) is a static, read-first entry point for
querying a generated city's data at runtime — both in the Editor's Play Mode and in a finished
player build. It works against whatever the tool most recently generated: no setup, no instance to
fetch, no assembly reference beyond the package itself.

```csharp
using CityGenerator.Runtime;

if (CityGeneratorAPI.IsCityAvailable)
{
    Vector2Int size = CityGeneratorAPI.City.GetGridSize();
    int vehicles = CityGeneratorAPI.Traffic.GetVehicleCount();
}
```

## How it works

- `CityGeneratorAPI.IsCityAvailable` is `true` once a `CityGeneratorInfo` component — added to the
  city's root object by `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler` on every Build
  and Re-Build — has been found in the loaded scene(s). It's resolved lazily on first use and then
  **cached for the rest of the session**: the API assumes one generated city per Play session, the
  same assumption the rest of the tool makes. Regenerating the city at runtime (not something the
  tool itself does — generation is an Editor-only workflow) would leave the cached reference
  pointing at a destroyed object.
- `CityGeneratorInfo`'s own fields drive `CityGeneratorAPI`'s getters, but its Inspector is read-only
  (a custom editor greys it out) — it's a snapshot filled once at Build/Re-Build time, not a live
  control, so editing it by hand has no effect on the running city. Go through `CityGeneratorAPI`
  instead.
- Every getter, on every module, returns a safe default — `0`, `false`, `Vector2Int.zero`,
  `Vector3.zero`, or `null` for the few cases returning an object — when `IsCityAvailable` is
  `false`. None of them throw. There's no need to guard every call with an `IsCityAvailable` check
  first, though doing so once up front (as above) avoids redundant work.
- The API is pull-only: call a getter whenever you need the value. There's no event/callback system
  (no `OnCityReady`, no `OnHourChanged`) — poll from your own `Update`, coroutine, or UI refresh as
  needed.
- Almost everything is read-only. The handful of exceptions are setters over behaviour the tool
  itself already performs safely at runtime (the Day/Night Cycle preview, the Minimap HUD) — see
  below. Nothing in this API spawns, despawns, or resizes anything: vehicle/pedestrian counts,
  building layout, and the grid itself are fixed at generation time.

## `CityGeneratorAPI.City`

Grid shape, generated content counts, seed, and the Day/Night Cycle (folded in here rather than
its own module, since conceptually it's about the current city, not a separate system).

| Member | Returns / does |
| --- | --- |
| `IsCustomGrid()` | `true` if this city used Custom Grid instead of a rectangular grid. |
| `GetGridSize()` | Rectangular grid: `(gridWidth, gridHeight)`. Custom Grid: bounding box of the real cells. |
| `GetBlockCount()` | Number of real blocks (plazas included). |
| `GetBuildingCount()` | Number of generated building instances. |
| `GetPlazaCount()` | Number of blocks that are plazas. |
| `GetCustomPlaceCount()` | Number of generated Custom Place instances. |
| `GetLampCount()` | Number of generated street lamps. |
| `GetBinCount()` | Number of generated bins. |
| `GetStreetTreeCount()` | Number of generated street trees. |
| `GetTrafficLightCount()` | Number of generated traffic lights (generated at every 4-way intersection regardless of whether traffic itself is enabled). |
| `IsSeeded()` | `true` if this city was generated with a Custom Seed. |
| `GetSeed()` | The seed used, or `0` when `IsSeeded()` is `false`. |
| `IsDayNightEnabled()` | `true` if the Day/Night Cycle is enabled and actively advancing. |
| `SetDayNightEnabled(bool enabled)` | Enables/disables the cycle's `Update` (`DayNightCycle.enabled`) — disabling freezes the light at its current hour. |
| `GetCurrentHour()` | Current simulated hour (0-24). |
| `SetHour(float hour)` | Repositions the light instantly to `hour`, exactly like the Editor's own preview. |

## `CityGeneratorAPI.Player`

Read-only. `IsEnabled()` is `false` (and every other getter returns its default) whenever the
Player was disabled at generation time.

| Member | Returns |
| --- | --- |
| `IsEnabled()` | `true` if the Player is enabled and its instance exists. |
| `GetPosition()` | The Player's current world position, or `Vector3.zero` if disabled. |
| `IsFreeViewActive()` | `true` if Free View is currently active. There is no setter — Free View is toggled only by its own input action (the V key by default), by design. |

## `CityGeneratorAPI.Traffic`

Read-only.

| Member | Returns |
| --- | --- |
| `IsEnabled()` | `true` if traffic (vehicles) is enabled. |
| `GetVehicleCount()` | The **live** number of registered `CarAgent`s — not a count frozen at generation time. A car can self-disable (e.g. a Custom Grid dead end), so this can be lower than the configured Vehicle Count. |

## `CityGeneratorAPI.Pedestrians`

Read-only.

| Member | Returns |
| --- | --- |
| `IsEnabled()` | `true` if (general) pedestrians are enabled. |
| `GetPedestrianCount()` | The **live** number of registered `PedestrianAgent`s (general pedestrians and Custom Pedestrians combined) — not a count frozen at generation time. |
| `GetCustomPedestrianCount()` | The configured Custom Pedestrian entry count (a budget independent of `IsEnabled()`), not a live agent count. |

## `CityGeneratorAPI.Minimap`

| Member | Returns / does |
| --- | --- |
| `IsEnabled()` | `true` if the Minimap HUD is enabled. |
| `GetPointOfInterestCount()` | Number of Custom Places marked as Point of Interest. |
| `GetViewRadiusMeters()` | Current view radius, in meters. |
| `SetViewRadiusMeters(float meters)` | Changes the view radius; takes effect the next frame. |
| `IsVisible()` | `true` if the HUD's Canvas GameObject is currently active. |
| `SetVisible(bool visible)` | Shows/hides the HUD by toggling its Canvas GameObject. |

## `CityGeneratorAPI.Audio`

Read-only.

| Member | Returns |
| --- | --- |
| `IsAmbienceEnabled()` | `true` if 2D ambience audio is enabled. |
| `GetAmbienceClipCount()` | Number of active ambience AudioSources. |
| `IsPlazaAudioEnabled()` | `true` if positional plaza audio is enabled. |
| `GetPlazaAudioSourceCount()` | Number of active plaza AudioSources (one per plaza, per configured clip). |

## Things this API deliberately does not do

- **No mutation that spawns, despawns, or resizes content** — vehicles, pedestrians, buildings, the
  grid itself. Generation is exclusively an Editor workflow.
- **No `SetFreeViewActive`** — Free View stays a pure input toggle (the V key).
- **No setters on Traffic/Pedestrians/Audio/Player** — those modules are read-only.
- **No events/callbacks.** Poll instead.
- **No support for multiple simultaneous cities** in the same scene — the API assumes the tool's
  normal case, one active city.

A setter called on `Minimap` (or `City`'s Day/Night setters) acts directly on the live scene
instance; if the Editor then performs a Re-Build (only possible in the Editor, never in a build),
those runtime changes are lost along with everything else the previous city held.
