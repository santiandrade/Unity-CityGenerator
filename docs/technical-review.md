# Informe de revisión técnica — City Generator

## Contexto

Revisión técnica (rendimiento, memoria, buenas prácticas y calidad de código) del proyecto
**City Generator**, más el análisis de si merece la pena adoptar ECS/DOTS.

El producto de este repositorio es **la tool**: `Assets/CityGenerator/`, una ventana de
Editor que genera ciudades procedurales, pensada para copiarse como paquete portable a
cualquier proyecto Unity. Todo lo demás son medios para ese fin:

- `Assets/Prefabs`, `Assets/Materials`, `Assets/Meshes`, `Assets/Animations`,
  `Assets/Models` — **contenido de demo**, los prefabs de ejemplo con los que la tool
  funciona nada más abrirla (`CityGeneratorDefaultAssets`). No viajan con el paquete.
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

Revisado: los 23 scripts de `Assets/CityGenerator/`, `ProjectSettings/*`,
`Assets/Settings/*` (URP), los 22 prefabs de demo, los 14 materiales, `City.unity` y
`Packages/manifest.json`.

**Conclusión de una línea**: la mayor parte de lo detectado en la revisión inicial ya está
corregido en el código de la tool; lo que queda pendiente es una decisión de autoría
(ProBuilder en los prefabs de demo) y trabajo que solo se justifica si el tráfico crece un
orden de magnitud. ECS no resolvería nada de lo que hoy limita al proyecto.

## Cómo está organizado este informe

El criterio sigue siendo **dónde vive el fix**, pero reordenado según lo que importa ahora:
si el fix viaja con el paquete o no.

- **A — Código de la tool** (`Assets/CityGenerator/Runtime` y `Editor`): **viaja con el
  paquete**. Se arregla una vez y se aplica en cada generación futura, en este proyecto y en
  cualquier otro donde se instale la tool. Máxima prioridad por definición.
- **B — Contenido de demo** (`Assets/Prefabs`, `Assets/Materials`, `Assets/Models`): **no
  viaja**. Un fix aquí mejora las ciudades generadas en este proyecto y la primera impresión
  de quien pruebe la tool con los assets de ejemplo, pero desaparece en cuanto el usuario
  asigne sus propios prefabs.
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
| A.1 | Sin static flags: cero batching, occlusion imposible | `CityGeneratorContentAssembler.MarkStatic` aplica `Batching\|Occluder\|Occludee Static` a todos los grupos **menos `Vehicles`** (los mueve `CarAgent` por transform). Automático en cada generación |
| A.3 | Farolas con densidad fija de 3 por lado | `props.lampDensity` en `CityGeneratorSettings`, mismo patrón que `binDensity` |
| A.4 | `SphereCast` leyendo posiciones de física obsoletas | `TrafficNetwork` llama a `Physics.SyncTransforms()` una vez por frame tras mover los agentes, con el porqué comentado en el sitio |
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
| B.1 | Sombras `TwoSided` en los 6 prefabs de edificio | `m_CastShadows: 1` (On) en los seis |
| B.3 | FBX de edificios importando rig y animación | `animationType: 0`, `importAnimation: 0` en los 41 FBX de `Models/Buildings` |
| B.4 | 32 clips importados en `character-male-d.fbx` | `clipAnimations` reducido a los 5 que usa el `PlayerAnimator` |
| B.5 | Prefab `Lamp` con 4 renderers | Bajado a 2 `MeshRenderer` |
| C.1 | Shadowmap 8192 px, 4 cascadas, 150 m | `PC_RPAsset`: 2048 px, 2 cascadas, 70 m, `SoftShadowQuality: 1` |
| C.2 | `_CameraOpaqueTexture` generada sin consumidor | `m_RequireOpaqueTexture: 0`. `m_RequireDepthTexture` sigue a 1, justificado por el SSAO del `PC_Renderer` |
| C.3 | GPU Resident Drawer desactivado | `m_GPUResidentDrawerMode: 1` (Instanced Drawing), habilitado por A.1 |
| C.4 | Framerate sin objetivo fijado | `CityGenerator.Runtime.PerformanceBootstrap`: `vSyncCount = 0`, `targetFrameRate = 60`. Deliberadamente **en el paquete** y no en `ProjectSettings`, para que viaje con la tool |

### Pendientes

