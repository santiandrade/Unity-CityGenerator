# SPEC 23 — Districts en runtime (consulta del barrio por posición)

> **Estado:** Implementado
> **Depende de:** SPEC 14/15 (Runtime API por ciudad), SPEC 11 (Custom Grid), SPEC 16 (varias ciudades), SPEC 21 (Districts)
> **Fecha:** 2026-11-25
> **Objetivo:** Que una ciudad generada sepa en runtime, también en una build, qué *District* hay en cualquier punto del mundo, para que un juego pueda mostrar en qué barrio está el jugador.

---

## Contexto y problema

La SPEC 21 añadió los *Districts*, pero solo para generar: `DistrictEntry` y `CityGeneratorSettings.districts` están en el ensamblado **Editor**. Solo se usan en `CityGeneratorBuildingBuilder` para elegir los edificios de cada manzana, y no queda nada de ellos en la escena generada. Su propia sección *Fuera de alcance* ya lo dejaba para una spec futura: «Exponer los barrios en runtime (dato serializado en la escena, API de consulta por manzana)».

Hoy un juego no tiene forma de saber en qué barrio está un punto:

- `CityGeneratorInfo` solo guarda `gridSize` (en Custom Grid, la caja que envuelve las celdas, no el lienzo donde se colocan los centros de manzana), `blockCount` y contadores.
- La geometría de la cuadrícula (`CellPitch`, `MaxGridSize`) está en `CityGeneratorConstants`, también solo en Editor.
- `CityGeneratorAPI` / `CityGeneratorCity.City` no tiene ninguna consulta por posición.

El primer consumidor es City Races (su SPEC 59): un rótulo sobre el minimapa con el barrio donde está el coche.

---

## Scope

**Dentro:**

- **Datos guardados en la ciudad** (en `CityGeneratorInfo`, que ya es la fuente única de verdad en runtime de cada ciudad):
  - El nombre (recortado) y las celdas de cada District, en el orden de la lista de ajustes.
  - Lo necesario para pasar de una posición a una celda: la separación entre centros de manzana (`cellPitch`) y el tamaño del lienzo de colocación (`layoutSize`): `(gridWidth, gridHeight)` en cuadrícula rectangular y `MaxGridSize × MaxGridSize` en Custom Grid, lo mismo que recibe `CityGeneratorGrid.GetBlockCenter`.
  - `CityGeneratorContentAssembler` los escribe en cada Build y Re-Build, junto a `gridSize`/`blockCount`.
- **Consulta en runtime** (ver *Modelo de datos*):
  - `CityGeneratorInfo` pasa una posición del mundo a celda con la transformada de su raíz (`InverseTransformPoint`, como los grafos desde la SPEC 16, así que una ciudad desplazada funciona).
  - Busca la celda en un diccionario celda → índice de District, construido la primera vez que se consulta (sin serializar) y reconstruido en `OnValidate`.
  - O(1) y sin asignaciones de memoria por consulta, para poder llamarla en cada frame.
- **API pública** en `CityGeneratorCity.City` (`CityModule`): `DistrictCount`, `GetDistrictName(int)` y `TryGetDistrictAt(Vector3, out int)`.
- **Qué celda corresponde a cada punto:** la de la manzana cuyo centro está más cerca en cada eje (redondeo por eje). La frontera entre dos celdas pasa por el eje de la calle que las separa, y un cruce se reparte entre sus cuatro manzanas. Fuera del lienzo no hay celda.
- **Sin District:** fuera del lienzo, en una manzana que no está en ningún District, en una plaza (la SPEC 21 prohíbe plazas en un District) y en una celda vacía de una Custom Grid.
- **Tests:**
  - EditMode: el paso de posición a celda en las dos modalidades de cuadrícula, con tamaños pares e impares (los centros caen en múltiplos o en medios de `CellPitch`), los bordes del lienzo, el eje de una calle y una raíz desplazada. Debe coincidir con `CityGeneratorGrid.GetBlockCenter` en todos los centros de manzana.
  - EditMode: el ensamblador guarda los Districts tal como están en los ajustes (orden, nombres recortados, celdas).
  - PlayMode (`CityGeneratorAPITests`): `TryGetDistrictAt` en el centro de cada manzana de un District, en una manzana libre, en una plaza y fuera de la ciudad; `DistrictCount = 0` en una ciudad sin Districts.
