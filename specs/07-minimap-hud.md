# SPEC 07 — Minimap HUD

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 04 (Correcciones críticas y arquitectónicas), SPEC 06 (Custom Places)
> **Fecha:** 2026-08-27
> **Objetivo:** Añadir un HUD de minimapa circular, opcional (activado por defecto), que muestra en runtime una vista 2D estática de la ciudad generada centrada en el player en tiempo real, con los Custom Places marcados como Point of Interest señalados por nombre y marca, a partir de una snapshot ortográfica top-down capturada una sola vez durante la generación.

## Scope

**Dentro:**

- **Nueva tab "Minimap"** en `CityGeneratorWindow` (`CityGeneratorTabBar` gana una quinta pestaña, junto a City/Player/Pedestrians/Custom Places), con una card de ajustes: toggle `enabled` (activado por defecto), resolución de la textura snapshot, y tamaño en metros de la ventana de zoom del HUD (radio visible alrededor del player).
- **`Editor/CityGeneratorSettings.cs`** — nueva `struct MinimapSettings { enabled, textureResolution, viewRadiusMeters }` y `MinimapSettings minimap` en `CityGeneratorSettings`. Ver detalle en "Modelo de datos".
- **`Editor/CityGeneratorMinimapBuilder.cs`** (nuevo) — cuando `minimap.enabled`:
  - Calcula el bounding box del grid generado (`gridWidth`/`gridHeight` × pitch de manzana de `CityGeneratorConstants`, con margen).
  - Coloca una cámara ortográfica temporal top-down encuadrando ese bounding box, con `cullingMask` excluyendo las capas `Vehicle` y `Pedestrian` (creadas por `CityGeneratorTrafficBuilder`/`CityGeneratorPedestrianBuilder` si `includeTraffic`/peatones están activos; si aún no existen en el momento del snapshot, no hay nada que excluir).
  - Renderiza a una `RenderTexture` de `minimap.textureResolution`×`minimap.textureResolution`, la copia a un `Texture2D` y la guarda como asset PNG junto a la escena generada (p. ej. `Assets/Scenes/City1_Minimap.png`), reemplazando el asset existente en el mismo path en un Re-Build.
  - Destruye la cámara temporal al terminar (no queda ninguna cámara auxiliar en la escena generada).
  - Rellena un `Runtime/MinimapData.cs` (`[DisallowMultipleComponent]`, junto a `CityGeneratorRoot` en la raíz de la ciudad) con la referencia a la textura, el bounding box mundial cubierto (origen + tamaño en metros) y la lista de `(title, worldPosition)` de cada Custom Place con `isPointOfInterest == true`.
- **Pipeline**: `CityGeneratorMinimapBuilder` corre **después** de `CityGeneratorCustomPlaceBuilder` (necesita las posiciones finales de los Custom Places) y después de que toda la geometría estática relevante ya esté colocada — al final de `CityGeneratorContentAssembler.Assemble`, tras `TrafficBuilder` (para poder excluir sus capas del snapshot aunque el snapshot en sí sea solo de geometría estática).
- **`Runtime/MinimapHUD.cs`** (nuevo `MonoBehaviour`) — construido en runtime (UGUI: `Canvas` Screen Space Overlay + `RawImage`) en la esquina superior izquierda, forma circular (máscara vía sprite/shader simple), tamaño de HUD fijo en píxeles de pantalla. Cada frame: lee la posición XZ del player, calcula el UV correspondiente sobre la textura de `MinimapData` según `viewRadiusMeters`, y desplaza el `RawImage` (o sus UVs) para mantener al player centrado — el mapa **no rota** (norte fijo arriba); un marcador de player (sprite con flecha) sí rota según la orientación (yaw) del player. Los POI de `MinimapData` dentro del radio visible se dibujan como marcadores (un único sprite de pin, reutilizado para todos) con su `title` como etiqueta de texto, reposicionados cada frame igual que el marcador del player.
- **`Editor/CityGeneratorSceneBuilder`** — cuando `minimap.enabled`, instancia el `MinimapHUD` (prefab del propio paquete en `DefaultAssets/`) en la escena generada, igual que ya hace con cámara/player.
- **Assets nuevos en `DefaultAssets/`**: sprite de marcador de player (flecha), sprite de marcador de POI (pin), prefab base de `MinimapHUD` (Canvas + RawImage + máscara circular + marcadores), todo dentro del paquete para mantener portabilidad.
- **`Editor/CityGeneratorValidator.cs`** — validación no bloqueante (`HelpBox`) si `minimap.textureResolution` es excesiva (coste de memoria) o `viewRadiusMeters` es mayor que el bounding box cubierto (el zoom nunca podría alejarse más que el propio snapshot).
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- Representar en el minimapa elementos dinámicos (peatones, vehículos, o cualquier cosa en movimiento): el minimapa es una foto estática de la ciudad, tomada una vez en generación.
- Iconos de POI personalizables por entrada (campo `Sprite` en `CustomPlaceEntry`): esta spec usa un único icono genérico de pin para todo POI.
- Rediseño visual del HUD más allá de "circular, esquina superior izquierda, marcador de player centrado, marca+nombre de POI": color, borde, animaciones de aparición, estilo del texto se iterarán en una spec de UI posterior.
- Rotación del mapa según la orientación del player (estilo "arriba = donde miro"): el mapa mantiene el norte fijo arriba.
- Interacción del usuario con el minimapa (click para fast-travel, zoom manual con scroll, arrastrar, abrir un mapa completo a pantalla completa).
- Una capa "Minimap"/"MinimapIgnore" dedicada para excluir manualmente más categorías del snapshot (props, farolas, vegetación): solo se excluyen `Vehicle` y `Pedestrian`, que son las únicas capas dedicadas que el generador ya gestiona.
- Multi-piso / elevación: el snapshot es una proyección ortográfica pura desde arriba; geometría solapada en altura (p. ej. un puente sobre una calle) se aplana sin tratamiento especial.
- Publicación de una nueva versión del package (bump de `version`/tag): esta spec entrega el código; el release es un paso posterior con `Tools > City Generator > Release`.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public MinimapSettings minimap = MinimapSettings.Default();

