# SPEC 13 — Free Camera

> **Estado:** Implementado
> **Depende de:** SPEC 04 (autoridad única de Input System, no-singleton managers), arquitectura del Player/Cámara existente (`PlayerInputAuthority`, `ThirdPersonCamera`, `PlayerSettings`/`CameraSettings`)
> **Fecha:** 2026-09-02
> **Objetivo:** Añadir al Player un modo "Free View", activable/desactivable con la tecla V, que sustituye el control del personaje y de su cámara en tercera persona por una cámara volante en primera persona (WASD/QE + Shift, con colisión básica contra los colliders de la escena y rotación suavizada), configurable desde una nueva card "Free Camera" en la tab Player de la tool.

## Scope

**Dentro:**

- **Nuevo Action Map "Free View"** en `DefaultAssets/Input/InputSystem_Actions.inputactions` (y en cualquier Input Actions asset que el usuario quiera usar con esta feature), con sus propias acciones reconfigurables: `Move` (Vector2, WASD compuesto), `Vertical` (1D, Q/E compuesto), `Sprint` (button, Left Shift), `Look` (Vector2, `<Pointer>/delta`), y `Toggle` (button, V). La acción `Toggle` se añade **también** dentro del Action Map `Player` existente (mismo nombre de acción, bind por defecto V), para poder activar Free View desde el modo de juego normal.
- **`Runtime/FreeCameraController.cs`** (nuevo) — componente añadido junto a `ThirdPersonCamera` en el mismo GameObject de la Main Camera generada. Es la única autoridad que llama `Enable()`/`Disable()` sobre el Action Map `Free View` (mismo patrón de autoridad única que `PlayerInputAuthority` ya aplica al Action Map `Player`, sin tocarlo). Lee (solo lectura, sin gestionar su ciclo de vida) la acción `Toggle` del map `Player` mientras el Player está activo, y la acción `Toggle` de su propio map `Free View` mientras este está activo, para saber cuándo conmutar.
- **Conmutación**: al activarse Free View, el GameObject `Player` se `SetActive(false)` (lo que además desactiva `PlayerInputAuthority` y, con ello, el map `Player`) y `ThirdPersonCamera` se deshabilita; `FreeCameraController` toma el control de la Main Camera desde su transform actual (sin salto de posición/rotación al entrar). Al desactivarse, se restaura todo: `Player` vuelve a `SetActive(true)`, `ThirdPersonCamera` se rehabilita y retoma el orbit alrededor del jugador (con el reencuadre esperado de `ThirdPersonCamera`, ya documentado: solo la posición de seguimiento se suaviza, la rotación se recalcula cada frame).
- **Movimiento**: WASD (horizontal, relativo a la orientación de la cámara) + Q/E (vertical, mundo) a `moveSpeed` m/s, multiplicado por `sprintMultiplier` mientras se mantiene Sprint — mismos tres ejes, mismo `moveSpeed`. El vector de movimiento resultante se suaviza con `Vector3.SmoothDamp` (inercia de aceleración/frenado), no se aplica instantáneo.
- **Rotación**: yaw/pitch a partir de `Look`, con suavizado propio (`rotationSmoothTime`) — a diferencia de `ThirdPersonCamera`, aquí sí se suaviza la rotación en sí (mirar hacia atrás tarda un poco en alcanzar el ángulo objetivo), no solo el seguimiento de posición.
- **Colisión**: antes de aplicar el desplazamiento de cada frame, un chequeo tipo `SphereCast`/`OverlapSphere` (radio y máscara fijos en código, mismos valores que ya usa `ThirdPersonCamera`: 0.3 m, todas las capas) impide atravesar colliders de la escena, permitiendo deslizarse por los ejes no bloqueados.
- **Cursor**: mismo comportamiento que `ThirdPersonCamera.lockCursor` — bloqueado y oculto mientras Free View está activo; se restaura al desactivarlo (o al salir de Play).
- **`Editor/CityGeneratorSettings.cs`** — nueva clase `FreeCameraSettings` (`moveSpeed`, `sprintMultiplier`, `rotationSmoothTime`, más los nombres de acción/action map configurables) con un campo `enabled` (default `true`), siguiendo el mismo patrón que `MinimapSettings.enabled`/`AudioSettings` (un booleano propio de su propia clase de settings, no un alias a `GeneralSettings`).
- **Nueva card "Free Camera"** en la tab Player de `CityGeneratorWindow`, con el checkbox "Enabled" y el set mínimo de campos (`moveSpeed`, `sprintMultiplier`, `rotationSmoothTime`).
- **`CityGeneratorSceneBuilder.CreateMainCamera`** — añade y configura `FreeCameraController` igual que ya hace con `ThirdPersonCamera`, aplicando `FreeCameraSettings` a la instancia (nunca al prefab), condicionado a `settings.freeCamera.enabled`.
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`), `docs/architecture/runtime-and-traffic.md` (sección "Player and camera") y `docs/architecture/editor-tool.md` (tab Player).

**Fuera de alcance (para futuras specs):**

- Si `general.playerEnabled` es `false`, `settings.freeCamera.enabled` se ignora en silencio (no se añade Free Camera, sin error de validación) — no se introduce ningún nuevo check bloqueante en `CityGeneratorValidator` para esta dependencia.
- Cualquier HUD/indicador visual en pantalla de que Free View está activo (crosshair, texto "Free View ON", etc.).
- Persistencia de la posición/rotación de la cámara libre entre sesiones de Play o entre activaciones — cada vez que se desactiva y reactiva dentro de la misma sesión de Play, arranca desde donde `ThirdPersonCamera` esté orbitando en ese momento, nunca desde la última posición de vuelo libre.
- Soporte de mando/gamepad para Free View más allá de lo que el propio Input Actions asset del usuario decida enlazar — no se diseñan bindings de gamepad específicos para `Vertical`/`Toggle` (el Action Map los deja disponibles para que el usuario los enlace si quiere, igual que el resto de acciones del proyecto).
- Cualquier límite espacial (no se impide volar fuera de los límites del grid/ciudad generada).
- Publicación de una nueva versión del package: esta spec entrega el código; el release es un paso posterior.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public FreeCameraSettings freeCamera = new();

// Applied to the generated Main Camera's FreeCameraController by
// CityGeneratorSceneBuilder.CreateMainCamera, alongside CameraSettings/ThirdPersonCamera.
[Serializable]
internal class FreeCameraSettings
{
    [Tooltip("Add the Free View free-flying camera to the generated Player. Ignored (no Free Camera added, no error) when Player is disabled.")]
    public bool enabled = true;

    [Header("Movement")]
    [Tooltip("Base flying speed on all three axes (WASD horizontal, Q/E vertical), in metres/second.")]
    public float moveSpeed = 8f;
    [Tooltip("Multiplier applied to moveSpeed while holding the Free View Sprint action.")]
    public float sprintMultiplier = 2.5f;

    [Header("Rotation")]
    [Tooltip("Smoothing time for yaw/pitch reaching the Look-driven target angle. Unlike ThirdPersonCamera, Free Camera smooths rotation itself, not just position tracking.")]
    public float rotationSmoothTime = 0.08f;

    [Header("Input Actions")]
    [Tooltip("Name of the Free View action map in Input Actions.")]
    public string actionMapName = "Free View";
    [Tooltip("Name of the Move (Vector2, WASD) action within the Free View action map.")]
    public string moveActionName = "Move";
    [Tooltip("Name of the Vertical (1D, Q/E) action within the Free View action map.")]
    public string verticalActionName = "Vertical";
    [Tooltip("Name of the Sprint (button) action within the Free View action map.")]
    public string sprintActionName = "Sprint";
    [Tooltip("Name of the Look (Vector2) action within the Free View action map.")]
    public string lookActionName = "Look";
    [Tooltip("Name of the Toggle (button) action, present both in this map and in the Player action map, used to enter/exit Free View.")]
    public string toggleActionName = "Toggle";
}
```