| Alcance | # | Hallazgo | Ganancia | Coste | ¿Merece la pena? |
|---|---|---|---|---|---|
| **B** | B.2 | Mallas ProBuilder embebidas en cada instancia | **Muy alta** | Media | Sí, pero **requiere tu decisión** |
| **A** | A.2 | Combinar las marcas viales en una malla por material | Media | Media | Opcional, medir antes |
| **A** | A.7 | Tick centralizado de `CarAgent` (`TrafficManager`) | Media | Media | Solo si se sube de ~100 coches |
| **A** | A.13 | `ScriptableObject` de tuning de vehículos | Baja | Media | **No recomendado** |
| **A** | A.18 | README del paquete | Alta | Baja | **Sí** |
| **C** | C.5 | Falsos positivos: GPU instancing, matriz de colisiones | Nula | — | **No tocar** |
| **C** | C.6 | Ajustes de build | — | — | Sin acción hasta que haya build |
| — | **F** | **ECS / DOTS** | **Nula hoy** | Muy alto | **No** |

---

## A. Código de la tool — viaja con el paquete

### A.2 Combinar las marcas viales en una malla por material (opcional)

Una ciudad 3×3 coloca del orden de 176 marcas viales (`Dash_*` + `Zebra_*`), cada una un
`GameObject` con su propio `MeshRenderer`, sin collider, compartiendo solo dos materiales
(`RoadLine`, `Crosswalk`). La cifra escala con el tamaño de la rejilla.

El static batching de A.1 ya las agrupa en pocos draw calls por material sin trabajo
adicional, así que el draw call **ya no es el problema**; lo que queda es el recuento de
objetos en la jerarquía y su coste de carga de escena. Si molesta, la extensión natural es
combinarlas al final de `CityGeneratorGroundBuilder.BuildRoadMarkings` con
`Mesh.CombineMeshes`, una malla para dashes y otra para zebras.

**Contrapartida**: la malla combinada es un asset generado que hay que guardar en algún
sitio dentro del proyecto del usuario, lo cual va contra el principio de que la tool no
crea assets fuera de la escena. Eso, más que el coste técnico, es lo que mantiene esto en
"opcional". **Veredicto**: medir el recuento de objetos con A.1 ya aplicado antes de
decidir.

### A.7 Tick centralizado de agentes

Cada `CarAgent` tiene su propio `Update()`, con el coste fijo de marshalling que Unity
cobra por cada uno. Con 30 coches es irrelevante; con 200+ empieza a contar, y sobre todo
**impide escalonar trabajo**.

**Propuesta** (solo si se sube el número de coches): un `TrafficManager` con un único
`Update` que itere una `List<CarAgent>` llamando a `Tick(float dt)`. Habilita gratis
escalonar el `SphereCast` (los coches lejos de la cámara lo ejecutan 1 de cada N frames
reutilizando el `clearance` anterior) y sustituir el `FindFirstObjectByType<TrafficNetwork>()`
que hoy hace cada coche en `Start` por una referencia inyectada.

**Veredicto**: no prioritario. El techo real no es el rendimiento sino el gridlock, ya
acotado en `CityGeneratorConstants.VehicleDensityWarningThreshold` (0.4 de los nodos de
spawn; medido en 5×5: 38% fluía, 76% se atascaba desde el primer frame). Ver F.4.

### A.13 `ScriptableObject` para el tuning de vehículos — no recomendado

Los cuatro prefabs de coche llevan sus ~7 valores de conducción serializados en cada uno.
Centralizarlos en un `CarProfile` como `ScriptableObject` permitiría ajustar el tuning sin
tocar cada prefab. Es mejora de mantenibilidad, no de rendimiento, y **complica el
paquete**: el usuario tendría que crear y asignar perfiles además de prefabs, cuando hoy
solo tiene que arrastrar un prefab. **No hacerlo** salvo que el tuning se toque a menudo.

### A.18 README del paquete

Es el hueco más claro que queda de cara a distribuir la tool. `Assets/CityGenerator/` no
tiene documentación propia: quien copie la carpeta a otro proyecto no sabe qué necesita ni
qué esperar. Debería cubrir, como mínimo:

- **Requisitos**: Input System (referenciado por ambos asmdef) y una capa llamada `Vehicle`
  si se quiere tráfico (la tool avisa en vez de fallar si no existe, pero conviene decirlo).
- **Qué hacer con `CityGeneratorDefaultAssets.cs`**: es el único fichero deliberadamente no
  portable — apunta por ruta a los prefabs de demo de *este* repositorio. En otro proyecto
  hay que reescribirlo con los assets propios o borrarlo y dejar los campos vacíos.
- **Requisitos de los prefabs del usuario**: pivote en la base, edificios dimensionados al
  slot de 22 m (la tool **no** comprueba solape entre edificios), vehículos con un único
  `BoxCollider` en la raíz y sin `Rigidbody`.
- **Pasos posteriores por escena, responsabilidad del usuario**: hornear lightmaps y
  occlusion culling (la geometría ya sale marcada static), y añadir `LODGroup` a los
  prefabs propios si la ciudad es grande. Explícitamente fuera del alcance de la tool.
- **Configuración recomendada del proyecto destino**: los valores del grupo C.