[Serializable]
internal struct MinimapSettings
{
    [Tooltip("Whether a minimap HUD is generated and added to the scene. On by default.")]
    public bool enabled;
    [Tooltip("Resolution (width and height, in pixels) of the top-down snapshot texture. Higher values cost more texture memory and disk space for the generated PNG asset.")]
    public int textureResolution;
    [Tooltip("Radius, in meters, of the world area visible in the minimap HUD around the player. Must not exceed the snapshot's covered world size.")]
    public float viewRadiusMeters;

    public static MinimapSettings Default() => new MinimapSettings
    {
        enabled = true,
        textureResolution = 2048,
        viewRadiusMeters = 60f,
    };
}
```

```csharp
// Runtime/MinimapData.cs

namespace CityGenerator.Runtime
{
    [DisallowMultipleComponent]
    public class MinimapData : MonoBehaviour
    {
        [Tooltip("Top-down snapshot of the generated city, captured once during generation.")]
        public Texture2D snapshot;
        [Tooltip("World-space XZ origin (min corner) of the area covered by the snapshot.")]
        public Vector2 worldOrigin;
        [Tooltip("World-space size (width, depth) in meters of the area covered by the snapshot.")]
        public Vector2 worldSize;
        [Tooltip("Custom Places marked as Point of Interest: display title and world position.")]
        public List<PointOfInterestEntry> pointsOfInterest = new();
    }

