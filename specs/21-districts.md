# SPEC 21 — Districts (barrios)

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 06 (Custom Places), SPEC 11 (Custom Grid)
> **Fecha:** 2026-11-23
> **Objetivo:** Poder definir uno o más *Districts* (nombre + conjunto de manzanas + lista propia de prefabs de edificio) cuyas manzanas se rellenan aleatoriamente con esa lista en vez de con la lista base de Buildings.

## Scope

**Dentro:**

- **Modelo:** `struct DistrictEntry` y `List<DistrictEntry> districts` en `Editor/CityGeneratorSettings.cs` (ver "Modelo de datos").
- **Card "Districts"** en la tab City, justo después de la card "Buildings", con una lista nueva `CityGeneratorDistrictList` (`Editor/UI/`): añadir/quitar barrios; cada fila con `TextField` *Name*, `Toggle` *Override Buildings Per Block* + `SliderInt` 0-4 (visible solo con el toggle activo), una `CityGeneratorPrefabGrid` para sus edificios y su propio `CityGeneratorGridPreview` para marcar manzanas. Controles manuales, nunca `PropertyField` (la fila se crea después del `Bind()` del window).
- **Nuevo modo `CityGeneratorGridPreview` `DistrictMultiToggle`** (`BindDistrictCells`): clic marca/desmarca una manzana en el `cells` de ese barrio. Pinta las plazas en verde (`SetPlazaMask`, como Custom Places) y las manzanas de los demás barrios en un tono neutro (nuevo `SetOccupiedMask`), que siguen siendo pulsables — el solape lo detecta el validador. En modo Custom Grid solo muestra las celdas reales de la forma.
- **`CityGeneratorBuildingBuilder.BuildBuildings`** recibe `districts`: cada manzana de un barrio usa la lista de prefabs y el Buildings Per Block (override o global) de su barrio; el resto, la lista base y el valor global. La garantía "cada prefab al menos una vez" se aplica **por pool** (la base dentro de sus slots, cada barrio dentro de los suyos). Los slots reservados por Custom Places se siguen respetando dentro de un barrio.
- **Validación** (`CityGeneratorValidator`, errores bloqueantes con path `districts`): nombre vacío; nombre duplicado (trim, sin distinguir mayúsculas); ninguna manzana; manzana fuera del grid / inexistente en la forma del Custom Grid; manzana de plaza; manzana presente en dos barrios; lista de edificios vacía o con todas sus entradas a null. Warnings (no bloquean): entrada null suelta y prefab sin `Renderer`, mismo texto que la lista base.
- **Defaults:** `CityGeneratorDefaultAssets.ApplyTo` deja `districts` vacía; `CityGeneratorDefaultAssetsWriter` gana `AppendDistrictsList` para que "Set Current Selection As Default" no los borre.
- **UI derivada:** badge de la card ("N districts") y la estimación de edificios del resumen (`RefreshDynamicUi`) teniendo en cuenta los overrides.
- **Tests EditMode:** `Assets/Tests/EditMode/Generation/DistrictBuildingTests.cs` y casos nuevos en `CityGeneratorValidatorTests.cs`.
- **Documentación:** sección "Districts" en `docs/user-manual.md`/`.es.md` (tras Buildings) con captura en `docs/images/manual/`; apartado en `docs/architecture/editor-tool.md`; fila de la SPEC 21 en la tabla de `CLAUDE.md`; `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- Exponer los barrios en runtime (dato serializado en la escena, API de consulta por manzana).
- Tinte o etiqueta de barrio en el Minimap HUD.
- Agrupar la jerarquía por barrio (se mantiene `Buildings/Block_x_y/Building_x_y_n`).
- Cualquier otra configuración por barrio: vegetación, props, plazas, rotación de edificios o pesos por prefab.
- Poda automática de manzanas de un barrio cuando cambia el grid.
- Cambiar cómo trata hoy la lista **base** sus entradas null.
- Publicar una versión nueva del package.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs — CityGeneratorSettings gains:
public List<DistrictEntry> districts = new();

[Serializable]
internal struct DistrictEntry
{
    [Tooltip("Display name for this district in the tool UI and in validation messages. Required and unique.")]
    public string name;
    [Tooltip("Blocks (x, y) belonging to this district, picked by clicking its grid preview. At least one; never a plaza block; a block belongs to at most one district.")]
    public List<Vector2Int> cells;
    [Tooltip("Building prefabs randomly placed in this district's blocks instead of the base Buildings list. At least one non-empty entry required.")]
    public List<GameObject> buildingPrefabs;
    [Tooltip("If on, this district uses its own Buildings Per Block instead of the global one in General Options.")]
    public bool overrideBuildingsPerBlock;
    [Tooltip("How many of the 4 corner slots each block of this district fills (0-4). Only used when Override Buildings Per Block is on.")]
    public int buildingsPerBlock;
}
```

