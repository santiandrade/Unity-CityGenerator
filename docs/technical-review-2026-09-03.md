# Revisión de implementación — City Generator

**Revisión:** 3 de septiembre de 2026  
**Snapshot auditado:** `santiandrade/Unity-CityGenerator`, tag `v2.10.0`, commit `e025ba819d02f931be0bd16326790236062d2741`  
**Alcance:** package distribuible `Packages/com.santiandrade.citygenerator/`, tests, documentación, release y configuración de proyecto.  
**No se ha modificado el repositorio.**

## Veredicto ejecutivo

La implementación está en muy buena forma para un package Unity distribuible: separación `Editor`/`Runtime` mediante asmdefs, generación determinista con semilla, rebuild transaccional, tests EditMode/PlayMode/Performance, índices espaciales y managers centralizados. El informe técnico previo no está maquillado: las optimizaciones principales ya constan implementadas y documentadas.

No recomiendo una reescritura hacia ECS/DOTS, object pooling ni combinar globalmente las marcas viales: el propio código y las mediciones del proyecto justifican que aportarían poco o romperían culling/editabilidad. Sería una de esas limpiezas arquitectónicas que dejan el suelo brillante mientras el producto sigue sin vender más. Magnífico para el ego; menos para el roadmap.

La mejor inversión ahora no es más micro-optimización: es **automatizar la validación de releases**, endurecer el contrato público nuevo de Runtime API y reducir los puntos de acoplamiento que harán más caro seguir añadiendo funcionalidades.

## Corrección tras auditoría independiente

La auditoría paralela ha identificado un **fallo funcional reproducible** que eleva la prioridad de trabajo inmediato:

| Prioridad | Corrección | Ganancia / qué afecta | Coste | Evidencia y solución |
|---|---|---|---|---|
| **P0** | **Unificar la detección y validación de intersecciones señalizadas** | Elimina un fallo de generación en grids `1×2`, `2×1` y formas Custom con intersecciones T, incluso con tráfico desactivado. Afecta a integradores sin `trafficLightPrefab`. | **1–2 días** | `CityGeneratorValidator.cs:92–105` exige semáforo solo con grid rectangular >1×1 o Custom 2×2; `CityGeneratorTrafficBuilder.cs:82–108` genera semáforos para cualquier intersección con ≥3 brazos. La validación puede permitir una configuración que luego intenta instanciar un prefab nulo. Extraer un único predicado compartido y añadir regresiones EditMode/Generation. |

También suben de prioridad dos medidas de robustez del rebuild:

| Prioridad | Mejora | Ganancia / qué afecta | Coste | Evidencia y solución |
|---|---|---|---|---|
| **P1** | **Extender la transacción de Rebuild hasta el commit final** | Preserva de verdad la ciudad/HUD anterior ante excepciones tardías. Afecta solo al flujo de reconstrucción de escena. | **3–5 días** | `CityGeneratorSceneBuilder.cs:123–134` protege `Assemble` y snapshot, pero HUD/luz y la destrucción de la ciudad anterior ocurren después (`:136–159`) fuera del `try/catch`. Construir todos los recursos candidatos primero, confirmar el commit mediante Undo y restaurar estado completo ante cualquier excepción. |
| **P1** | **Eliminar búsquedas globales en rebuild y runtime** | Evita que una ciudad/HUD/luz/minimapa afecte a otra en escenas aditivas. Afecta a usuarios avanzados y a la API pública. | **5–8 días** | `SceneBuilder.cs:139,156,158,185` usa `GameObject.Find`; la Runtime API busca y cachea la primera ciudad. Pasar referencias por `CityGeneratorRoot`/`CityGeneratorInfo`, limitar búsquedas a la escena/raíz y exponer handles explícitos. |

## Base verificable y límites

- **Tamaño de código:** 61 ficheros C# y 14.154 líneas en el package.
- **Cobertura existente:** 13 ficheros de EditMode (98 métodos `[Test]`/`[TestCase]`), 5 de PlayMode (14 métodos) y 2 suites de Performance.
- **Rendimiento ya medido por el proyecto:** SPEC 05 registra -17,2 % en generación 10×10 y -7,0 % en runtime con 300 vehículos + 300 peatones frente al baseline.
- **Release:** versión de `package.json`, tag y GitHub Release coinciden en `v2.10.0`.
- **CI:** no existe `.github/workflows/`.
- **Ejecución Unity:** no hay binario Unity disponible en esta máquina, así que no he podido ejecutar Test Runner/PlayMode localmente. Los resultados de pruebas se han auditado por código, configuración y la evidencia documentada del repositorio; no se presentan como una ejecución nueva.

## Backlog priorizado por ganancia / coste

Coste estimado como esfuerzo de una persona Unity senior. Es una referencia de planificación, no un presupuesto; los assets 3D y la calidad visual pueden convertir cualquier "dos tardes" en una excavación arqueológica.

