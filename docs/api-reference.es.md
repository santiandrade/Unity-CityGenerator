🇬🇧 [Read in English](api-reference.md)

# Referencia de la API Runtime

`CityGeneratorAPI` (namespace `CityGenerator.Runtime`) es el punto de entrada de resolución para
los datos de una ciudad generada en tiempo de ejecución — tanto en el Play Mode del Editor como en
un build final. Resuelve un handle explícito, `CityGeneratorCity`, contra un registro que mantiene
el componente `CityGeneratorInfo` de cada ciudad generada, registrándose a sí mismo mientras está
activo. Sin setup, sin referencia a ningún assembly más allá del propio paquete.

```csharp
using CityGenerator.Runtime;

CityGeneratorCity? city = CityGeneratorAPI.Default;
if (city.HasValue)
{
    Vector2Int size = city.Value.City.GridSize;
    int vehicles = city.Value.Traffic.VehicleCount;
}

// o encadenando a través de Nullable<T> sin necesidad de `.Value`:
int buildingCount = CityGeneratorAPI.Default?.City.BuildingCount ?? 0;
```

## Cómo funciona

- `CityGeneratorInfo` — añadido a la raíz de la ciudad por
  `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler` en cada Build y Re-Build — se
  registra en `CityGeneratorAPI` en su propio `OnEnable` y se da de baja en `OnDisable`, el mismo
  patrón de ciclo de vida que `TrafficManager`/`PedestrianManager` usan para sus agentes. No hay
  ninguna caché ni ninguna búsqueda global (`FindFirstObjectByType`/`FindAnyObjectByType`/
  `FindObjectsByType`) en `CityGeneratorAPI`: una descarga de escena o un domain reload limpian las
  entradas obsoletas por construcción, porque el componente que las reportaría ya no existe.
- `CityGeneratorAPI.Default` resuelve la única ciudad registrada, o `null` cuando hay cero o más de
  una. Con más de una es genuinamente ambiguo qué ciudad quiere el llamante, así que devuelve
  `null` y escribe **un warning por sesión** (no por llamada) en vez de adivinar — usa `All`,
  `InScene` o `For` cuando pueda haber más de una ciudad registrada a la vez.
- `CityGeneratorAPI.All` lista todas las ciudades registradas (activas) actualmente, en orden de
  registro. `CityGeneratorAPI.InScene(scene)` resuelve la ciudad registrada en una `Scene`
  concreta. `CityGeneratorAPI.For(info)` resuelve el handle de un `CityGeneratorInfo` del que ya
  tienes referencia — la única de las cuatro vías que también resuelve una ciudad
  **desactivada**, ya que una raíz desactivada está (deliberadamente) ausente del registro y por
  tanto de `All`/`Default`/`InScene`.
- `CityGeneratorCity` es un handle inmutable — un `readonly struct` que envuelve la referencia a
  `CityGeneratorInfo`, no una copia de sus datos — así que nunca puede quedar desincronizado de su
  ciudad mientras `IsValid` sea `true`. `IsValid` es `false` en cuanto el `CityGeneratorInfo`
  subyacente ha sido destruido. `IsActive` es `false` también cuando la raíz de la ciudad está
  actualmente desactivada. Todo getter de módulo devuelve un valor por defecto seguro (`0`,
  `false`, `Vector2Int.zero`, `Vector3.zero`, o `null`) en cuanto `IsValid` es `false`, y todo
  setter es un no-op silencioso — ninguno lanza. La única operación que sí lanza sobre un handle
  inválido es `.Value` sobre un `CityGeneratorCity?` que es `null` — usa `?.` con `??`, no
  `.Value`, cuando no hayas comprobado antes `HasValue`.
- La API es de tipo pull: lee una propiedad cuando necesites el valor. No hay sistema de
  eventos/callbacks (ni `OnCityRegistered` ni `OnHourChanged`) — consulta desde tu propio `Update`,
  corrutina o refresco de UI según lo necesites.
- Casi todo es de solo lectura, expuesto como propiedad (`city.City.BuildingCount`). Las pocas
  excepciones son mutaciones sobre comportamiento que la propia tool ya ejecuta de forma segura en
  runtime (el preview del Day/Night Cycle, el Minimap HUD) — siempre un **método**
  (`city.City.SetHour(12f)`), nunca una propiedad con setter: los módulos son `readonly struct`s
  devueltos por valor, así que `city.Minimap.ViewRadiusMeters = 120f` no compilaría (CS1612). Nada
  en esta API genera, destruye ni redimensiona nada: los conteos de vehículos/peatones, el layout
  de edificios y la propia cuadrícula quedan fijados en el momento de la generación.