Notas:

- `cells` usa la misma convención `(gridX, gridY)` que `general.plazaCells` / `customBlockCells`.
- Una fila nueva se crea con `cells`/`buildingPrefabs` vacías, `overrideBuildingsPerBlock = false` y `buildingsPerBlock` = el global en ese momento.
- Builder y validador tratan siempre `cells`/`buildingPrefabs` como "null o vacía" (una entrada creada por código puede traer listas null).

## Plan de implementación

1. **Modelo.** `DistrictEntry` + `districts` en `CityGeneratorSettings`; lista vacía en `ApplyTo`; `AppendDistrictsList` en el writer. Compila; generación sin cambios.
2. **Builder.** `BuildBuildings(…, IReadOnlyList<DistrictEntry> districts = null)`: mapa celda → índice de barrio; por cada manzana no plaza (mismo orden que hoy), si su número efectivo de edificios es 0 o su pool (filtrado de nulls) está vacío se salta **sin consumir RNG**; si no, se barajan sus esquinas libres como hoy. `AssignPrefabs` se llama una vez por pool en orden fijo: base primero, luego barrios por orden de lista. El bucle de instanciación y su `random.Next(4)` de rotación siguen en el orden global de slots. El assembler pasa `settings.districts`. Con `districts` vacía, la secuencia aleatoria y el resultado son idénticos a hoy (incluido el caso lista base vacía / Buildings Per Block = 0).
3. **Tests del builder** (`DistrictBuildingTests`): regresión con seed sin barrios (misma asignación que antes del cambio); manzana de barrio solo recibe prefabs de su pool; garantía por pool; override de Buildings Per Block; Custom Place dentro de un barrio conserva su slot; manzanas fuera de barrio usan la base.
4. **Validación** con las reglas del Scope + casos en `CityGeneratorValidatorTests`.
5. **Grid preview.** Modo `DistrictMultiToggle`, `BindDistrictCells`, `SetOccupiedMask`, sin tocar los modos existentes. Verificación manual de Plazas, Custom Places, Custom Area y Custom Pedestrians.
6. **UI.** `CityGeneratorDistrictList` + card "Districts" tras Buildings; badge, resaltado por validación y estimación de edificios con overrides en `RefreshDynamicUi`.
7. **Documentación:** manual en/es con captura, `editor-tool.md`, fila en `CLAUDE.md`, `CHANGELOG.md`.

## Criterios de aceptación

- [ ] Con `districts` vacía y seed fija, la ciudad generada es idéntica a la de antes del cambio (mismo prefab, posición y rotación en cada slot) — cubierto por el test de regresión.
- [ ] En una manzana de un barrio solo se instancian prefabs de la lista de ese barrio.
- [ ] En una manzana sin barrio solo se instancian prefabs de la lista base.
- [ ] Si un barrio tiene al menos tantos slots como prefabs, cada prefab de su lista aparece al menos una vez en él.
- [ ] Con override activo a N, cada manzana del barrio sin Custom Places tiene exactamente N edificios; con override apagado, usa el valor global.
- [ ] Un Custom Place de 1/4 dentro de un barrio conserva su esquina; uno de manzana entera deja esa manzana sin edificios de barrio.
- [ ] Nombre vacío, nombre duplicado, barrio sin manzanas, manzana fuera del grid / de la forma, manzana de plaza, manzana en dos barrios, o lista de edificios vacía/todo null: cada caso bloquea ambos botones de Build y resalta en rojo la card Districts, sin diálogo.
- [ ] Una entrada null suelta o un prefab sin `Renderer` en la lista de un barrio produce un warning y no bloquea.
- [ ] El grid de cada barrio pinta sus manzanas, las plazas en verde y las de otros barrios en tono neutro; clic marca/desmarca.
- [ ] La card Districts permite añadir/quitar barrios y editar nombre, override, edificios y área; ningún campo de una fila nueva aparece vacío por un `PropertyField` sin bindear.
- [ ] "Set Current Selection As Default" con un barrio configurado lo conserva en el `CityGeneratorDefaultAssets.cs` generado.
- [ ] Las suites EditMode, PlayMode y Performance pasan completas.
- [ ] El manual en/es tiene la sección Districts con captura y `CHANGELOG.md` tiene la entrada en `## [Unreleased]`.

