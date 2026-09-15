# SPEC 18 — Marcadores dinámicos del minimapa

> **Estado:** Aprobada
> **Fecha:** 2026-09-15
> **Proyecto:** Unity-CityGenerator (`santiandrade/Unity-CityGenerator`)
> **Depende de:** SPEC 07 (Minimap HUD), SPEC 15 (Runtime API por ciudad), SPEC 16 (varias ciudades)
> **Objetivo:** Abrir el HUD del minimapa a marcadores registrados en tiempo de ejecución, con prefab propio, seguimiento de un Transform o posición fija y clamp opcional al borde, conservando los POI actuales.

## Contexto y problema

El paquete instalable vive en `Packages/com.santiandrade.citygenerator/`. La funcionalidad debe pertenecer a su ensamblado Runtime y al namespace `CityGenerator.Runtime`, sin depender de assets del proyecto consumidor ni de código Editor.

Actualmente `Runtime/MinimapHUD.cs` dibuja los POI de `MinimapData.pointsOfInterest` mediante un pool de clones de una plantilla común. En `LateUpdate`, calcula el desplazamiento XZ respecto al transform seguido por el HUD, oculta los puntos fuera de `viewRadiusMeters` y convierte el desplazamiento a unidades locales de UI usando el ancho del mapa. El mapa mantiene el norte arriba; solo el marcador del jugador gira.

`Runtime/API/CityGeneratorCity.cs` ya expone `MinimapModule` mediante `city.Minimap`, con operaciones de visibilidad y radio. Falta una API para registrar objetivos de juego —recogidas, destinos o elementos móviles— sin convertirlos en POI generados ni modificar el paquete desde el juego consumidor.

Esta spec amplía deliberadamente la exclusión de elementos dinámicos de SPEC 07. No reescribe aquella decisión histórica ni cambia el funcionamiento de los POI existentes.

## Objetivo

- Registrar y retirar marcadores durante Play Mode y en builds.
- Aceptar un prefab `RectTransform` por registro.
- Seguir un `Transform` móvil o conservar una posición fija de mundo.
- Mantener el marcador completamente visible cerca del borde cuando se solicita clamp, dentro del contrato geométrico del prefab.
- Reutilizar la proyección de los POI sin alterar su comportamiento.
- Ofrecer acceso directo por `MinimapHUD` y acceso delegado por `CityGeneratorCity.Minimap`.

## Fuera de alcance

- Modificar el prefab, la posición fija o las opciones de un registro mediante el handle. Para cambiar un registro fijo, eliminarlo y darlo de alta otra vez; para movimiento continuo, usar Transform.
- Rotación automática de iconos según el objetivo, flechas orientadas, animaciones gestionadas por el HUD o rediseño visual.
- Clicks, selección, navegación, interacción o mapa a pantalla completa.
- Persistencia, serialización de registros dinámicos o modificación de `MinimapData`.
- Registro automático de vehículos, peatones u otras categorías.
- Rediseñar la selección de ciudad/datos del HUD, introducir un registro global o ampliar las restricciones de traslación/rotación/escala de ciudades.
- Cambiar los POI generados, sus etiquetas, plantilla, contador o regla de ocultación.
- Publicar una versión, modificar la versión del paquete, crear ramas, commits o push como parte de la preparación de esta spec.

## Decisiones de diseño

### API pública

Los siguientes métodos se incorporan tanto a `MinimapHUD` como a `MinimapModule`:

```csharp
public MinimapMarkerHandle AddMarker(
    Transform target,
    RectTransform prefab,
    bool clampToEdge);

public MinimapMarkerHandle AddMarker(
    Vector3 position,
    RectTransform prefab,
    bool clampToEdge);

public void RemoveMarker(MinimapMarkerHandle handle);
```

- `MinimapModule` delega en el `info.minimapHUD` ya referenciado; no busca otro HUD como fallback.
- `Vector3 position` es una posición de mundo capturada al dar de alta. La altura Y no afecta a la proyección.
- El overload Transform consulta su posición de mundo en las actualizaciones del HUD.
- `MinimapMarkerHandle` es un identificador opaco con propiedad pública `bool IsValid`, ligado a la identidad de su HUD y registro. No expone operaciones para editar el marcador ni necesita exponer la instancia UI.
- Un handle por defecto, retirado o cuyo HUD/registro ha sido destruido es inválido. Una nueva alta no debe hacer válido un handle retirado.
- Cada llamada válida crea un registro independiente, aunque se repitan objetivo y prefab.
- La API es de runtime y se utiliza en el hilo principal de Unity.