| Prioridad | Mejora | Ganancia / qué afecta | Coste | Evidencia y enfoque |
|---|---|---|---|---|
| **P0** | **CI reproducible de compilación, tests y validación de package** | Evita publicar regresiones, incompatibilidades de Unity o errores de empaquetado. Afecta a cada PR y release; no cambia runtime. | **4–6 días** | No hay `.github/workflows/`. Ejecutar EditMode + PlayMode en batchmode sobre Unity 6000.0 y la versión exacta de desarrollo; validar `package.json`, tag/CHANGELOG y instalación limpia desde Git. Publicar resultados JUnit y artefactos de Performance. Bloquear release si falla la suite funcional; mantener benchmarks como informativos al principio. |
| **P1** | **API runtime con contexto explícito de ciudad, no singleton estático global** | Hace segura la API en escenas aditivas, varias ciudades y ciclo de vida de objetos. Afecta a integradores que consuman `CityGeneratorAPI`; reduce bugs silenciosos muy caros de depurar. | **3–5 días** | `Runtime/API/CityGeneratorAPI.cs:12–15` declara que cachea una única ciudad y no invalida la referencia. Sin embargo, `TrafficManager` y `PedestrianManager` se diseñan para coexistir por ciudad. Introducir `CityGeneratorHandle`/referencia a `CityGeneratorInfo`, consultas por handle y una conveniencia `Default`; invalidar correctamente al destruir/cambiar escena. Mantener la API actual marcada como compatibilidad/deprecated en la siguiente major. |
| **P1** | **Cubrir API, minimapa y cámara libre con tests de comportamiento** | Protege las tres funcionalidades con superficie pública/visible más reciente. Afecta a confianza de integradores y futuras refactorizaciones; coste bajo frente a una regresión post-release. | **2–4 días** | La búsqueda en `Assets/Tests` no encuentra referencias a `CityGeneratorAPI`, `MinimapHUD`, `FreeCameraController` ni `CityGeneratorWindow`. Añadir Edit/PlayMode para ausencia de ciudad, habilitar/deshabilitar ciclo día-noche, cambio de radio/visibilidad del HUD, cambio de escena y API con dos ciudades. |
| **P1** | **Cache BFS acotada y benchmark peatonal que mida régimen estable** | Evita crecimiento de memoria próximo a O(n²) y que las decisiones de escalado se basen en una carga artificialmente baja. Afecta a multitudes grandes, no a la experiencia normal de decenas de agentes. | **3–5 días** | `PedestrianNetwork.cs:157–160,972–1014` guarda un árbol `int[nodos]` por origen hasta `Build()`. Además, con 300 peatones el plan inicial se retrasa hasta ~2,99 s, pero `RuntimePerformanceTests.cs:47–73` comienza a medir tras tres frames. Aplicar LRU/TTL o presupuesto de caché; medir arranque y steady-state una vez todos los agentes tengan ruta. |
| **P1** | **Separar `CityGeneratorWindow` en presentador, estado y composición de cards** | Reduce el coste marginal de cada nueva pestaña/campo y hace testeable la validación/UI sin abrir una ventana Unity completa. Afecta mantenibilidad del Editor, no el runtime generado. | **4–6 días** | `Editor/CityGeneratorWindow.cs` tiene 1.234 líneas, conoce las seis tabs, cards, validación, progreso y acciones de build. Extraer `CityGeneratorWindowState`/presenter, un registro declarativo de tabs/cards y un servicio de validación puro. Hacerlo incremental, sin reescribir UXML/USS ni alterar UX. |
| **P2** | **Observabilidad de rendimiento utilizable, no solo resultados manuales** | Permite decidir con datos cuándo subir límites de grid/agentes y detectar regresiones de CPU/GC. Afecta al desarrollo y soporte, no a builds de usuario. | **2–3 días** | Las pruebas de Performance miden el frame completo y documentan que no hay `ProfilerMarker`s propios (`RuntimePerformanceTests.cs:11–17`). Añadir markers a ensamblado, pathfinding, tráfico, separación y minimapa; exportar JSON/CSV desde batchmode y comparar contra un baseline con tolerancias de aviso, no bloqueo inicial. |
| **P2** | **Presupuesto visual y LOD solo para el contenido demo** | Es la mejora con mayor potencial GPU en ciudades grandes, especialmente sombras y overdraw; no debe imponer LOD automático a prefabs del cliente. | **7–12 días** | El README deja LOD en manos del proyecto consumidor, una decisión correcta para prefabs ajenos. Implementar LOD/culling en edificios altos, árboles y vehículos de `DefaultAssets`, con un perfil de calidad reproducible y una escena 10×10 de referencia. Medir batches, VRAM, GPU ms y calidad visual antes/después. |
| **P3, condicional** | **Planificación de rutas y comportamiento de tráfico de alta densidad** | Aumenta la capacidad útil de tráfico y reduce gridlock; es producto/simulación, no una optimización cosmética. Afecta a `TrafficNetwork`, `CarAgent`, UI de densidad y tests. | **6–10 días** | README y `docs/technical-review.md` identifican la falta de route planning como el techo actual, no el coste del tick. Empezar con rutas por objetivos y replanificación limitada ante bloqueo; definir KPI (flujo, tiempo medio detenido, porcentaje de coches bloqueados) antes de tocar código. Solo ejecutar si "tráfico denso" es una propuesta de valor real. |

