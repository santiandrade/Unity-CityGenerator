# Informe de revisión técnica — City Generator

## Contexto

Revisión técnica (rendimiento, memoria, buenas prácticas y calidad de código) del proyecto
**City Generator**, más el análisis de si merece la pena adoptar ECS/DOTS.

El producto de este repositorio es **la tool**: `Packages/com.santiandrade.citygenerator/`,
una ventana de Editor que genera ciudades procedurales, distribuida como package embebido
instalable por git URL en cualquier proyecto Unity (SPEC 02). Todo lo demás son medios para
ese fin:

- `Packages/com.santiandrade.citygenerator/DefaultAssets/` — **contenido de demo**, los
  prefabs de ejemplo con los que la tool funciona nada más abrirla
  (`CityGeneratorDefaultAssets`). Desde SPEC 02 viaja **dentro** del package, salvo los
  modelos huérfanos que se quedan en `Assets/Models/` de este repo (no referenciados por
  ningún prefab de demo).
- `Assets/Scenes/City.unity` — **escena de prueba desechable**, generada por la tool y
  regenerada sin miramientos. No contiene trabajo manual que preservar.

Ese encuadre determina qué es un hallazgo y qué no. **Todo lo que solo afecte a una escena
concreta queda fuera de este informe**: hornear lightmaps, hornear occlusion culling,
ajustar la iluminación o colocar `LODGroup` son pasos que ejecuta —o no— quien use la tool
en su propio proyecto, sobre su propia ciudad. La responsabilidad de la tool termina en
**dejar la geometría lista** para que esos pasos sean un botón (marcar los objetos como
`Batching Static`/`Occluder Static`/`Occludee Static`), y eso ya lo hace. La versión previa
de este informe tenía un grupo D dedicado a ese trabajo por escena; se ha eliminado, y las
notas correspondientes pertenecen ahora al README del paquete, no aquí.

Revisado (alcance en el momento de la revisión, 2026-08-20): los 23 scripts de entonces de
`Packages/com.santiandrade.citygenerator/`, `ProjectSettings/*`, `Assets/Settings/*` (URP),
los 22 prefabs de demo, los 14 materiales, `City.unity` y `Packages/manifest.json`. El
paquete ha crecido bastante desde entonces (SPEC 03-08); las filas de abajo se anotan cuando
un cambio posterior las ha dejado desfasadas.

**Conclusión de una línea**: todo lo detectado en la revisión inicial que merecía la pena ya
está corregido en el código de la tool y en el contenido de demo (incluida la migración fuera
de ProBuilder, B.2, el tick centralizado de tráfico, A.7, y el README del paquete, A.18); lo
único que queda es trabajo que solo se justifica si el tráfico crece un orden de magnitud
(F.4) o mejoras explícitamente descartadas por sus contrapartidas (A.2, A.13). ECS no
resolvería nada de lo que hoy limita al proyecto.

## Cómo está organizado este informe

El criterio sigue siendo **dónde vive el fix**, pero reordenado según lo que importa ahora:
si el fix viaja con el paquete o no.

- **A — Código de la tool** (`Packages/com.santiandrade.citygenerator/Runtime` y `Editor`):
  **viaja con el paquete**. Se arregla una vez y se aplica en cada generación futura, en este
  proyecto y en cualquier otro donde se instale la tool. Máxima prioridad por definición.
- **B — Contenido de demo** (`Packages/com.santiandrade.citygenerator/DefaultAssets/`): desde
  SPEC 02 **viaja con el paquete** igual que el código (categoría heredada de cuando el
  contenido de demo vivía en `Assets/` del repo de desarrollo y no se distribuía). Un fix aquí
  mejora las ciudades generadas con los assets de ejemplo, en este proyecto y en cualquiera
  que instale el package, pero solo hasta que el usuario asigne sus propios prefabs.
- **C — Configuración de proyecto** (`ProjectSettings/*`, `Assets/Settings/*` URP):
  **no viaja**. Global a este proyecto Unity. Su valor real hoy es documental: es la lista
  de ajustes recomendados que conviene reproducir en el proyecto destino, y que debería
  acabar en el README del paquete.