### Propiedad y separación de datos

Cada HUD mantiene sus propios registros e instancias UI. Dos HUD no comparten registros automáticamente, aunque compartan datos o textura de minimapa. Dos accesos que referencien el mismo HUD operan sobre ese mismo propietario.

Las instancias dinámicas se mantienen separadas del pool de POI y de `MinimapData.pointsOfInterest`. `PointOfInterestCount` conserva su significado actual. El prefab se instancia bajo la jerarquía UI gestionada por el HUD; nunca se modifica el asset original.

### Proyección compartida

Extraer o reutilizar una única operación de proyección para POI y marcadores dinámicos, conservando la fórmula actual:

```text
worldOffset = (targetWorld.x - playerWorld.x,
               targetWorld.z - playerWorld.z)
pixelsPerMeter = mapRect.rect.width / (2 * viewRadiusMeters)
uiOffset = worldOffset * pixelsPerMeter
```

La referencia central sigue siendo el transform que sigue el HUD, no el objetivo del marcador. El término `pixelsPerMeter` corresponde a la convención del código existente: unidades locales del RectTransform, no necesariamente píxeles físicos de pantalla tras CanvasScaler.

No modificar el norte fijo, la rotación del jugador, los UV sin clamp, el material `MinimapWindow` ni la selección de `MinimapData`.

## Comportamiento detallado

### Alta y presentación

1. Resolver el HUD propietario y validar las referencias necesarias para la alta.
2. Si no existe HUD, el prefab es nulo o el overload Transform recibe un objetivo nulo/destruido, devolver un handle inválido sin crear objetos.
3. Registrar el objetivo o posición, el prefab instanciado y el flag de clamp.
4. Admitir altas y bajas antes del primer `Start` y con el HUD oculto. No exigir que su inicialización visual ya haya terminado; no mostrar instancias en una posición provisional incorrecta.
5. Cuando el HUD disponga de sus datos y referencia de seguimiento, actualizar los marcadores en su ciclo de actualización visual, junto con los POI.

Un HUD existente pero sin datos suficientes para renderizar no debe fabricar datos ni cambiar su mecanismo actual de inicialización/autoocultación. Un registro aceptado puede permanecer sin representación visible hasta que el HUD pueda actualizarse.

### Radio y clamp

- **Sin clamp:** aplicar la misma regla que a los POI: ocultar cuando la distancia XZ supera el radio; mostrar al regresar. En el límite exacto del radio, se conserva la regla actual de los POI.
- **Con clamp:** partir de la misma proyección, pero limitar su magnitud al radio interior seguro para el icono, sin cambiar la dirección hacia el objetivo.
- El margen se obtiene de las dimensiones y del pivot del `RectTransform` raíz de la instancia, expresados en el espacio UI del mapa. No basta con restar un margen fijo independiente del prefab.
- El RectTransform raíz debe abarcar todo el contenido visual del prefab. Contenido que sobresalga de ese rectángulo no queda cubierto por la garantía.
- Una implementación conservadora puede usar como margen la máxima distancia desde el pivot a las esquinas del rectángulo en el espacio del mapa y restarla al radio del círculo. Puede usar un ajuste más preciso si conserva la dirección y demuestra por pruebas que el icono completo queda dentro.
- El ajuste interior también se aplica cuando el punto está dentro del radio de mundo pero tan cerca del borde que el icono se recortaría. Lejos de ese margen, la posición coincide con la proyección compartida.
- En el centro, el resultado es el centro, sin normalizar un vector nulo.
- Si el icono es mayor que el área disponible, no puede garantizarse que quepa. Se documenta la limitación; el cálculo no debe producir un radio negativo ni invertir la dirección del marcador.
- Los cambios de radio visible o dimensiones relevantes del mapa/icono deben reflejarse en las siguientes actualizaciones visuales; no cachear permanentemente una geometría que haya dejado de ser válida.

### Ciclo de vida

