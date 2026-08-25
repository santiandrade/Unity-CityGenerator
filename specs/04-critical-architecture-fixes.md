# SPEC 04 — Correcciones críticas y arquitectónicas (informe técnico 2026-08-25)

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 02 (Unity Package Distribution), SPEC 03 (Red peatonal)
> **Fecha:** 2026-08-25
> **Objetivo:** Corregir los cuatro problemas de prioridad crítica del informe técnico externo (rebuild no transaccional, pérdida de POI peatonales en Play, colliders jerárquicos ignorados por los sensores, contradicción entre `Include Traffic` y los semáforos) junto con un bloque de mejoras de prioridad alta puramente arquitectónicas (framerate global, validación de inputs incompleta, tooling interno mezclado con el package, singletons de managers, autoridad del Input System), dejando fuera de esta ronda el rendimiento, los tests y la limpieza de contenido demo/documentación.

## Por qué existe esta spec

El 2026-08-25 se recibió un informe técnico externo (`docs/technical-review-2026-08-25.md`, aportado por ChatGPT tras una revisión de solo lectura del repo) que identifica 19 mejoras priorizadas. Cuatro de ellas son de prioridad crítica porque implican pérdida de trabajo del usuario (rebuild destructivo sin recuperación), comportamiento documentado que no ocurre en Play (POI peatonales), incompatibilidad silenciosa con prefabs de terceros (colliders jerárquicos) y una ruta de fallo reproducible durante la generación (semáforos vs `Include Traffic`).

El informe agrupa el resto en prioridad alta, media y baja/condicionada a medición, con un orden de trabajo recomendado: corregir lo crítico, retirar el override de framerate, crear tests de regresión, medir, y solo entonces optimizar rendimiento. Este spec sigue ese orden abordando el bloque crítico completo más los ítems de prioridad alta que son correcciones arquitectónicas puras (no dependen de medición de rendimiento): framerate global (5), validación de inputs (10), separación de tooling interno (11), singletons de managers (13) y autoridad del Input System (16). El resto de la prioridad alta —tests (6) e índice espacial/costes físicos/pathfinding (7, 8, 9)— se deja para un SPEC 05 posterior, siguiendo la propia recomendación del informe de no optimizar sin medir antes. Los ítems de prioridad media/baja sobre contenido demo, lightmaps y documentación (12, 14, 15, 17, 18, 19) se dejan para specs futuros aún sin numerar.

## Scope

**Dentro:**

- **Ítem 1 — Rebuild transaccional y recuperable.**
  - **`Runtime/CityGeneratorRoot.cs` (nuevo)** — marcador `MonoBehaviour` vacío (`[DisallowMultipleComponent]`, `[AddComponentMenu("")]`) añadido al root de toda ciudad generada, en `Runtime/` porque también debe existir en builds runtime, no solo en Editor.
  - **`Editor/CityGeneratorSceneBuilder.cs`** — `RebuildInActiveScene` deja de destruir el root existente antes de generar: crea el nuevo root bajo un nombre temporal (`"City (generating)"`), llama a `CityGeneratorContentAssembler.Assemble`, y solo si termina sin excepción localiza el root anterior por `GetComponent<CityGeneratorRoot>()` (no por `root.name == "City"`), lo destruye dentro de un `Undo.RegisterCompleteObjectUndo`/grupo de Undo, y renombra el nuevo a `"City"`. Si `Assemble` lanza, el root temporal fallido se destruye con `Object.DestroyImmediate`, el anterior queda intacto, y la excepción se repropaga para que `CityGeneratorWindow` la capture como hoy.
  - **`Editor/CityGeneratorContentAssembler.cs`** — `Assemble` añade `CityGeneratorRoot` al root recibido como primer paso (antes de cualquier builder), no al final, para que un fallo a mitad de generación deje igualmente el temporal marcado y localizable si hiciera falta depurarlo manualmente.
  - **`Editor/CityGeneratorWindow.cs`** — "Re-Build City in Current Scene" pasa por el nuevo flujo transaccional; el mensaje de error mostrado en el panel de resultado indica explícitamente que la ciudad anterior no se ha perdido.
  - Undo: la sustitución del root (destruir el viejo + el nuevo pasa a llamarse `"City"`) queda dentro de un único `Undo.CollapseUndoOperations`, de modo que Ctrl+Z tras un rebuild exitoso restaura el estado anterior en un solo paso.

