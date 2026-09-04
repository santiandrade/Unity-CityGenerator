# SPEC 16 — Varias ciudades en una misma escena

> **Estado:** Implementado
> **Depende de:** SPEC 01 (la tool original), SPEC 04 (managers no-singleton, cuya regla esta spec extiende a las redes), SPEC 07 (Minimap HUD), SPEC 11 (Custom Grid), SPEC 15 (Runtime API por instancia, que dejó este trabajo explícitamente anotado como su spec siguiente)
> **Fecha:** 2026-09-04
> **Objetivo:** Permitir que varias ciudades generadas coexistan en una misma escena en posiciones distintas —construyendo los grafos de tráfico y peatones relativos al root de cada ciudad y acotando a esa jerarquía las búsquedas de semáforos— y añadir un `Tools > City Generator > Rebuild Minimap` que recaptura la snapshot cubriendo todas las ciudades presentes.

## Por qué existe esta spec

La SPEC 15 alineó la API con la regla que la SPEC 04 se había impuesto (varias ciudades pueden coexistir) y dejó anotado, en su Scope y en su sección de límites heredados, exactamente lo que faltaba:

> *"La generación produce coordenadas de mundo absolutas y no relativas al root (`TrafficNetwork.IntersectionPosition` construye un `Vector3` directo desde los ejes, sin `TransformPoint`), así que mover el `CityGeneratorRoot` mueve la geometría pero no el grafo. Esta spec deja la API preparada para varias ciudades; el resto del sistema todavía no lo está."*

Esta es esa spec. El usuario coloca las ciudades a mano (copiar y pegar de una escena a otra); la tool no gana ninguna forma de generar una segunda ciudad en la escena. Lo que gana es que, una vez colocadas, funcionen.

## Scope

**Dentro:**

- **`TrafficNetwork` construye sus nodos relativos al root, no en mundo absoluto.** `IntersectionPosition`, `EntryPosition`, `ExitPosition` pasan a componer la posición local (igual que hoy) y proyectarla con `transform.TransformPoint(...)`; `Dirs[]` (las cuatro direcciones cardinales) se transforman con `transform.TransformDirection(...)`. `StopLinePosition`, `IntersectionCentre` y todo el resto de la API pública siguen devolviendo mundo, ahora correcto para cualquier posición del root — pero solo la traslación está soportada (ver "Fuera de alcance").
- **`PedestrianNetwork` recibe el mismo tratamiento** en `BlockCentre`, `BlockCentreOutside` y en los puntos construidos directamente en `BuildBlockRing`/`BuildInteriorCross`/`BuildBorderWalkway` (todos los `new Vector3(c.x ± offset, c.y, c.z ± offset)`), pasando por `transform.TransformPoint`.
- **Scoping por jerarquía, no por búsqueda global.** `TrafficNetwork.AssignTrafficLights` y `PedestrianNetwork.Build` dejan de usar `FindObjectsByType<TrafficLight>`/`FindObjectsByType<TrafficLightIntersection>` y en su lugar usan `GetComponentsInChildren` desde la raíz de la ciudad (el `CityGeneratorRoot` que contiene a la propia red, resuelto vía `GetComponentInParent<CityGeneratorRoot>()`). Efecto inmediato: cada red solo ve los semáforos/intersecciones de su propia ciudad, sin tocar ninguna referencia serializada ni requerir regenerar ciudades ya existentes. Si no hay ningún `CityGeneratorRoot` ancestro, cae a la búsqueda global (`FindObjectsByType`), igual que antes de esta spec.
- **`MinimapData` gana la huella local de la ciudad** (`localCenter`, `localSize`, en el espacio del `cityRoot`), calculada por `CityGeneratorMinimapBuilder` a partir de las mismas fórmulas que hoy usa para `worldCenter`/`width`/`depth`. `worldOrigin`/`worldSize` se siguen guardando como hoy (mundo, en el momento de la captura) pero pasan a poder quedar desincronizados si el usuario mueve la ciudad después — exactamente el caso que el nuevo menú corrige.
- **`MinimapHUD.Start` prefiere la ciudad que contiene al player.** En vez de `FindAnyObjectByType<MinimapData>()`, recorre `FindObjectsByType<MinimapData>` y elige aquella cuyo `worldOrigin`/`worldSize` contiene la posición XZ del player; si ninguna lo contiene, la más cercana por distancia del centro. Con una sola ciudad el comportamiento no cambia.
- **Nuevo `Tools > City Generator > Rebuild Minimap`** (`CityGeneratorWindow`, junto al `Rebuild Pedestrian Network` ya existente):
  - Encuentra todos los `CityGeneratorRoot` de la escena activa.
  - Si no hay ninguno: diálogo informativo, no-op.
  - Si hay uno: recaptura su snapshot en su posición actual (arregla el caso "he movido la ciudad y el minimapa quedó desincronizado", usando `localCenter`/`localSize` + su transform actual — no hace falta regenerar la ciudad para esto), sin diálogo de confirmación.
  - Si hay dos o más: diálogo de confirmación mostrando cuántas ciudades encontró y el área total que va a cubrir, con la resolución de textura editable (por defecto la de la primera ciudad con `MinimapData.snapshot != null`, o 2048 si ninguna la tiene); tras confirmar, captura una única snapshot que cubre la unión de las huellas (mundo) de todas las ciudades presentes y escribe el mismo `snapshot`/`worldOrigin`/`worldSize`/`pointsOfInterest` fusionado (POIs de todas las ciudades, en sus posiciones de mundo) en el `MinimapData` de cada una.
  - La captura se hace en dos fases para no toparse con la limitación de `Camera.Render()` documentada en `CityGeneratorMinimapBuilder` (un cambio de estado no se refleja hasta una actualización de Editor posterior a la que lo hizo, cuando el objeto ya se ha renderizado antes — el caso de ciudades ya presentes en la escena, a diferencia de la generación): la fase 1 oculta `Vehicles`/`Pedestrians` de cada ciudad y cede el control con `EditorApplication.delayCall`; la fase 2, en la siguiente actualización, coloca la cámara temporal, captura y restaura la visibilidad en un `finally`.
  - El PNG resultante se guarda en la ruta ya usada por `SaveSnapshotAsset` (`<CarpetaEscena>/<NombreEscena>/<NombreEscena>_Minimap.png`), sobrescribiendo el existente.
