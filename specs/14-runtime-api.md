# SPEC 14 — Runtime API

> **Estado:** Implemented
> **Depende de:** SPEC 07 (Minimap HUD), SPEC 08 (Day/Night Cycle), SPEC 09 (City Audio), SPEC 11 (Custom Grid), SPEC 12 (Custom Pedestrians) — arquitectura runtime existente que esta API expone en modo lectura y, en un número reducido de casos, escritura.
> **Fecha:** 2026-09-03
> **Objetivo:** Añadir una API Runtime estática (`CityGeneratorAPI`), organizada en un módulo por cada tab de la tool (City, Player, Traffic, Pedestrians, Minimap, Audio), que exponga en Play Mode — tanto en el Editor como en un build final — los datos relevantes de la ciudad generada y los setters ya seguros hoy (hora del día, activación del ciclo día/noche, visibilidad y radio del minimapa), respaldada por un nuevo componente `CityGeneratorInfo` que `CityGeneratorSceneBuilder` rellena en cada Build/Re-Build.

## Scope

**Dentro:**

- **`CityGeneratorAPI`** (nueva clase estática, `Packages/com.santiandrade.citygenerator/Runtime/API/`), con una propiedad `IsCityAvailable` y un submódulo estático por tab: `City`, `Player`, `Traffic`, `Pedestrians`, `Minimap`, `Audio`. Resuelve el `CityGeneratorRoot`/`CityGeneratorInfo` activo una sola vez (lazy, cacheado) mediante `FindFirstObjectByType`.
- **`CityGeneratorInfo`** (nuevo componente Runtime, `[AddComponentMenu("")]` igual que `CityGeneratorRoot`), añadido junto a `CityGeneratorRoot` en el `cityRoot`. Rellenado por `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler` en cada Build y Re-Build a partir de lo que hoy ya se calcula solo para el Editor (`CityBuildSummary`) más los datos de grid/semilla que hoy no se guardan en ningún sitio en tiempo de ejecución.
- **Módulo `City`**: tamaño/forma del grid (rectangular o custom), número de bloques reales, edificios, plazas, custom places, y el resto de conteos que ya calcula `CityBuildSummary` (farolas, papeleras, árboles de calle, semáforos), si se usó semilla y cuál — **más Day/Night**: si el ciclo está habilitado, hora actual, con setters `SetDayNightEnabled(bool)` y `SetHour(float)` reutilizando el comportamiento ya seguro de `DayNightCycle`.
- **Módulo `Player`**: si el Player está habilitado, su posición actual, si Free View está activo (solo lectura).
- **Módulo `Traffic`**: si el tráfico está habilitado, número de vehículos activos.
- **Módulo `Pedestrians`**: si los peatones están habilitados, número de peatones activos, número de custom pedestrians.
- **Módulo `Minimap`**: si el minimapa está habilitado, número de puntos de interés, radio de vista actual — **con setters**: mostrar/ocultar el HUD y cambiar `viewRadiusMeters` en caliente.
- **Módulo `Audio`**: si la ambience/audio de plazas están habilitadas, número de clips de ambience y de fuentes de audio de plaza colocadas (solo lectura).
- **Comportamiento sin ciudad activa**: todo getter devuelve un valor por defecto seguro (0 / false / `Vector2Int.zero` / lista vacía / `null`/`Vector3.zero` en los que devuelven un objeto/vector) cuando `IsCityAvailable` es `false`, sin excepciones.
- **Documentación**: nuevo `docs/api-reference.md` / `docs/api-reference.es.md`, enlazado desde `README.md`/`README.es.md`, más `CHANGELOG.md` (`## [Unreleased]`) y actualización de `docs/architecture/runtime-and-traffic.md` documentando `CityGeneratorAPI`/`CityGeneratorInfo`.

**Fuera de alcance (para futuras specs):**