## Decisiones tomadas y descartadas

- **Sí:** manzana en dos barrios = error de validación. **No:** gana el primero de la lista (convención del proyecto: los conflictos se señalan, nunca se resuelven adivinando). **No:** bloquear en el grid las manzanas de otros barrios — añade estado cruzado a la UI; basta con pintarlas en neutro.
- **Sí:** manzana de plaza dentro de un barrio = error. Mantiene la regla de SPEC 06 "una manzana es plaza o edificable", sin casos mixtos.
- **Sí:** un Custom Place convive con un barrio: el barrio solo rellena los slots libres, igual que la lista base hoy.
- **Sí:** barrio con lista de edificios vacía = error. **No:** caer a la lista base (ocultaría un descuido). **No:** dejarlo vacío a propósito — para eso está el override a 0.
- **Sí:** override opcional de Buildings Per Block por barrio (toggle + slider); apagado sigue al global. **No:** slider siempre visible inicializado al global — los barrios dejarían de seguir cambios posteriores del global sin avisar.
- **Sí:** garantía "cada prefab al menos una vez" por pool independiente, mismo algoritmo `AssignPrefabs`. **No:** quitar la garantía en barrios.
- **Sí:** sin barrios, la misma seed produce la misma ciudad: mismo orden de recorrido de manzanas, pools asignados en orden fijo (base primero), y una manzana sin nada que construir no consume RNG. **No:** aceptar un cambio de secuencia aleatoria — rompería la reproducibilidad de ciudades existentes.
- **Sí:** nombre obligatorio y único, área ≥ 1 manzana, como errores bloqueantes (igual que título/posición de un Custom Place).
- **Sí:** manzana que deja de existir al cambiar el grid = error. **No:** poda automática ni ignorarla en silencio — el usuario no pierde ni conserva datos sin saberlo.
- **Sí:** jerarquía generada sin cambios; el barrio es un concepto de generación. **No:** agrupar por barrio (cambia rutas sin necesidad).
- **Sí:** nombre "District" en código y UI; card en la tab City. **No:** tab propia — Custom Places ya tuvo una y se retiró a la tab City.
- **Sí:** el pool de cada barrio se filtra de nulls en el builder. **No:** tocar el tratamiento de nulls de la lista base — cambiaría la secuencia aleatoria de ciudades existentes y queda fuera de alcance.
- **Sí:** el campo se llama `name` (como lo pidió el usuario: "Nombre del barrio"), aunque `CustomPlaceEntry`/`CustomPedestrianEntry` usan `title`.
- **Sí:** la sección del manual va justo tras Buildings, renumerando las secciones siguientes de la tab City.

## Riesgos identificados

| Riesgo | Mitigación |
| --- | --- |
| Reestructurar `BuildBuildings` en pools cambia sin querer el orden de consumo de RNG. | Test de regresión con seed fija (paso 3) contra la asignación previa al cambio, escrito antes de tocar el builder. |
| Un modo nuevo en `CityGeneratorGridPreview` rompe los cuatro modos existentes. | Modo y estado aislados, como en SPEC 06/12; verificación manual de Plazas, Custom Places, Custom Area y Custom Pedestrians en el paso 5. |
| El writer de defaults olvida el campo y "Set Current Selection As Default" lo borra (como pasó con `vehiclePhysics.physicMaterial`). | Writer actualizado en el paso 1 y criterio de aceptación explícito. |