- **E** recoge lo que ya está bien y no hay que tocar. **F** es el análisis de ECS/DOTS.
  **G** propone el orden de lo que queda y **H** cómo verificarlo.

---

## Estado de los hallazgos

### Ya resueltos — no volver a abrirlos

Verificado contra el código actual (2026-08-20). Se listan para que no reaparezcan en una
revisión futura y para dejar constancia de por qué cada uno se dio por cerrado.

| # | Hallazgo | Cómo quedó |
|---|---|---|
| A.1 | Sin static flags: cero batching, occlusion imposible | `CityGeneratorContentAssembler.MarkStatic` aplica `Batching\|Occluder\|Occludee Static` a todos los grupos **menos `Vehicles` y `Pedestrians`** (los mueven `CarAgent`/`PedestrianAgent` por transform; `Pedestrians` se añadió a la exclusión con SPEC 03). Automático en cada generación |
| A.3 | Farolas con densidad fija de 3 por lado | `props.lampDensity` en `CityGeneratorSettings`, mismo patrón que `binDensity` |
| A.4 | `SphereCast` leyendo posiciones de física obsoletas | `Physics.SyncTransforms()` una vez por frame tras mover los agentes, con el porqué comentado en el sitio. Estaba en `TrafficNetwork`; SPEC 05 lo movió a `TrafficManager.Update`, y solo se ejecuta si hay agentes registrados |
| A.5 | `GetComponentInParent` por impacto y por frame | Registro estático `ColliderRegistry` indexado por `GetEntityId()` del collider |
| A.6 | Array de 8 impactos sin ordenar | Subido a 16 en `CarAgent.hits` y en `ThirdPersonCamera.collisionHits` |
| A.8 | `nextCarId` estático sin reset | Reset explícito (`CarAgent.cs:106`), a prueba de *Domain Reload* desactivado |
| A.9 | `WaitForSeconds` asignado por ciclo de semáforo | Las tres esperas cacheadas como campos |
| A.10 | Micro-optimizaciones | `isGrounded` cacheado una vez por frame; `SetPositionAndRotation` en `CarAgent`; `OnDrawGizmosSelected` ya **no** llama a `EnsureBuilt()`, con el motivo comentado |
| A.11 | Sin `.asmdef`: la tool no era un paquete | `CityGenerator.Runtime.asmdef` y `CityGenerator.Editor.asmdef` (`includePlatforms: [Editor]`). El paquete ya es autocontenido y compila aparte |
| A.12 | Ruta del `.inputactions` hardcodeada | `general.inputActions` es un campo `InputActionAsset` de los settings, rellenado por `CityGeneratorDefaultAssets` |
| A.14 | `OverlapsAny` recalculando bounds en cada comparación | `ObstacleCache.GetRect` cachea el `Rect` XZ de cada obstáculo, medido una sola vez |
| A.15 | Copias de la lista de obstáculos por llamada | La lista compartida se pasa y se amplía in situ; el motor añade directamente |
| A.16 | `DestroyImmediate` por candidato rechazado | `ObstacleCache.BorrowProbe`: una instancia "sonda" reutilizable por prefab, reposicionada en cada candidato, más `DestroyRemainingProbes` al final del run |
| A.17 | `File.Exists` con ruta relativa | `GetNextFreeScenePath` usa `AssetDatabase`; se documenta por qué **no** se usa `GenerateUniqueAssetPath` (rompería el nombrado `City<N>` con un sufijo con espacio) |
| A.19 | Paradas de autobús — bug encontrado y luego la categoría entera retirada (2026-08-20) | Primero se detectó y corrigió que nunca se colocaba ninguna: `CityGeneratorStreetCandidates.AddSide`, con `pointsPerSide == 1` (solo lo usaba `BuildBusStops`), ponía el candidato en `t=0`, la misma coordenada que el punto central de las 3 farolas por lado, siempre ocupado con `lampDensity=1`, así que el solape lo descartaba siempre en los 8 bloques no-plaza. Verificado en el Editor tras desplazarlo a `t=0.35`: 8 instancias, una por bloque. Poco después, **decisión del usuario** (sin más motivo dado): retirar la categoría entera en vez de mantenerla arreglada — `busStopPrefab`/`busStopDensity`, `BuildBusStops`, el prefab `Props/BusStop.prefab` y sus 4 mallas extraídas, todo fuera. Detalle en `specs/01-city-generator-tool.md` |
| B.2 | Mallas ProBuilder embebidas en cada instancia (2026-08-20) | Resuelto para los 11 prefabs con `ProBuilderMesh`. La revisión inicial solo había listado `Floors/Lawn\|RoadBase\|RoadDash\|RoadSidewalk\|RoadZebra` y `Props/Bench\|Bin\|Lamp` — ya estaban resueltos de antes (mallas extraídas a `Assets/Meshes/`, sin `ProBuilderMesh`, `m_IsReadable: 0`). El usuario detectó que el alcance original se había dejado corto: `Props/Fountain.prefab` (los 3 objetos `Water`, geometría procedural sobre el modelo `.glb` importado), `Props/TrafficLight.prefab` (8 partes: `Pole`, `PoleBase`, `Arm`, `Housing`, `Visor`, `Lamp_Red`\|`Amber`\|`Green`) y `Vegetation/Tree.prefab` (`Trunk`, `Crown`, `Crown_Top`) seguían con `ProBuilderMesh`. Convertidos con un script de Editor: `ToMesh()` + `Refresh()` + `EditorMeshUtility.Optimize(pb)`, copia de la malla resultante a un asset nuevo en `Assets/Meshes/` (14 activos: `Fountain_Water[_1][_2]`, `TrafficLight_*`, `Tree_*`), `MeshFilter`/`MeshCollider` reapuntados al asset, componente `ProBuilderMesh` eliminado. `m_IsReadable` en `false` requirió `Mesh.UploadMeshData(true)` **antes** de `AssetDatabase.CreateAsset` (llamarlo después, o editar el flag ya serializado vía `SerializedObject`, no persiste — comportamiento verificado empíricamente en este proyecto). Los `sharedMaterial` de los tres lamps del semáforo no se tocaron, así que el swap de estado en `TrafficLight.cs` sigue intacto. `City.unity` regenerada con "Re-Build City in Current Scene": `grep -c pb_Mesh` pasó de 227 a **0**, verificado visualmente sin regresiones |
| B.1 | Sombras `TwoSided` en los 6 prefabs de edificio | `m_CastShadows: 1` (On) en los seis |
| B.3 | FBX de edificios importando rig y animación | `animationType: 0`, `importAnimation: 0` en los 41 FBX de `Models/Buildings` |
| B.4 | 32 clips importados en `character-male-d.fbx` | `clipAnimations` reducido a los 5 que usa el `PlayerAnimator` |
| B.5 | Prefab `Lamp` con 4 renderers | Bajado a 2 `MeshRenderer` |
| C.1 | Shadowmap 8192 px, 4 cascadas, 150 m | `PC_RPAsset`: 2048 px, 2 cascadas, 70 m, `SoftShadowQuality: 1` |
| C.2 | `_CameraOpaqueTexture` generada sin consumidor | `m_RequireOpaqueTexture: 0`. `m_RequireDepthTexture` sigue a 1, justificado por el SSAO del `PC_Renderer` |
| C.3 | GPU Resident Drawer desactivado | `m_GPUResidentDrawerMode: 1` (Instanced Drawing), habilitado por A.1 |
| C.4 | Framerate sin objetivo fijado | `CityGenerator.Runtime.PerformanceBootstrap`: `vSyncCount = 0`, `targetFrameRate = 60`. Deliberadamente **en el paquete** y no en `ProjectSettings`, para que viaje con la tool |
| A.7 | Tick por-coche de `CarAgent` (2026-08-20) | Nuevo `CityGenerator.Runtime.TrafficManager`: `CarAgent` ya no implementa `Update()`, expone `Tick(float dt, bool runSensor)` y se registra contra el `TrafficManager` que resuelve vía `network.Manager` (con fallback a búsqueda/auto-creación si el componente no viene del generador). El `TrafficManager.Instance` singleton original se eliminó en SPEC 04 para que varias ciudades convivan en la misma escena. `TrafficManager.Update()` itera la lista de agentes registrados y llama a `Tick` desde un único punto. Con más de `staggerMinAgentCount` (60 por defecto) coches registrados, además escalona el `SphereCast` del sensor frontal para los coches lejos de `Camera.main`, reutilizando el último `clearance` en los frames que se saltan — por debajo de ese umbral (la demo por defecto tiene 30) el comportamiento es idéntico al `Update()` original. `CityGeneratorTrafficBuilder.AddManagerComponent` añade el componente al `GameObject` `TrafficNetwork` solo si `includeTraffic` está activo, y `BuildVehicles` inyecta la referencia a `TrafficNetwork` en cada `CarAgent` generado vía `SerializedObject`, sustituyendo el `FindFirstObjectByType<TrafficNetwork>()` que antes hacía cada coche en `Start`. Verificado en Play mode sobre la ciudad de prueba regenerada: 28/30 coches con `DistanceTravelled > 0.5 m` tras 4 s, sin errores en consola |
| A.18 | Sin README del paquete (2026-08-20) | `Assets/CityGenerator/README.md` en su momento, absorbido después por el `README.md`/`README.es.md` de la raíz del repo (SPEC 02): requisitos (Input System, capa `Vehicle`), qué hacer con `CityGeneratorDefaultAssets.cs` al portar la tool a otro proyecto, requisitos de los prefabs del usuario (pivote en la base, edificios al slot de 22 m, vehículos con `BoxCollider` único y sin `Rigidbody`), pasos posteriores por escena que quedan fuera del alcance de la tool (bake de lightmaps/occlusion, `LODGroup`), y la tabla de configuración de proyecto recomendada (grupo C) |