## Hallazgos técnicos que motivan el orden

### 1. La entrega está protegida por tests, pero no por una puerta de calidad automatizada

La suite es razonable y SPEC 05 incluye mediciones bien planteadas. Sin CI, sin embargo, su ejecución depende de disciplina manual y las métricas se copian a documentación. Eso es suficiente hasta que deja de serlo, normalmente a las 23:58 de una release. La automatización aporta más reducción de riesgo por euro que otra optimización interna.

### 2. La Runtime API introducida en 2.10 tiene una contradicción de arquitectura

`CityGeneratorAPI` usa `FindFirstObjectByType<CityGeneratorInfo>()` y guarda un cache global sin invalidación. El propio comentario limita el diseño a una ciudad por sesión. A la vez, los managers evitan el singleton y están pensados para ciudades independientes. El API debe adoptar la misma regla: la ciudad es una instancia con identidad, no "la primera que Unity devuelva".

### 3. La ventana del Editor funciona, pero concentra demasiadas responsabilidades

No es un bug urgente. Sí es deuda con intereses: las próximas features (presets, import/export, perfiles de plataforma, nuevas categorías) obligarán a modificar un fichero grande con estado UI, rutas de assets, validación y build en el mismo lugar. Una extracción incremental ahora reduce riesgo antes de que el fichero sea un jefe final con barra de vida propia.

### 4. Rendimiento: el proyecto ya hizo las optimizaciones correctas

El proyecto ya tiene hash espacial de placement, ocupación por carril, grid de proximidad peatonal, staggering, path-buffer pooling y deduplicación de separación. La evidencia de SPEC 05 respalda la inversión: ganancia creciente con carga sin cambiar el resultado determinista. Por tanto:

- **No** migrar a ECS/DOTS ahora: impondría dependencias y una reescritura completa para un problema que hoy no domina.
- **No** usar pooling de GameObjects: no hay `Instantiate`/`Destroy` en runtime que lo justifique.
- **No** combinar todas las marcas viales en una única malla: degrada culling y editabilidad de prefabs.
- **Sí** medir GPU/VRAM del contenido demo y abordar LOD si el objetivo comercial incluye 10×10 o dispositivos modestos.

## Plan recomendado

### Fase 0 — Corregir fallo de generación (1–2 días)

1. Unificar el predicado de intersección señalizada entre validador y builder.
2. Añadir casos de regresión: `1×2`, `2×1` y Custom Grid con intersección T, con y sin tráfico.
3. Decidir y documentar una sola política: si se generan luces con tráfico apagado, el prefab es requisito; si no, el builder no debe instanciarlas.

**Resultado:** no se vuelve a aceptar una configuración que falle después en generación.

### Fase 1 — Seguridad de entrega (6–10 días)

1. CI batchmode para Unity 6000.0 + versión de desarrollo, EditMode y PlayMode.
2. Tests de la Runtime API, minimapa y cámara libre.
3. Gate de release con coherencia tag/version/CHANGELOG.
4. Medición de rendimiento tras estabilizar los peatones, no tras tres frames.

**Resultado:** cada release tiene una evidencia reproducible de que compila, instala y pasa regresión funcional.

### Fase 2 — Contrato y mantenibilidad (7–11 días)

1. API contextual por ciudad, con plan de compatibilidad.
2. Extraer estado/presentador de `CityGeneratorWindow` sin alterar UI.
3. ProfilerMarkers y export de baseline.

**Resultado:** se puede extender el package sin aumentar linealmente el riesgo y se puede explicar dónde se gasta el frame.

### Fase 3 — Producto escalable, solo si hay demanda (13–22 días)

1. LOD y presupuesto visual del demo.
2. Route planning de tráfico con KPIs funcionales.

**Resultado:** ciudades grandes más atractivas y tráfico que sirve a una experiencia de simulación, no solo de ambientación.

## Criterios de aceptación sugeridos

- CI verde en una instalación limpia desde Git URL, con EditMode y PlayMode publicados como artefactos.
- Test con dos ciudades en escenas aditivas: cada handle de API devuelve y modifica solo su ciudad.
- Ningún cambio de la Fase 2 altera una generación con misma semilla salvo que esté documentado y aprobado.
- El refactor de UI conserva los tests existentes y añade validación unitaria de los errores/badges por tab.
- Antes de LOD/tráfico: captura de 300 frames y baseline GPU/CPU/VRAM; después, comparación equivalente y KPI acordado.

## Conclusión

**Prioridad real:** CI + tests API primero; contexto de ciudad después; refactor UI a continuación. Todo lo demás debe responder a una decisión de producto cuantificable: ¿quieres vender/usar ciudades de mayor escala o tráfico denso? Si no, meter DOTS ahora sería cambiar el motor de un coche que ya va bien porque nos gustan los motores. Y a mí me gustan, pero no tanto.