- **Documentación:**
  - `docs/user-manual.md`/`.es.md`: en *Districts*, que sus nombres y manzanas quedan guardados en la ciudad y se pueden consultar en runtime.
  - README/README.es: la consulta en la sección de la API.
  - `docs/architecture/editor-tool.md` y la doc de runtime: los campos nuevos de `CityGeneratorInfo` y su lookup.
  - Fila de la SPEC 23 en la tabla de `CLAUDE.md` y entrada en `CHANGELOG.md` (`## [Unreleased]`).
- **Publicación:** versión menor nueva (3.10.0), porque añade API sin romper nada.

**Fuera de alcance:**

- Tinte, borde o rótulo de barrio en el `MinimapHUD` del paquete o en su instantánea.
- Eventos de «cambio de District» o seguimiento de un `Transform`: el juego consulta cuando quiere.
- Que una plaza herede el District de sus vecinas, o cualquier regla de cercanía. La API dice exactamente lo que hay en la cuadrícula y cada juego decide cómo presentarlo.
- Consultas de manzana genéricas (`TryGetCellAt`, tipo de celda, plazas): no tienen consumidor todavía.
- Colores de District en runtime.
- Rotación o escala de la raíz: siguen sin soportarse, como desde la SPEC 16.

---

## Modelo de datos

```csharp
// Runtime/CityDistrict.cs (new)
[Serializable]
public struct CityDistrict
{
    [Tooltip("District name as set in the generator (trimmed).")]
    public string name;
    [Tooltip("Blocks (gridX, gridY) of this district, same convention as the generator's settings.")]
    public List<Vector2Int> cells;
}

// Runtime/CityGeneratorInfo.cs gains:
[Header("Districts")]
[Tooltip("Distance in metres between the centres of two adjacent blocks (CityGeneratorConstants.CellPitch at build time).")]
public float cellPitch;
[Tooltip("Block positions per axis that block centres are laid out on: (gridWidth, gridHeight), or MaxGridSize x MaxGridSize with Custom Grid.")]
public Vector2Int layoutSize;
[Tooltip("Districts of this city in the generator's order; empty when it has none or was built before SPEC 23.")]
public List<CityDistrict> districts = new();

// Runtime lookup on CityGeneratorInfo (internal, used by CityModule):
internal bool TryGetCell(Vector3 worldPosition, out Vector2Int cell);    // false outside layoutSize
internal bool TryGetDistrictIndex(Vector2Int cell, out int districtIndex);

// Runtime/API/CityGeneratorCity.cs — CityModule gains:
public int DistrictCount { get; }                                        // 0 without info or districts
public string GetDistrictName(int districtIndex);                        // null when out of range
public bool TryGetDistrictAt(Vector3 worldPosition, out int districtIndex); // -1 and false when none
```

Notas:

- **Paso de posición a celda:** con `local = transform.InverseTransformPoint(worldPosition)`, `gx = RoundToInt(local.x / cellPitch + (layoutSize.x - 1) / 2f)` y lo mismo con `local.z` y `layoutSize.y` para `gy`. Es la inversa exacta de `CityGeneratorGrid.GetBlockAxisPosition`. La celda es válida si `0 ≤ gx < layoutSize.x` y `0 ≤ gy < layoutSize.y`.
- **Por qué se guarda `cellPitch`** en vez de mover `CityGeneratorConstants` a Runtime: el invariante dice que los números de layout viven en `CityGeneratorConstants`. Guardar el valor con el que se construyó la ciudad mantiene una sola fuente y hace que cada ciudad sea coherente consigo misma aunque la constante cambie algún día.
- **Por qué se devuelve un índice y no el nombre:** un juego puede comparar el resultado de cada frame sin comparar cadenas, y el índice sirve para futuros datos por District (color, icono). El índice es estable entre dos generaciones mientras no cambie la lista.
- Una celda en dos Districts es un error de validación (SPEC 21). Si una ciudad generada a mano lo tuviera, el lookup se queda con el primero, igual que hace el builder.