    [Serializable]
    public struct PointOfInterestEntry
    {
        public string title;
        public Vector3 worldPosition;
    }
}
```

Notas:

- `MinimapData` sigue el mismo patrón que `CityGeneratorRoot`: un `MonoBehaviour` en `Runtime/` (no `Editor/`) para que exista también en player builds, añadido a la raíz de la ciudad generada. `CityGeneratorMinimapBuilder` lo puebla; `MinimapHUD` lo lee en `Awake`/`Start`.
- `PointOfInterestEntry` es un DTO mínimo runtime-friendly — el runtime no conoce `CustomPlaceEntry` (tipo `internal` de `Editor/`), así que `CityGeneratorMinimapBuilder` proyecta cada `CustomPlaceEntry` con `isPointOfInterest == true` a este struct al construir `MinimapData`.
- `worldOrigin`/`worldSize` son los mismos usados para encuadrar la cámara ortográfica del snapshot: `MinimapHUD` los usa para convertir la posición XZ del player a UV sobre `snapshot`, sin recalcular nada por su cuenta.
- `viewRadiusMeters` vive en `MinimapSettings` (editor-side, ajustable en la tab Minimap) pero no se duplica en `MinimapData`: `CityGeneratorSceneBuilder` lo escribe directamente en el campo serializado correspondiente del prefab `MinimapHUD` instanciado, mismo patrón que `CreateMainCamera` aplicando la Camera tab al `ThirdPersonCamera` generado.

## Plan de implementación

1. **Modelo de datos base.** Añadir `MinimapSettings` y el campo `minimap` a `CityGeneratorSettings.cs`, y `Runtime/MinimapData.cs` + `PointOfInterestEntry` a `Runtime/`. El proyecto compila; sin UI, builder ni HUD todavía, `minimap.enabled` no tiene ningún efecto observable.

2. **Assets del paquete.** Crear en `DefaultAssets/`: sprite de marcador de player (flecha), sprite de marcador de POI (pin), y el prefab base `MinimapHUD` (Canvas Screen Space Overlay + RawImage con máscara circular + marcador de player + contenedor de marcadores de POI, todo desactivado/sin lógica todavía — solo la jerarquía visual). Se construyen vía scripts editor one-off a través del MCP de Unity, no a mano en YAML.

3. **`Runtime/MinimapHUD.cs`.** Nuevo `MonoBehaviour` que en `Start` busca `MinimapData` en la escena (`FindAnyObjectByType`, mismo patrón de fallback que `CarAgent`), y en `LateUpdate`: calcula el UV de la posición XZ del player sobre `worldOrigin`/`worldSize`, desplaza el `RawImage` (o sus UVs) para centrar al player según `viewRadiusMeters`, rota el marcador de player según su yaw, y reposiciona/activa-desactiva los marcadores de POI que caen dentro del radio visible. Sin cablear todavía al pipeline de generación — se puede probar añadiendo el prefab a mano a una escena con un `MinimapData` rellenado manualmente.

4. **`Editor/CityGeneratorMinimapBuilder.cs`.** Nuevo builder: calcula el bounding box del grid (`gridWidth`/`gridHeight` × pitch de `CityGeneratorConstants` + margen), coloca una cámara ortográfica temporal top-down con `cullingMask` excluyendo `Vehicle`/`Pedestrian` si esas capas existen, renderiza a `RenderTexture` de `minimap.textureResolution`, copia a `Texture2D`, guarda como asset PNG junto a la escena (mismo path/GUID en Re-Build), destruye la cámara temporal, y rellena un `MinimapData` con la textura, el bounding box y los `PointOfInterestEntry` proyectados desde `customPlaces` donde `isPointOfInterest == true`. Se puede probar de forma aislada invocándolo sobre una escena ya generada con Custom Places.

5. **Cablear el pipeline.** `CityGeneratorContentAssembler.Assemble` llama a `CityGeneratorMinimapBuilder` al final (tras `TrafficBuilder`) cuando `minimap.enabled`, añadiendo `MinimapData` a la raíz de la ciudad (junto a `CityGeneratorRoot`). `CityGeneratorSceneBuilder` instancia el prefab `MinimapHUD` en la escena generada cuando `minimap.enabled`, escribiéndole `viewRadiusMeters` desde `minimap`, igual que ya hace con cámara/player. Generar una ciudad de principio a fin ya produce el HUD funcional con el snapshot real y los POI configurados.

6. **Validación.** `CityGeneratorValidator.ValidateDetailed` gana los dos `HelpBox` no bloqueantes: `textureResolution` por encima de un umbral razonable (coste de memoria/disco), y `viewRadiusMeters` mayor que el tamaño de mundo cubierto por el bounding box calculado.

7. **Tab "Minimap" en `CityGeneratorWindow`.** Nueva pestaña en `CityGeneratorTabBar`, card de ajustes con toggle `Enabled`, campo `Texture Resolution`, campo `View Radius (m)`, con los `HelpBox` de validación del paso 6. Badge de card y resaltado en rojo por validación, igual que el resto de cards.

8. **Valores por defecto y regresión.** `CityGeneratorDefaultAssets.ApplyTo` no necesita tocar `minimap` (los defaults de `MinimapSettings.Default()` ya sirven), pero si el demo requiere assets propios (prefab `MinimapHUD`, sprites) referenciarlos desde ahí igual que el resto de `DefaultAssets/`. Generar la escena de test (`Assets/Scenes/City.unity`) con el minimapa activo y al menos un Custom Place POI configurado, para verificar visualmente en Play Mode.

9. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`): añadir el Minimap HUD.

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con `minimap: MinimapSettings` tal como se definió, con `enabled = true` por defecto.
- [x] `Runtime/MinimapData.cs` existe en `Runtime/` (no `Editor/`), compila también en player builds, y expone `snapshot`, `worldOrigin`, `worldSize`, `pointsOfInterest`.
- [x] Generar una ciudad con `minimap.enabled = true` produce un asset PNG junto a la escena generada (p. ej. `Assets/Scenes/City1_Minimap.png`) con una vista ortográfica top-down de la ciudad, sin vehículos ni peatones visibles en la textura.
- [x] La raíz de la ciudad generada tiene un `MinimapData` con la textura, el bounding box correcto (coincide con el área realmente encuadrada por la cámara del snapshot) y un `PointOfInterestEntry` por cada Custom Place con `isPointOfInterest == true`, con el `title` y la posición mundial correctos.
- [x] Re-generar la misma escena (Re-Build City in Current Scene) sobrescribe el mismo asset PNG (mismo path/GUID), sin dejar assets huérfanos.
- [x] En Play Mode, el HUD aparece en la esquina superior izquierda, circular, con el marcador del player siempre centrado.
- [x] Al mover el player por la ciudad, la porción visible del minimapa se desplaza en tiempo real mostrando la zona alrededor del player, con el norte del mundo siempre arriba (el mapa no rota).
- [x] El marcador del player rota para reflejar su orientación (yaw) actual.
- [x] Cada Custom Place marcado como POI aparece en el minimapa, dentro del radio configurado (`viewRadiusMeters`), con el icono de pin genérico y su `title` como etiqueta, y deja de mostrarse cuando el player se aleja lo suficiente para que quede fuera del radio visible.
- [x] Con `minimap.enabled = false`, no se genera ningún asset PNG de minimapa, no se añade `MinimapData` a la ciudad, y no aparece ningún HUD en Play Mode — ninguna regresión en el resto de la generación.
- [x] La tab "Minimap" permite activar/desactivar, y editar `Texture Resolution`/`View Radius (m)`, con los `HelpBox` de validación no bloqueante activándose en los casos descritos (resolución excesiva, radio mayor que el mundo cubierto).
- [x] La suite EditMode/PlayMode/Performance de `Assets/Tests/` sigue pasando en su totalidad.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo el Minimap HUD.