- Cualquier mutación que implique regenerar, spawnear o despawnear contenido en runtime (añadir/quitar vehículos, peatones, edificios; redimensionar el grid). La generación sigue siendo exclusivamente un flujo del Editor.
- Activar/desactivar Free View por código (`CityGeneratorAPI.Player.SetFreeViewActive`) — descartado explícitamente en la ronda de preguntas; el toggle sigue siendo solo la tecla V.
- Cualquier setter sobre Traffic/Pedestrians/Audio/Player (contadores, prefabs, habilitación) — esta spec los deja de solo lectura.
- Sistema de eventos/callbacks (`OnCityReady`, `OnHourChanged`, etc.) — la API es de consulta directa (pull), no de suscripción (push).
- Soporte para múltiples `CityGeneratorRoot` simultáneos en la misma escena; se asume el caso actual de la tool (una ciudad activa a la vez).
- Cualquier cambio en `CityGeneratorWindow`, `CityGeneratorSettings` o el resto del asmdef Editor más allá de lo estrictamente necesario para que `CityGeneratorSceneBuilder` rellene `CityGeneratorInfo`.
- Publicación de una nueva versión del package — esta spec entrega el código y la documentación; el release es un paso posterior.

## Modelo de datos

```csharp
// Runtime/CityGeneratorInfo.cs — new file, namespace CityGenerator.Runtime

/// <summary>
/// Added to the root of every generated city (alongside CityGeneratorRoot) by
/// CityGeneratorSceneBuilder/CityGeneratorContentAssembler on every Build/Re-Build. Ships in
/// Runtime so it also exists in player builds, not just the Editor; CityGeneratorAPI reads it
/// as its single source of truth instead of each module resolving its own references.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class CityGeneratorInfo : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("True when this city was generated with Custom Grid (customBlockCells) instead of a rectangular gridWidth x gridHeight.")]
    public bool useCustomGrid;
    [Tooltip("Rectangular grid: (gridWidth, gridHeight). Custom Grid: bounding box of the real cells.")]
    public Vector2Int gridSize;
    public int blockCount;

    [Header("Content counts")]
    public int buildingCount;
    public int plazaCount;
    public int customPlaceCount;
    public int lampCount;
    public int binCount;
    public int streetTreeCount;
    public int trafficLightCount;
    [Tooltip("Configured Custom Pedestrian entry count (settings.customPedestrians), not a live agent count.")]
    public int customPedestrianCount;

    [Header("Seed")]
    public bool useCustomSeed;
    public int seed;

    [Header("Feature flags (from GeneralSettings at build time)")]
    public bool playerEnabled;
    public bool trafficEnabled;
    public bool pedestriansEnabled;

    [Header("Audio")]
    public bool ambienceEnabled;
    public int ambienceClipCount;
    public bool plazaAudioEnabled;
    public int plazaAudioSourceCount;

    [Header("Component references (resolved once at build time)")]
    public Transform player;
    public FreeCameraController freeCameraController;
    public TrafficManager trafficManager;
    public PedestrianManager pedestrianManager;
    public DayNightCycle dayNightCycle;
    public MinimapHUD minimapHUD;
    public MinimapData minimapData;
}
```

