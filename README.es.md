🇬🇧 [Read in English](README.md)

# City Generator

<img src="Packages/com.santiandrade.citygenerator/Editor/ToolThumbnail.png" alt="Miniatura de City Generator" width="100%">

Una herramienta de Editor para Unity que genera proceduralmente una ciudad en una
escena nueva o existente. Ábrela desde **Tools > City Generator > Open**.

📖 **[Manual de usuario](docs/user-manual.es.md)** — cada pestaña, card y parámetro
explicados, con capturas de pantalla, además del proceso completo de generar una ciudad.

- **Genera una ciudad completa:** carreteras, aceras, marcas viales, edificios, plazas,
  mobiliario urbano, semáforos, tráfico autónomo, peatones, un ciclo día/noche opcional,
  audio ambiente y un HUD de minimapa.
- **Configura todo desde las seis pestañas de la ventana:**
  - **City:** cuadrícula, suelo, edificios, plazas, mobiliario, un Day/Night Cycle
    opcional para la luz direccional generada (hora de inicio, multiplicador de velocidad
    y un gradiente de color/curva de intensidad a lo largo de las 24 h) y Custom Places.
  - **Custom Grid:** en lugar de un rectángulo ancho × alto, cambia el grid visual de la
    pestaña City a **Customize** y dibuja el contorno de la ciudad manzana a manzana —
    cualquier forma conexa. El resultado sigue saliendo como un rectángulo acabado: los
    huecos se rellenan con suelo y la ciudad termina en acera transitable en ambos modos.
  - **Custom Places:** lugares colocados a mano con título, prefab, una
    manzana/esquina elegida en un grid visual, orientación fija y un flag opcional
    "Is Point Of Interest" que marca la entrada en el minimapa. Se instancian en lugar
    de un edificio aleatorio en esa posición.
  - **Player:** Player Prefab, movimiento, ajuste del `CharacterController` y de la cámara
    en tercera persona, y una Free Camera opcional a la que puedes cambiar en tiempo de
    ejecución para volar por la ciudad generada.
  - **Traffic:** si se generan vehículos, cuántos, y la lista ponderada de prefabs.
  - **Pedestrians:** la lista de prefabs de peatones, su comportamiento al
    caminar/esperar, el ajuste de la multitud y **Custom Pedestrians** — peatones extra
    confinados a una ruta que trazas a mano sobre una vista previa del grafo peatonal, en
    vez de recorrer toda la ciudad.
  - **Minimap:** resolución de textura y radio de visión del HUD de minimapa en el juego.
  - **Audio:** ambiente 2D de ciudad en bucle, más fuentes 3D posicionales en cada plaza
    generada.
- **Instálala y genera de inmediato:** se distribuye como el package embebido
  `com.santiandrade.citygenerator`, instalable directamente desde una git URL, con un
  conjunto completo de prefabs de demostración incluido. No hace falta tocar el código
  del package.

## Instalación

En tu proyecto de Unity, abre **Window > Package Manager**, pulsa el botón **+**, elige
**Install package from git URL** y pega:

```
https://github.com/santiandrade/Unity-CityGenerator.git?path=/Packages/com.santiandrade.citygenerator
```

El segmento `?path=` apunta al package dentro de este repositorio (la raíz del
repositorio no es en sí misma un package). Esta forma sigue la punta de la rama por
defecto.

