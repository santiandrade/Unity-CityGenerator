# SPEC 22 — Traffic Light Side (semáforos a la derecha o a la izquierda)

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 11 (Custom Grid, regla de 3 brazos), release v3.7.2 (semáforo a la derecha del carril)
> **Fecha:** 2026-11-23
> **Objetivo:** Poder elegir en la card Props si los semáforos se colocan en la acera derecha (por defecto) o en la izquierda de cada carril de llegada, cada lado con su propio prefab.

## Contexto

Hasta la v3.7.1 el generador colocaba cada semáforo en una esquina y el prefab `TrafficLight` tenía el brazo hacia `+X`. La v3.7.2 movió el poste a la esquina simétrica (a la derecha del conductor que llega), con el mismo giro, y añadió `TrafficLight_Right` como **Prefab Variant** de `TrafficLight` con el brazo y la caja de luces hacia `−X`. Para que unos ajustes que aún apuntaran al prefab antiguo no lo colocaran mal, `CityGeneratorDefaultAssets.ResolveTrafficLightPrefab` lo sustituía en tiempo de generación por `TrafficLight_Right`.

Dos problemas encontrados al revisarlo:

- `ResolveTrafficLightPrefab` y sus dos constantes de ruta se escribieron a mano en `CityGeneratorDefaultAssets.cs`, un fichero que `CityGeneratorDefaultAssetsWriter` reescribe entero y que no sabía generarlos. Ejecutar "Set Current Selection As Default" los habría borrado y, como `CityGeneratorTrafficBuilder` los llamaba, **el paquete habría dejado de compilar**.
- No había forma de colocar los semáforos a la izquierda: el lado lo fija `PlaceTrafficLight`, y asignar el prefab antiguo con la colocación actual deja el brazo sobre la acera, no sobre la calzada.

## Scope

**Dentro:**

- **Modelo** (`Editor/CityGeneratorSettings.cs`): `enum TrafficLightSide { Right, Left }`; en `PropsSettings`, `trafficLightSide` (por defecto `Right`), `trafficLightPrefab` renombrado a `rightTrafficLightPrefab` con `[FormerlySerializedAs("trafficLightPrefab")]`, y `leftTrafficLightPrefab` nuevo. Dos propiedades no serializadas, `TrafficLightPrefab` y `TrafficLightPrefabPath`, devuelven el prefab y la ruta de validación del lado activo.
- **Colocación** (`CityGeneratorTrafficBuilder`): las dos sobrecargas de `BuildTrafficLights` reciben `TrafficLightSide`; `PlaceTrafficLight` usa la esquina `arm + driverRight` (derecha) o `arm − driverRight` (izquierda), con el mismo giro `LookRotation(approach)` en los dos casos.
- **Consumidores**: `CityGeneratorContentAssembler`, `CityGeneratorPedestrianPreview` (incluida `ComputeSettingsSignature`, que añade el lado) y `CityGeneratorValidator` usan siempre el prefab y la ruta del lado activo.
- **UI** (card Props): `Traffic Light Side` como enum y los dos campos de prefab como `AddRequiredField`; `RefreshDynamicUi` muestra solo el del lado elegido.
- **Demo content**: `TrafficLight_Right` pasa a prefab independiente (deja de ser variant); `TrafficLight.prefab` se renombra a `TrafficLight_Left.prefab` conservando su GUID y es el prefab izquierdo por defecto.
- **Defaults**: `ApplyTo` asigna los dos prefabs; el writer emite las dos asignaciones y gana `ReplaceEnumField` para el inicializador de `trafficLightSide`. Se elimina `ResolveTrafficLightPrefab`.
- **Tests EditMode**: el test de colocación de `SignalledIntersectionAgreementTests` cubre los dos lados en rejilla normal y Custom Grid; caso nuevo en `CityGeneratorValidatorTests` para el lado izquierdo; los tests existentes pasan al nombre nuevo del campo.
- **Documentación**: tabla de Props y mensajes de validación en `docs/user-manual.md`/`.es.md`, `docs/architecture/demo-content.md`, `CHANGELOG.md` (`## [Unreleased]`) y fila en la tabla de specs de `CLAUDE.md`.

**Fuera de alcance:**

- Cambiar `TrafficNetwork`, `PedestrianNetwork`, `CarAgent` o cualquier otro código de `Runtime/`.
- Elegir el lado por intersección o por calle, o reflejar automáticamente un único prefab (escala negativa) para servir a los dos lados.
- Migrar ciudades ya generadas: el lado solo se aplica al generar o regenerar.
- Una versión del manual con captura nueva de la card Props.
- Publicar una versión nueva del package.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs
[Serializable]
internal enum TrafficLightSide { Right, Left }