- **Ítem 2 — Persistencia de POI peatonales.**
  - **`Runtime/PedestrianNetwork.cs`** — nuevo struct serializable `PointOfInterestDescriptor` (posición, `lookAtPosition`, tipo `PointOfInterestKind`, índice/coordenada del nodo `Ring` de conexión) guardado en una `List<PointOfInterestDescriptor>` serializada de la instancia. `RegisterPointOfInterest` (llamado hoy desde `CityGeneratorPedestrianBuilder.RegisterPointsOfInterest`) pasa a añadir también el descriptor a esa lista. `Build()` reconstruye primero anillos/cruces como hoy y, antes de podar obstáculos (`PrunePlacedObstacles`), reinserta los nodos `PointOfInterest` a partir de los descriptores serializados y los reconecta a su nodo `Ring`.
  - **`Editor/CityGeneratorPedestrianBuilder.cs`** — sin cambios de comportamiento (sigue llamando al mismo método público), solo se documenta que los POI ya sobreviven al ciclo Edit→Play→Edit.

- **Ítem 3 — Colliders jerárquicos en vehículos y peatones.**
  - **`Editor/CityGeneratorColliderUtility.cs`** — `EnsureNonTriggerCollider` cambia de "añadir un `BoxCollider` solo si no hay ninguno" a **añadir siempre** un `BoxCollider` propio en el root de la instancia (dimensionado por `CityGeneratorBoundsUtility.GetWorldBounds`, igual que hoy), dedicado exclusivamente a ser detectado por los sensores — sigue en la layer `Vehicle`/`Pedestrian` que le asigne el builder. Los colliders del prefab del usuario, en cualquier profundidad de la jerarquía, dejan de forzarse a `isTrigger = false` y de tocarse en absoluto: quedan con su propia layer y su propio `isTrigger`, sirviendo solo de colisión física (p. ej. contra el `CharacterController` del jugador), nunca de canal de detección para `CarAgent`/`PedestrianAgent`.
  - **`Editor/CityGeneratorTrafficBuilder.cs`** / **`Editor/CityGeneratorPedestrianBuilder.cs`** — dejan de hacer `instance.layer = vehicleLayer` / `pedestrianLayer` sobre el root completo (que hoy no propaga a hijos); en su lugar, la layer se aplica únicamente al collider proxy nuevo, vía `SerializedObject`/`GameObject.layer` sobre el proxy. El resto de la jerarquía del prefab del usuario conserva su layer original.
  - **`Runtime/CarAgent.cs`** — sin cambios de lógica de sensor (sigue filtrando por `vehicleMask`/`pedestrianMask`); el comentario sobre "collider hijo invisible al sensor" se actualiza para reflejar que ya no puede ocurrir.

- **Ítem 4 — Contradicción `Include Traffic` / semáforos.**
  - **`Editor/CityGeneratorValidator.cs`** — el bloque `if (settings.general.includeTraffic)` que exige `props.trafficLightPrefab` (línea 47 actual) se sustituye por una condición basada en `settings.general.gridWidth > 1 && settings.general.gridHeight > 1` (hay intersección interior ⟺ hay cebra ⟺ se construye semáforo, independientemente de `includeTraffic`), manteniendo el mismo mensaje de error adaptado.
  - No se toca `CityGeneratorContentAssembler.cs` (los semáforos ya se construyen siempre e independientemente de `includeTraffic`, eso queda confirmado como comportamiento correcto): el fix es solo de validación, para que deje de aceptar una combinación que el pipeline real rechaza.

