# Informe de revisión técnica — Unity SmallCityDemo

## Contexto

Revisión completa del proyecto (rendimiento, memoria, buenas prácticas de desarrollo de
juegos y calidad de código) más un análisis de si merece la pena adoptar ECS/DOTS. El
proyecto es una ciudad procedural con tráfico autónomo, un personaje jugable en tercera
persona y una herramienta de Editor (`City Generator`) pensada para distribuirse como
paquete portable, y que se va a usar para generar varias ciudades a partir de ahora.

Revisado: los 23 scripts de `Assets/CityGenerator/`, `ProjectSettings/*`,
`Assets/Settings/*` (URP), los 24 prefabs, los 27 materiales, `City.unity` (9,74 MB) y
`Packages/manifest.json`.

**Conclusión de una línea**: el código está bien escrito; el rendimiento está limitado por
la **configuración de render y la ausencia total de batching**, no por la CPU ni por la
lógica. ECS no resolvería nada de lo que hoy limita al proyecto.

## Cómo está organizado este informe

El criterio de organización es **dónde vive el fix y cuántas veces hay que aplicarlo**, no
el área técnica. Con la tool generando ciudades nuevas de forma recurrente, esa es la
pregunta que importa: un fix en el código de la tool se hace una vez y beneficia a todas
las ciudades futuras sin esfuerzo extra; un fix en la escena actual habría que repetirlo a
mano en cada ciudad nueva. Cuatro grupos:

- **A — Código de la tool** (`Assets/CityGenerator/Runtime` y `Editor`): se arregla una vez
  en el código y se aplica automáticamente en cada generación futura, para siempre. **Máxima
  prioridad** dado el uso previsto.
- **B — Prefabs y materiales de referencia** (`Assets/Prefabs`, `Assets/Materials`,
  `Assets/Models`): se arregla el asset una vez y todas las instancias que la tool coloque a
  partir de ahí lo heredan sin repetir nada — pero **no viaja** si el paquete `CityGenerator`
  se copia a otro proyecto Unity, porque son los assets de la ciudad de referencia, no la
  tool en sí.
- **C — Configuración de proyecto** (`ProjectSettings/*`, `Assets/Settings/*` URP): es
  global al proyecto Unity, no a la escena. Se arregla una vez y todas las escenas —
  `City.unity` y cada `CityN.unity` que generes después — lo heredan sin tocar nada más,
  mientras te quedes en este proyecto. Tampoco viaja si empaquetas la tool para otro
  proyecto.
- **D — Por escena, inevitable**: depende de la geometría final de cada ciudad concreta
  (lightmaps, occlusion culling horneados). No hay forma de automatizarlo del todo dentro
  del generador; hay que repetirlo, aunque sea un paso corto, cada vez que generes una
  ciudad y quieras esa mejora en ella.

Cada hallazgo indica su grupo. La sección **E** recoge lo que ya está bien y no hay que
tocar. La sección **F** es el análisis de ECS/DOTS, aparte de esta clasificación porque el
veredicto es no implementarlo. **G** propone un orden de ejecución y **H** cómo verificar
cada fase.

---

## Resumen ejecutivo

| Alcance | # | Hallazgo | Ganancia | Coste | ¿Merece la pena? |
|---|---|---|---|---|---|
| **A** | A.1 | Static flags automáticos en el generador | **Muy alta** | Baja | **Sí, ya** |
| **A** | A.2 | Combinar marcas viales en una malla (extensión opcional) | Media | Media | Opcional |
| **A** | A.3 | Revisar la densidad fija de farolas (3 por lado, sin densidad) | Media | Baja | Sí |
| **A** | A.4 | `Physics.SyncTransforms()` una vez por frame | Alta | Trivial | **Sí** |
| **A** | A.5 | Lookup de colliders sin `GetComponentInParent` | Media | Baja | Sí |
| **A** | A.6 | Array de 8 hits sin ordenar, sin aviso de truncamiento | Media | Baja | Sí |
| **A** | A.7 | Tick centralizado de `CarAgent` (`TrafficManager`) | Media | Media | Solo si >100 coches |
| **A** | A.8 | `nextCarId` estático sin reset | Media | Trivial | Sí |
| **A** | A.9 | `WaitForSeconds` cacheados en los semáforos | Baja | Trivial | Sí |
| **A** | A.10 | Micro-optimizaciones sueltas (`isGrounded`, `SetPositionAndRotation`, gizmos) | Baja | Trivial | Sí |
| **A** | A.11 | `.asmdef` para Runtime y Editor | Alta | Baja | **Sí** |
| **A** | A.12 | Ruta del `.inputactions` hardcodeada | Alta | Baja | **Sí** |
| **A** | A.13 | `ScriptableObject` de tuning de vehículos | Baja | Media | **No recomendado** |
| **A** | A.14–A.17 | Rendimiento del generador (bounds cacheados, sin copias de lista, sin `DestroyImmediate` de más, `GenerateUniqueAssetPath`) | Media | Baja-Media | Sí |
| **B** | B.1 | Sombras `TwoSided` en los 6 prefabs de edificio | Alta | Trivial | **Sí, ya** |
| **B** | B.2 | 833 mallas ProBuilder embebidas y legibles | **Muy alta** | Media | Sí (decisión) |
| **B** | B.3 | FBX de edificios importando animación innecesaria | Baja | Trivial | Sí |
| **B** | B.4 | 32 clips importados en `character-male-d.fbx`, 6 usados | Baja | Trivial | Sí |
| **B** | B.5 | Prefab `Lamp` con 4 renderers | Media | Baja | Sí |
| **B** | B.6 | FBX de `Characters`/`Pets` no referenciados | Nula | — | Sin acción |
| **C** | C.1 | Shadowmap 8192 px, 4 cascadas, 150 m en `PC_RPAsset` | **Muy alta** | Trivial | **Sí, ya** |
| **C** | C.2 | `_CameraOpaqueTexture` generada sin consumidor | Alta | Trivial | **Sí, ya** |
| **C** | C.3 | GPU Resident Drawer desactivado | Alta | Baja | Sí |
| **C** | C.4 | Sin `vSync` ni `targetFrameRate` fijado | Media | Trivial | Sí |
| **C** | C.5 | Falsos positivos: GPU instancing en materiales, matriz de colisiones abierta | Nula | — | **No tocar** |
| **C** | C.6 | Ajustes de build (`bakeCollisionMeshes`, stripping, splash) | — | — | Sin acción por ahora |
| **D** | D.1 | Sin lightmaps horneados pese a Shadowmask ya configurado | Alta | Media | Sí, por escena |
| **D** | D.2 | Sin datos de occlusion culling horneados | Alta | Baja | Sí, por escena |
| — | **F** | **ECS / DOTS** | **Nula hoy** | Muy alto | **No** |

---

## A. Código de la tool — cero repetición, para siempre

Todo lo de este bloque vive en `Assets/CityGenerator/Runtime/` o `Assets/CityGenerator/
Editor/`. Es la inversión con mejor retorno dado que vas a generar varias ciudades: cada
línea que cambies aquí se ejecuta en cada `Build City` / `Re-Build City` futuro sin que
tengas que volver a tocar nada.

### A.1 Static flags automáticos en el generador

Verificado exhaustivamente: `m_StaticEditorFlags: 0` en los 28 GameObjects de `City.unity`,
en los 41 nodos de los 24 prefabs, y **0 overrides** de ese campo en las 398 instancias.
**No hay un solo objeto estático en el proyecto.** Consecuencias encadenadas:

- **Sin static batching** → cada acera, marca vial, farola, banco y edificio es un draw call
  propio, en el pase principal y en cada cascada de sombra.
- **Sin occlusion culling posible** → no hay datos horneados y no puede haberlos sin
  geometría marcada como occluder/occludee (ver D.2).
- **Sin lightmaps posibles** → toda la iluminación es en tiempo real (ver D.1).

**Propuesta**: una llamada a `GameObjectUtility.SetStaticEditorFlags` por grupo dentro de
`CityGeneratorContentAssembler.Assemble`, aplicada a `Roads`, `Sidewalks`, `RoadMarkings`,
`Buildings`, `Plaza`, `Trees`, `StreetLights`, `Props`, `TrafficLights` con
`Batching Static | Occluder Static | Occludee Static`. Excluir solo `Vehicles`.