### Pendientes

| Alcance | # | Hallazgo | Ganancia | Coste | ¿Merece la pena? |
|---|---|---|---|---|---|
| **A** | A.2 | Combinar las marcas viales en una malla por material | Media | Media | **No — probado y revertido** |
| **A** | A.13 | `ScriptableObject` de tuning de vehículos | Baja | Media | **No recomendado** |
| **C** | C.5 | Falsos positivos: GPU instancing, matriz de colisiones | Nula | — | **No tocar** |
| **C** | C.6 | Ajustes de build | — | — | Sin acción hasta que haya build |
| — | **F** | **ECS / DOTS** | **Nula hoy** | Muy alto | **No** |

---

## A. Código de la tool — viaja con el paquete

### A.2 Combinar las marcas viales en una malla por material — probado y revertido (2026-08-20)

Se implementó y se descartó en la misma sesión: `CityGeneratorGroundBuilder.BuildRoadMarkings`
seguía instanciando cada `Dash_*`/`Zebra_*` individualmente, pero al final combinaba cada
categoría con `Mesh.CombineMeshes` en un único `GameObject` (`Dashes_Combined`,
`Zebras_Combined`) y destruía los originales. Funcionaba (verificado: `RoadMarkings` pasó de
~176 objetos a 2) y el static batching de A.1 ya cubre el problema de draw calls que
originalmente motivaba esto, así que el único beneficio real era el recuento de objetos en la
jerarquía. Se revirtió por dos razones que pesan más que ese beneficio:

- **Occlusion culling roto a escala.** Una malla combinada tiene un único bounds que abarca
  toda la rejilla de calles. Unity no puede recortarla por frustum ni por occlusion salvo que
  esté completamente fuera de cámara: en una ciudad 3×3 casi no se nota, pero en una 10×10 es
  justo el caso que el usuario esperaría que occlusion culling resolviera, y con la malla
  combinada no puede — se renderiza entera en cuanto se ve un solo fragmento. El static
  batching de A.1 no tiene este problema porque conserva los bounds por renderer original.
- **Se pierde la instancia de prefab.** Un `Dash_*`/`Zebra_*` combinado deja de ser una
  `PrefabUtility.InstantiatePrefab` y pasa a ser un `GameObject` con una malla y un material
  sueltos, sin conexión al asset. El usuario pierde poder retocar el color/material de las
  líneas o los pasos de cebra generados desde el propio prefab (algo que sí puede hacer con
  cualquier otro elemento generado por la tool), y cualquier componente que el prefab tenga en
  el futuro se perdería silenciosamente al combinar.

**Veredicto**: no implementar. Si el recuento de objetos llega a ser un problema real, la
alternativa a explorar sería combinar por trozos (por bloque o por segmento de calle) en vez
de por categoría completa, para no perder ni la granularidad de culling ni, si se resuelve
además el problema de la conexión a prefab (p. ej. dejando el `MeshFilter`/`MeshRenderer`
combinado como hijo de un prefab instance vacío en vez de sustituirlo), la editabilidad — pero
nadie ha medido todavía que el recuento de objetos sea, en la práctica, un problema.