[Serializable]
internal class PropsSettings
{
    public TrafficLightSide trafficLightSide = TrafficLightSide.Right;
    [FormerlySerializedAs("trafficLightPrefab")]
    public GameObject rightTrafficLightPrefab; // required if trafficLightSide == Right
    public GameObject leftTrafficLightPrefab;  // required if trafficLightSide == Left
    // ... lamp/bin fields unchanged ...

    public GameObject TrafficLightPrefab => trafficLightSide == TrafficLightSide.Left ? leftTrafficLightPrefab : rightTrafficLightPrefab;
    public string TrafficLightPrefabPath => trafficLightSide == TrafficLightSide.Left ? "props.leftTrafficLightPrefab" : "props.rightTrafficLightPrefab";
}
```

Convención de prefab: el semáforo mira al conductor que llega por su `−Z` local; un prefab con brazo volado lo extiende por `−X` local en el lado derecho y por `+X` en el izquierdo, para que pase por encima de la calzada.

## Plan de implementación

1. **Arreglo del writer.** Quitar `ResolveTrafficLightPrefab`, sus constantes y sus dos llamadas; dejar la línea del default en el formato exacto que emite el writer.
2. **Assets.** Desempaquetar `TrafficLight_Right` (instanciar, `UnpackPrefabInstance` completo, `SaveAsPrefabAsset` sobre la misma ruta) y comprobar que GUID y jerarquía serializada quedan idénticos; renombrar `TrafficLight.prefab` a `TrafficLight_Left.prefab` con `AssetDatabase.MoveAsset`.
3. **Modelo y colocación.** Enum, campos, propiedades del lado activo y parámetro `side` en `BuildTrafficLights`/`PlaceTrafficLight`.
4. **Consumidores.** Assembler, vista previa de peatones (y su firma) y validador a través de `TrafficLightPrefab`/`TrafficLightPrefabPath`.
5. **UI.** Enum y los dos campos en Props; visibilidad según el lado en `RefreshDynamicUi`.
6. **Defaults.** `ApplyTo` y writer (dos asignaciones + `ReplaceEnumField`).
7. **Tests y documentación.**

## Criterios de aceptación

- [x] Con Traffic Light Side en Right, la colocación, el prefab y el emparejamiento de cada semáforo con su carril de llegada son los mismos que en la v3.7.2.
- [x] Con Left, cada semáforo queda a 6,2 m a la izquierda del conductor y 6,2 m antes del centro del cruce, mirándole, con el brazo y la caja de luces sobre la calzada, y regula el carril por el que llega ese conductor (rejilla normal y Custom Grid).
- [x] Solo se valida y se exige el prefab del lado elegido; el error se reporta en la ruta de ese campo y resalta la card Props.
- [x] En la ventana solo se ve el campo de prefab del lado elegido, y cambiar de lado no borra el valor del otro.
- [x] "Set Current Selection As Default" regenera `CityGeneratorDefaultAssets.cs` idéntico al actual con los valores por defecto, sin avisos, y escribe el lado elegido en el inicializador de `trafficLightSide`.
- [x] Las ciudades generadas con la v3.7.1 o anterior (instancias de `TrafficLight`) y con la v3.7.2 (instancias de `TrafficLight_Right`) siguen cargando sus semáforos.
- [x] La suite EditMode pasa completa.
- [x] Las suites PlayMode y Performance pasan, salvo el fallo preexistente de `CarAgentPhysicsRecoveryTests` ya documentado en la SPEC 21 (ver notas).
- [x] Generadas con la misma seed, una ciudad con Right y otra con Left quedan conectadas igual: cada semáforo regula exactamente un carril de llegada y la red peatonal tiene los mismos nodos por tipo, pasos de cebra conectados y nodos bloqueados.
- [x] La captura `docs/images/manual/card-props.png` muestra la card nueva.

## Decisiones tomadas y descartadas

- **Sí:** un prefab por lado, con el lado como enum. **No:** un único prefab con el lado elegido aparte — el usuario tendría que acordarse de cambiar también el prefab al cambiar de lado. **No:** reflejar el prefab con escala `X = −1` — invierte detalles asimétricos (texto, señales) y Unity avisa de `BoxCollider` con escala negativa.
- **Sí:** mostrar solo el campo del lado elegido y conservar el otro valor. **No:** borrar o reasignar el prefab del otro lado al cambiar, para no perder un prefab personalizado.
- **Sí:** mismo giro para los dos lados; solo cambia la esquina. Así `TrafficNetwork.AssignTrafficLights` (empareja por orientación y distancia < 14 m al centro) y `PedestrianNetwork.FindNearestIntersection` (media de posiciones de los semáforos) funcionan igual sin tocar `Runtime/`.
- **Sí:** reutilizar el semáforo antiguo como prefab izquierdo, renombrado con su GUID. **No:** borrarlo — rompía las escenas generadas con la v3.7.1 o anterior (instancias "Missing Prefab": sin semáforos, coches sin parar en los cruces, peatones sin cruzar). **No:** mantenerlo solo como deprecado, que fue la decisión intermedia antes de esta spec.
- **Sí:** `TrafficLight_Right` y `TrafficLight_Left` como prefabs independientes. **No:** variant uno del otro — cambiar o retirar la base rompería las instancias del otro.
- **Sí:** quitar `ResolveTrafficLightPrefab` del todo. **No:** moverlo fuera de `CityGeneratorDefaultAssets` — con un prefab por lado, el prefab antiguo ya no es un error que corregir: es el prefab izquierdo.
- **Sí:** renombrar el campo a `rightTrafficLightPrefab` con `FormerlySerializedAs`, por simetría con `leftTrafficLightPrefab`. **No:** mantener `trafficLightPrefab` como nombre del prefab derecho.
- **Sí:** el lado entra en `ComputeSettingsSignature`: en una intersección en T las esquinas usadas cambian con el lado, así que la vista previa de Custom Pedestrians debe reconstruirse.
- **Sí:** farolas y papeleras sin cambios: ocupan esquinas de manzana y ya convivían con semáforos en cualquiera de las cuatro esquinas de un cruce.

## Riesgos identificados

| Riesgo | Mitigación |
| --- | --- |
| Desempaquetar el variant cambia los IDs internos del prefab y desengancha las instancias de `City.unity`. | Comprobado tras el desempaquetado: Unity conservó los fileIDs y las 12 referencias distintas de la escena siguen existiendo en el prefab. |
| El writer olvida un campo nuevo y "Set Current Selection As Default" lo borra (precedente: `vehiclePhysics.physicMaterial` y el propio `ResolveTrafficLightPrefab`). | Writer actualizado y verificado por reflexión: regenera el fichero idéntico y parchea el enum. |
| Un semáforo a la izquierda no se empareja con su carril. | Test parametrizado por lado que comprueba el `Light` de cada nodo de llegada. |

## Implementation notes

- **Ventanas abiertas durante la actualización:** una ventana del generador abierta antes del cambio conserva sus ajustes serializados; `rightTrafficLightPrefab` recupera su valor gracias a `FormerlySerializedAs`, pero `leftTrafficLightPrefab` aparece vacío hasta pulsar "Reset to Defaults" o asignarlo a mano. Una ventana nueva ya lo trae relleno. No se añadió un relleno automático porque un campo vacío también puede ser una elección del usuario.
- **Etiquetas de la UI:** se mantiene el sufijo "(if Include Traffic)" que ya tenía el campo, aunque el validador exige el prefab también con el tráfico desactivado (preexistente, fuera de alcance).
- **Verificación visual:** se renderizó una intersección con cada lado en una escena de previsualización (`EditorSceneManager.NewPreviewScene`), sin tocar la escena abierta; en los dos casos el brazo queda sobre la calzada.
- **Suites:** EditMode 170/170 (167 previos, más los dos casos de colocación a la izquierda y el caso nuevo del validador). PlayMode + Performance 118/119. El único fallo, `CarAgentPhysicsRecoveryTests.Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt`, es el preexistente que ya recoge la SPEC 21 (física de `CarAgent`, sin cambios en `Runtime/`). En la primera pasada también falló `RuntimePerformanceTests.PerFrameCost_1000Agents_15x15_Physics_ScalingOptions`, pero por un error de consola que provocó la herramienta de automatización al consultar el estado del Editor mientras el test lo bloqueaba (`Main thread operation timed out after 30000ms`); repetido solo y sin consultas, pasa.
- **Equivalencia de conexiones Right/Left:** se generaron con el pipeline completo (`CityGeneratorContentAssembler.Assemble`, seed fija, en una escena aditiva temporal) una rejilla 5×5 y una forma personalizada con varios cruces en T, con cada lado. Resultado idéntico: 5×5 con 112 semáforos y 715 nodos peatonales; forma personalizada con 34 semáforos y 253 nodos. En ambos casos cada semáforo está asignado a un único nodo de entrada y los 150 / 48 nodos `Ring` bloqueados por obstáculos son los mismos con los dos lados.
- **Captura del manual:** `card-props.png` se tomó desde script pintando la vista del Editor en una textura (`GUIView.GrabPixels`), porque leer la pantalla (`InternalEditorUtility.ReadScreenPixel`) devolvía blanco aunque Unity estuviera al frente. Esa textura sale invertida en vertical y refleja el giro de la flecha de la card, así que durante la captura se fijó la flecha en la orientación que se ve en pantalla (⌄, desplegada). El encuadre sigue a `card-districts.png`: el ancho de la ventana acoplada, con 2 px de fondo alrededor del marco. La captura anterior era más ancha (990 px).