```csharp
// Runtime/API/CityGeneratorAPI.cs — new file, namespace CityGenerator.Runtime

/// <summary>
/// Read/query entry point for a generated city's data at runtime (Editor Play Mode and player
/// builds alike). Resolves the active CityGeneratorInfo once (lazily, cached) and every module
/// reads straight from it; every getter returns a safe default (0/false/Vector2Int.zero/empty
/// list/null) when IsCityAvailable is false, never throws.
/// </summary>
public static class CityGeneratorAPI
{
    public static bool IsCityAvailable { get; } // true once a CityGeneratorInfo has been resolved

    public static class City
    {
        public static bool IsCustomGrid();
        public static Vector2Int GetGridSize();
        public static int GetBlockCount();
        public static int GetBuildingCount();
        public static int GetPlazaCount();
        public static int GetCustomPlaceCount();
        public static int GetLampCount();
        public static int GetBinCount();
        public static int GetStreetTreeCount();
        public static int GetTrafficLightCount();
        public static bool IsSeeded();
        public static int GetSeed(); // 0 when IsSeeded() is false

        // Day/Night (folded into City)
        public static bool IsDayNightEnabled();
        public static void SetDayNightEnabled(bool enabled); // DayNightCycle.enabled
        public static float GetCurrentHour();
        public static void SetHour(float hour); // DayNightCycle.currentHour + ApplySun
    }

    public static class Player
    {
        public static bool IsEnabled();
        public static Vector3 GetPosition(); // Vector3.zero if no player
        public static bool IsFreeViewActive();
    }

    public static class Traffic
    {
        public static bool IsEnabled();
        public static int GetVehicleCount(); // TrafficManager.AgentCount (new property)
    }

    public static class Pedestrians
    {
        public static bool IsEnabled();
        public static int GetPedestrianCount(); // PedestrianManager.AgentCount (new property)
        public static int GetCustomPedestrianCount();
    }

    public static class Minimap
    {
        public static bool IsEnabled();
        public static int GetPointOfInterestCount();
        public static float GetViewRadiusMeters();
        public static void SetViewRadiusMeters(float meters); // MinimapHUD.viewRadiusMeters
        public static bool IsVisible();
        public static void SetVisible(bool visible); // toggles the MinimapHUD's Canvas GameObject
    }

    public static class Audio
    {
        public static bool IsAmbienceEnabled();
        public static int GetAmbienceClipCount();
        public static bool IsPlazaAudioEnabled();
        public static int GetPlazaAudioSourceCount();
    }
}
```

Notas:

- `TrafficManager`/`PedestrianManager` ganan una propiedad pública `AgentCount` (sobre su `HashSet`/`List` privado ya existente) — es la única forma de que `Traffic.GetVehicleCount()`/`Pedestrians.GetPedestrianCount()` reflejen el número **vivo** de agentes en vez de un conteo congelado al momento de generación (un `CarAgent` puede autodesactivarse en un dead-end de Custom Grid, por ejemplo).
- `CityGeneratorInfo` es la única fuente de verdad para todo lo que no exista ya como componente vivo consultable; todo lo demás (contadores de vehículos/peatones, HUD del minimapa, ciclo día/noche) se lee de las referencias que guarda, nunca duplicado como dato propio.
- `CityGeneratorAPI` no añade ningún otro tipo de dato nuevo — su único estado interno es la referencia cacheada a `CityGeneratorInfo`, resuelta una vez con `FindFirstObjectByType<CityGeneratorInfo>()`.
- `SetHour`/`SetDayNightEnabled` no tocan `CityGeneratorSettings` (Editor-only, inalcanzable desde Runtime) — llaman directamente a los métodos/campos ya seguros de `DayNightCycle` que la propia tool usa para el preview en Editor.

## Plan de implementación

1. **`CityGeneratorInfo` (esqueleto) + `AgentCount` en los managers.** Crear `Runtime/CityGeneratorInfo.cs` con todos los campos del modelo de datos, sin poblar todavía en ningún sitio. Añadir la propiedad pública `AgentCount => agents.Count` a `TrafficManager` y `PedestrianManager`. El proyecto compila; sin cambio de comportamiento (el componente no se añade a ningún `cityRoot` aún). Test manual: compilar en Unity sin errores en consola.

2. **Poblar `CityGeneratorInfo` en el pipeline de generación.** `CityGeneratorContentAssembler.Assemble` añade `CityGeneratorInfo` junto a `CityGeneratorRoot` (igual que hace hoy con `CityGeneratorRoot`) y rellena ahí mismo lo que ya calcula para `CityBuildSummary` (grid, content counts, custom-place count) más lo que hoy no calculaba (`useCustomGrid`, `seed`/`useCustomSeed`, los tres flags `*Enabled`, el conteo de custom pedestrians, las referencias a `trafficManager`/`pedestrianManager` que `BuildVehicles`/`BuildPedestrians` ya devuelven, y los conteos/flags de audio que `AudioBuilder` ya conoce). `CityGeneratorSceneBuilder` rellena el resto (`player`, `freeCameraController`, `dayNightCycle`, `minimapHUD`, `minimapData`) justo después de crear cada uno, localizando la instancia vía `cityRoot.GetComponent<CityGeneratorInfo>()` — tanto en `BuildAndSaveScene` como en `RebuildInActiveScene`. Test manual: generar la ciudad de test con los valores por defecto, entrar en Play, inspeccionar `CityGeneratorInfo` en el Inspector sobre el GameObject `City` y confirmar que ningún campo se ha quedado en su valor por defecto sin sentido (grid size correcto, counts > 0, las 6 referencias no nulas salvo que su feature esté deshabilitada).