### A.13 `ScriptableObject` para el tuning de vehículos — no recomendado

Los cuatro prefabs de coche llevan sus ~7 valores de conducción serializados en cada uno.
Centralizarlos en un `CarProfile` como `ScriptableObject` permitiría ajustar el tuning sin
tocar cada prefab. Es mejora de mantenibilidad, no de rendimiento, y **complica el
paquete**: el usuario tendría que crear y asignar perfiles además de prefabs, cuando hoy
solo tiene que arrastrar un prefab. **No hacerlo** salvo que el tuning se toque a menudo.

---

## B. Contenido de demo — viaja con el paquete desde SPEC 02

### B.6 Modelos no referenciados — sin acción

`Assets/Models/Pets` no está referenciada por ningún prefab de demo ni escena, así que
Unity no la incluye en la build: cero coste en runtime. **No tocar**: se mantiene a
propósito, hay planes de usar esos modelos; SPEC 02 ya excluye estos huérfanos del package
por el mismo motivo (`AssetDatabase.GetDependencies`, no inspección manual). Actualización:
los huérfanos de `Characters`/`Buildings`/`Cars`/`Props` sí acabaron borrándose del repo una
vez los modelos referenciados se movieron dentro del package, así que `Pets` es hoy lo único
que queda en `Assets/Models/`.

---

## C. Configuración de proyecto — ajustes recomendados para el proyecto destino

Vive en `ProjectSettings/*` y `Assets/Settings/*`. **No viaja con el paquete**: en cuanto la
tool se instale en otro proyecto habrá que reproducir estos valores allí. Ya recogidos en el
`README.md` de la raíz del repo (sección *Recommended project settings*); se conservan aquí
como referencia de qué se ajustó y por qué.

**Valores aplicados en este proyecto**, y recomendados en cualquier otro:

| Ajuste | Valor | Motivo |
|---|---|---|
| `m_MainLightShadowmapResolution` | 2048 | 8192 son ~134–268 MB de VRAM sin ganancia visible en geometría de ciudad |
| `m_ShadowCascadeCount` | 2 | Con 4 cascadas toda la geometría se re-renderiza cuatro veces por frame solo para sombras |
| `m_ShadowDistance` | 70 | Una ciudad 3×3 mide ±90 m; 150 m cubría de sobra toda la escena |
| `m_SoftShadowQuality` | 1 | High no aporta a esta escala |
| `m_RequireOpaqueTexture` | 0 | Ningún shader lee `_CameraOpaqueTexture`; forzaba una copia del color buffer por frame |
| `m_GPUResidentDrawerMode` | 1 | Requiere objetos marcados static (A.1) y URP en Forward+; hace batching vía `BatchRendererGroup` y añade GPU occlusion culling |
| `targetFrameRate` | 60 | Lo fija `PerformanceBootstrap`, dentro del paquete, no aquí |

Nota de coherencia: `QualitySettings.asset` declara sus propios `shadowDistance`/
`shadowCascades`, pero bajo URP se ignoran y manda el URP Asset. No intentar alinearlos: es
ruido, no configuración efectiva.

`m_RequireDepthTexture: 1` se mantiene a propósito — el `PC_Renderer` tiene la Renderer
Feature **SSAO** activa. Tiene coste medible y es la palanca disponible si hace falta más
margen.

### C.5 Falsos positivos — no tocar

- **GPU instancing en materiales**: `m_EnableInstancingVariants: 0` en todos. No es un
  problema: el SRP Batcher está activo (`m_UseSRPBatcher: 1`) y tiene prioridad sobre el GPU
  instancing en URP, así que activarlo no cambiaría nada. Es el hallazgo de optimización más
  citado y menos aplicable de Unity — se documenta para que no acabe en una lista de tareas.