- **`Rebuild City in Current Scene` con varias ciudades.** `RebuildInActiveScene` deja de localizar "el primer `CityGeneratorRoot`"; en su lugar recoge **todos** los `CityGeneratorRoot` de la escena. Con exactamente uno, comportamiento idéntico al actual. Con dos o más, un diálogo de confirmación indica cuántas ciudades va a destruir (nombre de cada root); si se confirma, las mueve todas fuera del paso de la nueva antes de generar (mismo mecanismo de aislamiento por desplazamiento que ya usa para la única ciudad previa, con un offset distinto por índice) y las destruye todas al terminar con éxito, dejando la escena con la única ciudad recién generada. Si se cancela, no se genera nada.
- **Tests automatizados** en `Assets/Tests/EditMode/Generation/` (o carpeta equivalente): dos `TrafficNetwork`/`PedestrianNetwork` sintéticos en raíces distintas, desplazadas y no rotadas, verificando que (a) las posiciones de nodos de cada red están en su root correspondiente, no en el origen ni mezcladas; (b) cada red solo empareja semáforos/intersecciones de su propia jerarquía, no las de la otra ciudad; (c) el fallback a búsqueda global sigue funcionando sin `CityGeneratorRoot` ancestro.
- **Documentación**: `docs/user-manual.md`/`.es.md` (cómo copiar una ciudad de una escena a otra, qué está soportado — traslación — y qué no; el nuevo menú Rebuild Minimap); `docs/architecture/runtime-and-traffic.md`/`pedestrians.md` (coordenadas relativas al root, scoping por jerarquía); `docs/architecture/editor-tool.md` (el nuevo menú, la fusión de MinimapData); `CLAUDE.md` (nuevos invariantes: las redes construyen sus nodos vía `TransformPoint`/`TransformDirection`, nunca en mundo absoluto; ninguna red busca semáforos/intersecciones globalmente, solo dentro de su propia jerarquía); `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- **Rotación y escala del root de una ciudad.** Solo traslación está soportada y verificada. Rotar o escalar un `CityGeneratorRoot` copiado a mano queda explícitamente documentado como no soportado — el grafo se desplazaría con `TransformPoint` pero las direcciones de los sensores/carriles, el minimapa (norte fijo) y las distancias en metros (radios de sensor, `laneOffset`, `viewRadiusMeters`) asumirían una escala/orientación 1:1 que dejaría de cumplirse.
- **Que la tool ofrezca ninguna forma de añadir una segunda ciudad a la escena** (duplicar, generar-junto-a-la-existente, un botón "Add City"). El usuario sigue copiando manualmente el GameObject de una escena a otra.
- **Ambiencia 2D con varias ciudades.** `CityGeneratorAudioBuilder` no se toca: con dos ciudades sonarán ambas ambiencias 2D superpuestas. Se documenta como limitación conocida; el usuario puede desactivar la ambiencia de todas menos una a mano.
- **Vehículos/peatones cruzando entre ciudades**, o cualquier interacción entre los dos grafos (tráfico o peatonal) de ciudades distintas. Cada red sigue siendo un grafo cerrado sobre su propia jerarquía; dos ciudades adyacentes no se conectan.
- **`CityGeneratorAPI`/`CityGeneratorCity` de la SPEC 15.** Ya soportan `All`/`InScene`/`For` sobre varias ciudades sin cambios; esta spec no toca `Runtime/API/`.
- **Detección/resolución automática de solapes entre ciudades** (dos ciudades colocadas demasiado cerca, geometría que se pisa). Queda en manos del usuario.
- **CI en batchmode.** Sigue siendo el otro P0 pendiente del informe técnico, ajeno a esta spec.

## Modelo de datos

### `MinimapData` — gana la huella local

```csharp
// Runtime/MinimapData.cs — campos añadidos sobre el componente existente

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class MinimapData : MonoBehaviour
{
    [Tooltip("Top-down snapshot of the generated city, captured once during generation, or the last time Rebuild Minimap ran.")]
    public Texture2D snapshot;

    [Tooltip("World-space XZ origin (min corner) of the area covered by the snapshot, at the time it was captured.")]
    public Vector2 worldOrigin;
    [Tooltip("World-space size (width, depth) in meters of the area covered by the snapshot, at the time it was captured.")]
    public Vector2 worldSize;

    [Tooltip("This city's own footprint centre, in the city root's local space -- invariant to moving the root. Set once by CityGeneratorMinimapBuilder and never touched afterwards.")]
    public Vector3 localCenter;
    [Tooltip("This city's own footprint size (width, depth), in local space -- invariant to moving the root.")]
    public Vector2 localSize;

    [Tooltip("Custom Places marked as Point of Interest: display title and world position.")]
    public List<PointOfInterestEntry> pointsOfInterest = new();
}
```

Notas:

- `worldOrigin`/`worldSize` **no cambian de significado**: siguen siendo el encuadre real de `snapshot`, en mundo, tal como lo dejó la última captura (generación, o el nuevo `Rebuild Minimap`). `MinimapHUD` los sigue leyendo sin ningún cambio propio. La diferencia es que ahora pueden quedar desincronizados del root si el usuario mueve la ciudad *después* de capturar — ya ocurría implícitamente antes de esta spec (el campo era estático desde generación), simplemente ahora hay una forma de corregirlo sin regenerar.
- `localCenter`/`localSize` son la fuente de verdad estable: `Rebuild Minimap` los lee para recalcular `worldOrigin`/`worldSize` = `cityRoot.TransformPoint(localCenter)` ± `localSize`/2 en la posición **actual** del root, sea cual sea. Una ciudad generada con una versión anterior a esta spec no tiene estos campos rellenados (quedan a `Vector3.zero`/`Vector2.zero`); `Rebuild Minimap` lo trata igual que "sin huella conocida" y usa `Renderer.bounds` de la ciudad como fallback documentado — evita que el menú falle en frío sobre una escena ya existente, a cambio de un encuadre menos ceñido para esas ciudades hasta que se regeneren.
- Ningún campo nuevo en `CityGeneratorSettings`/`MinimapSettings`: el nuevo menú no es una opción de generación, es una operación de escena.

### Sin cambios de tipo en `TrafficNetwork`/`PedestrianNetwork`

Esta spec no añade ni quita campos serializados a ninguna de las dos redes — el cambio es puramente en cómo `IntersectionPosition`/`EntryPosition`/`ExitPosition` (`TrafficNetwork`) y `BlockCentre`/`BlockCentreOutside`/los puntos de `BuildBlockRing`/`BuildInteriorCross`/`BuildBorderWalkway` (`PedestrianNetwork`) proyectan sus coordenadas locales a mundo, y en que `AssignTrafficLights`/`Build` buscan semáforos/intersecciones por jerarquía en vez de con `FindObjectsByType`. Ninguna migración de datos: una ciudad ya generada sigue funcionando en cuanto se reconstruye su grafo (`Awake`, o `Rebuild Pedestrian Network`), porque el grafo nunca se serializa, solo los ejes/flags de entrada que ya existían.

### Nuevo diálogo del menú — sin persistencia

El diálogo de confirmación de `Rebuild Minimap` (recuento de ciudades, área cubierta, resolución editable) y el de `Rebuild City in Current Scene` (recuento de ciudades a destruir) son `EditorUtility.DisplayDialog`, sin ningún dato nuevo que persista en `CityGeneratorSettings` ni en ningún asset.

## Plan de implementación

Cada paso deja el proyecto compilando y es comprobable por sí solo.

1. **`TrafficNetwork` a coordenadas relativas al root.** `IntersectionPosition`, `EntryPosition`, `ExitPosition` pasan por `transform.TransformPoint(...)`; `Dirs[]` se usa para construir direcciones locales y se transforma con `transform.TransformDirection(...)` allí donde se usa como vector de mundo (`RightOfDir`, el filtro de `AssignTrafficLights`, `FindNodeAhead`). `StopLinePosition`/`IntersectionCentre` no cambian de firma, solo el resultado ahora es correcto para un root desplazado. Test manual: con el `City` root de la escena de test movido a `(200, 0, 0)`, entrar en Play y comprobar en el Inspector (`drawGraph`) que los gizmos de `TrafficNetwork` aparecen en la posición desplazada y que los vehículos siguen circulando con normalidad.

2. **`PedestrianNetwork` a coordenadas relativas al root**, mismo tratamiento sobre `BlockCentre`, `BlockCentreOutside`, y los `new Vector3(...)` de `BuildBlockRing`/`BuildInteriorCross`/`BuildBorderWalkway`. Test manual: mismo escenario del paso 1; los peatones caminan sobre las aceras desplazadas, no sobre las del origen.

3. **Scoping por jerarquía en `AssignTrafficLights` y `PedestrianNetwork.Build`.** Sustituir `FindObjectsByType<TrafficLight>`/`FindObjectsByType<TrafficLightIntersection>` por `GetComponentInParent<CityGeneratorRoot>()` (desde el propio `TrafficNetwork`/`PedestrianNetwork`) seguido de `GetComponentsInChildren<TrafficLight>(true)`/`GetComponentsInChildren<TrafficLightIntersection>(true)` sobre esa raíz. Si no hay ningún `CityGeneratorRoot` ancestro, cae a `FindObjectsByType` como hoy. Test manual: duplicar el `City` root de la escena de test (root2 en otra posición, con su propio `TrafficNetwork`/`PedestrianNetwork`/semáforos), entrar en Play y comprobar que los coches/peatones de cada ciudad respetan solo los semáforos de su propia ciudad.

4. **`MinimapData.localCenter`/`localSize`.** `CityGeneratorMinimapBuilder.BuildCore` calcula y guarda estos dos campos junto a los existentes, a partir de los mismos `width`/`depth`/`worldCenter` que ya calcula (`localCenter = cityRoot.InverseTransformPoint(worldCenter)`, `localSize = (width, depth)`). Sin efecto observable todavía. Test manual: generar una ciudad y comprobar en el Inspector de `MinimapData` que `localCenter`/`localSize` tienen valores no nulos coherentes con el grid.

5. **`MinimapHUD` prefiere la ciudad del player.** `Start` pasa de `FindAnyObjectByType<MinimapData>()` a recorrer `FindObjectsByType<MinimapData>` y elegir la que contiene al player (XZ dentro de `worldOrigin`/`worldOrigin+worldSize`) o, si ninguna, la de centro más cercano. Test manual: escena con dos ciudades y minimapa habilitado en ambas, el player spawneado en una de ellas; en Play, el HUD muestra el mapa de la ciudad donde está el player.

6. **`Tools > City Generator > Rebuild Minimap`.** Nuevo `MenuItem` en `CityGeneratorWindow`, junto a `RebuildPedestrianNetworkMenuItem`: recoge todos los `CityGeneratorRoot` de la escena activa; cero → diálogo informativo, no-op; uno o más → diálogo de confirmación con recuento, área total cubierta y resolución editable; al confirmar, lanza la captura diferida en dos fases (paso 7). Test manual: escena de test con minimapa habilitado, ejecutar el menú con una sola ciudad y comprobar que recaptura sin diálogo de "varias ciudades"; duplicar la ciudad, moverla, ejecutar de nuevo y comprobar el diálogo de confirmación y que tras aceptar ambos `MinimapData` apuntan al mismo PNG con ambas ciudades visibles y sus POIs.

7. **Captura diferida en dos fases.** Nuevo método en `CityGeneratorMinimapBuilder` (p. ej. `RebuildCombinedSnapshot`): fase 1 (síncrona) desactiva `Vehicles`/`Pedestrians` de cada ciudad guardando su estado previo y programa la fase 2 con `EditorApplication.delayCall`; fase 2 coloca la cámara ortográfica temporal encuadrando la unión de huellas, captura, restaura la visibilidad en un `finally`, guarda el PNG con la misma convención de ruta que `SaveSnapshotAsset`, y escribe `snapshot`/`worldOrigin`/`worldSize`/`pointsOfInterest` en el `MinimapData` de cada ciudad encontrada. Reutiliza `CaptureSnapshot` sin cambios. Test manual (en modo Edit, sin Play): el PNG resultante no contiene ningún vehículo/peatón aunque sus GameObjects existan en la escena.

8. **`RebuildInActiveScene` con varias ciudades.** Sustituir la búsqueda de "el primer root" por una lista completa de `CityGeneratorRoot` en la escena activa. Con 0 o 1, comportamiento idéntico al actual. Con 2+, diálogo de confirmación listando los nombres; si se cancela, no genera nada; si se confirma, todas se mueven fuera de paso y se destruyen al terminar con éxito. Test manual: escena con dos ciudades, `Re-Build City in Current Scene`; confirmar y comprobar que la escena queda con una única ciudad nueva; repetir cancelando y comprobar que ambas ciudades previas siguen intactas.

9. **Tests automatizados.** Nuevo `Assets/Tests/EditMode/Generation/MultiCityNetworkTests.cs`: dos `CityGeneratorRoot` sintéticos con sus propios `TrafficNetwork`/`PedestrianNetwork`/`TrafficLight`/`TrafficLightIntersection` hijos, uno en el origen y otro desplazado. Casos: nodos de cada red en las posiciones esperadas; `AssignTrafficLights` de la red A no empareja `TrafficLight` de B y viceversa; `PedestrianNetwork.Build` de A no encuentra `TrafficLightIntersection` de B; una red sin `CityGeneratorRoot` ancestro sigue funcionando (fallback a búsqueda global).

10. **Documentación.** `docs/user-manual.md`/`.es.md`: cómo copiar una ciudad entre escenas, qué se soporta y el nuevo menú. `docs/architecture/runtime-and-traffic.md`/`pedestrians.md`: coordenadas relativas al root y scoping por jerarquía. `docs/architecture/editor-tool.md`: el nuevo menú y la fusión de `MinimapData`. `CLAUDE.md`: los dos invariantes nuevos. `CHANGELOG.md`: entrada en `## [Unreleased]`.