**Ganancia**: alta — sin esto el paquete no es distribuible. **Coste**: bajo.

---

## B. Contenido de demo — no viaja con el paquete

### B.2 Mallas ProBuilder embebidas en cada instancia — requiere decisión

Los prefabs de suelo y props (`Floors/*`, `Props/Bench|Bin|Lamp`) conservan su
componente `ProBuilderMesh`. Consecuencia: **cada instancia regenera su malla y la guarda
como override local de la escena** en vez de compartir la del prefab. La `City.unity` actual
(3×3, 398 instancias) pesa ~5 MB con **227 mallas `pb_Mesh*` embebidas**; la anterior 5×5
llegaba a 9,74 MB con 833. Además esas mallas se marcan `m_IsReadable: 1`, así que mantienen
una copia permanente en RAM de CPU además de la de GPU.

No es un problema de una escena concreta: **cada ciudad generada reproduce el patrón**,
porque el origen es cómo están autorados los prefabs, no la escena. Y afecta a la primera
impresión de cualquiera que pruebe la tool con los assets de demo.

**Propuesta**: cerrada la autoría de esa geometría, convertir los prefabs a mallas normales
— extraer la malla a un asset en `Assets/Meshes/` (donde ya viven las 17 mallas extraídas),
quitar el componente `ProBuilderMesh` y dejar `isReadable` a 0.

**Ganancia**: muy alta en memoria y tiempo de carga, multiplicada por cada ciudad generada.
**Coste**: medio (script de Editor). **Contrapartida real**: se pierde la edición con
ProBuilder de esa geometría, y con ella la forma en que se autoró originalmente. **Requiere
tu decisión explícita** antes de ejecutarse.

### B.6 Modelos no referenciados — sin acción

Buena parte de `Assets/Models/Characters` y `Assets/Models/Pets` no está referenciada por
ningún prefab ni escena, así que Unity no los incluye en la build: cero coste en runtime.
**No tocar**: la carpeta `Models` se mantiene íntegra a propósito, hay planes de usar más de
esos modelos.

---

## C. Configuración de proyecto — ajustes recomendados para el proyecto destino

Vive en `ProjectSettings/*` y `Assets/Settings/*`. **No viaja con el paquete**: en cuanto la
tool se instale en otro proyecto habrá que reproducir estos valores allí. Su sitio natural
es el README (A.18); se conservan aquí como referencia de qué se ajustó y por qué.

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
2. A.7 (tick centralizado + escalonado de sensores).
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

Queda poco, y lo que queda no es urgente. Por orden:

**1 — A.18: README del paquete.** Coste bajo, y es lo único que hoy separa a
`Assets/CityGenerator/` de ser distribuible. Hazlo primero aunque no sea el de mayor
ganancia técnica.

**2 — B.2: decidir sobre ProBuilder.** Es la única mejora grande que queda y depende de una
decisión tuya, no de escribir código. Si la respuesta es sí, es un script de Editor de una
tarde.

**3 — A.2: medir antes de decidir.** Cuenta objetos y tiempo de carga en una ciudad 5×5 con
A.1 ya aplicado. Solo si el recuento molesta, implementar la combinación de mallas.

**4 — A.7 y F.4: solo si el plan es subir el tráfico.** Y en ese caso, empezar por la
planificación de rutas (F.4.1), no por el rendimiento.

**No hacer**: F (ECS/DOTS), `LODGroup` dentro de la tool (documentarlo como responsabilidad
del usuario en el README), C.5, A.13.

---

## H. Verificación

Para cualquiera de los cambios pendientes, la comprobación mínima:

- **Línea base con el Profiler**: capturar 300 frames en una ciudad generada antes y después.
  Mirar ms de CPU/GPU, `SetPass calls`, `Batches`, `GC Alloc`/frame, y memoria de mallas.
  Con `targetFrameRate` ya fijado a 60 por `PerformanceBootstrap`, la comparación es estable.
- **B.2 en concreto**: el indicador directo es el tamaño de `City.unity` y el recuento de
  `pb_Mesh` embebidos (`grep -c pb_Mesh Assets/Scenes/City.unity`, hoy 227). Debería caer a
  cero. Verificar además que la geometría se ve idéntica y que los colliders siguen ahí.
- **Cualquier cambio en la tool**: generar una ciudad nueva con `useCustomSeed` activado y la
  misma semilla antes y después. Si el cambio no pretendía alterar la colocación, la
  jerarquía resultante debe ser idéntica. Es la prueba de regresión más barata que tiene el
  proyecto — úsala.
- **Cambios en el tráfico**: un pase de Play mode de varios minutos vigilando
  `CurrentStopReason`/`StoppedTime`/`DistanceTravelled`. El fallo que hay que descartar
  siempre es el mismo: la cola en punto muerto que ya ocurrió una vez.