- **Ítem 5 — Framerate global.**
  - **`Runtime/PerformanceBootstrap.cs`** — se elimina el fichero completo (el `[RuntimeInitializeOnLoadMethod]` que fuerza `vSyncCount = 0`/`targetFrameRate = 60` al cargar cualquier escena del proyecto consumidor).
  - **`Editor/CityGeneratorContentAssembler.cs` / `Runtime/`** — no se añade ningún sustituto (confirmado: sin componente opt-in). Se documenta en el CHANGELOG como *breaking* para quien dependiera implícitamente de ese ajuste.

- **Ítem 10 — Validación de inputs y prefabs (los 7 huecos del informe).**
  - **`Editor/CityGeneratorValidator.cs`** — `ValidateDetailed` gana bloques nuevos, cada uno condicionado al toggle real que activa ese sistema (siguiendo el patrón ya existente en líneas 47 y 62-104):
    1. Elementos `null` en `buildingPrefabs`/`vegetation.prefabs` (además del ya existente "lista vacía").
    2. `vehicleCount`/`pedestrianCount` dejan de validarse cuando `includeTraffic`/`includePedestrians` está desactivado (hoy se validan por el conteo, no por el toggle).
    3. `player.walkSpeed`/`runSpeed` y `pedestrianBehaviour.walkReferenceSpeed`/`runReferenceSpeed` iguales a cero, o walk == run, bloqueantes (división por cero / NaN en el blend tree de animación).
    4. Radios, duraciones, tamaños de celda (`crowd`, `pedestrianBehaviour`) y distancias negativos, bloqueantes.
    5. `general.inputActions` — el nombre de acción configurado para Move/Sprint/Jump/Look existe realmente en el `InputActionAsset` asignado y es del tipo esperado (Value/Button según corresponda).
    6. `player.controllerRadius`/`controllerHeight`/`stepOffset`/`skinWidth` — coherencia mínima (`stepOffset < controllerHeight`, `skinWidth < controllerRadius`, todos positivos).
    7. Prefabs de edificios/vehículos/peatones/props sin ningún `Renderer` en su jerarquía — hoy caen al footprint ficticio de 0,5 m sin avisar; pasa a warning no bloqueante.
  - Warnings no bloqueantes (no impiden generar) para (1) y (7); el resto son errores bloqueantes como el resto de `ValidateDetailed`.

- **Ítem 11 — Tooling interno fuera del package.**
  - **`Assets/Editor/CityGeneratorSetDefaultsWindow.cs` (nuevo, junto a `CityGeneratorReleaseWindow.cs`)** — el `[MenuItem("Tools/City Generator/Set Current Selection As Default")]` se mueve aquí desde `CityGeneratorWindow.cs`. Localiza la ventana abierta (`EditorWindow.GetWindow<CityGeneratorWindow>` o `Resources.FindObjectsOfTypeAll`) y llama a `CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault(window.settings)`.
  - **`Packages/com.santiandrade.citygenerator/Editor/CityGenerator.Editor.asmdef`** — gana `[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]` (vía un `Editor/AssemblyInfo.cs` nuevo), único cambio de visibilidad necesario para que el comando movido siga leyendo `window.settings` (`internal`).
  - **`Packages/com.santiandrade.citygenerator/Editor/CityGeneratorWindow.cs`** — pierde el `[MenuItem]` y la llamada directa a `CityGeneratorDefaultAssetsWriter`; `CityGeneratorDefaultAssetsWriter.cs` permanece en el package (sigue reescribiendo el propio código fuente del package, que es su función), solo se mueve el punto de entrada del menú.

