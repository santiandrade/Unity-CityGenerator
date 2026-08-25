# Informe técnico priorizado — Unity City Generator

## Resumen ejecutivo

El proyecto tiene una base sólida: package separado por ensamblados Editor/runtime, generación determinista, sensores `NonAlloc`, managers centralizados, caché de bounds, static batching y assets razonablemente contenidos. No recomiendo una migración a ECS/DOTS en el estado actual.

He encontrado cuatro problemas de prioridad crítica, principalmente relacionados con pérdida de datos, persistencia del grafo peatonal y compatibilidad con prefabs personalizados. Después, los mayores márgenes de rendimiento están en la búsqueda de solapamientos durante la generación, las consultas físicas del tráfico, el pathfinding peatonal y los assets de demostración.

Alcance revisado: 39 scripts del package —7.296 líneas—, builders, UI, runtime, 54 prefabs, 67 FBX, materiales, mallas, configuración URP/física, escena generada, specs, documentación y logs actuales. No encontré archivos de test o benchmark.

Estimaciones para una persona con experiencia en Unity:

- **XS:** unas horas.
- **S:** 1–2 días.
- **M:** 3–5 días.
- **L:** 1–2 semanas.
- **XL:** más de 2 semanas.

## Prioridad crítica

### 1. Hacer el “Re-Build” transaccional y recuperable

**Descripción:** El rebuild localiza el primer objeto raíz llamado `City`, lo destruye inmediatamente y después empieza la generación. Si cualquier prefab, layer, collider o builder produce una excepción, la ventana captura el error, pero la ciudad anterior ya se ha perdido y queda una ciudad parcial. Tampoco existe Undo. Además, un objeto del usuario llamado `City` puede confundirse con una ciudad generada.