- **Consultar demasiado pronto puede no encontrar una ciudad que sí existe.** El registro ocurre en
  el propio `OnEnable` de `CityGeneratorInfo`, y Unity no garantiza ningún orden entre los
  `OnEnable` de objetos distintos. Un script que llame a `CityGeneratorAPI.Default` desde su propio
  `Awake`/`OnEnable` puede recibir `null` aunque la ciudad esté a punto de existir un instante
  después. Consultar desde `Start` en adelante es seguro; la API estática de v2.10 disimulaba esto
  porque resolvía perezosamente con `FindFirstObjectByType`, que encuentra un objeto exista o no
  haya corrido aún su propio `OnEnable`.
- **Varias ciudades son resolubles, pero la tool todavía no soporta varias ciudades coexistiendo en
  posiciones físicas distintas.** `All`/`InScene`/`For` existen porque nada impide que dos
  componentes `CityGeneratorInfo` estén cargados en memoria a la vez (p. ej. dos escenas cargadas
  aditivamente durante una transición, o una segunda ciudad deliberadamente mantenida desactivada
  en un pool), y esta API resuelve cada una correctamente. Pero la generación en sí sigue
  produciendo coordenadas de mundo absolutas — `TrafficNetwork.IntersectionPosition` construye su
  `Vector3` directamente desde los ejes de la cuadrícula, sin `TransformPoint` — así que mover un
  `CityGeneratorRoot` mueve su geometría pero no su grafo de tráfico/peatones. No construyas sobre
  la suposición de que dos ciudades registradas pueden reubicarse en distintas partes del mismo
  mundo; eso es trabajo futuro, no algo que esta API ya entregue.

## `CityGeneratorCity.City`

Forma de la cuadrícula, conteos de contenido generado, semilla, y el Day/Night Cycle (integrado
aquí en vez de en un módulo propio, porque conceptualmente pertenece a "la ciudad actual", no a un
sistema aparte).

| Miembro | Devuelve / hace |
| --- | --- |
| `IsCustomGrid` | `true` si esta ciudad usó Custom Grid en vez de una cuadrícula rectangular. |
| `GridSize` | Cuadrícula rectangular: `(gridWidth, gridHeight)`. Custom Grid: bounding box de las celdas reales. |
| `BlockCount` | Número de bloques reales (plazas incluidas). |
| `BuildingCount` | Número de instancias de edificio generadas. |
| `PlazaCount` | Número de bloques que son plazas. |
| `CustomPlaceCount` | Número de instancias de Custom Place generadas. |
| `LampCount` | Número de farolas generadas. |
| `BinCount` | Número de papeleras generadas. |
| `StreetTreeCount` | Número de árboles de calle generados. |
| `TrafficLightCount` | Número de semáforos generados (se generan en cada intersección de 4 vías, independientemente de si el tráfico está habilitado). |
| `IsSeeded` | `true` si esta ciudad se generó con Custom Seed. |
| `Seed` | La semilla usada, o `0` cuando `IsSeeded` es `false`. |
| `IsDayNightEnabled` | `true` si el Day/Night Cycle está habilitado y avanzando activamente. |
| `SetDayNightEnabled(bool enabled)` | Activa/desactiva el `Update` del ciclo (`DayNightCycle.enabled`) — desactivarlo congela la luz en su hora actual. |
| `CurrentHour` | Hora simulada actual (0-24). |
| `SetHour(float hour)` | Reposiciona la luz instantáneamente a `hour`, igual que el preview del propio Editor. |

## `CityGeneratorCity.Player`

Solo lectura. `IsEnabled` es `false` (y el resto de getters devuelven su valor por defecto)
siempre que el Player estuviera deshabilitado en el momento de la generación.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled` | `true` si el Player está habilitado y su instancia existe. |
| `Position` | La posición mundial actual del Player, o `Vector3.zero` si está deshabilitado. |
| `IsFreeViewActive` | `true` si Free View está activo actualmente. No hay setter — Free View se alterna solo mediante su propia acción de input (la tecla V por defecto), de forma deliberada. |

## `CityGeneratorCity.Traffic`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled` | `true` si el tráfico (vehículos) está habilitado. |
| `VehicleCount` | El número **vivo** de `CarAgent`s registrados — no un conteo congelado en el momento de la generación. Un coche puede autodesactivarse (p. ej. un dead-end de Custom Grid), así que puede ser menor que el Vehicle Count configurado. |

## `CityGeneratorCity.Pedestrians`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled` | `true` si los peatones (generales) están habilitados. |
| `Count` | El número **vivo** de `PedestrianAgent`s registrados (peatones generales y Custom Pedestrians combinados) — no un conteo congelado en el momento de la generación. |
| `CustomCount` | El número de entradas de Custom Pedestrian configuradas (un presupuesto independiente de `IsEnabled`), no un conteo de agentes vivos. |

## `CityGeneratorCity.Minimap`

| Miembro | Devuelve / hace |
| --- | --- |
| `IsEnabled` | `true` si el Minimap HUD está habilitado. |
| `PointOfInterestCount` | Número de Custom Places marcados como Point of Interest. |
| `ViewRadiusMeters` | Radio de vista actual, en metros. |
| `SetViewRadiusMeters(float meters)` | Cambia el radio de vista; tiene efecto en el siguiente frame. |
| `IsVisible` | `true` si el GameObject del Canvas del HUD está activo actualmente. |
| `SetVisible(bool visible)` | Muestra/oculta el HUD alternando su GameObject del Canvas. |