- **Ítem 13 — Singletons globales de managers.**
  - **`Runtime/TrafficNetwork.cs`** — gana un campo (asignado por `CityGeneratorTrafficBuilder.AddManagerComponent`, mismo `GameObject`) que referencia su `TrafficManager` asociado.
  - **`Runtime/PedestrianNetwork.cs`** — análogo, referencia a su `PedestrianManager`.
  - **`Runtime/CarAgent.cs`** — `Start`/`OnEnable` resuelve el manager vía `network.Manager` en vez de `TrafficManager.Instance`; si es `null` (uso standalone sin builder), cae a `FindAnyObjectByType<TrafficManager>()` como hoy. Registro se mueve de `Start` a `OnEnable` (idempotente: `TrafficManager.Register` ignora una segunda llamada del mismo agente ya registrado); desregistro se mantiene en `OnDisable`.
  - **`Runtime/PedestrianAgent.cs`** — mismo cambio, espejado, con `PedestrianManager`.
  - **`Runtime/TrafficManager.cs`** / **`Runtime/PedestrianManager.cs`** — se retira el `static Instance`; `Register` pasa a ser idempotente (`if (!agents.Contains(agent)) agents.Add(agent)`, o un `HashSet`).
  - Efecto: varias ciudades en la misma escena (o cargada aditivamente) ya no comparten manager por el "último `Awake` gana", y un agente reactivado (`gameObject.SetActive(true)` tras estar desactivado) vuelve a quedar registrado sin depender de que `Start` se repita.

- **Ítem 16 — Autoridad única del Input System.**
  - **`Runtime/PlayerInputAuthority.cs` (nuevo)** — componente ligero, único que llama `playerActionMap.Enable()`/`Disable()`, en `OnEnable`/`OnDisable`. Expuesto vía referencia directa al `InputActionAsset` (mismo campo `general.inputActions` que ya usan `PlayerController`/`ThirdPersonCamera`). No es `UnityEngine.InputSystem.PlayerInput` (ver Decisiones).
  - **`Runtime/PlayerController.cs`** — deja de llamar `Enable()`/`Disable()` sobre el mapa; solo lee las acciones (`Move`, `Sprint`, `Jump`) ya habilitadas por `PlayerInputAuthority`.
  - **`Runtime/ThirdPersonCamera.cs`** — mismo cambio para `Look`; gana además una restauración explícita de cursor/visibilidad en `OnDisable` (hoy ausente, señalado por el informe).
  - **`Editor/CityGeneratorSceneBuilder.cs`** (`ConfigurePlayer`) — añade `PlayerInputAuthority` a la instancia del Player Prefab junto a `CharacterController`/`PlayerController`, igual que ya hace con esos dos.

**Fuera de alcance (para specs posteriores):**

- Ítem 6 (suite de tests EditMode/PlayMode/performance) y los ítems de rendimiento 7 (índice espacial de solapamiento), 8 (coste físico del tráfico / `SyncTransforms`) y 9 (pathfinding y separación peatonal) — SPEC 05, posterior a este, siguiendo la recomendación del informe de medir antes de optimizar.
- Ítems 12 (flags `ContributeGI`), 14 (limpieza de importadores/colliders del contenido demo), 15 (LOD en el contenido demo), 17 (chunking de marcas viales), 18 (dividir `CityGeneratorWindow`) y 19 (actualizar `docs/technical-review.md`, `docs/pedestrian-network-plan.md`, `package.json`) — specs futuros aún sin numerar, sobre contenido demo/documentación.
- Cualquier decisión de rendimiento o arquitectura no listada explícitamente arriba, aunque aparezca mencionada de pasada en el informe (p. ej. ECS/DOTS, object pooling, GPU instancing) — el informe mismo los descarta para el estado actual del proyecto.
- Publicación de una nueva versión del package (bump de `version`/tag): este spec entrega código; el release es un paso posterior con `Tools > City Generator > Release`.
- Migrar `PlayerInputAuthority` al componente estándar `UnityEngine.InputSystem.PlayerInput` de Unity — se descartó esa alternativa por requerir recablear `PlayerController`/`ThirdPersonCamera` a eventos/callbacks; el nombre del nuevo componente no debe confundirse con esa clase de Unity (podría renombrarse en `/spec-impl` si genera ambigüedad).

## Modelo de datos

```csharp
// Runtime/CityGeneratorRoot.cs (nuevo)
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class CityGeneratorRoot : MonoBehaviour { }
```