Para una instalación reproducible fijada a una versión concreta, añade `#vX.Y.Z` con un
tag de la [página de Releases](https://github.com/santiandrade/Unity-CityGenerator/releases)
— por ejemplo, `...citygenerator#v1.0.1` para esa versión exacta.

### Actualizar

Si instalaste el package **sin** tag `#vX.Y.Z` (siguiendo la punta de la rama por
defecto), Package Manager puede detectar y aplicar la actualización por ti: abre
**Package Manager > tu entrada instalada de "City Generator" > Manage**, y pulsa
**Update** si aparece disponible. Esto vuelve a resolver la git URL y sustituye el commit
fijado en `Packages/packages-lock.json` por lo que haya ahora en la rama por defecto, sin
necesidad de eliminar y reinstalar.

Si la instalaste **con** un tag `#vX.Y.Z` fijado a una versión concreta, Package Manager
no ofrece una actualización automática a un tag nuevo — el botón **Update** solo sigue la
misma referencia con la que instalaste. Para pasar a una versión nueva, reinstala el
package: en **Package Manager**, elimina tu entrada instalada de "City Generator" y
vuelve a instalarla desde la git URL usando el tag nuevo de la
[página de Releases](https://github.com/santiandrade/Unity-CityGenerator/releases).

## Requisitos

- **Unity 6000.0** o superior.
- **El nuevo Input System de Unity** (`com.unity.inputsystem`, declarado como
  dependencia del package). Los `.asmdef` de la herramienta referencian
  `Unity.InputSystem`; la API clásica `UnityEngine.Input` no se usa en ningún sitio.
- **glTFast** (`com.unity.cloud.gltfast`, declarado como dependencia del package).
  Necesario para importar la fuente de demostración, que es un modelo `.glb`.
- **uGUI** (`com.unity.ugui`, declarado como dependencia del package). Necesario para
  el HUD de minimapa, que es un `Canvas` + `RawImage` de UGUI.
- Una layer llamada **`Vehicle`**, que usa el tráfico para que los vehículos se
  detecten entre sí con su sensor frontal. No hace falta que la crees tú — la
  herramienta la crea la primera vez que genera tráfico con ella, usando el primer
  slot de layer libre, y avisa por consola de que lo ha hecho. Solo si ya están todos
  los slots de layer ocupados, recurre a avisar en vez de crearla — en ese caso los
  vehículos dejan de detectarse entre sí por completo (siguen parando en semáforos y
  por prioridad en cruces sin semáforo) hasta que liberes un slot.
- Una layer llamada **`Pedestrian`**, con la misma idea: se crea automáticamente la
  primera vez que generas peatones, para que el sensor de peatones de `CarAgent` pueda
  detectarlos. Mismo fallback fail-closed si no queda ningún slot libre — los vehículos
  simplemente no detectan peatones hasta que liberes uno.
- Un prefab `TrafficLight` con un componente `CityGenerator.Runtime.TrafficLight`
  siempre que la ciudad tenga al menos una intersección que requiera semáforo — la
  herramienta lo valida y bloquea la generación si falta, independientemente de si el tráfico está activado: la red de tráfico
  y sus semáforos siempre se generan para que los cruces queden conectados a un
  semáforo real, incluso sin ningún vehículo.

## Contenido de demostración

El package incluye un conjunto completo de assets de demostración bajo su carpeta
`DefaultAssets/` — edificios, vehículos, personajes, vegetación, mobiliario urbano,
piezas de suelo, materiales, clips de audio y el prefab/sprites del HUD de minimapa —
así que `Tools > City Generator > Open` se abre con todos los campos obligatorios ya
rellenos y una ciudad está a un clic de distancia.

Este contenido de demostración vive dentro del package, que Unity trata como de solo
lectura en tu proyecto. Si quieres modificar un prefab de demostración, cópialo primero
a tu propia carpeta `Assets/` y asigna tu copia en la ventana de la herramienta, en vez
del original del package.

## Requisitos para tus propios prefabs

La herramienta nunca modifica un asset de prefab —todo lo que cambia lo hace sobre las
*instancias de escena* que genera— pero sí espera algunas cosas de lo que le asignes:

- **Pivote en la base**, para cada categoría de prefab (edificios, props, vegetación,
  suelos). La herramienta posiciona todo colocando el pivote en el punto objetivo sobre
  el suelo.
- **Edificios dimensionados al slot de esquina de 22 m**
  (`CityGeneratorConstants.BuildingSlotPitch`). Los edificios son la única categoría que
  la herramienta **no** comprueba contra solapamiento, ni entre sí ni contra el borde
  de la manzana — un prefab de edificio sobredimensionado se solapará visiblemente con
  su vecino. Es deliberado: dimensionar tus propios prefabs al slot es tu
  responsabilidad, igual que con cualquier otro asset proporcionado por el usuario.
- **Vehículos y peatones**: sin `Rigidbody` — ambos se mueven por transform cada frame
  (`CarAgent`/`PedestrianAgent`). No hace falta que añadas tú el collider: el root de la
  instancia generada siempre acaba con un collider propio, sin trigger, dedicado a la
  detección por sensor — si el root ya trae uno se reutiliza, y si no, se añade un
  `BoxCollider` dimensionado a partir de los bounds combinados de los renderers del
  propio prefab. Un collider que solo exista en un hijo de la jerarquía se deja
  completamente intacto (su propia layer e `isTrigger` quedan libres para lo que
  quieras usarlos) — solo el proxy del root es lo que permite a los vehículos
  detectarse entre sí con un `SphereCast` frontal en la layer `Vehicle` y detectar a
  los peatones (y al jugador) del mismo modo; el `CharacterController` del jugador
  puede seguir chocando físicamente con cualquier collider del prefab, esté donde esté.
  Los peatones además
  admiten, si quieres animación de caminar/parado, un `Animator` que controle los
  parámetros `Speed`/`Grounded` de `CharacterAnimator.controller` (o tu propio
  controller con los mismos nombres) — sin él siguen caminando igualmente, solo que sin
  animación.
- **El resto de prefabs** (props, vegetación, suelos, contenido de plaza) solo
  necesitan un `Renderer` en algún punto de su jerarquía — la herramienta mide su huella
  a partir de los bounds combinados de los renderers (`CityGeneratorBoundsUtility`)
  para colocarlo y comprobarlo contra otros objetos ya colocados.

## Lo que la herramienta *no* hace — tu responsabilidad por escena

Todo lo siguiente se aplica a la escena concreta en la que has generado, no a la
herramienta. El trabajo de la herramienta termina dejando la geometría lista para que
estos pasos sean un solo botón:

- **Hacer bake de lightmaps y occlusion culling.** Cada grupo generado excepto
  `Vehicles` y `Pedestrians` (que `CarAgent`/`PedestrianAgent` mueven por transform
  cada frame, incompatible con el batching estático) ya está marcado como
  `Batching Static | Occluder Static | Occludee Static`, así que ambos bakes están
  listos para ejecutarse sin configuración manual — la herramienta simplemente no los
  ejecuta por ti.
- **Añadir `LODGroup`s** a tus propios prefabs si generas una ciudad grande. La
  herramienta no tiene opinión sobre LOD; solo coloca el prefab que le des.
- **Ajustar la iluminación** — la escena generada trae una única luz direccional y
  ningún `Global Volume` (eliminado a propósito, para no depender de ningún pipeline
  de render). Esa luz se crea siempre orientada aproximadamente este-oeste, para que el
  sol salga hacia la derecha del minimapa, y lleva el componente del ciclo día/noche; con
  el ciclo desactivado simplemente se queda fija en la Start Hour configurada.

## Ajustes de proyecto recomendados

No los aplica la herramienta (son globales al proyecto de Unity, no algo que un
generador pueda llevar consigo), pero merece la pena fijarlos en tu proyecto para el
mismo perfil de rendimiento con el que se distribuye este repositorio:

| Ajuste | Valor | Por qué |
|---|---|---|
| `Main Light Shadow Resolution` (asset URP) | 2048 | 8192 cuesta ~134–268 MB de VRAM sin ganancia visible en geometría a escala de ciudad |
| `Shadow Cascades` | 2 | 4 cascadas vuelven a renderizar toda la geometría que proyecta sombra cuatro veces por frame |
| `Shadow Distance` | 70 m | Cubre cómodamente una ciudad generada 3×3 (±90 m) sin desperdiciar distancia de dibujado |
| `Soft Shadow Quality` | Medio/Bajo | Un filtrado de sombras alto no se percibe distinto a esta escala |
| `Opaque Texture` (asset URP) | Off | Solo actívalo si un shader que añadas lee `_CameraOpaqueTexture` |
| `Depth Texture` (asset URP) | On | Necesario si usas SSAO u otra Renderer Feature que lea la profundidad de la escena |
| GPU Resident Drawer | Instanced Drawing | Requiere geometría marcada como estática (ya aplicado por la herramienta) y renderizado Forward+ |

`targetFrameRate`/`vSyncCount` **no** están en esta tabla a propósito — desde la v2.0.0
el package ya no los fija por ti (antes sí lo hacía, en tiempo de ejecución, vía
`CityGenerator.Runtime.PerformanceBootstrap`). Configura tu propia preferencia de frame
rate/VSync para tu proyecto; no hay ningún sustituto opt-in a nivel de package.

## Escalar el tráfico

`CarAgent` no tiene planificación de rutas ni evitación de congestión: superada cierta
fracción de los nodos de spawn de una rejilla ocupados, el tráfico tiende a colapsar en
vez de fluir. La ventana muestra un aviso junto a **Vehicle Count** en cuanto cruzas ese
umbral, antes de generar. Si necesitas tráfico más denso, cada vez que el tráfico está
activado se genera automáticamente un `CityGenerator.Runtime.TrafficManager` — este
actualiza cada `CarAgent` desde un único `Update` central y, pasado un número de coches
propio, escalona el sensor frontal de los coches lejos de la cámara. Eso da algo de
margen, pero el techo real es la falta de planificación de rutas, no el coste de
actualización por coche.

## Escalar los peatones

Los peatones solo aparecen en los nodos del anillo de acera alrededor de cada manzana, así
que la herramienta avisa (sin bloquear) cuando **Pedestrian Count** se acerca a la
capacidad transitable del grafo — a partir de ahí la multitud se percibe como abarrotada,
aunque `PedestrianAgent` no tiene ninguna mecánica de atasco propia (solo se separa de
vecinos muy cercanos, nunca se queda bloqueado de forma permanente como puede pasarle a un
coche). Por ese motivo su umbral es una fracción bastante mayor que el de los vehículos.

Una **rejilla 1×N o N×1** no tiene intersecciones con semáforo, así que tampoco tiene
pasos de cebra — los peatones de cada manzana quedan confinados a su propio anillo de
acera, sin poder cruzar a la manzana vecina. La herramienta también avisa de esto cuando
los peatones están activados.

El grafo peatonal se auto-repara contra un obstáculo movido/añadido cada vez que entras
en Play (y también mediante `Tools > City Generator > Rebuild Pedestrian Network` sin
necesidad de entrar en Play), usando una pequeña sonda física por nodo de acera.

Esa sonda física es, por diseño, lo **único** que bloquea un nodo peatonal. Un objeto
**sin ningún `Collider`** en su jerarquía nunca se trata como obstáculo peatonal: los
peatones lo atravesarán. Si algo que colocas debe bloquearlos, dale un `Collider`. (La
evitación de solapamientos *en el momento de la generación* es independiente y no depende
de colliders: props, vegetación y Custom Places se siguen separando entre sí por los
bounds de sus renderers.)

## Pipeline de render

Los materiales de demostración están creados como **URP/Lit** y se verán magenta
bajo Built-in o HDRP. El código propio de la herramienta no tiene dependencia de
pipeline de render — no requiere ni configura URP, y no se genera ningún
`Global Volume`— así que funciona con cualquier pipeline siempre que le proporciones
materiales que ese pipeline entienda. Solo el contenido de demostración incluido es
específico de URP.

## Licencia

MIT — ver [LICENSE.md](LICENSE.md).