**Ganancia**: muy alta. **Coste**: bajo. **Contrapartida**: el static batching duplica
vértices en disco/memoria; con geometría tan simple compensa con creces. **Nota**: esto deja
la geometría *lista* para hornear lightmaps/occlusion, pero el bake en sí sigue siendo un
paso manual por escena — ver D.1 y D.2. También es requisito previo de C.3 (GPU Resident
Drawer).

### A.2 Combinar las marcas viales en una malla por material (extensión opcional)

Hay 96 `Dash_*` + 80 `Zebra_*` = 176 marcas viales por ciudad de referencia (más en rejillas
mayores), cada una un `GameObject` con su propio `MeshRenderer`, sin collider, compartiendo
solo dos materiales (`RoadLine`, `Crosswalk`). El static batching de A.1 ya las agrupa en
pocos draw calls por material sin ningún trabajo adicional — hazlo primero y mide.

Si tras medir el conteo de objetos por escena sigue molestando, se puede ir un paso más
allá **dentro de la propia tool**: al final de `CityGeneratorGroundBuilder.BuildRoadMarkings`,
combinar todas las dashes en una malla y todas las zebras en otra con `Mesh.CombineMeshes`,
en vez de dejarlas como 176 `GameObject` sueltos. Convertiría un ajuste manual por escena en
un paso automático de cada generación.

**Ganancia**: media (reduce el recuento de objetos, no solo draw calls). **Coste**: medio
(cambio no trivial en el builder; hay que decidir si sigue mereciendo la pena tras A.1).
**Veredicto**: opcional, revisar después de medir con A.1 ya aplicado.

### A.3 Revisar la densidad fija de farolas

`CityGeneratorStreetPropsBuilder.BuildLamps` coloca 3 farolas por lado y por bloque, **sin
densidad**, vía `CityGeneratorPlacementEngine.PlaceAll`. En la ciudad de referencia (3×3)
son **100 instancias** de `Props/Lamp` — no 32 como decía la documentación previa del
proyecto —, cada una con 4 renderers y 3 colliders: 400 draw calls y 300 colliders solo en
farolas. La cifra escala linealmente con el tamaño de la rejilla, así que en una 10×10
serían del orden de mil farolas.

**Propuesta**: exponer la cifra (`CityGeneratorConstants.LampPointsPerSide`, hoy fijo a 3)
como densidad configurable en `CityGeneratorSettings`, igual que ya existe para vegetación,
bancos y papeleras — en vez de un valor fijo por bloque que no se puede ajustar sin tocar
código.

**Ganancia**: media (control directo sobre cuántos renderers/colliders añade cada ciudad
nueva). **Coste**: bajo — es el mismo patrón que ya usan `busStopDensity`/`binDensity`.

### A.4 `Physics.SyncTransforms()` una vez por frame (bug latente)

`ProjectSettings/DynamicsManager.asset` tiene `m_AutoSyncTransforms: 0` (esto es
configuración de proyecto — grupo C —, pero el fix efectivo va en el código de la tool).
`CarAgent.Update` mueve el coche escribiendo `transform.position` (sin `Rigidbody`,
confirmado: 0 en los 5 prefabs de vehículo) y en el mismo frame lanza
`Physics.SphereCastNonAlloc` contra los colliders de los demás (`CarAgent.cs:247`). Con el
auto-sync desactivado la escena de física **no ve** las posiciones nuevas hasta el
siguiente `FixedUpdate` (50 Hz): a 60+ FPS el sensor lee posiciones de hasta 20 ms de
antigüedad, ~25 cm de error a 12 m/s.

Encaja exactamente con el síntoma histórico documentado en `CLAUDE.md` — coches que "no se
veían entre sí y se metían unos dentro de otros en las curvas" — y probablemente contribuyó
a los deadlocks que motivaron toda la maquinaria de reservas.

**Propuesta**: `Physics.SyncTransforms()` **una vez por frame**, después de que todos los
`CarAgent` se hayan movido (orden de ejecución de scripts, o el `TrafficManager` de A.7).
Corregir esto en el código de `CarAgent`/`TrafficNetwork` (grupo A) es preferible a activar
`m_AutoSyncTransforms: 1` en el proyecto (grupo C), porque esa alternativa sincroniza en
cada query en vez de una vez por frame — más cara y, sobre todo, no viaja con la tool si se
empaqueta para otro proyecto.

**Ganancia**: alta (corrección). **Coste**: trivial.

### A.5 Lookup de colliders sin `GetComponentInParent`

`CarAgent.cs:256` resuelve el componente recorriendo la jerarquía en cada impacto, cada
frame, por cada coche. Con 30 coches es asumible; con 200 son miles de travesías
nativo↔gestionado por frame — y como vas a generar ciudades más grandes, el número de
coches por ciudad puede crecer.

**Propuesta**: registro estático `Dictionary<int, CarAgent>` indexado por el
`GetInstanceID()` del collider, poblado en `OnEnable` y limpiado en `OnDisable`. Como cada
vehículo tiene **un único `BoxCollider` en la raíz** (confirmado en los 5 prefabs), sirve
incluso la versión trivial: comparar `hits[i].collider.transform == transform`.

### A.6 El array de 8 impactos no está ordenado

`hits` es de tamaño 8 (`CarAgent.cs:53`) y `SphereCastNonAlloc` **no garantiza orden por
distancia** ni avisa de truncamiento. En un atasco denso, si 8 colliders entran en el
barrido, el coche inmediatamente delante puede quedar fuera del array y volverse invisible.

**Propuesta**: subir a 16 y registrar un warning si `count == hits.Length`. Igual en
`ThirdPersonCamera.collisionHits` (`ThirdPersonCamera.cs:50`), donde el riesgo es menor.

### A.7 Tick centralizado de agentes

Cada `CarAgent` tiene su propio `Update()`. Unity paga un coste fijo de marshalling por
cada uno. Con 30 coches es irrelevante; con 200+ empieza a contar, y sobre todo **impide
escalonar trabajo**.

**Propuesta** (solo si se sube el número de coches en ciudades futuras): un
`TrafficManager` con un único `Update` que itere una `List<CarAgent>` llamando a
`Tick(float dt)`. Habilita gratis: escalonar el `SphereCast` (los coches lejos de la cámara
lo ejecutan 1 de cada N frames reutilizando el `clearance` anterior), la llamada única de
A.4 en el sitio correcto, y sustituir el `FindFirstObjectByType<TrafficNetwork>()` que hoy
hace **cada** coche en `Start` (`CarAgent.cs:97`) por una referencia inyectada.

**Veredicto**: no prioritario con 30 coches y el umbral de gridlock del 40 % ya documentado
en `CityGeneratorConstants.VehicleDensityWarningThreshold`. Sí prioritario si el plan es
generar ciudades con mucho más tráfico.

### A.8 `nextCarId` estático sin reset

`CarAgent.cs:56`: `private static int nextCarId = 1;`. Si se desactiva el *Domain Reload*
(práctica habitual para iterar rápido generando ciudades sucesivas en el editor), el
contador no se reinicia entre sesiones de Play y crece indefinidamente, rompiendo el
desempate por `carId` de `IsDeadlockedWith`.