```csharp
// Runtime/PedestrianNetwork.cs

/// <summary>Serialized so a point of interest (bench/fountain stop) survives the Awake → Build()
/// cycle: nodes.Clear() wipes the runtime graph every Build(), so POIs must be re-added from
/// something serialized, not just left in the in-memory node list.</summary>
[Serializable]
public struct PointOfInterestDescriptor
{
    public Vector3 position;
    public Vector3 lookAt;
    // Position of the Ring node this POI connects to. Node indices are not stable across
    // Build() calls (the node list is rebuilt from scratch), so the connection is re-resolved
    // by nearest Ring node position — deterministic, since ring geometry only depends on
    // settings/grid, not on random.
    public Vector3 connectedRingPosition;
}

[SerializeField] private List<PointOfInterestDescriptor> pointsOfInterest = new();

/// <summary>Called by CityGeneratorPedestrianBuilder.RegisterPointsOfInterest. Adds the node to
/// the live graph immediately (as today) and appends its descriptor so it survives future
/// Build() calls (Play mode, Rebuild Pedestrian Network).</summary>
public int RegisterPointOfInterest(Vector3 position, Vector3 lookAt, int connectedRingNode);
```

```csharp
// Runtime/TrafficNetwork.cs
[SerializeField] private TrafficManager manager; // same GameObject, set by CityGeneratorTrafficBuilder.AddManagerComponent
public TrafficManager Manager => manager;

// Runtime/PedestrianNetwork.cs — mirrored
[SerializeField] private PedestrianManager manager;
public PedestrianManager Manager => manager;
```

```csharp
// Runtime/TrafficManager.cs — Instance removed
private readonly HashSet<CarAgent> agents = new(); // was List<CarAgent>; membership check is what makes Register idempotent
public void Register(CarAgent agent) => agents.Add(agent); // HashSet.Add is a no-op if already present
public void Unregister(CarAgent agent) => agents.Remove(agent);

// Runtime/PedestrianManager.cs — mirrored with PedestrianAgent
```

```csharp
// Runtime/PlayerInputAuthority.cs (nuevo)
public sealed class PlayerInputAuthority : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions; // same asset as general.inputActions
    private InputActionMap playerMap;

    private void OnEnable()  { playerMap = inputActions.FindActionMap("Player"); playerMap.Enable(); }
    private void OnDisable() { playerMap?.Disable(); }
}
```

No se introducen estructuras de datos nuevas para el ítem 4 (solo cambia una condición en `CityGeneratorValidator`), el ítem 10 (reutiliza `CityGeneratorValidationIssue` ya existente) ni el ítem 11 (solo mueve un `[MenuItem]` y añade un atributo de ensamblado).

## Plan de implementación

1. **Retirar `PerformanceBootstrap` (ítem 5).** Eliminar `Runtime/PerformanceBootstrap.cs`. Verificación: el proyecto compila, y una escena que no genera ninguna ciudad deja de ver forzado `vSyncCount`/`targetFrameRate` al entrar en Play.

2. **Corregir la validación de semáforos (ítem 4).** En `CityGeneratorValidator.ValidateDetailed`, sustituir la condición `settings.general.includeTraffic` que exige `props.trafficLightPrefab` por `settings.general.gridWidth > 1 && settings.general.gridHeight > 1`. Verificación: con `includeTraffic` desactivado, `trafficLightPrefab` vacío y una rejilla ≥2×2, el botón Build queda deshabilitado con el error visible en la card de Props; con una rejilla 1×N, `trafficLightPrefab` vuelve a ser opcional.

3. **`CityGeneratorRoot` + rebuild transaccional (ítem 1).** Crear `Runtime/CityGeneratorRoot.cs`. Modificar `CityGeneratorContentAssembler.Assemble` para añadirlo al root nada más entrar. Reescribir `CityGeneratorSceneBuilder.RebuildInActiveScene` con el flujo temporal→sustitución descrito en Scope, envuelto en un grupo de Undo. Verificación manual: regenerar una ciudad varias veces seguidas comprueba que el resultado es idéntico a hoy; forzar un fallo (p. ej. asignar temporalmente un prefab de edificio roto) confirma que la ciudad anterior sigue intacta y el panel de resultado muestra el error; Ctrl+Z tras un rebuild exitoso restaura la ciudad anterior en un solo paso.