Notas:

- `collisionRadius`/`collisionMask` **no** son campos serializados: quedan fijos en código (`FreeCameraController`, mismos valores que `ThirdPersonCamera` ya usa hoy — 0.3 m, `~0`), por decisión explícita de mantener la card con el set mínimo.
- `FreeCameraController` no añade ningún tipo de dato nuevo más allá de esta clase de settings — su estado interno (yaw/pitch actual, velocidad suavizada) es runtime puro, no serializado, igual que `ThirdPersonCamera`.
- El Action Map `Player` existente gana una acción `Toggle` (button) adicional — esto es un cambio en `PlayerSettings`, no en `FreeCameraSettings`: se añade `public string toggleActionName = "Toggle";` junto al resto de nombres de acción de `PlayerSettings` (sección "Input Actions"), reutilizando el mismo `actionMapName` que ya tiene.

## Plan de implementación

1. **Modelo de datos base.** Añadir `FreeCameraSettings` y `CityGeneratorSettings.freeCamera` a `CityGeneratorSettings.cs`; añadir `toggleActionName` a `PlayerSettings`. El proyecto compila; sin runtime ni UI todavía, no cambia el comportamiento de generación.

2. **Action Map "Free View" en el asset de demo.** En `DefaultAssets/Input/InputSystem_Actions.inputactions`, vía el editor de Input Actions de Unity (no editando el JSON a mano — mismo criterio del proyecto de no escribir a mano assets estructurales), añadir el nuevo map `Free View` con `Move` (WASD compuesto), `Vertical` (Q/E compuesto 1D), `Sprint` (Left Shift), `Look` (`<Pointer>/delta`), `Toggle` (V); y añadir la acción `Toggle` (V) dentro del map `Player` existente. Manual test: abrir el asset en el editor de Input Actions y confirmar que ambos maps listan las acciones esperadas con sus bindings.