**Propuesta**: `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
que lo devuelva a 1. Dos líneas.

### A.9 `WaitForSeconds` en el bucle de los semáforos

`TrafficLightIntersection.cs:53,57,59`: tres `new WaitForSeconds(...)` por ciclo, por
intersección. Insignificante en cifras absolutas, pero es basura evitable cacheando las
tres instancias como campos.

### A.10 Micro-optimizaciones sueltas

- `PlayerController` consulta `controller.isGrounded` tres veces por frame
  (`PlayerController.cs:127,138` + `OnJumpPerformed`); cachear en una variable local.
- `CarAgent.Update` escribe `transform.rotation` y `transform.position` por separado;
  `SetPositionAndRotation` es una sola travesía nativa.
- `TrafficNetwork.OnDrawGizmosSelected` llama a `EnsureBuilt()`, **construyendo el grafo
  completo en modo edición** solo por seleccionar el objeto. Añadir guarda o
  representación ligera para gizmos.

### A.11 Separar Runtime y Editor en `.asmdef`

Todo vive hoy en `Assembly-CSharp` / `Assembly-CSharp-Editor`. Consecuencias:

1. **Compilación**: cualquier cambio en cualquier script recompila todo el proyecto —
   incluido cada vez que ajustes la tool entre generación y generación de ciudades.
2. **Portabilidad rota**: el objetivo declarado es distribuir `Assets/CityGenerator/` como
   paquete. Sin asmdef no es un paquete, es una carpeta que se mezcla con el código del
   proyecto destino.
3. **Tests imposibles**: `com.unity.test-framework 1.7.0` está instalado y no se puede usar
   sin un asmdef que referencie NUnit. Hoy el proyecto no tiene ni un test.

**Propuesta**: `CityGenerator.Runtime.asmdef` (referencia `Unity.InputSystem`) y
`CityGenerator.Editor.asmdef` (`includePlatforms: [Editor]`, referencia al de runtime).

**Ganancia**: alta. **Coste**: bajo, aunque puede destapar dependencias implícitas.

### A.12 Ruta de input hardcodeada

`CityGeneratorSceneBuilder.cs:19`: `Assets/InputSystem_Actions.inputactions`. En otro
proyecto (o si algún día renombras el asset en este) resuelve a `null` en silencio y la
cámara se queda sin input, sin error.

**Propuesta**: exponerlo como campo `InputActionAsset` en `CityGeneratorSettings`
(rellenado por `CityGeneratorDefaultAssets`, que ya es el fichero deliberadamente no
portable) y avisar desde el validador si falta habiendo `playerPrefab`.

### A.13 `ScriptableObject` para el tuning de vehículos — no recomendado

Los cuatro tipos de coche llevan sus ~7 valores de conducción serializados en cada prefab
(grupo B). Centralizarlos en un `CarProfile` como `ScriptableObject` referenciado desde
`CarAgent` (cambio de código, grupo A) permitiría ajustar el tuning sin tocar cada prefab.
Es mejora de mantenibilidad, no de rendimiento, y **complica el paquete**: el usuario
tendría que crear y asignar perfiles además de prefabs. **No hacerlo** salvo que el tuning
se toque a menudo entre generaciones.

### A.14–A.17 Rendimiento del propio generador (código de Editor)

No afecta al runtime de las ciudades generadas, solo al tiempo que tarda cada generación —
relevante si vas a generar muchas o rejillas grandes:

- **A.14 — `OverlapsAny` es O(n²) y recalcula bounds**: `CityGeneratorPlacementEngine.cs:111`
  recorre toda la lista de obstáculos por cada candidato y llama a
  `CityGeneratorBoundsUtility.GetWorldBounds`, que hace `GetComponentsInChildren<Renderer>()`
  — una asignación de array nueva en cada comparación. En 10×10, con la lista creciendo
  monótonamente a través de todas las categorías, son millones de asignaciones. Cachear el
  `Rect` XZ junto al obstáculo al colocarlo, en vez de recalcular.
- **A.15 — copias redundantes de la lista de obstáculos**: `PlaceByDensity`/`PlaceAll` hacen
  `new List<GameObject>(obstacles)` en cada llamada (`:45`, `:89`), y
  `CityGeneratorStreetPropsBuilder` las llama una vez por bloque. Pasar la lista compartida
  y dejar que el motor añada directamente.
- **A.16 — `DestroyImmediate` en el bucle de colocación**: `CityGeneratorPlacementEngine.
  cs:62,102` instancia, mide y destruye si solapa — en casos densos, la mayoría de
  candidatos. Medir los bounds del prefab una sola vez por prefab+rotación y proyectarlos
  antes de instanciar.
- **A.17 — detalle cosmético**: `CityGeneratorSceneBuilder.GetNextFreeScenePath` (`:130`)
  usa `File.Exists` con ruta relativa; funciona pero lo idiomático es
  `AssetDatabase.GenerateUniqueAssetPath`.

**Ganancia conjunta**: media (solo tiempo de generación). **Coste**: bajo-medio.

---

## B. Prefabs y materiales de referencia — arreglas el asset una vez, las instancias futuras lo heredan

Estos assets viven en `Assets/Prefabs/`, `Assets/Materials/City/` y `Assets/Models/`. La
tool los instancia por referencia (directamente, o vía `CityGeneratorDefaultAssets` como
valores por defecto), así que arreglar el asset una vez basta para todas las ciudades
futuras generadas **en este proyecto** — pero, a diferencia del grupo A, **no forman parte
del paquete portable**: son la ciudad de referencia, específica de este repositorio. Si
algún día `CityGenerator/` se copia a otro proyecto, estos fixes no viajan con la carpeta.

### B.1 Los edificios proyectan sombras a dos caras

Los 6 prefabs de `Assets/Prefabs/Buildings/` sobrescriben `m_CastShadows: 2` = **TwoSided**.
Eso desactiva el backface culling en el shadow pass: cada edificio renderiza el doble de
triángulos en cada una de las 4 cascadas, sin ningún beneficio visual en geometría cerrada y
opaca.

**Propuesta**: `m_CastShadows: 1` (On) en los seis prefabs, vía script de Editor con
`PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset` (nunca editando el YAML a mano).

**Ganancia**: alta. **Coste**: trivial.

### B.2 833 mallas ProBuilder embebidas y legibles

`City.unity` pesa **9,74 MB** y contiene **833 mallas embebidas** `pb_Mesh*`, referenciadas
por **1 738 overrides** `propertyPath: m_Mesh`: cada instancia de acera, marca vial, farola
o banco lleva su propia copia de la malla dentro del fichero de escena en vez de compartir
la del prefab. Y esas mallas tienen `m_IsReadable: 1`, `m_KeepVertices: 1`,
`m_KeepIndices: 1` — mantienen una copia permanente en RAM de CPU además de la de GPU. 833
mallas × 2 copias que deberían ser ~15 mallas compartidas.

Esto no es un problema de una escena concreta: **cada ciudad que generes reproducirá el
mismo patrón**, porque el origen es cómo están autorados los prefabs de suelo/props, no algo
específico de `City.unity`.

**Propuesta**: cerrada la autoría de la geometría, convertir los prefabs de suelo/props a
mallas normales: extraer la malla a un asset en `Assets/Meshes/`, quitar el componente
`ProBuilderMesh` del prefab y dejar `isReadable` a 0.

**Ganancia**: muy alta en memoria y tiempo de carga, y se multiplica por cada ciudad nueva
que generes. **Coste**: medio (script de Editor). **Contrapartida real**: se pierde la
edición con ProBuilder de esa geometría. **`CLAUDE.md` advierte explícitamente de no
"limpiar" esas mallas**, así que **requiere tu decisión explícita** antes de ejecutarse.

### B.3 Los FBX de edificios importan animación

`materialImportMode: 2`, `importAnimation: 1` y `animationType: 2` (Generic) en los FBX de
edificios — geometría estática sin un solo hueso. Se importa un rig y un Avatar inútiles.

**Propuesta**: `animationType: 0` (None) e `importAnimation: 0` en los FBX de
`Assets/Models/Buildings`, vía script de Editor sobre el `ModelImporter`, siguiendo el mismo
patrón ya usado para `character-male-d.fbx`.

### B.4 Clips de animación sobrantes en el personaje

`character-male-d.fbx` importa 32 takes (combate, silla de ruedas, emotes…) de los que
`PlayerAnimator` usa 6. Limitar `clipAnimations` a los seis necesarios.

### B.5 Reducir los renderers del prefab `Lamp`

Cada instancia de `Props/Lamp` tiene 4 `MeshRenderer` y 3 colliders. Combinado con A.3
(revisar la densidad), bajar esto a 1-2 renderers si la geometría lo permite reduce el
coste por farola multiplicado por las ~100+ instancias de cada ciudad.

**Ganancia**: media. **Coste**: bajo — trabajo de modelado/prefab, no de código.

### B.6 Modelos no referenciados — sin acción

El resto de FBX de `Assets/Models/Characters` y `Assets/Models/Pets` no está referenciado
por ningún prefab ni escena, así que Unity no los incluye en la build. Se descarta como
problema.

---

## C. Configuración de proyecto — arreglas una vez por proyecto Unity, no por escena

Vive en `ProjectSettings/*` y `Assets/Settings/*` (los assets URP). Es **global al
proyecto**, no a la escena: arreglarlo una vez cubre `City.unity` y cada `CityN.unity` que
generes después sin volver a tocarlo. La salvedad es que **no viaja** si el paquete
`CityGenerator` se copia a otro proyecto Unity — en ese caso habría que documentar estos
valores recomendados en el README de la tool y volver a aplicarlos allí.

### C.1 Sombras sobredimensionadas (`Assets/Settings/PC_RPAsset.asset`)

```
m_MainLightShadowmapResolution: 8192   # el Mobile_RPAsset usa 1024
m_ShadowCascadeCount: 4                #                        1
m_ShadowDistance: 150                  #                       50
m_SoftShadowQuality: 3  (High)         #                        2
m_AdditionalLightShadowsSupported: 1   #                        0
```

Un atlas de 8192×8192 son ~134–268 MB de VRAM según formato. Con 4 cascadas y 150 m de
distancia (la ciudad de referencia mide ±90 m) toda la geometría se re-renderiza cuatro
veces por frame solo para el shadow pass — y sin batching (A.1) eso son del orden de
**3 400 draw calls adicionales por frame** únicamente en sombras.

Además hay una incoherencia: `QualitySettings.asset` declara `shadowDistance: 40` y
`shadowCascades: 2`, pero bajo URP esos valores se ignoran y manda el URP Asset (150 / 4).

El perfil **Mobile está bien configurado**; el desequilibrio es exclusivo de PC.

**Propuesta**: `m_MainLightShadowmapResolution: 2048`, `m_ShadowCascadeCount: 2`,
`m_ShadowDistance: 70`, `m_SoftShadowQuality: 1`. Alinear `QualitySettings` con los mismos
valores para que no desinforme.

**Ganancia**: muy alta. **Coste**: cuatro valores. Es el mejor cambio del informe en
términos absolutos, y al ser de proyecto cubre todas las ciudades que generes de aquí en
adelante sin ningún esfuerzo extra.

### C.2 `_CameraOpaqueTexture` se genera y nadie la usa

`PC_RPAsset.asset`: `m_RequireOpaqueTexture: 1` con `m_OpaqueDownsampling: 1`. Fuerza una
copia completa del color buffer cada frame, a mitad de resolución. Los 27 materiales del
proyecto usan `Universal Render Pipeline/Lit` sin ninguna textura asignada y ningún shader
lee `_CameraOpaqueTexture`. El perfil Mobile ya lo tiene a 0.

`m_RequireDepthTexture: 1` sí está justificado: el `PC_Renderer` tiene la Renderer Feature
**SSAO** activa. Se menciona para que conste que tiene un coste medible y es la palanca
disponible si hace falta más margen.

**Propuesta**: `m_RequireOpaqueTexture: 0`. **Ganancia**: alta. **Coste**: un valor.

### C.3 GPU Resident Drawer desactivado

`m_GPUResidentDrawerMode: 0` en ambos URP Assets. URP 17.5 y `PC_Renderer` en **Forward+**
ya cumplen los requisitos del GPU Resident Drawer, que hace batching automático de
renderers estáticos vía `BatchRendererGroup` y añade GPU occlusion culling.

**Propuesta**: activarlo (`Instanced Drawing`) **después de A.1** (requiere objetos
marcados static) y medir.

**Ganancia**: alta. **Coste**: bajo.

### C.4 Sin `vSync` ni `targetFrameRate`

`vSyncCount: 0` en ambos niveles de calidad y `Application.targetFrameRate` no aparece en
ningún script. El framerate corre libre: hoy no hay forma de saber si se cumple el objetivo
de 60 FPS de una medición a otra. Fijar un objetivo explícito antes de medir cualquier otra
mejora.

**Propuesta**: `vSyncCount` o `Application.targetFrameRate` fijado. Puede ir en
`ProjectSettings.asset` (grupo C, no viaja) o, si se prefiere que viaje con la tool, como
una línea en un bootstrap runtime de `CityGenerator.Runtime` (grupo A) — a decidir según si
importa que el ajuste acompañe al paquete.

### C.5 Falsos positivos — no tocar

- **GPU instancing en materiales**: los 27 materiales tienen `m_EnableInstancingVariants:
  0`. No es un problema: el SRP Batcher está activo (`m_UseSRPBatcher: 1`) y tiene
  prioridad sobre el GPU instancing en URP, así que activarlo no cambiaría nada. Es el
  hallazgo de optimización más citado y menos aplicable de Unity — se documenta para que no
  acabe en una lista de tareas.
- **Matriz de colisiones abierta**: `m_LayerCollisionMatrix` es `ff…ff` en las 32 capas, así
  que la capa 8 `Vehicle` colisiona con todo. En la práctica no cuesta nada: los coches no
  tienen `Rigidbody`, no hay simulación entre ellos, y el sensor usa `vehicleMask`
  explícitamente.

### C.6 Ajustes de build — sin acción por ahora

`bakeCollisionMeshes: 0`, `StripUnusedMeshComponents: 0`, `managedStrippingLevel: {}`,
`m_ShowUnitySplashScreen: 1`. Son los valores por defecto y solo importan cuando se haga una
build real, no en el uso actual del proyecto (Editor + Play mode). Se anotan para esa fase
futura.

---

## D. Por escena, inevitable — hay que repetirlo en cada ciudad generada

Dependen de la geometría final de cada ciudad concreta. Ni el mejor código de tool los
elimina del todo, aunque A.1 (marcar static) es requisito previo para que ambos tengan
efecto.

### D.1 Iluminación totalmente en tiempo real, con la infraestructura de bake ya puesta

La `Directional Light` de `City.unity` es `m_Lightmapping: 4` = **Realtime** con
`Soft Shadows`. Pero la escena ya tiene configurado `m_MixedBakeMode: 2` (Shadowmask),
`m_EnableBakedLightmaps: 1`, GPU Progressive con 512 samples y 2 bounces — todo listo, y
**nunca se ha ejecutado el bake**.

Con una ciudad cuya geometría es ~100 % estática, pasar la luz a **Mixed + Shadowmask** y
hornear es el caso de uso para el que existe esa configuración: las sombras de edificios y
aceras dejan de calcularse cada frame y solo los objetos dinámicos (coches y jugador) usan
el shadowmap en tiempo real.

**Por qué es por escena**: cada ciudad generada tiene geometría distinta (otra rejilla, otra
disposición de edificios), así que el mapa de lightmaps horneado de una no sirve para otra.
Requiere `Window > Rendering > Lighting > Generate Lighting` después de cada generación en
la que quieras esta mejora.

**Ganancia**: alta. **Coste**: medio (requiere A.1 primero; el bake en sí tarda). El
generador **no debe** hornear nada automáticamente — solo dejar la geometría marcada como
static (A.1) para que tú decidas cuándo hornear cada ciudad.

**Hecho (2026-08-19)**, aplicado directamente sobre `City.unity`. Como esta escena nunca se
generó con la tool, A.1 no la había tocado (`City.unity` es la ciudad de referencia hecha a
mano, no una salida del generador) — así que antes de hornear marqué los mismos 9 grupos que
usa `CityGeneratorContentAssembler.MarkStatic` (`Roads`, `Sidewalks`, `RoadMarkings`,
`Buildings`, `Plaza`, `Trees`, `StreetLights`, `Props`, `TrafficLights`; excluido `Vehicles`),
948 objetos, añadiendo también `Contribute GI` (necesario para el bake de lightmaps, y
deliberadamente fuera de A.1 — ver la nota de esa sección: static batching/occlusion es
responsabilidad de la tool, lightmapping es una decisión por escena). Luz a `Mixed`,
`Lightmapping.BakeAsync()` con GPU Progressive (512 samples, 2 bounces, ya configurado):
19 lightmaps con shadowmask.

**Bug real encontrado y corregido durante el bake**: la primera pasada dejó **todo el suelo
en negro** (calzada, aceras, marcas viales, mobiliario urbano — cualquier malla ProBuilder
migrada en B.2/B.5). Causa: esas mallas se generan a partir de `ProBuilderMesh` y nunca han
tenido un segundo canal UV (de lightmap); al marcarlas `Contribute GI` sin UV2 válido, el
lightmapper las incluye en el bake con un UV2 degenerado en vez de excluirlas con fallback a
iluminación en tiempo real (la propia consola avisaba: *"227 ProBuilder meshes included in
lightmap bake with missing UV2"*). Arreglo: como las mallas horneadas de B.2/B.5 ya estaban
`isReadable: 0` (no se pueden reprocesar), descarté esos 9 prefabs con `git checkout` —
cambios sin commitear de esta misma sesión, confirmado con el usuario antes de descartarlos —
y repetí la migración añadiendo `UnityEditor.Unwrapping.GenerateSecondaryUVSet(mesh)` antes de
`CreateAsset`/`UploadMeshData(true)`. Repetidos también los `RevertPropertyOverride` sobre
`City.unity` (las mallas nuevas tienen GUID distinto) y el bake completo. Resultado: aviso de
UV2 desaparecido, suelo con sombreado e iluminación indirecta correctos.

**Limitación conocida, no arreglada**: los 6 FBX de edificio (`building-a`, `-f`, `-i`, `-m`,
`-skyscraper-c`, `-skyscraper-e`) tienen su propio problema de UV2 —
*"Invalid Mesh was removed from light baking input: Every triangle in the Mesh UV layout has
zero area"* — un fallo real de sus UV importados, anterior a esta sesión y sin relación con
B.2/B.5 (son mallas FBX, no ProBuilder). Sus 32 instancias en `City.unity` quedaron excluidas
del bake: siguen recibiendo luz/sombra en tiempo real de la `Directional Light`, simplemente
no contribuyen a ni reciben GI horneada — visualmente correcto (se ven iguales que antes), solo
sin el ahorro de shadow pass que si tuvieran lightmap. Arreglarlo de raíz exigiría regenerar el
UV2 de esos 6 FBX (`ModelImporter.generateSecondaryUV` o re-exportar el modelo), fuera del
alcance de esta sesión.

### D.2 Sin datos de occlusion culling horneados

`m_OcclusionCullingData: {fileID: 0}` — no hay bake, pese a que la cámara tiene
`m_OcclusionCulling: 1` activo (pidiendo datos que no existen). Igual que D.1, depende de la
geometría concreta de cada ciudad: hay que hornear (`Window > Rendering > Occlusion
Culling > Bake`) después de cada generación en la que quieras esta mejora, una vez aplicado
A.1.

**Ganancia**: alta en ciudades con edificios que se ocluyen entre sí. **Coste**: bajo — un
botón, pero por escena.

**Hecho (2026-08-19)** — `StaticOcclusionCulling.GenerateInBackground()` sobre `City.unity`
(mismos 948 objetos ya marcados `Occluder Static`/`Occludee Static` para D.1). Generó
`Assets/Scenes/City/OcclusionCullingData.asset`; `m_OcclusionCullingData` en la escena ya no
es `{fileID: 0}`. Repetido tras el arreglo del bug de D.1 (la geometría cambió al regenerar
las mallas). Sin errores en consola. Verificación visual (Scene View, detectó el suelo negro en el
primer intento) y un pase de Play mode tras el arreglo, sin errores ni warnings.

---

## E. Lo que ya está bien — no tocar

Cross-cutting, no encaja en la clasificación por alcance porque no requiere ningún fix.
Merece la pena registrarlo para que nadie lo "arregle" después:

- Hashes de Animator cacheados (`PlayerController.cs:44-47`), componentes resueltos en
  `Awake`.
- `Time.deltaTime` en todo movimiento; nada dependiente de framerate.
- Ni un `GameObject.Find` ni un `GetComponent` dentro de ningún `Update`.
- Sin comparaciones de tag por string.
- `TrafficLight` usa `sharedMaterial`, no `material`: evita instanciar una copia de material
  por semáforo. Correcto y deliberado.
- Arrays de `RaycastHit` preasignados para los casts `NonAlloc`.
- La lógica de deadlock de `CarAgent` y la ponderación de rutas de `TrafficNetwork` están
  documentadas como resultado de bugs reales: **no simplificar** ninguna de las tres piezas
  descritas en `CLAUDE.md`.
- El perfil `Mobile_RPAsset` está correctamente dimensionado (renderScale 0.8, 1 cascada,
  sin soft shadows, sin sombras de luces adicionales).
- **Object pooling no aplica**: no hay `Instantiate`/`Destroy` en runtime en ningún punto;
  los coches se crean en generación y viven toda la sesión. Se revisó y se descartó por no
  ser aplicable, no por omisión.

---

## F. ¿Merece la pena ECS/DOTS?

**Veredicto: no. No lo implementes en este proyecto.** No encaja en la clasificación por
alcance porque no es un fix puntual: es una decisión arquitectónica aparte.

### F.1 Qué costaría

El stack DOTS **no está instalado**: `manifest.json` no incluye `com.unity.entities`,
`burst`, `collections`, `mathematics` ni `jobs`. Adoptarlo implicaría:

1. Instalar Entities + Entities Graphics + Unity Physics.
2. Reescribir `CarAgent` y `TrafficNetwork` como `IComponentData` + `ISystem`. El grafo usa
   `class Node` con `List<Exit>` — tipos gestionados que habría que convertir a
   `BlobAssetReference` o `NativeArray` planos.
3. Sustituir `Physics.SphereCast` por Unity Physics: los 5 prefabs de vehículo tendrían que
   re-autorizarse con `PhysicsShape` en vez de `BoxCollider`.
4. Baking de los 24 prefabs a entity prefabs, con `Baker` propios.
5. Mantener un puente híbrido para el jugador (`CharacterController`), la cámara y los
   semáforos, que seguirían siendo GameObjects.
6. Reescribir el generador de Editor para emitir subescenas.

Es, de forma realista, una reescritura completa del proyecto.

### F.2 Qué ganarías

Nada medible hoy. El coste de CPU del tráfico es 30 coches × (un `SphereCast` + aritmética
trivial): unos cientos de microsegundos por frame. El límite del proyecto está en el
**shadow pass y en ~890 draw calls sin batching** (grupos B y C), y **ECS no arregla
ninguno de los dos**. El GPU Resident Drawer (C.3) *es* `BatchRendererGroup`, la misma
tecnología que usa Entities Graphics, disponible sin migrar nada.

### F.3 Dónde sí tendría sentido, y a partir de cuándo

- **Vehículos** — el único candidato real. El umbral práctico está en **2 000–5 000 agentes
  simultáneos**. Hoy hay 30, y la propia herramienta advierte de gridlock por encima del
  40 % de ocupación de nodos. El límite para escalar el tráfico **no es el rendimiento, es
  la ausencia de planificación de rutas** en `CarAgent`: migrar a ECS haría llegar al atasco
  más rápido, no lo evitaría.
- **Peatones** — si algún día se quieren cientos o miles con comportamiento simple, ese sí
  sería el caso donde DOTS aporta. No existen hoy.
- **Geometría estática** — herramienta equivocada; lo correcto es A.1 + C.3.
- **Jugador, cámara, semáforos, generador** — nunca.

### F.4 La alternativa que sí merece la pena

Si el objetivo real es **escalar el tráfico** en ciudades futuras manteniendo los FPS, el
camino con mejor relación coste/beneficio no es ECS, sino, por orden:

1. Grupos B y C de este informe. Es lo que limita hoy, con 30 coches o con 300.
2. A.7 (tick centralizado + escalonado de sensores).
3. Sustituir el `SphereCast` por una **rejilla espacial** propia: los coches ya viven en un
   grafo de carriles conocido, así que "el coche de delante" se resuelve por índice de
   carril en O(1) **sin tocar el motor de física** — y de paso desaparece el problema de
   A.4. Cambio localizado en `CarAgent` + `TrafficNetwork` (grupo A).
4. Solo si tras eso el perfilado señala a la lógica de agentes: Jobs + Burst sobre esa
   rejilla espacial, **sin** migrar a Entities.

Con esos cuatro pasos el proyecto soportaría del orden de 500–1 000 coches por ciudad
generada.

---

## G. Orden de ejecución propuesto

El criterio de orden ya no es solo "ganancia/coste" sino también **alcance**: los cambios
de grupo A y C se hacen una vez y benefician a todas las ciudades futuras, así que van
primero. El grupo B requiere una decisión tuya en un punto (B.2). El grupo D se repite por
diseño, así que se documenta pero no se "ejecuta" como fase.

**Fase 0 — Medir (imprescindible)**
- C.4: fijar `targetFrameRate`/`vSyncCount` para tener un objetivo estable.
- Capturar 300 frames con el Profiler en `City.unity`: ms de CPU/GPU, `SetPass calls`,
  `Batches`, `GC Alloc`/frame, memoria de texturas y mallas. Sin esta línea base no se puede
  demostrar ninguna mejora.

**Fase 1 — Grupo C: configuración de proyecto (horas, ganancia máxima, cero repetición)**
- C.1, C.2: shadowmap, cascadas, distancia, `RequireOpaqueTexture`. Alinear
  `QualitySettings`.
- C.3: GPU Resident Drawer (después de A.1, ver Fase 2).

**Fase 2 — Grupo A: código de la tool (horas–medio día, cero repetición)**
- A.1: static flags automáticos en el generador — hazlo antes de activar C.3.
- A.4–A.10: correcciones de runtime del tráfico.
- A.11, A.12: `.asmdef` y ruta de input.
- A.14–A.17: rendimiento del generador.
- A.3: revisar densidad de farolas.
- A.2: combinar marcas viales (opcional, medir después de A.1).

**Fase 3 — Grupo B: prefabs de referencia (requiere decisiones)**
- B.1, B.3, B.4: fixes directos sobre los prefabs/importadores.
- B.5: reducir renderers del prefab `Lamp`.
- B.2: **requiere tu aprobación explícita** — migrar los prefabs ProBuilder a mallas
  compartidas, ya que contradice una instrucción de `CLAUDE.md`.

**Grupo D — por escena, cuando generes cada ciudad**
- D.1, D.2: hornear lightmaps y occlusion culling en cada ciudad nueva en la que los
  quieras, una vez aplicado A.1.

**No hacer**: F (ECS/DOTS), `LODGroup` dentro de la tool (sí documentarlo como
responsabilidad del usuario en el README del paquete), C.5 (GPU instancing en materiales,
matriz de colisiones), A.13 (`ScriptableObject` de tuning), E (object pooling).

---

## H. Verificación

1. **Línea base (Fase 0)** — antes de tocar nada, según se describe arriba. Anotar las
   cifras en este documento.

   **Cifras capturadas (2026-08-19)**, `City.unity` en Play mode, cámara del jugador en su
   posición inicial, `targetFrameRate` ya fijado a 60 (ver C.4 más abajo) — Editor Profiler,
   ~280 frames por muestra, dos capturas separadas en el tiempo:

   | Métrica | Captura 1 | Captura 2 |
   |---|---|---|
   | Frames por encima de 16,67 ms | 46,8 % (141/301) | 50,9 % (143/281) |
   | CPU máx. / GPU en ese frame | 77,12 ms / 37,21 ms | 71,77 ms / 40,37 ms |
   | CPU mediana / GPU en ese frame | 16,61 ms / 8,61 ms | 16,68 ms / 13,62 ms |
   | `SetPass Calls Count` | 54–55 (mediana 54) | 54–55 (mediana 54) |
   | `Triangles Count` | 170 004–188 932 (mediana 180 356) | 208 296–224 140 (mediana 216 608) |
   | GC Alloc por frame (2000 frames) | mediana 0 B, máx. 657 969 B en 1 frame (1,9 % de frames > 8 KB) | — |
   | `Profiler.GetTotalAllocatedMemoryLong` | ~4 901 MB | — |
   | `Profiler.GetAllocatedMemoryForGraphicsDriver` | ~1 709 MB | — |

   Notas de lectura:
   - El recuento de `SetPass Calls` (54) es mucho menor que los ~890 draw calls estimados en
     el informe porque la cámara del jugador solo ve una parte de la ciudad, no una vista
     aérea completa — el frustum culling ya recorta la mayoría; sigue siendo una cifra alta
     para lo que hay en pantalla, dado que no hay ni un objeto estático (A.1).
   - GC Alloc en mediana es 0, coherente con la sección E (nada asigna en `Update`), pero hay
     picos puntuales de hasta ~640 KB en un solo frame — a vigilar tras la Fase 2, puede venir
     de `WaitForSeconds` sin cachear (A.9) o de los semáforos/tráfico.
   - Con >50 % de frames por encima de 16,67 ms y GPU llegando a 40 ms en el peor frame, el
     cuello de botella es claramente de GPU/render, coherente con el diagnóstico del informe
     (sombras C.1 y ausencia de batching A.1/C.3).
   - Contadores `Draw Calls Count` / `Batches Count` no devolvieron datos con las herramientas
     de Profiler disponibles en esta sesión (limitación de la herramienta, no del proyecto);
     `SetPass Calls` y `Triangles` sirven como proxy suficiente para esta línea base.

   **Fecha del cambio en C.4**: se fijó `Application.targetFrameRate = 60` y
   `QualitySettings.vSyncCount = 0` vía un nuevo bootstrap runtime,
   `Assets/CityGenerator/Runtime/PerformanceBootstrap.cs` (grupo A, para que viaje con la
   tool en vez de quedar solo en `ProjectSettings.asset`), antes de capturar estas cifras.
2. **Tras la Fase 1 (grupo C)** — repetir la captura. Se espera una caída sustancial de
   `Batches`, `SetPass calls` y tiempo de GPU. Comprobar visualmente que las sombras no se
   cortan desde la cámara del jugador a ras de suelo, y que la ciudad se ve igual tras
   apagar `_CameraOpaqueTexture`.

   **Hecho (2026-08-19)** — C.1 y C.2 aplicados en `Assets/Settings/PC_RPAsset.asset` vía
   `SerializedObject` (no a mano): `m_MainLightShadowmapResolution` 8192→2048,
   `m_ShadowCascadeCount` 4→2, `m_ShadowDistance` 150→70, `m_SoftShadowQuality` 3→1,
   `m_RequireOpaqueTexture` 1→0 (`m_RequireDepthTexture` se deja en 1, sigue siendo
   necesario para SSAO). `QualitySettings.asset` (nivel `PC`) alineado: `shadowDistance`
   40→70 (`shadowCascades` ya estaba en 2). Unity reserializó de paso ambos niveles de
   calidad a `serializedVersion: 5` y añadió campos nuevos por defecto (`meshLodThreshold`,
   plataforma `Nintendo Switch 2`) — efecto secundario inocuo de la versión del Editor, sin
   relación con estos cambios.

   Recaptura del Profiler (mismo procedimiento que la línea base, ~270 frames en Play mode):

   | Métrica | Antes (línea base) | Después de C.1/C.2 |
   |---|---|---|
   | Frames por encima de 16,67 ms | 46,8–50,9 % | 49,8 % |
   | CPU máx. / GPU en ese frame | 71,77–77,12 ms / 37,21–40,37 ms | 64,95 ms / **33,82 ms** |
   | CPU mediana / GPU en ese frame | 16,61–16,68 ms / 8,61–13,62 ms | 16,67 ms / 13,95 ms |
   | `SetPass Calls Count` | mediana 54 | mediana 54 (sin cambio, esperado — batching es A.1/C.3) |
   | `Triangles Count` | mediana ~180k–217k | mediana ~200k |

   Coherente con lo previsto: el pico de GPU baja (menos trabajo por objeto en el shadow
   pass y en la copia de color eliminada), pero `SetPass Calls` no se mueve porque el cuello
   de botella de recuento de draw calls solo lo resuelve el static batching de A.1 /
   GPU Resident Drawer de C.3, pendientes en la Fase 2. Verificación visual con
   `Unity_SceneView_CaptureMultiAngleSceneView`: la ciudad se ve igual que antes, sin
   geometría ni sombras rotas.

   **C.3, hecho (2026-08-19), después de A.1** — con la Fase 2 ya completada (A.1 marca
   9 grupos como static), se activó `m_GPUResidentDrawerMode` = `InstancedDrawing` (valor 1)
   en **ambos** `Assets/Settings/PC_RPAsset.asset` y `Mobile_RPAsset.asset` vía
   `SerializedObject`, tal como pedía el hallazgo ("en ambos URP Assets"). Al activarlo, la
   consola avisó de un requisito adicional no mencionado explícitamente en el informe:
   `GraphicsSettings.m_BrgStripping` (Edit > Project Settings > Graphics > "BatchRendererGroup
   Variants") debía pasar de `Strip if Entities Graphics Package is not installed` a
   `Keep All`, si no los shaders de instancing GPU se eliminarían en una build real aunque
   funcionen bien en el Editor — corregido también, en `ProjectSettings/GraphicsSettings.asset`.

   Medido generando una ciudad 3×3 de prueba con seed fijo (mismo procedimiento que la
   verificación de la Fase 2) y perfilando en Play mode:

   | Métrica | Antes (línea base, sin A.1/C.3) | Después de A.1 + C.3 |
   |---|---|---|
   | Frames por encima de 16,67 ms | 46,8–50,9 % | 48,6 % |
   | CPU máx. / GPU en ese frame | 71,77–77,12 ms / 37,21–40,37 ms | **18,04 ms** / **14,49 ms** |
   | CPU mediana / GPU en ese frame | 16,61–16,68 ms / 8,61–13,62 ms | 16,66 ms / 13,75 ms |
   | `SetPass Calls Count` | mediana 54 | **mediana 22** |
   | `Triangles Count` | mediana ~180k–217k | mediana ~157k |

   El pico de CPU/GPU cae drásticamente (de ~75/38 ms a ~18/14 ms) y `SetPass Calls` baja a
   menos de la mitad, confirmando que el cuello de botella de draw calls sí lo resuelve el
   batching (A.1 + C.3 juntos), no las sombras por sí solas. La comparación no es
   perfectamente controlada (la línea base se midió sobre `City.unity`, la ciudad de
   referencia hecha a mano; esta captura es sobre una ciudad 3×3 generada por la tool con la
   misma posición de cámara hardcodeada), pero la cámara y el tamaño de rejilla son
   equivalentes y la magnitud del cambio es demasiado grande para explicarse por esa
   diferencia. Aún queda un ~49 % de frames por encima de 16,67 ms — coherente con que el
   frame más lento capturado (18 ms) está justo por encima del objetivo, no con un problema
   nuevo. Verificación visual: sin glitches de geometría ni de sombreado tras activar el
   batching.
3. **Tras la Fase 2 (grupo A)** — dejar el tráfico 5 minutos y comprobar con
   `CarAgent.CurrentStopReason` / `StoppedTime` / `DistanceTravelled` que no aparecen
   atascos permanentes. `GC Alloc` por frame debe quedar en 0 en régimen estacionario.
   Comprobar que el proyecto compila con los `.asmdef`, que `Tools > City Generator` sigue
   abriendo con los defaults rellenos, y que generar una ciudad nueva sigue moviendo al
   jugador y respondiendo la cámara (valida que el `.inputactions` se resuelve). Generar una
   rejilla 10×10 con seed fijo antes y después de A.14–A.17 y comprobar que el resultado es
   **idéntico** y que el tiempo de generación baja.

   **Hecho (2026-08-19)** — Grupo A completo salvo A.2 (opcional, no aplicado) y A.13 (no
   recomendado, no aplicado). A.7 tampoco se aplicó: su propio veredicto en este informe es
   "no prioritario con 30 coches", y no forma parte de la Fase 2 según la sección G.

   - **A.1** — `CityGeneratorContentAssembler.MarkStatic` aplica `Batching Static | Occluder
     Static | Occludee Static` a los 9 grupos indicados (todos salvo `Vehicles`), recorriendo
     `GetComponentsInChildren<Transform>` de cada uno tras generar. Verificado generando una
     ciudad 3×3 de prueba: 1053 objetos con `m_StaticEditorFlags: 22` (= la combinación de los
     tres flags) en el `.unity` resultante, 0 en `Vehicles`.
   - **A.3** — `PropsSettings.lampDensity` (0–1, por defecto 1) sustituye la colocación fija de
     farolas: `BuildLamps` pasó de `PlaceAll` a `PlaceByDensity`, igual que bus stops/bins.
     `PlaceAll` quedó sin ningún otro caller y se eliminó de `CityGeneratorPlacementEngine`.
     Con densidad 1 (comportamiento por defecto, equivalente al fijo anterior) la ciudad 3×3
     de prueba generó 100 farolas, coincidiendo con la cifra ya documentada para la ciudad de
     referencia.
   - **A.4** — `TrafficNetwork.LateUpdate` llama a `Physics.SyncTransforms()` una vez por
     frame, después de que todos los `CarAgent.Update` (que corren en `Update`, no
     `LateUpdate`) hayan movido su transform. Corregido en el código de la tool, no en
     `DynamicsManager.asset`, tal como recomienda el hallazgo.
   - **A.5** — `CarAgent` mantiene un `Dictionary<EntityId, CarAgent>` estático
     (`ColliderRegistry`) poblado/limpiado en `OnEnable`/`OnDisable`, en vez de
     `GetComponentInParent` por cada impacto del sensor.
   - **A.6** — `hits` (`CarAgent`) y `collisionHits` (`ThirdPersonCamera`) subieron de 8 a 16;
     `CarAgent` avisa por consola si el barrido llena el array (`count == hits.Length`).
   - **A.8** — `CarAgent.ResetCarIdCounter`, con
     `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, resetea `nextCarId` a 1 (y
     limpia `ColliderRegistry`) en cada entrada a Play, incluso con Domain Reload desactivado.
   - **A.9** — `TrafficLightIntersection` cachea `greenWait`/`amberWait`/`allRedWait` como
     campos creados una vez en `Start`, en vez de un `new WaitForSeconds(...)` por fase y ciclo.
   - **A.10** — `PlayerController` cachea `controller.isGrounded` una vez por `Update` en el
     campo `grounded`, leído también por `OnJumpPerformed`. `CarAgent.Update` calcula la
     rotación en una variable local y hace un único `transform.SetPositionAndRotation`
     (cuidando que la posición siga usando el forward de la rotación ya actualizada, igual que
     el código original). `TrafficNetwork.OnDrawGizmosSelected` ya no llama a `EnsureBuilt()`:
     si el grafo no está construido, no dibuja nada en vez de construirlo entero solo por
     seleccionar el objeto en el Editor.
   - **A.11** — `Assets/CityGenerator/Runtime/CityGenerator.Runtime.asmdef` (referencia
     `Unity.InputSystem`) y `Assets/CityGenerator/Editor/CityGenerator.Editor.asmdef`
     (`includePlatforms: Editor`, referencia al de Runtime y a `Unity.InputSystem`, que hace
     falta para el campo `InputActionAsset` de A.12). Verificado con
     `CompilationPipeline.GetAssemblies()`: 7 ficheros en `CityGenerator.Runtime`, 17 en
     `CityGenerator.Editor`, ninguno ya en `Assembly-CSharp`/`-Editor`.
   - **A.12** — `GeneralSettings.inputActions` (`InputActionAsset`) sustituye la ruta
     hardcodeada `Assets/InputSystem_Actions.inputactions` en
     `CityGeneratorSceneBuilder.CreateMainCamera`. `CityGeneratorDefaultAssets` lo rellena con
     ese mismo asset (es el único fichero no portable, así que sigue siendo el sitio correcto).
     El validador bloquea la generación si hay `playerPrefab` sin `inputActions`; la ventana
     marca el campo como requerido (asterisco condicional) en ese mismo caso.
   - **A.14–A.16** — `CityGeneratorPlacementEngine` incorpora `ObstacleCache`: `GetRect` mide
     el rect XZ de cada obstáculo una sola vez (no en cada comparación), y `BorrowProbe`/
     `Consume` reutilizan una única instancia "sonda" por prefab para medir cada candidato
     rechazado en vez de instanciar y `DestroyImmediate` uno por candidato — el objeto
     rechazado simplemente se reposiciona para el siguiente candidato del mismo prefab, y
     `DestroyRemainingProbes()` limpia cualquier sonda que quedó viva al final de la
     generación. Nota: esto se desvía del literal de A.16 ("proyectar bounds sin instanciar")
     porque asumir rotaciones múltiplo de 90° acoplaría el motor genérico a esa suposición;
     reutilizar la instancia evita el par Instantiate/Destroy igualmente y funciona con
     cualquier rotación. `obstacles` ya no se copia por llamada (A.15): `PlaceByDensity` muta
     la lista compartida directamente y `ContentAssembler`/`PlazaBuilder`/
     `StreetPropsBuilder` ya no hacen sus propias copias intermedias.
   - **A.17** — `CityGeneratorSceneBuilder.GetNextFreeScenePath` usa
     `AssetDatabase.LoadAssetAtPath<SceneAsset>` en vez de `File.Exists` con ruta relativa.
     Nota: no se adoptó literalmente `AssetDatabase.GenerateUniqueAssetPath` porque, al existir
     ya `Assets/Scenes/City.unity`, generaría `City 1.unity` (con espacio) en vez de
     `City1.unity`, rompiendo la convención de nombres ya documentada en `CLAUDE.md`.

   **Verificación realizada**: compilación limpia (0 errores/warnings) tras separar en
   `.asmdef` — surgió un problema real de dependencias implícitas, tal como avisa el propio
   hallazgo A.11: `Object.GetInstanceID()` está obsoleto en esta versión de Unity (usar
   `GetEntityId()`, que devuelve `UnityEngine.EntityId` en vez de `int`), y solo se detectó al
   aislar `CarAgent.cs` en su propio assembly con los checks de warnings-as-errors del
   proyecto. Corregido usando `Dictionary<EntityId, CarAgent>`. Generación de extremo a
   extremo probada con un arnés de validación temporal (creado y borrado en la misma sesión,
   no forma parte del repositorio): ciudad 3×3 con seed fijo (12345) generada sin errores ni
   excepciones, con edificios/plaza/farolas/paradas/papeleras/árboles/coches presentes y en
   las cantidades esperadas; confirmado visualmente por captura de Scene View. 10 segundos de
   Play mode sobre esa ciudad sin errores ni warnings en consola (ni siquiera el aviso de
   truncamiento de A.6). No se hizo la comparación de "resultado idéntico antes/después de
   A.14–A.17 con seed fijo" de forma aislada: A.3 cambia deliberadamente el consumo de
   `random` de las farolas (antes no consumían ningún número aleatorio; con densidad ahora sí),
   así que una ciudad generada con el conjunto completo de cambios de la Fase 2 no puede ser
   bit a bit idéntica a la de antes por diseño, no por un fallo del refactor de rendimiento.
4. **Tras la Fase 3 (grupo B)** — comparar tamaño de `City.unity` en disco, tiempo de carga
   de escena y memoria de mallas en el Memory Profiler tras B.2; verificar que ninguna
   instancia perdió su malla.

   **Hecho (2026-08-19)** — Grupo B completo: B.1, B.2, B.3, B.4 y B.5.

   - **B.1** — `m_CastShadows` 2 (TwoSided) → 1 (On) en el único `MeshRenderer` de cada uno de
     los 6 prefabs de `Assets/Prefabs/Buildings/`, vía `PrefabUtility.LoadPrefabContents` /
     `SaveAsPrefabAsset`. Cada edificio es en realidad una variante sobre una instancia del
     modelo FBX importado (el override vive sobre el `target` del propio modelo), no un
     prefab plano — el patrón de carga/guardado funciona igual.
   - **B.3** — `animationType: None`, `importAnimation: false` en los 41 FBX de
     `Assets/Models/Buildings/` (no solo los 6 usados por los prefabs actuales: toda la
     carpeta es geometría estática sin huesos). Sin errores tras reimportar; ciudad
     verificada visualmente sin cambios.
   - **B.4** — Antes de tocar `character-male-d.fbx` rastreé las transiciones reales de
     `PlayerAnimator.controller` (no me fié del "6" del hallazgo): el `Base Layer` tiene 30
     `AnimatorState` — uno por cada clip salvo el blend tree —, pero **solo 5 son alcanzables**
     desde `Entry`: `Locomotion` (blend de `idle`/`walk`/`sprint`) con transiciones a `Jump`
     (clip `jump`) y `Fall` (clip `fall`). Los otros 25 (incluidos `crouch` y `static`, que
     `CLAUDE.md` documentaba como parte de un grupo de "6 con loop activado") son estados sin
     transición de entrada — huérfanos, nunca alcanzables en juego. `clipAnimations` se
     recortó a esos 5 (`idle`, `walk`, `sprint`, `jump`, `fall`), no 6; los ~25 `AnimatorState`
     huérfanos quedan con `m_Motion` apuntando a un clip que ya no existe, inofensivo porque
     nunca se entra en ellos. Verificado en Play mode: el personaje anda, esprinta y salta sin
     errores en consola.
   - **B.2** — Migrados a mallas compartidas los prefabs ProBuilder-autorados de suelo/props
     donde es seguro (sin romper lógica de runtime): `RoadBase`, `RoadSidewalk`, `RoadDash`,
     `RoadZebra`, `Lawn`, `Bench`, `Bin`, `BusStop` — 15 mallas nuevas en `Assets/Meshes/`,
     componente `ProBuilderMesh` eliminado, `Mesh.UploadMeshData(true)` para `isReadable: 0`.
     **Excluidos deliberadamente**: `TrafficLight` (el runtime cambia `sharedMaterial` de
     renderers concretos por bombilla — combinar/rebautizar esas mallas rompería esa
     referencia) y `Fountain` (malla importada de glTF + solo 3 discos ProBuilder de agua,
     instancia única en la escena, bajo valor/mayor riesgo). `BusStop` anida una instancia de
     `Bench`, migrada primero para que la instancia anidada ya heredase el cambio sin
     duplicar trabajo.

     Con los prefabs arreglados, las **398 instancias ya colocadas en `City.unity`** seguían
     con su malla embebida (override `m_Mesh` de la instancia, que Unity no revierte solo
     porque el prefab base cambie). Reverting explícito con
     `PrefabUtility.RevertPropertyOverride` sobre la propiedad `m_Mesh` de cada `MeshFilter`
     bajo `City` — sin tocar posición/rotación/escala — afectó a **833 instancias**, la misma
     cifra que documentaba el hallazgo.

     **Hallazgo no anticipado por el informe**: revertir el override no basta para que
     `City.unity` baje de tamaño en disco. Unity no poda las mallas embebidas huérfanas de un
     `.unity` al guardar — quedan como bytes muertos en el fichero aunque ya no las referencie
     nadie. Comprobado: `m_Name: pb_Mesh` sigue devolviendo 833 coincidencias tras el revert,
     tras `AssetDatabase.ForceReserializeAssets`, e incluso tras cerrar y reabrir la escena
     desde disco y regrabar (mismo tamaño exacto, confirmando que no es un artefacto de la
     sesión de Editor en memoria). El tamaño de `City.unity` en disco pasó de 9 740 784 B a
     9 780 907 B (prácticamente igual, ligera subida neta por los propios overrides
     actualizados). **La ganancia real y perdurable de B.2 es para las ciudades que genere la
     tool de aquí en adelante** (`PrefabUtility.InstantiatePrefab` desde un prefab sin
     `ProBuilderMesh` no crea ningún override de malla, así que no hay nada que purgar) — para
     `City.unity` en concreto, purgar de verdad esas mallas muertas exigiría reconstruir la
     escena desde cero, lo que destruiría su disposición hecha a mano; no se ha hecho.
     Corrijo aquí la expectativa de la sección H.4 de este mismo documento: la comparación de
     tamaño de `City.unity` tras B.2 no debe esperar una caída significativa por esta razón.

   - **B.5** — Prefab `Lamp`: `Pole` + `PoleBase` + `Arm` (mismo material `MetalDark`)
     combinados con `Mesh.CombineMeshes` en un único `MeshRenderer` sobre `Pole` (mismo
     collider); `PoleBase` (sin collider) se eliminó por completo; `Arm` conserva su
     `GameObject`/`BoxCollider` pero pierde su renderer propio. `Head` (material `LampWarm`,
     distinto) se dejó como renderer independiente. Resultado: **4 → 2 `MeshRenderer`**, los 3
     `BoxCollider` intactos. Mismo problema de overrides que B.2: las ~108 instancias de
     `Lamp` ya colocadas en `City.unity` (no las ~100 que documentaba `CLAUDE.md`, cifra que
     debería corregirse allí) tenían sus propios overrides de malla para `Pole`/`Head` — 216
     reversiones (2 por lámpara) para que usen la malla combinada nueva.

   Verificación: compilación limpia en todo el proceso (0 errores/warnings salvo un aviso
   benigno y no relacionado de "Releasing render texture..." típico de las capturas de Scene
   View). Verificación visual con `Unity_SceneView_CaptureMultiAngleSceneView` tras cada paso
   (edificios, farolas, mobiliario urbano, sin geometría rota). Dos pases de Play mode de ~6 s
   cada uno (tras B.4 y al final de toda la Fase 3) sin errores ni warnings en consola,
   confirmando que el personaje anima y que el tráfico/colliders siguen funcionando.
5. **Cada vez que se aplique D** — comprobar visualmente el resultado del bake en esa
   ciudad concreta antes de darla por terminada.