## Criterios de aceptación

**Coordenadas relativas al root**

- [ ] Con el `City` root movido a una posición distinta de `(0,0,0)`, los nodos de `TrafficNetwork` (gizmos y posiciones reales de spawn/circulación) aparecen en esa posición desplazada, no en el origen.
- [ ] Con el mismo desplazamiento, los nodos de `PedestrianNetwork` aparecen igualmente desplazados y los peatones caminan sobre ellos con normalidad.
- [ ] Una ciudad generada sin mover su root se comporta exactamente igual que antes de esta spec.
- [ ] `TrafficNetwork.StopLinePosition`, `IntersectionCentre` y el resto de la API pública siguen devolviendo coordenadas de mundo correctas tras el cambio.

**Scoping por jerarquía**

- [ ] Con dos ciudades en la misma escena, cada una en su propia posición, con sus propios semáforos, cada `TrafficNetwork` solo empareja los `TrafficLight` bajo su propio `CityGeneratorRoot`.
- [ ] En el mismo escenario, `PedestrianNetwork.Build` de cada ciudad solo encuentra las `TrafficLightIntersection` de su propia jerarquía.
- [ ] Una `TrafficNetwork`/`PedestrianNetwork` instanciada sin ningún `CityGeneratorRoot` ancestro sigue construyendo su grafo correctamente, cayendo a la búsqueda global.
- [ ] Ambas ciudades, generadas de forma independiente en distintas escenas y luego copiadas a mano a la misma, funcionan igual de bien que si se hubieran generado juntas.