3. **`CityGeneratorAPI` — núcleo + módulo `City`.** Nuevo `Runtime/API/CityGeneratorAPI.cs`: resolución perezosa y cacheada de `CityGeneratorInfo`, `IsCityAvailable`, y el submódulo `City` completo (grid/content/seed + Day/Night). Test manual: en Play Mode sobre la ciudad de test, invocar los métodos desde la consola de Unity (`eval`) o un script temporal — `GetGridSize()`/`GetBuildingCount()` devuelven los valores esperados, `SetHour(12f)` mueve la luz direccional instantáneamente igual que ya hace el preview del Editor, y todo devuelve valores por defecto sin excepción en una escena vacía sin ciudad generada.

4. **Módulos `Player`, `Traffic`, `Pedestrians`.** Test manual: con Player/Traffic/Pedestrians habilitados, `Player.GetPosition()` seguido de mover al jugador en Play confirma que el valor cambia; `Traffic.GetVehicleCount()`/`Pedestrians.GetPedestrianCount()` coinciden con el `Vehicle Count`/`Pedestrian Count` configurados (o menos, si algún agente se autodesactivó); deshabilitar cada feature y regenerar confirma que su módulo cae a los valores por defecto sin errores.

5. **Módulos `Minimap`, `Audio`.** Test manual: `Minimap.SetVisible(false)` oculta el HUD en Play y `SetVisible(true)` lo restaura; `SetViewRadiusMeters` cambia el radio visible en el siguiente frame; `Audio.GetAmbienceClipCount()`/`GetPlazaAudioSourceCount()` coinciden con lo configurado en la tab Audio.

6. **Documentación.** Nuevo `docs/api-reference.md` (y `.es.md`) documentando cada método por módulo con su firma y semántica de "sin ciudad" (valor por defecto); enlazado desde `README.md`/`README.es.md`. `CHANGELOG.md` (`## [Unreleased]`). Actualizar `docs/architecture/runtime-and-traffic.md` con una sección `CityGeneratorInfo`/`CityGeneratorAPI` (quién la puebla, en qué punto del pipeline, la garantía de "sin excepciones").

## Criterios de aceptación