4. **Persistencia de POI peatonales (ítem 2).** Añadir `PointOfInterestDescriptor` y la lista serializada a `PedestrianNetwork`, cambiar `RegisterPointOfInterest` para que también la alimente, y hacer que `Build()` reinserte los descriptores tras reconstruir anillos/cruces y antes de podar obstáculos. Verificación manual: generar una ciudad con plaza, entrar en Play, confirmar en el Gizmo/Scene view (o con `[ContextMenu] Rebuild Pedestrian Network`) que los nodos `PointOfInterest` siguen presentes y conectados tras el `Awake()` de Play; algún peatón debe seguir parándose junto a un banco/fuente.

5. **Colliders jerárquicos (ítem 3).** Cambiar `CityGeneratorColliderUtility.EnsureNonTriggerCollider` para que siempre añada el proxy en el root sin tocar los colliders existentes del prefab, y actualizar `CityGeneratorTrafficBuilder`/`CityGeneratorPedestrianBuilder` para asignar la layer únicamente al proxy. Verificación manual: usar un vehículo/peatón de prueba con un collider solo en un hijo (no en el root) y confirmar que otros vehículos/peatones lo detectan (frenan) y que el jugador sigue chocando físicamente contra él.

6. **Managers no globales (ítem 13).** Añadir el campo `Manager` a `TrafficNetwork`/`PedestrianNetwork`, quitar los `static Instance` de `TrafficManager`/`PedestrianManager`, pasar `agents` a `HashSet` (o chequeo de contención equivalente), mover `Register` a `OnEnable` en `CarAgent`/`PedestrianAgent` resolviendo el manager vía `network.Manager` con fallback a `FindAnyObjectByType`. Verificación manual: generar dos ciudades en la misma escena (rebuild manual del root secundario con nombre distinto, o cargar aditivamente una segunda escena generada) y confirmar que ambos tráficos/peatones se mueven; desactivar y reactivar un vehículo confirma que retoma su Tick.

7. **Autoridad única del Input System (ítem 16).** Crear `Runtime/PlayerInputAuthority.cs`, quitar `Enable()`/`Disable()` del mapa en `PlayerController`/`ThirdPersonCamera`, añadir la restauración de cursor/visibilidad en `ThirdPersonCamera.OnDisable`, y hacer que `CityGeneratorSceneBuilder.ConfigurePlayer` añada el nuevo componente. Verificación manual: generar una ciudad y confirmar en Play que moverse, saltar, esprintar y orbitar la cámara siguen funcionando igual; desactivar el `PlayerController` a mano confirma que la cámara sigue recibiendo `Look` (antes se habría cortado si compartían el `Enable`/`Disable`).

8. **Completar validación de inputs y prefabs (ítem 10).** Añadir los 7 bloques descritos en Scope a `CityGeneratorValidator.ValidateDetailed`, cada uno condicionado a su toggle real. Verificación manual: forzar cada condición de error una por una (velocidad a 0, radio negativo, nombre de acción inexistente, etc.) y confirmar que la card/tab correspondiente se marca en rojo y el botón Build se deshabilita; confirmar que con todo válido la generación sigue funcionando sin falsos positivos.

9. **Separar el tooling interno del package (ítem 11).** Añadir `Editor/AssemblyInfo.cs` con `InternalsVisibleTo("Assembly-CSharp-Editor")` al package; crear `Assets/Editor/CityGeneratorSetDefaultsWindow.cs` con el `[MenuItem]` movido; quitarlo de `CityGeneratorWindow.cs`. Verificación manual: `Tools > City Generator > Set Current Selection As Default` sigue funcionando igual (sobrescribe `CityGeneratorDefaultAssets.cs`/`CityGeneratorSettings.cs`) desde su nueva ubicación fuera del package.