**Minimap HUD**

- [ ] Con dos ciudades con minimapa habilitado, en Play, `MinimapHUD` muestra el mapa de la ciudad cuyo `worldOrigin`/`worldSize` contiene al player.
- [ ] Con una sola ciudad, el comportamiento del HUD es idéntico al de antes de esta spec.
- [ ] `MinimapData.localCenter`/`localSize` quedan rellenados tras generar una ciudad con minimapa habilitado, con valores coherentes con el tamaño real del grid.

**Menú Rebuild Minimap**

- [ ] Con cero ciudades, el menú muestra un diálogo informativo y no modifica nada.
- [ ] Con una ciudad cuya posición ha cambiado desde su generación, el menú recaptura la snapshot en la posición actual y actualiza `worldOrigin`/`worldSize` sin pedir confirmación.
- [ ] Con dos o más ciudades, el menú muestra un diálogo de confirmación con recuento, área cubierta y resolución editable.
- [ ] Tras confirmar con dos o más ciudades, el PNG cubre la unión de las huellas, sin vehículos ni peatones visibles, y el `MinimapData` de cada ciudad involucrada apunta al mismo `snapshot`/`worldOrigin`/`worldSize`, con los POIs de todas fusionados en posiciones de mundo correctas.
- [ ] El PNG se guarda en `<CarpetaEscena>/<NombreEscena>/<NombreEscena>_Minimap.png`, sobrescribiendo el existente sin dejar assets huérfanos.
- [ ] Una ciudad generada con una versión anterior a esta spec no rompe el menú: su huella se calcula por el fallback de `Renderer.bounds`.