- [x] `CityGeneratorInfo` existe en `Runtime/`, con todos los campos del modelo de datos, y se añade automáticamente junto a `CityGeneratorRoot` en cada Build y Re-Build (tanto para un grid rectangular como para Custom Grid).
- [x] `TrafficManager.AgentCount` y `PedestrianManager.AgentCount` existen y reflejan el número de agentes actualmente registrados, no un valor congelado en el momento de generación.
- [x] Tras generar la ciudad de test con los valores por defecto y entrar en Play, `CityGeneratorInfo` tiene todos sus campos rellenados con valores coherentes con lo generado (grid size, counts, seed, flags, las 6 referencias de componente cuando su feature está habilitada).
- [x] `CityGeneratorAPI.IsCityAvailable` es `false` en una escena sin ninguna ciudad generada, y todo getter de todos los módulos devuelve su valor por defecto seguro (0 / `false` / `Vector2Int.zero` / lista vacía / `Vector3.zero`) sin lanzar ninguna excepción en ese caso.
- [x] `City.GetGridSize()`/`GetBlockCount()`/`GetBuildingCount()`/`GetPlazaCount()`/`GetCustomPlaceCount()`/`GetLampCount()`/`GetBinCount()`/`GetStreetTreeCount()`/`GetTrafficLightCount()` devuelven los valores reales de la ciudad generada, en ambos modos de grid.
- [x] `City.IsSeeded()`/`GetSeed()` reflejan `useCustomSeed`/`seed` de la generación.
- [x] `City.IsDayNightEnabled()`/`GetCurrentHour()` reflejan el estado real de `DayNightCycle`; `City.SetDayNightEnabled(false)` congela la luz en su hora actual (Unity deja de llamar `Update` en un `Behaviour` deshabilitado) y `SetHour(h)` reposiciona la luz instantáneamente, igual que el preview del Editor.
- [x] `Player.IsEnabled()`/`GetPosition()`/`IsFreeViewActive()` reflejan el estado real del Player generado, incluyendo el caso Player deshabilitado (posición `Vector3.zero`, `IsEnabled() == false`).
- [x] `Traffic.IsEnabled()`/`GetVehicleCount()` y `Pedestrians.IsEnabled()`/`GetPedestrianCount()`/`GetCustomPedestrianCount()` reflejan lo generado, incluyendo el caso de cada feature deshabilitada.
- [x] `Minimap.IsEnabled()`/`GetPointOfInterestCount()`/`GetViewRadiusMeters()` reflejan lo generado; `SetVisible(bool)` muestra/oculta el HUD en Play; `SetViewRadiusMeters(float)` cambia el radio visible en el siguiente frame.
- [x] `Audio.IsAmbienceEnabled()`/`GetAmbienceClipCount()`/`IsPlazaAudioEnabled()`/`GetPlazaAudioSourceCount()` reflejan lo configurado en la tab Audio.
- [x] Ninguna regresión en el comportamiento existente de generación, Day/Night, Minimap o Audio cuando la API no se usa (todo el trabajo de esta spec es aditivo).
- [x] `docs/api-reference.md`/`.es.md` documentan cada método de cada módulo, enlazados desde `README.md`/`README.es.md`.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo la nueva API.
- [x] `docs/architecture/runtime-and-traffic.md` documenta `CityGeneratorInfo`/`CityGeneratorAPI`.

## Decisiones tomadas y descartadas

- **API Runtime-only (`Packages/.../Runtime/API/`), nunca Editor.** Decisión explícita del usuario: debe funcionar tanto en Play Mode del Editor como en un build final del juego, no solo mientras se edita. Esto obliga a que toda la información pase por un componente Runtime nuevo (`CityGeneratorInfo`) en vez de leerse directamente de `CityGeneratorSettings`, que es Editor-only.
- **Clase estática con módulos anidados (`CityGeneratorAPI.City`, `.Player`, etc.), en vez de un singleton `MonoBehaviour`.** Decisión explícita del usuario: no requiere que el código del usuario obtenga ninguna instancia (`Instance`), ni depende de que exista un GameObject concreto en la escena más allá de `CityGeneratorInfo`, que la API resuelve internamente.
- **Un componente `CityGeneratorInfo` como única fuente de verdad, en vez de que cada módulo resuelva sus propias referencias (`FindAnyObjectByType` por submódulo).** Más barato (una sola resolución cacheada) y evita ambigüedad si en el futuro coexistieran varias redes/ciudades en la misma escena (ver el comentario de `PedestrianManager` sobre ese escenario, hoy fuera de alcance).
- **Todo el alcance de las 6 tabs en una sola spec, en vez de dividir en varias.** Decisión explícita del usuario tras planteárselo como alternativa; se acepta el mayor tamaño de la spec a cambio de entregar la API completa de una vez.
- **`DayNight` como parte del módulo `City`, no como módulo propio.** Decisión explícita del usuario, revirtiendo la propuesta inicial — aunque `DayNightCycle` vive en la Directional Light (fuera de `cityRoot`) y no en la tab City de la ventana, conceptualmente es información/control sobre "la ciudad actual", no sobre otra tab.
- **Solo lectura salvo Day/Night (hora + enabled) y Minimap (visibilidad + radio de vista).** Decisión explícita del usuario tras la ronda de preguntas: son las únicas mutaciones que los sistemas ya soportan de forma segura hoy (`DayNightCycle.enabled`/`currentHour`, `MinimapHUD.viewRadiusMeters`) sin implicar regenerar o re-espawnear nada. Free View por código se descartó explícitamente (queda solo la tecla V).
- **Sin excepciones: todo getter devuelve un valor por defecto seguro cuando no hay ciudad activa**, en vez de lanzar o exigir un patrón `TryGet`/evento `OnCityReady`. Decisión explícita del usuario, priorizando una API simple de usar sobre una que fuerce comprobaciones previas.
- **`CityGeneratorInfo` resuelto una única vez y cacheado, no re-resuelto en cada llamada.** Decisión explícita del usuario: cubre el caso normal de la tool (una ciudad generada, jugada tal cual) sin pagar el coste de un `FindFirstObjectByType` por llamada; si en el futuro se soporta regenerar en runtime, esa spec futura tendrá que revisar esta decisión.
- **`TrafficManager.AgentCount`/`PedestrianManager.AgentCount` como conteo vivo, en vez de duplicar `vehicleCount`/`pedestrianCount` configurados en `CityGeneratorInfo`.** Evita que la API devuelva un número que ya no coincide con la realidad (un `CarAgent` puede autodesactivarse en un dead-end de Custom Grid) y evita una segunda fuente de verdad para el mismo dato.
- **Sin tests automatizados (`Assets/Tests/`) para esta spec, solo tests manuales por paso.** Mismo criterio que SPEC 13 (Free Camera): es una feature de superficie de API sobre comportamiento runtime ya existente y verificado, no lógica de generación nueva que necesite cobertura de regresión.