## Criterios de aceptación

- [x] Regenerar una ciudad ("Re-Build City in Current Scene") varias veces produce el mismo resultado que hoy (sin diferencias visuales ni de jerarquía salvo el nuevo componente `CityGeneratorRoot`).
- [x] Forzar una excepción a mitad de un rebuild (p. ej. prefab de edificio roto temporalmente) deja la ciudad anterior intacta en la escena, con el error visible en el panel de resultado de `CityGeneratorWindow`.
- [x] Un rebuild exitoso es deshacible con un único Ctrl+Z (grupo de Undo), restaurando exactamente la ciudad anterior.
- [x] La detección de "ciudad generada" no depende de que el root se llame `"City"`: un objeto renombrado por el usuario a `"City"` sin `CityGeneratorRoot` no se destruye en un rebuild; un root generado renombrado por el usuario a otro nombre sí se detecta y sustituye.
- [x] Tras generar una ciudad con al menos una plaza (banco/fuente) y entrar en Play, los nodos `PointOfInterest` de `PedestrianNetwork` siguen presentes y conectados después de `Awake()`, y al menos un peatón se para junto a un banco o fuente durante una sesión de Play de varios minutos.
- [x] `Tools > City Generator > Rebuild Pedestrian Network` (fuera de Play) también conserva los POI tras reconstruir la red.
- [x] Un vehículo o peatón de prueba con un `Collider` únicamente en un hijo (no en el root) es detectado por el sensor de otros vehículos/peatones (frenan ante él) y sigue bloqueando físicamente al `CharacterController` del jugador.
- [x] Con `includeTraffic` desactivado, `props.trafficLightPrefab` vacío y una rejilla con `gridWidth > 1 && gridHeight > 1`, el botón Build queda deshabilitado con el error correspondiente visible; con `gridWidth == 1` o `gridHeight == 1`, el mismo prefab vacío no bloquea la generación.
- [x] Ninguna escena que solo instale el package (sin generar ninguna ciudad) ve `vSyncCount`/`targetFrameRate` modificados al entrar en Play.
- [x] `Tools > City Generator > Set Current Selection As Default` funciona igual que antes desde su nueva ubicación en `Assets/Editor/`, y `CityGeneratorWindow.cs` ya no contiene ese `[MenuItem]`.
- [x] Dos ciudades generadas en la misma escena (o una cargada aditivamente) mueven su tráfico y peatones de forma independiente, sin que el `Awake` de una pise el manager de la otra.
- [x] Desactivar y reactivar (`SetActive`) un vehículo o peatón generado hace que retome su `Tick` sin necesitar recargar la escena.
- [x] En Play, mover al jugador, saltar, esprintar y orbitar la cámara funcionan igual que antes de introducir `PlayerInputAuthority`; desactivar `PlayerController` a mano no corta el input de `ThirdPersonCamera` (ni viceversa), y salir de Play/desactivar la cámara restaura correctamente cursor y visibilidad.
- [x] Cada uno de los 7 huecos de validación del ítem 10 se puede forzar individualmente y produce el error o warning esperado, marcando la card/tab correcta; con todos los campos válidos, la generación no muestra ningún falso positivo.
- [x] El proyecto compila sin warnings nuevos y una generación completa (rejilla 5×5 con tráfico y peatones activados) sigue completándose sin excepciones tras aplicar todos los cambios de este spec.

## Decisiones tomadas y descartadas