- Objetivo Transform activo: seguir su posición.
- Objetivo con `gameObject.activeInHierarchy == false`: ocultar el marcador conservando el registro. Al reactivarse, volver a actualizar y mostrar si corresponde.
- Objetivo destruido: retirar automáticamente el registro y su instancia UI al procesar la siguiente actualización de mantenimiento disponible. No dejar un icono congelado ni lanzar errores por referencias Unity destruidas.
- Posición fija: permanece registrada hasta la baja explícita o destrucción del HUD.
- Ocultar el HUD conserva los registros y oculta sus instancias junto con la jerarquía. Al reactivarlo, reconciliar los objetivos destruidos/inactivos antes de mostrarlos; un HUD inactivo no ejecuta LateUpdate.
- Destruir el HUD elimina registros e instancias y hace inválidos sus handles.
- `RemoveMarker` retira el registro y deja de mostrar el icono; la destrucción del GameObject puede completarse al final del frame conforme a Unity.
- Retirar dos veces, retirar un handle por defecto/inválido o entregarlo a un HUD diferente es un no-op. Nunca retirar otro registro por coincidencia de identificador local.

## Plan de implementación

1. **Handle y registro runtime.** Añadir el tipo público `MinimapMarkerHandle` y almacenamiento por instancia en `MinimapHUD`. Garantizar identidad estable, invalidación y validaciones de alta.
2. **Proyección común sin regresiones.** Aislar la fórmula existente de los POI y cubrirla con pruebas de caracterización antes de usarla para los dinámicos. Mantener su pool y su condición de radio.
3. **Instancias UI y ciclo visual.** Instanciar el prefab sin modificar el asset, disponer un espacio de posicionamiento compatible con el mapa y aplicar seguimiento, visibilidad, clamp y limpieza. Reutilizar las referencias existentes del HUD cuando sea viable para conservar compatibilidad con escenas ya generadas.
4. **Fachada de API.** Incorporar ambos overloads y la baja a `MinimapModule`, delegando en la referencia existente sin búsquedas globales.
5. **Pruebas.** Ampliar `Assets/Tests/PlayMode/MinimapHUDTests.cs` o añadir una suite vecina para marcadores y cubrir la fachada en las pruebas de API. Añadir pruebas geométricas donde encajen en la estructura existente.
6. **Documentación de implementación.** Actualizar `docs/api-reference.md` y `docs/api-reference.es.md` con firmas, ejemplos de alta/baja, espacios de coordenadas, ciclo de vida y contrato geométrico. Documentar el mecanismo en `docs/architecture/editor-tool.md` y registrar la funcionalidad en `Packages/com.santiandrade.citygenerator/CHANGELOG.md`, sin publicar release.

No introducir dependencias externas, pooling por prefab ni optimizaciones complejas sin una necesidad demostrada. Evitar búsquedas globales por frame y nuevas asignaciones administradas evitables durante el seguimiento estable.

## Datos, seguridad y compatibilidad

- Datos exclusivamente en memoria durante runtime; sin persistencia ni migración de POI o snapshots.
- No modificar prefabs fuente, assets del consumidor ni Project Settings.
- No depender de `Assets/Scenes/City.unity`: es escena de prueba, no ubicación del arreglo.
- Mantener el paquete portable y el namespace/asmdef runtime existente.
- Los consumidores aportan sus prefabs; los componentes que contengan se ejecutan bajo las reglas habituales de instanciación de Unity.
- Compatibilidad con escenas/HUD existentes: no exigir regeneración solo para disponer de la nueva API. Si hacen falta contenedores auxiliares, resolverlos de forma compatible en runtime sin alterar el pool actual de POI.
- Los registros son locales al HUD, no a la ciudad inferida por la posición del objetivo. No restringir los objetivos a la huella capturada: un destino fuera de ella puede señalarse con clamp.
- Esta spec no redefine la política global de radios no válidos ya aceptados por los setters existentes; la implementación debe evitar introducir resultados no finitos en la nueva lógica y plantear cualquier cambio de contrato existente antes de realizarlo.

## Pruebas y criterios de aceptación

### API y propiedad

- Ambos overloads funcionan desde `MinimapHUD` y desde `city.Minimap`, sobre el mismo propietario.
- Alta válida devuelve handle válido; baja lo invalida y elimina la representación visible.
- HUD ausente, prefab nulo y Transform nulo/destruido devuelven handle inválido sin crear objetos.
- Handles por defecto, bajas repetidas y handles de otro HUD no provocan excepciones ni afectan a registros ajenos.
- Una nueva alta después de una baja no reactiva el handle antiguo.
- Repetir objetivo/prefab produce registros independientes.