## Lo que **no** entra en esta spec

- Barrios en runtime o en el minimapa.
- Jerarquía agrupada por barrio.
- Ajustes por barrio distintos de prefabs de edificio y Buildings Per Block.
- Poda automática de manzanas inexistentes.
- Cambios en el tratamiento de la lista base de Buildings.

Cada uno, si llega, va en su propia spec.

## Implementation notes

- **Test de regresión (paso 3) sin captura previa:** en vez de guardar una "asignación dorada" antes de tocar el builder, `DistrictBuildingTests.LegacyAssignment` es una copia literal del algoritmo previo (mismo consumo de RNG, sin instanciar) y el test compara slot a slot (prefab, posición, rotación) para 6 combinaciones de seed / Buildings Per Block / plazas / slots reservados, con `districts` null y vacía. Equivale al test planeado y no depende de datos congelados; la copia no debe actualizarse nunca para seguir al builder.
- **Pool base sin filtrar:** `BuildBuildings` salta una manzana sin consumir RNG si su pool tiene `Count == 0`; para la base se usa la lista tal cual (con sus nulls), así que "pool vacío" significa lista base vacía, igual que antes. Solo los pools de barrio se filtran de nulls, como dice el spec.
- **Nombre "vacío" = solo espacios también:** la validación usa `string.IsNullOrWhiteSpace`, coherente con la regla de unicidad, que ya compara con `Trim()`.
- **`SetOccupiedMask` recibe un getter** (`Func<HashSet<Vector2Int>>`) leído al pintar, no un conjunto fijo, para que las manzanas de otro barrio no queden desfasadas entre repintados; `CityGeneratorDistrictList.RefreshPreviews` repinta todas las filas en cada `RefreshDynamicUi`.
- **Orden `SetShapeMask` → `SetGrid`:** el preview solo pasa al lienzo `MaxGridSize` dentro de `SetGrid`, así que la lista de barrios vuelve a llamar a `SetGrid` tras cambiar la máscara (si no, en Custom Grid el picker se quedaba en el tamaño rectangular hasta el siguiente refresco). El picker de Custom Places tiene el mismo problema de orden (preexistente, fuera de alcance; ver observaciones del cierre).
- **`ToggleDistrictCell` duplica `TogglePlazaCell`** en vez de compartir un helper, para no tocar el código de los modos existentes (riesgo del spec).
- **Estimación de edificios:** se ajusta con la diferencia entre el valor efectivo de cada barrio y el global, contando cada manzana real no plaza una sola vez y para el primer barrio que la reclama (igual que el builder). Como antes, no descuenta Custom Places.
- **Estilos:** las filas de `CityGeneratorDistrictList` reutilizan las clases USS `cg-custom-place-list__*` (como ya hace `CityGeneratorCustomPedestrianList`); no se añadió USS nuevo. Icono de la card: `d_Folder Icon`.
- **`AppendDistrictsList` conserva la entrada aunque ningún prefab resuelva** (a diferencia de Custom Places/Pedestrians, que la descartan): el nombre y las manzanas son datos del usuario y el validador ya marca el barrio sin prefabs. `DistrictEntry.buildingsPerBlock` comparte nombre con `GeneralSettings.buildingsPerBlock`; `ReplaceScalarDefaults` no lo toca porque su regex exige inicializador `=` y el campo del struct no lo tiene (verificado).
- **Documentación extra:** además de lo listado, se citan los barrios en la fila de Buildings Per Block, en el texto de Buildings y en la línea de resumen de ambos manuales, y `DistrictBuildingTests` en `docs/architecture/tests-scene-and-release.md`.
- **Suites:** EditMode 167/167 (incluidos los 12 de `DistrictBuildingTests` y los 14 casos nuevos de `CityGeneratorValidatorTests`); PlayMode + Performance 118/119. El único fallo, `CarAgentPhysicsRecoveryTests.Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt`, es preexistente: falla igual con los cambios de esta spec apartados (código de `main`), donde además `AfterSettling_RecoveringExits_AndReattachesToAValidNode` falló de forma intermitente. Es un test de física runtime de `CarAgent` que esta spec no toca (sin cambios en `Runtime/`); queda fuera de alcance.
