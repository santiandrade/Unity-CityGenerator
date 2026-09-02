🇬🇧 [Read in English](api-reference.md)

# Referencia de la API Runtime

`CityGeneratorAPI` (namespace `CityGenerator.Runtime`) es un punto de entrada estático, orientado
a lectura, para consultar los datos de una ciudad generada en tiempo de ejecución — tanto en el
Play Mode del Editor como en un build final. Funciona sobre lo que la tool haya generado más
recientemente: sin setup, sin instancia que obtener, sin referencia a ningún assembly más allá del
propio paquete.

```csharp
using CityGenerator.Runtime;

if (CityGeneratorAPI.IsCityAvailable)
{
    Vector2Int size = CityGeneratorAPI.City.GetGridSize();
    int vehicles = CityGeneratorAPI.Traffic.GetVehicleCount();
}
```

## Cómo funciona

- `CityGeneratorAPI.IsCityAvailable` es `true` en cuanto se encuentra en la(s) escena(s) cargada(s)
  un componente `CityGeneratorInfo` — añadido a la raíz de la ciudad por
  `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler` en cada Build y Re-Build. Se resuelve
  de forma perezosa en el primer uso y luego **queda cacheado durante el resto de la sesión**: la
  API asume una ciudad generada por sesión de Play, la misma asunción que hace el resto de la tool.
  Regenerar la ciudad en runtime (algo que la propia tool no hace — la generación es un flujo
  exclusivo del Editor) dejaría la referencia cacheada apuntando a un objeto destruido.
- Los propios campos de `CityGeneratorInfo` son los que alimentan los getters de `CityGeneratorAPI`,
  pero su Inspector es de solo lectura (un editor personalizado lo muestra deshabilitado) — es una
  instantánea rellenada una vez en cada Build/Re-Build, no un control en vivo, así que editarla a
  mano no tiene ningún efecto sobre la ciudad en marcha. Usa `CityGeneratorAPI` en su lugar.
- Todo getter, en todos los módulos, devuelve un valor por defecto seguro — `0`, `false`,
  `Vector2Int.zero`, `Vector3.zero`, o `null` en los pocos casos que devuelven un objeto — cuando
  `IsCityAvailable` es `false`. Ninguno lanza una excepción. No hace falta proteger cada llamada con
  una comprobación previa de `IsCityAvailable`, aunque hacerlo una vez por adelantado (como arriba)
  evita trabajo redundante.
- La API es de tipo pull: llama a un getter cuando necesites el valor. No hay sistema de
  eventos/callbacks (ni `OnCityReady` ni `OnHourChanged`) — consulta desde tu propio `Update`,
  corrutina o refresco de UI según lo necesites.
- Casi todo es de solo lectura. Las pocas excepciones son setters sobre comportamiento que la
  propia tool ya ejecuta de forma segura en runtime (el preview del Day/Night Cycle, el Minimap
  HUD) — ver más abajo. Nada en esta API genera, destruye ni redimensiona nada: los conteos de
  vehículos/peatones, el layout de edificios y la propia cuadrícula quedan fijados en el momento de
  la generación.

## `CityGeneratorAPI.City`

Forma de la cuadrícula, conteos de contenido generado, semilla, y el Day/Night Cycle (integrado
aquí en vez de en un módulo propio, porque conceptualmente pertenece a "la ciudad actual", no a un
sistema aparte).

| Miembro | Devuelve / hace |
| --- | --- |
| `IsCustomGrid()` | `true` si esta ciudad usó Custom Grid en vez de una cuadrícula rectangular. |
| `GetGridSize()` | Cuadrícula rectangular: `(gridWidth, gridHeight)`. Custom Grid: bounding box de las celdas reales. |
| `GetBlockCount()` | Número de bloques reales (plazas incluidas). |
| `GetBuildingCount()` | Número de instancias de edificio generadas. |
| `GetPlazaCount()` | Número de bloques que son plazas. |
| `GetCustomPlaceCount()` | Número de instancias de Custom Place generadas. |
| `GetLampCount()` | Número de farolas generadas. |
| `GetBinCount()` | Número de papeleras generadas. |
| `GetStreetTreeCount()` | Número de árboles de calle generados. |
| `GetTrafficLightCount()` | Número de semáforos generados (se generan en cada intersección de 4 vías, independientemente de si el tráfico está habilitado). |
| `IsSeeded()` | `true` si esta ciudad se generó con Custom Seed. |
| `GetSeed()` | La semilla usada, o `0` cuando `IsSeeded()` es `false`. |
| `IsDayNightEnabled()` | `true` si el Day/Night Cycle está habilitado y avanzando activamente. |
| `SetDayNightEnabled(bool enabled)` | Activa/desactiva el `Update` del ciclo (`DayNightCycle.enabled`) — desactivarlo congela la luz en su hora actual. |
| `GetCurrentHour()` | Hora simulada actual (0-24). |
| `SetHour(float hour)` | Reposiciona la luz instantáneamente a `hour`, igual que el preview del propio Editor. |