### Posicionamiento y clamp

- Un POI y un marcador dinámico sin clamp en la misma posición dentro del radio tienen la misma posición UI, con tolerancia numérica explícita en el test.
- Un Transform móvil se sigue en las actualizaciones siguientes; un Vector3 registrado no cambia cuando se mueve un objeto que se usó para calcularlo.
- Cambiar Y sin cambiar XZ no altera la posición UI.
- Sin clamp, verificar entrada/salida y distancia exactamente igual al radio.
- Con clamp, verificar direcciones cardinales y diagonales, objetivos lejanos, proximidad al borde desde dentro y objetivo en el centro.
- Para prefabs que cumplen el contrato y caben, comprobar las esquinas del rectángulo visual en espacio del mapa: todas quedan dentro del círculo, no solo el pivot.
- Cubrir tamaños diferentes, pivot no centrado y dimensiones no cuadradas. Cubrir un icono excesivo para asegurar que no hay inversión ni resultados no finitos, sin exigir que quepa.
- Repetir comprobaciones después de cambiar el radio del HUD y las dimensiones UI relevantes.

### Ciclo de vida y regresión

- Desactivar/reactivar el objetivo oculta/restaura el marcador sin invalidar el registro.
- Destruir el objetivo retira el registro en la siguiente actualización de mantenimiento; probar también destrucción mientras el HUD está oculto y posterior reactivación.
- Ocultar/mostrar el HUD conserva registros vivos y sus posiciones se actualizan antes de la presentación correcta.
- Alta y baja antes de Start y con HUD oculto no producen errores ni iconos huérfanos.
- Destruir el HUD invalida handles y elimina las instancias asociadas.
- Dos HUD mantienen registros aislados, incluso con datos de minimapa compartidos.
- `PointOfInterestCount`, etiquetas/pool de POI, textura, UV, orientación norte y selección de ciudad conservan el comportamiento anterior.
- El prefab fuente conserva sus propiedades tras alta, seguimiento y baja.

### Ejecución y evidencia

Ejecutar la suite pertinente mediante Unity Test Runner y registrar los resultados reales al implementar. El repositorio no dispone de pipeline CLI/CI equivalente que permita afirmar cobertura sin ejecutar Unity.

Completar QA visual con un prefab de recogida y otro de destino: movimiento del jugador, seguimiento de un objetivo móvil, borde circular, zoom y ocultación/reactivación. Una prueba geométrica no sustituye revisar el recorte real de UGUI. Verificar además uso desde un consumidor del paquete o un build disponible, sin depender de assets fuera del paquete salvo los prefabs aportados expresamente por el consumidor.

Esta spec está aprobada como documento; no implica que estas pruebas se hayan ejecutado ni que la funcionalidad esté implementada.

## Riesgos y decisiones abiertas

- **Contenido fuera del RectTransform raíz:** impide garantizar icono completo con una medida de ese rectángulo. Mitigación: contrato documentado y QA con prefabs representativos.
- **Margen conservador:** puede separar el icono del borde más de lo estrictamente necesario, especialmente con rectángulos alargados o pivots descentrados. Prioridad aprobada: verlo completo manteniendo la dirección, no pegar el pivot al perímetro.
- **Orden de inicialización y HUD inactivo:** Start/LateUpdate no siempre han corrido al llamar desde un consumidor. El registro no debe depender de ellos para aceptar altas/bajas; la visualización sí necesita datos válidos.
- **Reentrancia por scripts de prefab y referencias destruidas:** no iterar una colección de forma que callbacks de activación/destrucción puedan romperla; no confundir referencias Unity destruidas con objetivos fijos.
- **Regresión de POI al compartir proyección:** cubrir primero su comportamiento, especialmente el límite del radio. El margen interior es exclusivo de los dinámicos con clamp.
- **Varias ciudades:** la fachada sigue la referencia al HUD ya existente. Esta spec no corrige ni rediseña cómo se seleccionan los datos de ciudad o cómo varios accesos pueden compartir un HUD.

No quedan decisiones de producto bloqueantes tras la aprobación. El detalle interno del handle, almacenamiento y cálculo geométrico puede elegirse durante implementación siempre que satisfaga este contrato. Cualquier necesidad de ampliar la API o modificar comportamiento preexistente requiere nueva aprobación.