**Rebuild City in Current Scene**

- [ ] Con una sola ciudad (o ninguna), el comportamiento es idéntico al actual, sin ningún diálogo nuevo.
- [ ] Con dos o más ciudades, aparece un diálogo de confirmación listando los nombres de las ciudades a destruir.
- [ ] Al confirmar, la escena queda con una única ciudad: la recién generada; todas las anteriores han sido destruidas de forma transaccional.
- [ ] Al cancelar, no se genera nada y todas las ciudades previas quedan exactamente como estaban.

**Tests**

- [ ] `Assets/Tests/EditMode/Generation/MultiCityNetworkTests.cs` (o ruta equivalente) existe y cubre: nodos de cada red en la posición esperada de su propio root; scoping sin fugas entre jerarquías; el fallback a búsqueda global.
- [ ] La suite completa (EditMode, PlayMode y Performance) de `Assets/Tests/` sigue pasando en su totalidad.

**Documentación**

- [ ] `docs/user-manual.md`/`.es.md` documentan cómo copiar una ciudad entre escenas, qué transformaciones están soportadas, y el menú Rebuild Minimap.
- [ ] `docs/architecture/runtime-and-traffic.md` y `pedestrians.md` documentan las coordenadas relativas al root y el scoping por jerarquía.
- [ ] `docs/architecture/editor-tool.md` documenta el nuevo menú y la fusión de `MinimapData`.
- [ ] `CLAUDE.md` tiene los dos invariantes nuevos añadidos a la lista de invariantes del proyecto.
- [ ] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo el soporte multi-ciudad y el nuevo menú.