3. **`Runtime/FreeCameraController.cs`.** Nuevo componente: resuelve ambos Action Maps (`Player` para leer su `Toggle` de solo lectura, `Free View` como única autoridad de `Enable()`/`Disable()`), implementa el vuelo (movimiento suavizado por `SmoothDamp`, rotación suavizada yaw/pitch, colisión por `SphereCast`/`OverlapSphere` igual que `ThirdPersonCamera`), y expone lo necesario para que `CityGeneratorSceneBuilder` lo conecte con el GameObject `Player` (referencia a desactivar/reactivar) y con `ThirdPersonCamera` (referencia a deshabilitar/rehabilitar). Empieza deshabilitado (`enabled = false`) hasta el primer `Toggle`. Manual test: en una escena de prueba con Player + Main Camera montados a mano, entrar en Play, pulsar V, confirmar que el Player desaparece, la cámara vuela con WASD/QE/Shift sin salto de posición al entrar, colisiona con paredes, y pulsar V de nuevo restaura el Player y el orbit sin errores en consola.

4. **Cablear `CityGeneratorSceneBuilder.CreateMainCamera`.** Añadir `FreeCameraController` a la Main Camera generada cuando `settings.freeCamera.enabled` (y `settings.general.playerEnabled`) son true, aplicando `FreeCameraSettings` a la instancia, y pasarle las referencias a `player`/`ThirdPersonCamera` ya disponibles en ese método. Cuando `freeCamera.enabled` es false (o Player está deshabilitado), no se añade el componente — el toggle V simplemente no hace nada, sin necesitar ningún flag runtime adicional. Manual test: generar una ciudad de test con Free Camera Enabled, confirmar en Play que V activa/desactiva el modo; generar otra con Free Camera Enabled=false y confirmar que V no hace nada y no hay errores.

5. **Tab Player: card "Free Camera".** Nueva card en `CityGeneratorWindow` (tab Player, después de la card Camera existente) con el checkbox "Enabled" y los campos `moveSpeed`/`sprintMultiplier`/`rotationSmoothTime`, mismo patrón de binding que el resto de cards de esta tab. Manual test: abrir la tool, confirmar que la card aparece, los valores por defecto coinciden con `FreeCameraSettings`, y editarlos se refleja en el siguiente Build.

6. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`), `docs/architecture/runtime-and-traffic.md` ("Player and camera": añadir `FreeCameraController`, la relación de autoridad con `PlayerInputAuthority` sobre los dos Action Maps, y la conmutación Player/cámara) y `docs/architecture/editor-tool.md` (tab Player: nueva card).

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con `freeCamera: FreeCameraSettings` tal como se definió, y `PlayerSettings` gana `toggleActionName`.
- [x] `DefaultAssets/Input/InputSystem_Actions.inputactions` tiene el Action Map `Free View` (`Move`, `Vertical`, `Sprint`, `Look`, `Toggle`) y una acción `Toggle` (V) añadida al Action Map `Player` existente.
- [x] Generar una ciudad con Free Camera "Enabled" (default) y Player "Enabled": en Play, pulsar V hace desaparecer el Player y su cámara en tercera persona deja de orbitar, tomando el control una cámara en primera persona sin salto de posición/rotación en el instante de la conmutación.
- [x] En Free View, WASD mueve horizontalmente relativo a la orientación de la cámara, Q/E mueve verticalmente, y ambos a la misma `moveSpeed`, multiplicada por `sprintMultiplier` mientras se mantiene Sprint (Left Shift por defecto).
- [x] El ratón rota la cámara con suavizado perceptible (no instantáneo) tanto en yaw como en pitch.
- [x] La cámara no atraviesa colliders de la escena (edificios, suelo, mobiliario) mientras vuela; se detiene/desliza contra ellos en vez de atravesarlos.
- [x] Pulsar V de nuevo restaura el Player (reaparece exactamente donde quedó, congelado) y la cámara en tercera persona retoma el orbit.
- [x] Generar una ciudad con Free Camera "Enabled" pero Player "Enabled" desactivado: no se añade `FreeCameraController`, la tecla V no hace nada en Play, sin errores en consola.
- [x] Generar una ciudad con Free Camera "Enabled" = false: no se añade `FreeCameraController`, la tecla V no hace nada en Play, sin errores en consola.
- [x] La nueva card "Free Camera" (tab Player) muestra "Enabled" (true por defecto) y los campos `moveSpeed`/`sprintMultiplier`/`rotationSmoothTime`; editarlos cambia el comportamiento del siguiente Build/Re-Build.
- [x] Ninguna regresión en el comportamiento existente del Player/`ThirdPersonCamera`/`PlayerInputAuthority` cuando Free Camera está desactivada o Player está desactivado.
- [x] `docs/architecture/runtime-and-traffic.md` y `docs/architecture/editor-tool.md` documentan Free Camera.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo Free Camera.

## Decisiones tomadas y descartadas

- **Nuevo Action Map "Free View" reconfigurable, en vez de leer `Keyboard.current` directamente.** Decisión explícita del usuario: coherente con el patrón ya existente en el proyecto (Move/Sprint/Jump/Look como acciones nombradas y configurables desde la card), y deja a cualquier usuario del tool remapear las teclas si lo necesita, en vez de fijar WASD/QE/Shift/V en código.
- **La acción `Toggle` vive duplicada en ambos Action Maps (`Player` y `Free View`)**, en vez de en un tercer map "siempre activo". Evita crear una tercera autoridad de Input System solo para un botón, y encaja con que solo un map está habilitado a la vez: `PlayerInputAuthority` sigue siendo la única autoridad de `Player`, `FreeCameraController` la única de `Free View`, cada uno lee (sin gestionar el ciclo de vida) la copia de `Toggle` del otro map cuando ese map está habilitado.
- **Se reusa la misma Main Camera generada** (deshabilitando `ThirdPersonCamera` y habilitando `FreeCameraController` sobre el mismo GameObject) en vez de instanciar una cámara independiente. Evita gestionar un segundo `AudioListener`/`Camera.main` duplicado y mantiene "una sola Main Camera por ciudad generada" como hasta ahora.
- **El Player se `SetActive(false)` en vez de solo deshabilitar sus componentes uno a uno.** Más simple, y como efecto colateral deseado desactiva también `PlayerInputAuthority` (y con ello el map `Player`, incluida su copia de `Toggle`) sin lógica adicional — evita que ambos Toggles compitan mientras Free View está activo.
- **Sin dependencia bloqueante entre Free Camera y Player Enabled.** Si Free Camera está activada pero Player no, la feature simplemente no se añade, sin error de validación — decisión explícita del usuario, priorizando simplicidad sobre un nuevo check en `CityGeneratorValidator`.
- **Set mínimo de campos en la card** (`moveSpeed`, `sprintMultiplier`, `rotationSmoothTime`): `collisionRadius`/`collisionMask` quedan fijos en código, reutilizando los mismos valores que `ThirdPersonCamera` ya usa (0.3 m, todas las capas) — decisión explícita del usuario para no sobrecargar la card.
- **Velocidad única para los 3 ejes de movimiento (WASD + Q/E)**, sin campo `verticalSpeed` separado — decisión explícita del usuario, replicando el comportamiento de la cámara Scene del propio Editor de Unity.
- **Rotación suavizada de verdad (yaw/pitch interpolados), a diferencia de `ThirdPersonCamera`** (que solo suaviza el seguimiento de posición y recalcula la rotación cada frame). Decisión explícita del usuario ("sus rotaciones deben ser suaves"); es una divergencia intencional respecto a la invariante de `ThirdPersonCamera` — no la contradice, porque aplica a un componente distinto con un propósito distinto (vuelo libre vs. orbit siguiendo a un target).
- **Movimiento también suavizado (inercia con `SmoothDamp`), no solo instantáneo.** Decisión explícita del usuario, sobre la recomendación inicial de dejar el movimiento instantáneo tipo Scene view — prioriza sensación de vuelo más suave sobre fidelidad estricta al editor de Unity.
- **Sin límite espacial ni de altura**: se puede volar fuera del área generada sin restricción, coherente con no complicar el alcance de esta primera versión.
- **Sin persistencia de la posición de vuelo libre entre activaciones**: cada entrada a Free View arranca desde la posición/rotación actual de `ThirdPersonCamera` en ese momento, nunca recuerda dónde quedó la última vez que se voló libremente.

## Riesgos identificados

- **Repartir la autoridad de Input System entre dos componentes (`PlayerInputAuthority` sobre `Player`, `FreeCameraController` sobre `Free View`) toca la invariante "single Input System authority" de SPEC 04.** Un error aquí podría reintroducir el bug original (dos componentes peleando por el mismo map). Mitigación: cada componente sigue siendo dueño exclusivo de `Enable()`/`Disable()` de **su propio** map, nunca del otro — la lectura cruzada de `Toggle` es de solo lectura (`WasPressedThisFrame()`), nunca `Enable()`/`Disable()`.
- **`SetActive(false)` sobre el Player desactiva de golpe `PlayerInputAuthority`, `PlayerController`, `CharacterController` y cualquier otro componente que el prefab del usuario traiga.** Si algún prefab de Player tiene lógica propia en `OnDisable`/`OnEnable` que asuma un ciclo de vida distinto (p. ej. reiniciar estado en vez de pausarlo), podría comportarse de forma inesperada al reactivarse. Mitigación: mismo mecanismo que ya usa `PlayerInputAuthority` hoy (`OnEnable`/`OnDisable`), sin introducir un ciclo de vida nuevo; se documenta como comportamiento esperado.
- **Modificar `DefaultAssets/Input/InputSystem_Actions.inputactions` (un asset ya usado por la demo) para añadir un map y una acción nueva** podría, si se hace a mano en vez de con el editor de Input Actions, corromper el asset o dejar IDs de bindings inconsistentes. Mitigación: paso 2 del plan exige usarlo vía el editor de Unity, nunca edición manual del JSON.
- **El salto de reencuadre al salir de Free View** (la cámara vuela lejos, y al desactivar `ThirdPersonCamera` recalcula su rotación instantáneamente hacia el jugador) puede sentirse brusco si el usuario voló muy lejos. Mitigación: aceptado explícitamente como comportamiento esperado (ver Decisiones); si el manual QA lo pide, se puede mitigar en una spec futura (p. ej. clamping de distancia o un fade).
