# Custom Places

Detail behind the "Custom Places" bullet of the root `CLAUDE.md`. Added by `specs/06-custom-places.md`.

A `CustomPlaceEntry` (`Editor/CityGeneratorSettings.cs`: `title`, `prefab`, `isPointOfInterest`, `occupiesFullBlock`, `blockCell`, `cornerSlot`, `facing`, `positionAssigned`) is a manually-placed, deliberately non-random alternative to a building: the user picks its block (and, unless it occupies the whole block, one of the 4 corner slots) from a per-entry `CityGeneratorGridPreview` in `SingleSelectQuadrant` mode, and a fixed 90°-step `facing` — never the random rotation a normal building gets.

## `CityGeneratorCustomPlaceBuilder.BuildCustomPlaces`

Instantiates every valid entry — title, prefab and an assigned position that resolves to a real, non-plaza block, checked defensively here even though `CityGeneratorValidator` already blocks Build on anything invalid — at its exact block centre (`occupiesFullBlock`) or corner offset (reusing `CityGeneratorBuildingBuilder.SlotOffsets`' 0-3 index, never a separate corner enum) and fixed `facing` rotation (90° steps, never randomised, unlike a normal building's random `Quaternion.Euler(0, 90 * random.Next(4), 0)`).

Runs **before** `CityGeneratorBuildingBuilder` in the pipeline and returns the reserved-slots set that builder excludes (`(gridX, gridY, slot)`, `slot == -1` meaning the whole block), plus its own instances, which the assembler prepends to the shared `obstacles` list — a Custom Place participates in overlap avoidance like props/vegetation, unlike a normal building.

## Rules

- `positionAssigned` distinguishes "never placed yet" from a legitimate `(0, 0)` selection.
- `CityGeneratorValidator.ValidateDetailed` blocks generation on: a missing title/prefab; no position assigned; a `blockCell` outside the grid or pointing at a plaza block; a slot conflict between two entries (same corner, or either entry occupying the whole block); and two entries sharing the same title (trimmed, case-insensitive). Same "explicit blocking error, never silently resolved by list order" convention as every other list in the tool.
- `cornerSlot` reuses `CityGeneratorBuildingBuilder.SlotOffsets`' 0-3 index rather than a separate corner enum, so the picker, `CityGeneratorCustomPlaceBuilder` and `CityGeneratorBuildingBuilder` all agree on one geometric source of truth.
- `isPointOfInterest` (SPEC 07) marks the entry as a Point of Interest on the Minimap HUD: `CityGeneratorCustomPlaceBuilder.BuildCustomPlaces` projects every entry with it set into a runtime `PointOfInterestEntry` (title + final world position), which `CityGeneratorMinimapBuilder` collects onto `MinimapData.pointsOfInterest`. See `pedestrians.md` for why the *old* pedestrian-side POI mechanism was removed instead of reused, and `editor-tool.md`'s "Minimap" section for the HUD itself.
- A Custom Place is never allowed on a plaza block (same rule as normal buildings: a block is either a plaza or buildable, no mixed case), and a quarter-block entry coexists with up to 3 random buildings in the same block's other corners — only `occupiesFullBlock` clears the whole block.

## UI

`CityGeneratorCustomPlaceList` (`Editor/UI/`) is the list editor for the Custom Places card, on the **City** tab: each row is self-contained (own title/prefab/toggle/facing controls plus its own grid preview), same convention as `CityGeneratorWeightedPrefabList`'s rows. Its fields are plain `TextField`/`ObjectField`/`Toggle`/`EnumField` controls, **not** `PropertyField` — a `PropertyField` on a row created after the window's one-time `Bind()` call never binds and renders empty (see `editor-tool.md`).