- **Matriz de colisiones abierta**: `m_LayerCollisionMatrix` es `ff…ff`, así que la capa 8
  `Vehicle` colisiona con todo. En la práctica no cuesta nada: los coches no tienen
  `Rigidbody`, no hay simulación entre ellos, y el sensor usa `vehicleMask` explícitamente.

### C.6 Ajustes de build — sin acción por ahora

`bakeCollisionMeshes: 0`, `StripUnusedMeshComponents: 0`, `managedStrippingLevel: {}`,
`m_ShowUnitySplashScreen: 1`. Son los valores por defecto y solo importan cuando se haga una
build real, no en el uso actual (Editor + Play mode). Se anotan para esa fase futura.

---

## E. Lo que ya está bien — no tocar

Merece la pena registrarlo para que nadie lo "arregle" después:

- Hashes de Animator cacheados, componentes resueltos en `Awake`.
- `Time.deltaTime` en todo movimiento; nada dependiente de framerate.
- Ni un `GameObject.Find` ni un `GetComponent` dentro de ningún `Update`.
- Sin comparaciones de tag por string.
- `TrafficLight` usa `sharedMaterial`, no `material`: evita instanciar una copia de material
  por semáforo. Correcto y deliberado.
- Arrays de `RaycastHit` preasignados para los casts `NonAlloc`.
- La lógica de deadlock de `CarAgent` y la ponderación de rutas de `TrafficNetwork` son el
  resultado de bugs reales y están documentadas como tal: **no simplificar** ninguna de las
  piezas descritas en `CLAUDE.md`.
- Toda la generación es determinista con `general.useCustomSeed`: un único `System.Random`
  pasado por parámetro a cada builder, ninguno toca `UnityEngine.Random`. No romper esa
  disciplina al añadir builders.
- `CityGeneratorTrafficBuilder` añade `CarAgent` y la capa `Vehicle` **solo a las instancias
  de escena**, nunca al prefab del usuario. Es la regla que hace la tool segura de instalar.
- El perfil `Mobile_RPAsset` está correctamente dimensionado (renderScale 0.8, 1 cascada,
  sin soft shadows, sin sombras de luces adicionales).
- **Object pooling no aplica**: no hay `Instantiate`/`Destroy` en runtime; los coches se
  crean en generación y viven toda la sesión. Revisado y descartado por no ser aplicable, no
  por omisión.

---

## F. ¿Merece la pena ECS/DOTS?

**Veredicto: no. No lo implementes en este proyecto.** No es un fix puntual sino una
decisión arquitectónica, y para una tool de Editor cuyo producto es un paquete portable
sería además un requisito impuesto al usuario.

### F.1 Qué costaría

El stack DOTS **no está instalado**: `manifest.json` no incluye `com.unity.entities`,
`burst`, `collections`, `mathematics` ni `jobs`. Adoptarlo implicaría:

1. Instalar Entities + Entities Graphics + Unity Physics — **en todo proyecto que instale la
   tool**, no solo en este. Por sí solo descarta la idea: la tool dejaría de ser una carpeta
   que se copia y pasaría a imponer un stack completo.
2. Reescribir `CarAgent` y `TrafficNetwork` como `IComponentData` + `ISystem`. El grafo usa
   `class Node` con `List<Exit>` — tipos gestionados que habría que convertir a
   `BlobAssetReference` o `NativeArray` planos.
3. Sustituir `Physics.SphereCast` por Unity Physics: los prefabs de vehículo tendrían que
   re-autorizarse con `PhysicsShape` en vez de `BoxCollider` — de nuevo, un requisito
   trasladado a los prefabs del usuario.
4. Baking de los prefabs a entity prefabs, con `Baker` propios.
5. Mantener un puente híbrido para el jugador (`CharacterController`), la cámara y los
   semáforos, que seguirían siendo GameObjects.
6. Reescribir el generador de Editor para emitir subescenas.

Es, de forma realista, una reescritura completa.

### F.2 Qué ganarías

Nada medible hoy. El coste de CPU del tráfico es 30 coches × (un `SphereCast` + aritmética
trivial): unos cientos de microsegundos por frame. Lo que limitaba al proyecto era el shadow
pass y la ausencia de batching, y **ambos ya están resueltos** por A.1 + C.1 + C.3 sin tocar
la arquitectura. El GPU Resident Drawer *es* `BatchRendererGroup`, la misma tecnología que
usa Entities Graphics, disponible sin migrar nada.