## Decisiones tomadas y descartadas

**Transformaciones soportadas**

- **Solo traslación del root, no rotación ni escala.** Decisión explícita del usuario. Cubre el caso pedido (varias ciudades en distintas posiciones) al coste mínimo: `TransformPoint`/`TransformDirection` ya son correctos bajo rotación también, pero verificarlo (matching de semáforos por producto escalar, orientación de agentes, el norte fijo del minimap, todas las distancias en metros del sistema) es trabajo no pedido y de riesgo alto. Rotación/escala quedan documentadas como no soportadas en vez de silenciosamente rotas.
- **Descartado: soporte completo de transform (rotación + escala) en esta misma spec.** Habría exigido revisar el matching de semáforos por `Vector3.Dot`, la orientación del marcador de player y el norte fijo del `MinimapHUD`, y todas las distancias en metros (`laneOffset`, `stopLineBack`, `pruneCheckRadius`, `viewRadiusMeters`) dejarían de tener sentido bajo escala no uniforme. Se deja como ampliación futura si llega a pedirse.

**Scoping de semáforos/intersecciones**

- **Por jerarquía (`GetComponentInParent<CityGeneratorRoot>` + `GetComponentsInChildren`), no por referencias serializadas en generación.** Decisión explícita del usuario. Funciona también sobre ciudades ya generadas con una versión anterior a esta spec sin necesidad de regenerarlas, y sobrevive a que el usuario mueva la ciudad — una lista serializada en generación se queda igual de correcta cuando se mueve el root, pero no cubre el caso de ciudades preexistentes.
- **Fallback a búsqueda global cuando no hay ningún `CityGeneratorRoot` ancestro.** Necesario para no romper el uso ya existente de `TrafficNetwork`/`PedestrianNetwork` fuera del pipeline completo de generación (tests sintéticos de la SPEC 15, o cualquier escena que instancie la red suelta). Es una degradación segura: sin una jerarquía de ciudad reconocible, "buscar en toda la escena" es exactamente el comportamiento que había antes de esta spec.