---

## Plan de implementación

1. `CityDistrict` y los campos nuevos de `CityGeneratorInfo`. `CityGeneratorContentAssembler` los rellena (copia de `settings.districts` con nombres recortados y listas no nulas).
2. Lookup en `CityGeneratorInfo` (diccionario perezoso, invalidado en `OnValidate`) y la API de `CityModule`.
3. Tests EditMode y PlayMode.
4. Documentación, `CHANGELOG`, regenerar la escena demo para que traiga los datos, release 3.10.0 con tag.

---

## Criterios de aceptación

- Tras *Build* o *Re-Build*, el inspector de `CityGeneratorInfo` muestra los Districts con sus nombres y celdas, `cellPitch = 56` y el `layoutSize` correcto (15 × 15 en Custom Grid).
- En el centro de cada manzana de un District, `TryGetDistrictAt` devuelve su índice, y `GetDistrictName` el nombre tal como está en los ajustes, recortado.
- A ±1 m del eje de una calle entre dos Districts devuelve uno a cada lado.
- En una manzana libre, en una plaza, en una celda vacía de Custom Grid y fuera de la ciudad devuelve `false` e índice -1.
- Con la raíz de la ciudad desplazada (SPEC 16), los resultados son los mismos en coordenadas relativas a la raíz.
- Una ciudad generada antes de esta spec, sin regenerar: `DistrictCount = 0`, `TryGetDistrictAt` siempre `false`, sin errores ni avisos.
- Una ciudad sin Districts se genera igual que antes, bit a bit, salvo por los campos nuevos de `CityGeneratorInfo`.
- `TryGetDistrictAt` no asigna memoria después de la primera llamada (comprobado con el Profiler o con un test de asignaciones).
- La suite completa de EditMode y PlayMode pasa.

---

## Decisiones tomadas y descartadas

- **Tomada: la cuadrícula es la única fuente de verdad.** Los Districts se definen por manzanas, así que la consulta es aritmética sobre la cuadrícula más un diccionario.
- **Descartada: triggers o colliders por District.** Costarían física, necesitarían una capa (el paquete no debe tocar las capas ni la matriz de colisiones del usuario) y obligarían a un `Rigidbody` en lo que se quiere seguir.
- **Descartada: polígonos o contornos por District.** Darían bordes más «bonitos», pero sobran mientras un District sea un conjunto de manzanas.
- **Descartada: guardar una tabla plana `int[layoutSize.x * layoutSize.y]`.** Es tan rápida como el diccionario, pero en el inspector se lee peor y repite los datos de los ajustes con otra forma. Se guarda la lista tal cual y el índice se construye en memoria.
- **Descartada: que las plazas hereden el District de sus vecinas.** Va contra la regla de la SPEC 21 (una plaza no es de ningún District) y es una decisión de presentación de cada juego.
- **Descartada: un evento `DistrictChanged`.** Necesitaría que el paquete supiera qué `Transform` seguir. Consultar es barato y el juego decide cada cuánto y con qué margen.

---

## Riesgos identificados

- **Ciudades sin regenerar:** el juego no verá ningún District hasta hacer *Re-Build*. Es silencioso a propósito, y se documenta en el `CHANGELOG`.
- **Cambiar los ajustes sin regenerar** deja los datos guardados desfasados respecto a la ventana. Es lo mismo que ya pasa con el resto de la ciudad: la escena manda.
- **Raíz rotada o escalada:** `InverseTransformPoint` daría una respuesta coherente con la transformada, pero el resto de la ciudad no lo soporta. No se cambia nada.

---

## Lo que **no** entra en esta spec

- Cualquier cambio visual en el `MinimapHUD` del paquete.
- Datos por District más allá de nombre y celdas.
- Cambios en la validación o en la UI de la card *Districts*.