### F.3 Dónde sí tendría sentido, y a partir de cuándo

- **Vehículos** — el único candidato real. El umbral práctico está en **2 000–5 000 agentes
  simultáneos**. Hoy hay 30, y la propia tool advierte de gridlock por encima del 40 % de
  ocupación de nodos. El límite para escalar el tráfico **no es el rendimiento, es la
  ausencia de planificación de rutas** en `CarAgent`: migrar a ECS haría llegar al atasco más
  rápido, no lo evitaría.
- **Peatones** — si algún día se quieren cientos o miles con comportamiento simple, ese sí
  sería el caso donde DOTS aporta. No existen hoy.
- **Geometría estática** — herramienta equivocada; lo correcto ya está hecho (A.1 + C.3).
- **Jugador, cámara, semáforos, generador** — nunca.

### F.4 La alternativa que sí merece la pena

Si el objetivo real es **escalar el tráfico** en las ciudades generadas manteniendo los FPS,
el camino con mejor relación coste/beneficio no es ECS, sino, por orden:

1. **Planificación de rutas en `CarAgent`**. Es el techo real: sin ella, más coches solo
   significa atascarse antes. Todo lo demás es prematuro hasta resolver esto.
2. A.7, ya implementado (`TrafficManager`: tick centralizado + escalonado de sensores por
   encima de `staggerMinAgentCount`).
3. Sustituir el `SphereCast` por una **rejilla espacial** propia: los coches ya viven en un
   grafo de carriles conocido, así que "el coche de delante" se resuelve por índice de carril
   en O(1) **sin tocar el motor de física** — y de paso desaparece la necesidad del
   `Physics.SyncTransforms()` de A.4. Cambio localizado en `CarAgent` + `TrafficNetwork`, y
   viaja con el paquete.
4. Solo si tras eso el perfilado señala a la lógica de agentes: Jobs + Burst sobre esa
   rejilla, **sin** migrar a Entities.

Con esos pasos el proyecto soportaría del orden de 500–1 000 coches por ciudad generada.

---

## G. Orden de ejecución propuesto

A.7 y A.18 ya están implementados (2026-08-20; ver "Ya resueltos" arriba). Lo único que queda
no es urgente:

**Si el plan es subir el tráfico, seguir F.4** — empezar por la planificación de rutas
(F.4.1), no por el rendimiento; A.7 (F.4.2) ya está hecho.

**No hacer**: F (ECS/DOTS), A.2 (probado y revertido — rompe occlusion culling a escala y la
conexión a prefab de las marcas viales), `LODGroup` dentro de la tool (ya documentado como
responsabilidad del usuario en el README), C.5, A.13.

---

## H. Verificación

Para cualquiera de los cambios pendientes, la comprobación mínima:

- **Línea base con el Profiler**: capturar 300 frames en una ciudad generada antes y después.
  Mirar ms de CPU/GPU, `SetPass calls`, `Batches`, `GC Alloc`/frame, y memoria de mallas.
  Con `targetFrameRate` ya fijado a 60 por `PerformanceBootstrap`, la comparación es estable.
- **B.2 (resuelta, 2026-08-20)**: se verificó con el mismo indicador — `grep -c pb_Mesh
  Assets/Scenes/City.unity` bajó de 227 a 0 tras regenerar la escena con "Re-Build City in
  Current Scene". Comprobado visualmente que la geometría no cambió y que los colliders
  siguen ahí.
- **Cualquier cambio en la tool**: generar una ciudad nueva con `useCustomSeed` activado y la
  misma semilla antes y después. Si el cambio no pretendía alterar la colocación, la
  jerarquía resultante debe ser idéntica. Es la prueba de regresión más barata que tiene el
  proyecto — úsala.
- **Cambios en el tráfico**: un pase de Play mode de varios minutos vigilando
  `CurrentStopReason`/`StoppedTime`/`DistanceTravelled`. El fallo que hay que descartar
  siempre es el mismo: la cola en punto muerto que ya ocurrió una vez.