**Minimap: huella y menú**

- **`localCenter`/`localSize` como fuente estable, separada de `worldOrigin`/`worldSize`.** Decisión explícita del usuario. Sin ellos, recalcular la unión de huellas tras mover una ciudad requeriría rehacer las fórmulas de `CityGeneratorMinimapBuilder` fuera del builder (duplicación) o fiarse de `Renderer.bounds` (encuadre distinto al de generación, incluye cualquier cosa añadida a mano). Guardarlos en `MinimapData` mantiene una única fuente de verdad para la huella propia de la ciudad, independiente de dónde esté su root ahora mismo.
- **Fallback a `Renderer.bounds` solo para ciudades sin `localCenter`/`localSize`** (generadas antes de esta spec). Evita que `Rebuild Minimap` falle en frío sobre una escena existente; el coste (encuadre menos ceñido, incluye contenido añadido a mano) se acepta porque desaparece en cuanto la ciudad se regenera una vez.
- **Diálogo de confirmación con resolución editable**, en vez de heredar silenciosamente la resolución de la primera ciudad o pedirla de los `settings` de la ventana. Decisión explícita del usuario: da control en el caso — previsible — de que la unión de varias ciudades cubra un área bastante mayor que una sola, donde la resolución "de generación" podría quedarse corta.
- **El menú aplica igual con una sola ciudad**, recapturando en su posición actual sin diálogo de confirmación. Decisión explícita del usuario: arregla directamente el bug que esta spec introduce (mover una ciudad desincroniza su `worldOrigin`/`worldSize`) sin forzar al usuario a distinguir "tengo una" de "tengo varias".
- **Captura diferida en dos fases (`EditorApplication.delayCall`), no captura síncrona con aislamiento por desplazamiento.** Decisión explícita del usuario, forzada por la limitación ya documentada en `CityGeneratorMinimapBuilder`: un `Camera.Render()` manual no refleja un cambio de estado (ocultar, mover) hecho en la misma llamada de script sobre un objeto ya renderizado antes — y las ciudades que el menú captura, a diferencia de la generación, llevan ya un tiempo en la escena y han sido renderizadas por el Editor. Ocultar en una actualización y capturar en la siguiente reproduce la misma garantía que el mecanismo de generación consigue con objetos recién creados.
- **PNG unificado en la misma ruta que el de generación** (`<NombreEscena>_Minimap.png`), no un asset aparte. Decisión explícita del usuario: una ciudad copiada de otra escena debe dejar de referenciar el PNG de su escena de origen — mantener dos assets (uno "de generación" por ciudad, otro "combinado" de escena) obligaría además a una regla de precedencia en `MinimapHUD` que el usuario no pidió.

**Re-Build con varias ciudades**