---

## Implementation notes

- **`Runtime/AssemblyInfo.cs` (nuevo)** con `InternalsVisibleTo` para `CityGenerator.Editor` y los ensamblados de tests de EditMode y PlayMode. El modelo de datos declara `TryGetCell`/`TryGetDistrictIndex` como `internal`, y el ensamblado Runtime no exponía nada a nadie: sin esto, ni los tests podían llamarlos ni el ensamblador podía invalidar el índice.
- **`CityGeneratorInfo.InvalidateDistrictLookup()` (internal)**, además de `OnValidate`. El ensamblador escribe los campos desde código, y eso no dispara `OnValidate`; si el mismo componente ya hubiera respondido una consulta antes, el índice quedaría desfasado. `OnValidate` lo llama también.
- **La escritura del ensamblador está en `CityGeneratorContentAssembler.WriteDistricts(info, settings)`** (internal static), llamada justo después de `gridSize`/`blockCount`. No consume números aleatorios, así que una ciudad con la misma seed sale igual.
- **`TryGetDistrictAt` devuelve `false` sin calcular la celda cuando `DistrictCount == 0`**: así una ciudad sin Districts, o generada antes de esta spec, ni siquiera hace la transformación.
- **API en README:** el README no tiene una sección de API propia, solo un enlace a `docs/api-reference.md`/`.es.md`. La consulta se documenta completa allí (filas en la tabla de `CityGeneratorCity.City` y una subsección *Which district is here* con un ejemplo), y la línea del README que enlaza la referencia la menciona. También se añadió `DistrictInfoTests` a `docs/architecture/tests-scene-and-release.md`.
- **Se corrigió `docs/architecture/editor-tool.md`**, que decía que los Districts eran «purely a generation concept».
- **Tests EditMode en un fichero nuevo**, `Assets/Tests/EditMode/Generation/DistrictInfoTests.cs`. El EditMode también recorre la API pública sobre ciudades generadas con el ensamblador real: plaza, manzana libre, celda vacía de Custom Grid, fuera de la ciudad y ±1 m del eje de una calle entre dos Districts. El test de asignaciones está en PlayMode (`CityGeneratorAPITests.TryGetDistrictAt_DoesNotAllocate_AfterTheFirstCall`) y usa `Is.Not.AllocatingGCMemory()` del Test Framework.
- **La release 3.10.0 no va en esta rama.** Como las anteriores (`Release v3.9.0`, `Release v3.8.0`), se hace en `main` tras el merge, en un commit aparte con `Tools > City Generator > Release`. El tag lo crea siempre una persona (`docs/architecture/tests-scene-and-release.md`), y el push queda fuera de lo que esta skill puede hacer sin preguntar. La entrada del CHANGELOG queda en `## [Unreleased]`.
- **La escena demo (`Assets/Scenes/City.unity`) no se ha regenerado en esta rama.** Unity se cerró por un fallo del driver gráfico (D3D12) mientras se ejecutaban los tests, y el usuario decidió cerrar la spec sin esperar. Hay que regenerarla con *Re-Build* para que su `CityGeneratorInfo` traiga `cellPitch`/`layoutSize` (la demo no tiene Districts por defecto). Hasta entonces es justo el caso «ciudad generada antes de esta spec» de los criterios: `DistrictCount = 0`, sin errores.
- **Resultado de la suite:** EditMode, 189/189. PlayMode, 122/123, más los 4 tests nuevos de SPEC 23, que pasan. El que falla es de `CarAgentPhysicsRecoveryTests`, que no usa nada de lo que cambia esta spec. Al repetir la clase fallaron dos tests distintos (`AfterSettling_RecoveringExits_AndReattachesToAValidNode` y `Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt`), con el Editor en segundo plano (`Application.isFocused = false`) y unos tests que dependen de `Time.time`: parece inestabilidad de temporización. Falta confirmarlo sin los cambios de SPEC 23; Unity se cerró antes de terminar esa comprobación.