## `CityGeneratorCity.Audio`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsAmbienceEnabled` | `true` si el audio de ambiente 2D está habilitado. |
| `AmbienceClipCount` | Número de AudioSources de ambiente activas. |
| `IsPlazaAudioEnabled` | `true` si el audio posicional de plaza está habilitado. |
| `PlazaAudioSourceCount` | Número de AudioSources de plaza activas (una por plaza, por cada clip configurado). |

## Resolver una ciudad: `CityGeneratorAPI`

| Miembro | Devuelve / hace |
| --- | --- |
| `Default` | `CityGeneratorCity?` — la única ciudad registrada, o `null` con cero o más de una (escribe un warning por sesión cuando es ambiguo). |
| `All` | `IReadOnlyList<CityGeneratorCity>` — todas las ciudades registradas (activas), en orden de registro. Una vista en vivo, no una copia. |
| `Count` | Número de ciudades registradas. |
| `InScene(Scene scene)` | `CityGeneratorCity?` — la ciudad registrada en esa escena, o `null`. |
| `For(CityGeneratorInfo info)` | `CityGeneratorCity?` — resuelve incluso una ciudad desactivada; `null` solo si `info` es `null` o está destruido. |

## Cosas que esta API deliberadamente no hace

- **Ninguna mutación que genere, destruya o redimensione contenido** — vehículos, peatones,
  edificios, la propia cuadrícula. La generación sigue siendo exclusivamente un flujo del Editor.
- **Sin `SetFreeViewActive`** — Free View se mantiene como un toggle de input puro (la tecla V).
- **Sin setters en Traffic/Pedestrians/Audio/Player** — esos módulos son de solo lectura.
- **Sin eventos/callbacks.** Consulta por polling en su lugar.
- **Sin soporte para varias ciudades coexistiendo en posiciones físicas distintas** — ver la nota
  de arriba; resolver varias ciudades registradas sí está soportado, reubicarlas no.

Un setter llamado sobre `Minimap` (o los setters de Day/Night de `City`) actúa directamente sobre
la instancia viva de la escena; si el Editor hace después un Re-Build (solo posible en el Editor,
nunca en un build), esos cambios en runtime se pierden junto con todo lo demás que tenía la ciudad
anterior.

## Migrando desde v2.10

v2.10 entregó una API estática, cacheada, de ciudad única. Se **elimina** en esta release (breaking
change) en favor del handle explícito de arriba, porque la caché estática nunca se invalidaba — el
resultado de `FindFirstObjectByType` se resolvía una vez y se reutilizaba para siempre, lo que
contestaba en silencio sobre una ciudad destruida o equivocada en cuanto existía más de una en
memoria.

| v2.10 | Esta release |
| --- | --- |
| `CityGeneratorAPI.IsCityAvailable` | `CityGeneratorAPI.Default.HasValue` (o `CityGeneratorAPI.Default is { } city` y usar `city` directamente) |
| `CityGeneratorAPI.City.GetGridSize()` | `CityGeneratorAPI.Default?.City.GridSize` |
| `CityGeneratorAPI.City.GetBuildingCount()` | `CityGeneratorAPI.Default?.City.BuildingCount ?? 0` |
| `CityGeneratorAPI.City.SetHour(12f)` | `CityGeneratorAPI.Default?.City.SetHour(12f)` |
| `CityGeneratorAPI.Traffic.GetVehicleCount()` | `CityGeneratorAPI.Default?.Traffic.VehicleCount ?? 0` |
| `CityGeneratorAPI.Minimap.SetViewRadiusMeters(120f)` | `CityGeneratorAPI.Default?.Minimap.SetViewRadiusMeters(120f)` |
| cualquier otro getter/setter estático | mismo nombre, propiedad `PascalCase` en vez de método `Get*()`/`Is*()`, alcanzado a través de un `CityGeneratorCity` resuelto |

Todos los getters/setters de arriba mantienen exactamente la misma superficie de datos y mutación
que v2.10 — esta release cambia cómo se **resuelve** la ciudad, no qué se puede leer o escribir.
Dos trampas de migración que merece la pena señalar explícitamente:

- **Prefiere `?.` con `??` en vez de `.Value`.** `CityGeneratorAPI.Default` es
  `CityGeneratorCity?`; una migración mecánica a
  `CityGeneratorAPI.Default.Value.City.BuildingCount` lanza donde v2.10 habría devuelto `0` en
  silencio. El patrón `?.`/`??` de arriba conserva la garantía de "nunca lanza" de antes.
- **Mueve cualquier consulta fuera de `Awake`/`OnEnable` a `Start` (o más tarde).** Ver "Consultar
  demasiado pronto" arriba — es el único cambio de comportamiento capaz de romper código que
  funcionaba con la caché perezosa de `FindFirstObjectByType` anterior.