- **Limpia todas y deja la escena con una sola ciudad**, en vez de preguntar cuál reemplazar o usar la selección de la Hierarchy. Decisión explícita del usuario. Evita introducir una noción de "ciudad activa/seleccionada" que hoy no existe en ninguna parte de la tool, a cambio de que el usuario deba volver a copiar a mano cualquier otra ciudad que quisiera conservar tras un Re-Build.
- **Confirmación solo cuando hay dos o más.** Decisión explícita del usuario: con una sola ciudad (el caso de siempre) el flujo no cambia ni una línea de UX; el diálogo solo aparece cuando la operación es genuinamente más destructiva que antes.

**Verificación**

- **Tests automatizados de las redes, en vez de solo QA manual.** Decisión explícita del usuario. El scoping por jerarquía y el cambio a coordenadas relativas son exactamente el tipo de lógica que una regresión silenciosa rompe sin ningún síntoma visual inmediato (dos ciudades muy separadas seguirían "funcionando" aunque sus semáforos se emparejaran mal, hasta que alguien las acercara) — el mismo criterio que llevó a la SPEC 15 a testear su ciclo de vida en vez de fiarse de QA manual.
- **Tests sintéticos (roots + redes construidos a mano), no ciudades generadas por el pipeline completo.** Mismo criterio que la SPEC 15: lo que se prueba es el scoping y las coordenadas, no la generación, y el pipeline completo es Editor-only y lento para instanciar dos veces en un mismo test.
- **Sin escena de test dedicada con dos ciudades.** Descartado explícitamente frente a la opción de añadir `Assets/Scenes/TwoCities.unity`: los tests automatizados ya cubren la lógica con precisión, y una segunda escena de test permanente sube el peso del repo por un escenario que QA manual puntual ya cubre sin dejar rastro en el repositorio.

## Riesgos identificados

- **Cambiar `TrafficNetwork`/`PedestrianNetwork` a coordenadas relativas al root es la parte de mayor riesgo de regresión silenciosa de toda la spec.** Ambas clases construyen decenas de posiciones a mano y un solo punto olvidado sin `TransformPoint` dejaría ese subconjunto de nodos en mundo absoluto mientras el resto se mueve con el root — un bug que solo se manifiesta al mover una ciudad, no con el caso por defecto en `(0,0,0)` que cubre casi todo el QA existente. Mitigación: los tests del paso 9 verifican explícitamente posiciones con el root desplazado; el criterio de aceptación "una ciudad sin mover se comporta igual que antes" acota el blast radius si algo se escapa.
- **`GetComponentInParent<CityGeneratorRoot>` asume que `TrafficNetwork`/`PedestrianNetwork` siempre cuelgan, directa o indirectamente, del `CityGeneratorRoot` de su propia ciudad.** Es cierto en el pipeline de generación actual pero un reordenamiento futuro de la jerarquía, o un usuario que reparent-ee manualmente la red fuera de su ciudad, rompería el scoping sin ningún error visible. Mitigación: documentado como invariante en `CLAUDE.md`; el fallback a búsqueda global es la degradación más segura disponible sin más información.
- **La captura diferida en dos fases dobla la complejidad de `CityGeneratorMinimapBuilder`.** Un flujo partido en dos actualizaciones de Editor con `EditorApplication.delayCall` introduce una ventana en la que el estado intermedio (grupos ocultos, huellas ya calculadas) vive fuera de la propia llamada de método, más difícil de depurar si algo falla entre fases. Mitigación: la fase 2 revalida que los roots capturados en la fase 1 siguen existiendo antes de tocarlos, y restaura la visibilidad en un `finally` que se ejecuta incluso si la captura en sí falla.
- **El diálogo de confirmación de `Rebuild Minimap`/`Rebuild City in Current Scene` es responsabilidad del propio menú, no de `CityGeneratorWindow` en su forma habitual (validación + botón).** Más fácil de que quede inconsistente en estilo/mensaje con el resto de diálogos. Mitigación: reutilizar exactamente el patrón `EditorUtility.DisplayDialog` ya usado por `RebuildPedestrianNetworkMenuItem`.
- **Ambiencia 2D duplicada con varias ciudades es un fallo audible que esta spec deja sin resolver.** Un usuario que siga el flujo documentado y no lea la limitación se encontrará dos fuentes de ambiente sonando a la vez sin ninguna pista visual de por qué. Mitigación: documentado explícitamente en `docs/user-manual.md`/`.es.md` junto al resto de limitaciones de esta spec.