## `CityGeneratorAPI.Player`

Solo lectura. `IsEnabled()` es `false` (y el resto de getters devuelven su valor por defecto)
siempre que el Player estuviera deshabilitado en el momento de la generación.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled()` | `true` si el Player está habilitado y su instancia existe. |
| `GetPosition()` | La posición mundial actual del Player, o `Vector3.zero` si está deshabilitado. |
| `IsFreeViewActive()` | `true` si Free View está activo actualmente. No hay setter — Free View se alterna solo mediante su propia acción de input (la tecla V por defecto), de forma deliberada. |

## `CityGeneratorAPI.Traffic`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled()` | `true` si el tráfico (vehículos) está habilitado. |
| `GetVehicleCount()` | El número **vivo** de `CarAgent`s registrados — no un conteo congelado en el momento de la generación. Un coche puede autodesactivarse (p. ej. un dead-end de Custom Grid), así que puede ser menor que el Vehicle Count configurado. |

## `CityGeneratorAPI.Pedestrians`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsEnabled()` | `true` si los peatones (generales) están habilitados. |
| `GetPedestrianCount()` | El número **vivo** de `PedestrianAgent`s registrados (peatones generales y Custom Pedestrians combinados) — no un conteo congelado en el momento de la generación. |
| `GetCustomPedestrianCount()` | El número de entradas de Custom Pedestrian configuradas (un presupuesto independiente de `IsEnabled()`), no un conteo de agentes vivos. |

## `CityGeneratorAPI.Minimap`

| Miembro | Devuelve / hace |
| --- | --- |
| `IsEnabled()` | `true` si el Minimap HUD está habilitado. |
| `GetPointOfInterestCount()` | Número de Custom Places marcados como Point of Interest. |
| `GetViewRadiusMeters()` | Radio de vista actual, en metros. |
| `SetViewRadiusMeters(float meters)` | Cambia el radio de vista; tiene efecto en el siguiente frame. |
| `IsVisible()` | `true` si el GameObject del Canvas del HUD está activo actualmente. |
| `SetVisible(bool visible)` | Muestra/oculta el HUD alternando su GameObject del Canvas. |

## `CityGeneratorAPI.Audio`

Solo lectura.

| Miembro | Devuelve |
| --- | --- |
| `IsAmbienceEnabled()` | `true` si el audio de ambiente 2D está habilitado. |
| `GetAmbienceClipCount()` | Número de AudioSources de ambiente activas. |
| `IsPlazaAudioEnabled()` | `true` si el audio posicional de plaza está habilitado. |
| `GetPlazaAudioSourceCount()` | Número de AudioSources de plaza activas (una por plaza, por cada clip configurado). |

## Cosas que esta API deliberadamente no hace

- **Ninguna mutación que genere, destruya o redimensione contenido** — vehículos, peatones,
  edificios, la propia cuadrícula. La generación sigue siendo exclusivamente un flujo del Editor.
- **Sin `SetFreeViewActive`** — Free View se mantiene como un toggle de input puro (la tecla V).
- **Sin setters en Traffic/Pedestrians/Audio/Player** — esos módulos son de solo lectura.
- **Sin eventos/callbacks.** Consulta por polling en su lugar.
- **Sin soporte para varias ciudades simultáneas** en la misma escena — la API asume el caso normal
  de la tool, una única ciudad activa.

Un setter llamado sobre `Minimap` (o los setters de Day/Night de `City`) actúa directamente sobre
la instancia viva de la escena; si el Editor hace después un Re-Build (solo posible en el Editor,
nunca en un build), esos cambios en runtime se pierden junto con todo lo demás que tenía la ciudad
anterior.