## Decisiones tomadas y descartadas

- **Snapshot capturada una sola vez en el Editor durante la generación, no en runtime.** Descartado renderizar una cámara top-down oculta en el primer frame de Play: el usuario explícitamente no necesita que el minimapa refleje cambios en tiempo real (peatones/vehículos quedan fuera de scope), así que pagar ese coste en runtime — y mantener una cámara auxiliar permanente en la escena generada — no aporta nada frente a una textura estática generada una vez y guardada como asset.
- **Snapshot como asset PNG versionable junto a la escena, no en memoria ni regenerada en cada Play.** Coherente con el resto del pipeline (la ciudad generada es contenido guardado, no procedural en runtime) y evita pagar el coste de renderizado cada vez que el usuario entra en Play Mode durante iteración/testing.
- **UGUI (Canvas + RawImage) en vez de UI Toolkit runtime.** El editor de la tool usa UI Toolkit, pero es un contexto distinto (ventana de Editor vs. HUD de un player build). UGUI es el estándar más simple para HUDs de juego en este proyecto y evita introducir una dependencia de UI Toolkit runtime nueva solo para esta feature.
- **El mapa mantiene el norte fijo arriba; no rota con la orientación del player.** Más simple de implementar (solo desplazamiento de UV, sin rotar textura/máscara circular) y más legible para orientarse respecto al mundo. El marcador del player sí rota, que es lo que comunica "hacia dónde miro" sin sacrificar la lectura del norte.
- **La textura cubre todo el grid generado; el zoom es un recorte configurable (`viewRadiusMeters`) en runtime, no varias resoluciones o snapshots.** Evita renderizar y guardar múltiples texturas o depender de un tamaño de mundo fijo de antemano: una sola captura de alta resolución sirve para cualquier `viewRadiusMeters` razonable, y cambiar el radio de zoom no requiere volver a generar la ciudad.
- **Icono de POI único y genérico, no un campo `Sprite` por `CustomPlaceEntry`.** Mantiene esta spec acotada a la mecánica del HUD; personalizar el icono por entrada es una mejora de UX que puede añadirse después sin tocar el modelo de datos del minimapa (solo el de Custom Places).
- **Culling mask del snapshot excluye solo `Vehicle` y `Pedestrian`, sin una capa "Minimap"/"MinimapIgnore" nueva.** Son las únicas capas dedicadas que el generador ya gestiona por completo (creadas dinámicamente, layer asignado por instancia); introducir una capa adicional de propósito general replicaría la complejidad de `EnsureVehicleLayerExists` (gestión de slots de capa, fallback si no hay libres) para un caso de uso que nadie ha pedido todavía.
- **`MinimapData` es un DTO runtime propio (`PointOfInterestEntry`), no una referencia directa a `CustomPlaceEntry`.** `CustomPlaceEntry` es un tipo `internal` de `Editor/`, inaccesible (y semánticamente incorrecto) desde `Runtime/`; el builder proyecta los campos relevantes (`title`, posición mundial) al construir `MinimapData`, igual que el resto de builders traducen configuración de Editor a estado de escena.
- **Nueva tab dedicada "Minimap" en vez de una card dentro de "Player".** Aunque el HUD es parte de la experiencia del player, agrupar sus ajustes (activar/desactivar, resolución, radio de zoom) en su propia tab evita sobrecargar la tab Player (que ya tiene Player Prefab, Input Actions, Player y Camera) y deja la puerta abierta a que crezca (más ajustes visuales del HUD) sin reordenar tabs existentes.
- **Bounding box del snapshot calculado automáticamente del grid (`gridWidth`/`gridHeight` × pitch), no introducido como campo manual.** Evita que el usuario tenga que recalcular y mantener sincronizado un tamaño de mundo cada vez que cambia las dimensiones del grid — el mismo criterio que ya sigue `CityGeneratorPlayerSpawner`/overlap avoidance, que derivan geometría de los mismos constantes en vez de pedir valores redundantes.
- **`CityGeneratorMinimapBuilder` corre al final del pipeline (tras `TrafficBuilder`), no justo después de `CustomPlaceBuilder`.** Necesita conocer las capas `Vehicle`/`Pedestrian` para excluirlas del `cullingMask` — solo existen una vez que `TrafficBuilder`/`PedestrianBuilder` las han creado — aunque el propio snapshot solo capture geometría estática.