## Riesgos identificados

- **`CityGeneratorInfo` puede quedar desincronizado si se olvida rellenar algún campo en una ruta del pipeline (Build en escena nueva vs. Re-Build en escena activa son dos caminos de código distintos en `CityGeneratorSceneBuilder`).** Un campo sin rellenar en una de las dos rutas haría que la API devolviera datos correctos en un flujo y por defecto/obsoletos en el otro, sin ningún error visible. Mitigación: el test manual del paso 2 del plan cubre explícitamente ambos flujos (Build y Re-Build), no solo uno.
- **Las referencias a componentes (`dayNightCycle`, `minimapHUD`, `player`, `freeCameraController`) se resuelven en distintos puntos del pipeline (`ContentAssembler` vs. `SceneBuilder`, antes/después de `Assemble`), a diferencia de los campos escalares que `ContentAssembler` ya calcula de una vez para `CityBuildSummary`.** Un error de orden (asignar antes de que el componente exista, o después de que `CityGeneratorAPI` ya lo haya cacheado) dejaría esa referencia en `null` de forma silenciosa. Mitigación: cada asignación ocurre inmediatamente después de crear/`AddComponent` el objeto correspondiente, siguiendo el mismo patrón que `CityGeneratorSceneBuilder` ya usa para aplicar `CameraSettings`/`PlayerSettings` a sus instancias.
- **`CityGeneratorAPI` cachea `CityGeneratorInfo` en la primera llamada; si el usuario regenera la ciudad en runtime por su cuenta (fuera del alcance de esta spec, pero técnicamente posible llamando a código del propio paquete) la caché quedaría apuntando al `cityRoot` destruido.** Aceptado como riesgo conocido, documentado explícitamente en `docs/api-reference.md`: la API asume una única ciudad generada por sesión de Play, coherente con el resto de la tool hoy.
- **Añadir `CityGeneratorInfo` junto a `CityGeneratorRoot` en cada ciudad generada aumenta ligeramente el peso serializado de la escena** (referencias a varios componentes más los conteos). Impacto esperado despreciable frente al resto de la geometría generada; no se mide explícitamente en esta spec.
- **Los setters de `Minimap` (`SetVisible`, `SetViewRadiusMeters`) actúan directamente sobre la instancia de `MinimapHUD` en escena, sin pasar por `CityGeneratorSettings`.** Si el usuario los llama y después la escena hace un Re-Build (solo posible en Editor), esos cambios se pierden — comportamiento esperado y coherente con que el resto de mutaciones en runtime tampoco sobreviven a una regeneración, pero vale la pena documentarlo explícitamente para que no sorprenda.