Evidencia: [`CityGeneratorSceneBuilder.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorSceneBuilder.cs#L71) y [`CityGeneratorWindow.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorWindow.cs#L803).

**Mejora propuesta:** generar primero bajo un root temporal; si termina correctamente, sustituir el root anterior dentro de un grupo de Undo. Añadir un componente marcador, GUID o metadata propia para reconocer ciudades generadas sin depender del nombre.

**Coste:** S–M.

**Ganancia:** muy alta. Elimina riesgo de pérdida de trabajo, permite Ctrl+Z y hace segura la regeneración ante prefabs de terceros defectuosos.

---

### 2. Persistir correctamente los puntos de interés peatonales

**Descripción:** Los POI de bancos y fuentes se añaden al grafo durante la generación, pero los nodos viven en una lista privada no serializada. Al entrar en Play, `Awake()` reconstruye el grafo, `Build()` ejecuta `nodes.Clear()` y solo recrea anillos y cruces. Los POI añadidos por el builder desaparecen.

Evidencia: [`PedestrianNetwork.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianNetwork.cs#L88), [`PedestrianNetwork.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianNetwork.cs#L112) y [`CityGeneratorContentAssembler.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorContentAssembler.cs#L146).

**Mejora propuesta:** serializar descriptores de POI —posición, tipo, `LookAt` y conexión al bloque— en `PedestrianNetwork`. Cada `Build()` debe reconstruir primero la red base y después volver a incorporar esos descriptores antes de podar obstáculos. No recomiendo serializar directamente todas las listas runtime del BFS.

**Coste:** S–M.

**Ganancia:** alta. Recupera el comportamiento documentado de paradas junto a bancos y fuentes y hace que el grafo sea coherente entre Edit y Play.

---

### 3. Corregir layers y registro de colliders jerárquicos

**Descripción:** La herramienta promete aceptar colliders en cualquier punto de un prefab, pero asigna la layer `Vehicle`/`Pedestrian` únicamente al root. `CarAgent` también registra solo `GetComponent<Collider>()` en el root. Un collider situado en un hijo:

- puede conservar una layer distinta;
- puede quedar fuera del `vehicleMask`/`pedestrianMask`;
- aunque sea alcanzado, no aparece en `ColliderRegistry`;
- puede hacer que vehículos o peatones personalizados sean invisibles para los sensores.

Evidencia: [`CityGeneratorColliderUtility.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorColliderUtility.cs#L19), [`CityGeneratorTrafficBuilder.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorTrafficBuilder.cs#L200), [`CityGeneratorPedestrianBuilder.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorPedestrianBuilder.cs#L142) y [`CarAgent.cs`](../Packages/com.santiandrade.citygenerator/Runtime/CarAgent.cs#L120).

**Mejora propuesta:** establecer una política única. La más robusta sería crear en la instancia un collider proxy de sensor sobre el root y su layer correcta, conservando aparte los colliders físicos del usuario. Alternativamente, asignar layer y registro a todos los colliders hijos.

**Coste:** M.

**Ganancia:** muy alta. Hace real la compatibilidad con prefabs arbitrarios y evita vehículos atravesándose o ignorando peatones silenciosamente.

---

### 4. Resolver la contradicción entre `Include Traffic` y los semáforos

**Descripción:** Los semáforos se construyen siempre, antes de comprobar `includeTraffic`. Sin embargo, el validador solo exige `trafficLightPrefab` cuando `includeTraffic` está activo. En una rejilla con intersecciones interiores, desactivar tráfico y dejar el prefab vacío supera la validación pero falla durante la generación.

Evidencia: [`CityGeneratorContentAssembler.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorContentAssembler.cs#L128) frente a [`CityGeneratorValidator.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorValidator.cs#L47).

**Mejora propuesta:** dado que la especificación quiere semáforos incluso sin coches, exigir el prefab siempre que `gridWidth > 1 && gridHeight > 1`. En rejillas sin intersecciones interiores puede seguir siendo opcional.

**Coste:** XS.

**Ganancia:** alta. Evita una ruta de fallo reproducible y alinea UI, documentación y pipeline.

## Prioridad alta

### 5. Eliminar el override global e incondicional del framerate

**Descripción:** instalar el package basta para que `PerformanceBootstrap` desactive VSync y fuerce 60 FPS antes de cargar cualquier escena, incluso si el usuario nunca genera una ciudad. Es un efecto global poco apropiado para una tool instalable y puede perjudicar móviles, portátiles, consolas, monitores de alta frecuencia o juegos que gestionen su propio frame pacing.

Evidencia: [`PerformanceBootstrap.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PerformanceBootstrap.cs#L14).

**Mejora propuesta:** retirar el inicializador global o convertirlo en un componente/configuración explícitamente opt-in generado en la escena. La configuración debe poder diferenciar plataformas y respetar VSync.

**Coste:** XS–S.

**Ganancia:** alta en arquitectura y portabilidad; evita que el package cambie el comportamiento completo del proyecto consumidor.

---

### 6. Añadir una suite automatizada de regresión y rendimiento

**Descripción:** no hay tests ni benchmarks propios, pese a que el proyecto contiene algoritmos puros y generación determinista. Los fallos de POI, toggle de tráfico y colliders jerárquicos son casos que una suite pequeña habría detectado.

**Mejora propuesta:**

- EditMode: grid, porcentajes, validación, pesos de rutas, BFS, persistencia de POI y determinismo.
- PlayMode: semáforos, cruce peatonal, registro/desregistro de agentes, múltiples ciudades y prefabs con colliders hijos.
- Tests de generación con semillas fijas para 1×3, 5×5 y 10×10.
- Performance Testing: tiempo de generación, `GC Alloc`, tiempo de `Physics.SyncTransforms`, managers y memoria.

**Coste:** M–L.

**Ganancia:** alta. Reduce drásticamente regresiones y permite optimizar con datos en lugar de intuición.

---

### 7. Sustituir el solapamiento O(n²) por un índice espacial

**Descripción:** cada candidato aceptable compara su `Rect` contra toda la lista acumulada de obstáculos. La caché evita recalcular bounds, pero no evita que el número de comparaciones crezca cuadráticamente. La propia spec reconoce este límite y la UI lo mitiga restringiendo la rejilla a 10×10.

Evidencia: [`CityGeneratorPlacementEngine.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorPlacementEngine.cs#L93).

**Mejora propuesta:** introducir un hash espacial uniforme. Al insertar un obstáculo, registrar las celdas que ocupa; al probar un candidato, consultar únicamente sus celdas y vecinas. El grid procedural existente proporciona un tamaño de celda natural.

**Coste:** M.

**Ganancia:** alta en generación de rejillas grandes. El coste esperado pasa de O(n²) a aproximadamente O(n·k), con `k` limitado a obstáculos cercanos.

---

### 8. Reducir el coste físico del tráfico y condicionar `SyncTransforms`

**Descripción:** cada vehículo puede ejecutar dos `SphereCastNonAlloc` —vehículos y peatones— y `TrafficNetwork` llama a `Physics.SyncTransforms()` todos los frames. La red se genera siempre, por lo que el sync puede ocurrir incluso sin un solo coche.

Evidencia: [`CarAgent.cs`](../Packages/com.santiandrade.citygenerator/Runtime/CarAgent.cs#L321) y [`TrafficNetwork.cs`](../Packages/com.santiandrade.citygenerator/Runtime/TrafficNetwork.cs#L119).

**Mejora propuesta por etapas:**

1. Ejecutar el sync solo cuando haya agentes móviles registrados.
2. Mover su responsabilidad al manager, no al grafo.
3. Mantener un índice de ocupación por carril/segmento para localizar el coche delantero sin física.
4. Mantener una rejilla espacial compartida para peatones próximos a la calzada.

**Coste:** S para el guardado del sync; M–L para reemplazar sensores.

**Ganancia:** alta al superar 100–200 agentes; elimina uno de los principales costes de CPU y escala mejor que aumentar el escalonado de casts.

---

### 9. Escalar mejor pathfinding y separación peatonal

**Descripción:** cada peatón reserva un array del tamaño total del grafo. Al planificar destino prueba hasta ocho candidatos y puede ejecutar un BFS completo por cada uno. Todos los peatones planifican en `Start`, creando un pico concentrado. Además, el staggering solo omite decisiones; movimiento, animación, reconstrucción del grid, separación y evitación del jugador continúan cada frame para todos.

Evidencia: [`PedestrianAgent.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianAgent.cs#L68), [`PedestrianAgent.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianAgent.cs#L273), [`PedestrianNetwork.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianNetwork.cs#L419) y [`PedestrianManager.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianManager.cs#L67).

**Mejora propuesta:** calcular componentes conexas y escoger destinos alcanzables directamente; repartir la planificación inicial entre varios frames; reutilizar rutas o árboles BFS por origen; usar buffers compactos/pooling. En separación, procesar cada pareja una sola vez y reducir la frecuencia para agentes lejanos.

**Coste:** M–L.

**Ganancia:** media-alta. Reduce picos al entrar en Play, memoria O(peatones × nodos) y coste de multitudes grandes.

---

### 10. Completar la validación de inputs y prefabs

**Descripción:** la validación actual cubre referencias principales y porcentajes, pero quedan huecos:

- listas de edificios o vegetación con elementos `null`;
- conteos de vehículos/peatones validados aunque su toggle esté desactivado;
- velocidades cero o referencias de animación iguales, capaces de producir divisiones por cero/NaN;
- radios, duraciones, tamaños de celda y distancias negativos;
- nombres de acciones inexistentes o tipos de acciones incompatibles;
- `CharacterController.stepOffset`, radio, altura y skin width incoherentes;
- prefabs sin renderers aceptados con un footprint ficticio de 0,5 m.

**Mejora propuesta:** validación semántica separada por módulos, con errores bloqueantes y warnings no bloqueantes. Las condiciones deben depender del toggle que realmente activa cada sistema.

**Coste:** S–M.

**Ganancia:** alta. Evita fallos tardíos, NaN en movimiento/Animator y ciudades parcialmente generadas.

---

### 11. Separar las herramientas internas de desarrollo del package distribuido

**Descripción:** “Set Current Selection As Default” viaja en el package y sobrescribe archivos fuente del propio package mediante `File.WriteAllText`. En una instalación Git/Package Cache esos archivos pueden no ser editables o no existir bajo la ruta física asumida. Tampoco hay operación atómica: el primer archivo puede quedar actualizado aunque el segundo falle.

Evidencia: [`CityGeneratorWindow.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorWindow.cs#L91) y [`CityGeneratorDefaultAssetsWriter.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorDefaultAssetsWriter.cs#L35).

**Mejora propuesta:** mover este comando a `Assets/Editor`, junto a la herramienta de release, o mostrarlo únicamente cuando `PackageInfo.source` sea `Embedded`/`Local`. Para usuarios finales, ofrecer presets en `Assets/` mediante `ScriptableObject`, sin reescribir C#.

**Coste:** S–M.

**Ganancia:** alta para la fiabilidad del package y la separación entre tooling de autor y API pública.

## Prioridad media

### 12. Marcar correctamente la geometría que debe participar en lightmaps

Actualmente solo se aplican `BatchingStatic`, `OccluderStatic` y `OccludeeStatic`; la escena utiliza flags `22`, sin `ContributeGI`. Esto contradice la afirmación de que la ciudad queda lista para hornear lightmaps.

Evidencia: [`CityGeneratorContentAssembler.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorContentAssembler.cs#L48).

**Coste:** XS.

**Ganancia:** media. El bake de GI funcionará según lo documentado. Si no se desea marcar todo automáticamente, debe corregirse el README.

---

### 13. Eliminar singletons globales de managers y corregir su ciclo de vida

`TrafficManager.Instance` y `PedestrianManager.Instance` son globales, aunque una escena puede contener varias ciudades o cargarse aditivamente. El último `Awake` gana. Además, los agentes se registran en `Start` y se desregistran en `OnDisable`; si vuelven a habilitarse, `Start` no se repite y dejan de ser actualizados.

Evidencia: [`TrafficManager.cs`](../Packages/com.santiandrade.citygenerator/Runtime/TrafficManager.cs#L25) y [`PedestrianManager.cs`](../Packages/com.santiandrade.citygenerator/Runtime/PedestrianManager.cs#L41).

**Mejora propuesta:** obtener el manager desde la red serializada o desde el mismo root de ciudad y hacer registro idempotente en `OnEnable`.

**Coste:** M.

**Ganancia:** media-alta en arquitectura, escenas aditivas, streaming y reactivación de agentes.

---

### 14. Limpiar importadores y colliders del contenido demo

Los 15 FBX de vehículos importan animación y usan `animationType: Humanoid`, aunque los coches no la necesitan. El árbol tiene dos `MeshCollider` para las copas, y papelera/fuente también usan `MeshCollider`. Además, los personajes demo ya incluyen `CapsuleCollider`; al usarlos como jugador se añade también un `CharacterController`, dejando dos colliders móviles.

Evidencia: [`sedan.fbx.meta`](../Packages/com.santiandrade.citygenerator/DefaultAssets/Models/Cars/sedan.fbx.meta#L86), [`Tree.prefab`](../Packages/com.santiandrade.citygenerator/DefaultAssets/Prefabs/Vegetation/Tree.prefab#L95), [`Character-Male-D.prefab`](../Packages/com.santiandrade.citygenerator/DefaultAssets/Prefabs/Characters/Character-Male-D.prefab#L73) y [`CityGeneratorSceneBuilder.cs`](../Packages/com.santiandrade.citygenerator/Editor/CityGeneratorSceneBuilder.cs#L105).

**Mejora propuesta:** desactivar rig/animación en coches, eliminar animaciones embebidas no usadas en personajes secundarios, sustituir colliders decorativos por uno primitivo y desactivar el collider ordinario al convertir un personaje en Player.

**Coste:** S–M.

**Ganancia:** media: menos componentes de animación, memoria/importación y carga física.

---

### 15. Añadir LOD al contenido de demostración

Ninguno de los prefabs demo tiene `LODGroup`. No recomiendo que la tool invente automáticamente LOD para prefabs del usuario, pero sí que edificios, árboles y vehículos incluidos ofrezcan LOD/culling razonable.

**Mejora propuesta:** priorizar árboles, edificios altos y vehículos; añadir al menos LOD0, LOD1 simplificado y culling. Para una primera fase barata, usar LOD0 + culling y completar las mallas simplificadas después.

**Coste:** M–L.

**Ganancia:** alta en GPU, sombras y ancho de banda para ciudades 10×10; baja en la escena pequeña por defecto.

---

### 16. Centralizar la propiedad del Input System

`PlayerController` y `ThirdPersonCamera` habilitan y deshabilitan el mismo `InputActionMap`. Deshabilitar uno de los componentes puede cortar la entrada del otro. La cámara tampoco restaura explícitamente cursor y visibilidad en `OnDisable`.

**Mejora propuesta:** una única autoridad —por ejemplo `PlayerInput` o un controlador de input— habilita el mapa; movimiento y cámara solo consumen acciones/referencias.

**Coste:** S.

**Ganancia:** media en robustez y reutilización de los componentes.

## Prioridad baja o condicionada a medición

### 17. Reducir GameObjects de marcas viales mediante chunking

La combinación total ya fue probada y correctamente descartada porque rompe el culling. La alternativa válida es combinar por segmento de calle, intersección o bloque, conservando bounds pequeños.

**Coste:** M.

**Ganancia:** media únicamente en ciudades grandes: menos GameObjects, transforms y coste de jerarquía/importación. Debe medirse antes.

### 18. Dividir `CityGeneratorWindow`

La ventana tiene 894 líneas y concentra UI, validación, defaults, generación, resultados y acciones de mantenimiento. Separar presenters/controladores por pestaña y un servicio de generación facilitaría testear sin abrir UI.

**Coste:** M.

**Ganancia:** media en mantenibilidad; prácticamente nula en FPS.

### 19. Actualizar documentación y metadata

`docs/technical-review.md` afirma que todo está resuelto y describe una versión anterior a los peatones. `docs/pedestrian-network-plan.md` todavía dice que no está implementado. La descripción de `package.json` omite peatones, jugador y cámara.

**Coste:** XS.

**Ganancia:** media en mantenimiento y decisiones futuras; evita que se descarten problemas basándose en una auditoría obsoleta.

## Mejoras que no recomiendo ahora

- **ECS/DOTS:** no compensa para 80 coches y 90 peatones. Antes deben eliminarse el O(n²) del generador, los casts físicos y los BFS redundantes. Jobs/Burst solo tendría sentido después de perfilar varios cientos o miles de agentes.
- **Object pooling:** no hay creación/destrucción frecuente en runtime; los agentes viven toda la sesión.
- **Combinar toda la señalización en una única malla:** degradaría frustum/occlusion culling.
- **Activar GPU instancing indiscriminadamente:** el proyecto ya usa SRP Batcher y GPU Resident Drawer; debe decidirse con Frame Debugger, no como checkbox general.

## Orden de trabajo recomendado

1. Corregir rebuild transaccional, POI, colliders jerárquicos y validación de semáforos.
2. Retirar el override global de framerate.
3. Crear tests de regresión para esos casos.
4. Instrumentar y medir generación 1×3, 5×5 y 10×10.
5. Introducir índice espacial para colocación.
6. Medir 60/150/300 coches y peatones; después optimizar sensores, sync, BFS y separación.
7. Limpiar importadores/colliders y añadir LOD al contenido demo.
8. Finalmente abordar modularización y documentación.

Criterios mínimos de medición: 300 frames por escenario, CPU/GPU frame time, `GC Alloc/frame`, coste de `Physics.SyncTransforms`, `SphereCast`, `PedestrianManager.Update`, batches/SetPass, memoria de meshes/animación y tiempo total de generación.

La revisión original fue completamente de solo lectura. No se ejecutó Play Mode ni profiling en vivo para evitar que Unity modificase escena, `Library` o configuración; por tanto, las ganancias de rendimiento indicadas son estimaciones arquitectónicas que conviene confirmar con los benchmarks propuestos.