## Riesgos identificados

- **Sincronización entre `worldOrigin`/`worldSize` del snapshot y el UV calculado por `MinimapHUD`.** Un desajuste entre cómo `CityGeneratorMinimapBuilder` encuadra la cámara ortográfica y cómo `MinimapHUD` traduce la posición del player a UV haría que el marcador del player no coincida con su posición real en la textura (offset o escala incorrecta). Mitigación: ambos leen los mismos `worldOrigin`/`worldSize` de `MinimapData` como única fuente de verdad — nunca recalculados por separado — y el criterio de aceptación de "moverse por la ciudad" lo cubre visualmente.
- **Coste de memoria/disco de la textura snapshot en proyectos con grids grandes.** Una resolución alta (p. ej. 4096×4096) en un grid de 15×15 manzanas genera un asset PNG considerable y una `RenderTexture` temporal del mismo tamaño durante la generación. Mitigación: el `HelpBox` no bloqueante del paso 6 del plan avisa antes de generar, siguiendo el mismo patrón que el aviso de densidad de vehículos (`VehicleDensityWarningThreshold`).
- **La cámara ortográfica temporal del snapshot podría quedar huérfana en la escena si `CityGeneratorMinimapBuilder` lanza una excepción a mitad de renderizado.** Mitigación: la destrucción de la cámara temporal se envuelve en un `try/finally` (o equivalente), igual que el resto del pipeline es transaccional (`RebuildInActiveScene` ya asume que un builder puede fallar a mitad de camino).
- **Culling mask basado en capas `Vehicle`/`Pedestrian` que pueden no existir aún si el snapshot se capturara antes de que `TrafficBuilder`/`PedestrianBuilder` las creen.** Ya mitigado por la decisión de orden en el pipeline (builder al final), pero si en el futuro alguien reordena el pipeline sin darse cuenta de esta dependencia, el snapshot podría volver a incluir vehículos/peatones estáticos en el frame de la captura. Mitigación: el criterio de aceptación de "sin vehículos ni peatones visibles en la textura" lo detectaría en QA manual, y el paso 5 del plan fija explícitamente el orden.
- **Rendimiento de `MinimapHUD.LateUpdate` en el hot path del player.** El cálculo de UV, rotación del marcador y recorrido de `pointsOfInterest` para decidir visibilidad corren cada frame; con muchos POI (aunque hoy no hay límite declarado) podría acumular coste. Mitigación: el volumen esperado de Custom Places es bajo (decenas, no miles) dado que se colocan manualmente uno a uno vía la tab Custom Places, por lo que un recorrido lineal por frame no es un riesgo de performance real en este alcance — si en el futuro crece mucho, se puede indexar espacialmente igual que `CityGeneratorSpatialHash` hace en generación.