- **Marcador de ciudad: componente vacío, no GUID.** Se descarta guardar un GUID por generación porque hoy no hay ningún caso de uso que necesite distinguir entre varias ciudades por identidad (solo por presencia/ausencia del marcador). Un campo así se puede añadir después sin romper compatibilidad si surge la necesidad.
- **Fallo de rebuild: destruir el temporal, no dejarlo para inspección.** Se descarta dejar visible el root fallido en la escena porque el error ya queda en consola y en el panel de resultado de la ventana; dejar basura a medio construir obligaría al usuario a limpiarla a mano cada vez, y el caso de depuración fina de un builder roto es responsabilidad de quien desarrolla el package, no del flujo normal de uso.
- **Colliders jerárquicos: proxy en el root, no propagar layer a los hijos.** Se descarta reasignar la layer de cada collider hijo del prefab del usuario porque cambiaría layers que el usuario pudiera estar usando con otro propósito (física propia, otros sistemas), y sería más difícil de revertir. El proxy es aditivo y no toca nada del prefab original.
- **Semáforos: condición basada en geometría de la rejilla, no en `includeTraffic` ni en `includePedestrians`.** Se mantiene el hallazgo ya documentado en SPEC 03 (cebra ⟺ semáforo, determinado por `gridWidth`/`gridHeight`, no por ningún toggle), en vez de acoplar la validación a un toggle que no determina si se construyen semáforos.
- **POI peatonales: descriptores serializados ligeros, no serializar las listas runtime del BFS.** Siguiendo la recomendación explícita del informe: los buffers de BFS son puramente derivados y recalculables; solo los POI son estado que el generador decide y que no puede recomputarse a partir de la geometría sola.
- **Framerate: eliminar sin sustituto, no convertir en opt-in.** Se descarta un componente opt-in porque añadiría superficie nueva (otro ajuste más en la ventana) para un beneficio que cualquier usuario puede configurar por sí mismo en su propio proyecto; el package deja de tener opinión sobre VSync/frame rate del proyecto consumidor.
- **Tooling interno: mover a `Assets/Editor` con `InternalsVisibleTo`, no exponer `settings`/`CityGeneratorWindow` como público.** Mantiene la convención ya documentada en `CLAUDE.md` de "todo `internal` salvo lo imprescindible" — el acceso queda limitado a este repo de desarrollo, no se convierte en API pública del package para terceros.
- **Managers: resolución por referencia (`network.Manager`) en vez de un registro central o `Instance` por escena.** Se descarta un `Instance` "por escena" (p. ej. un diccionario `Scene → TrafficManager`) por ser más maquinaria de la necesaria: el manager ya vive en el mismo `GameObject` que la red, que los agentes ya referencian.
- **Registro idempotente vía `OnEnable`/`HashSet`, no un flag `isRegistered` manual.** Un `HashSet` (o el chequeo de contención equivalente) resuelve la idempotencia sin añadir estado adicional a cada agente ni duplicar la lógica de "¿ya estoy registrado?" en dos sitios.
- **Autoridad de Input: componente propio (`PlayerInputAuthority`), no `UnityEngine.InputSystem.PlayerInput`.** Se descarta el componente estándar de Unity porque recablearía `PlayerController`/`ThirdPersonCamera` de referencias directas a eventos/callbacks, un cambio de superficie mayor que el problema que se está resolviendo (propiedad única del `Enable()`/`Disable()` del mapa).
- **Validación de ítem 10: los 7 huecos completos en este spec, no divididos.** Se descarta partirlos en "los que evitan NaN/crash" vs. "cosméticos" porque todos comparten el mismo mecanismo (`ValidateDetailed`) y el mismo patrón de condicionar por toggle; dividirlos habría sido trabajo redundante entre dos specs.
- **Rendimiento y tests quedan fuera de este spec.** Siguiendo el orden de trabajo recomendado por el propio informe técnico: corregir lo crítico y arquitectónico primero, medir después, optimizar con datos reales en un SPEC 05 posterior — evita optimizar a ciegas ítems como el índice espacial o el coste físico del tráfico sin una suite de benchmarks que confirme la mejora.
- **Contenido demo y documentación quedan fuera de este spec.** Son cambios de otra naturaleza (importadores FBX, LOD, chunking de geometría, texto de documentación) sin dependencias con los cambios de arquitectura de este spec; se abordarán en specs futuros aún sin numerar.

No se identifican riesgos adicionales que requieran una sección propia más allá de lo ya cubierto en Scope/Decisiones (p. ej. el carácter *breaking* de retirar `PerformanceBootstrap`, ya anotado ahí).
