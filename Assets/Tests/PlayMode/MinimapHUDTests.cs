using System.Collections;
using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CityGenerator.Tests.PlayMode
{
    internal sealed class MinimapMarkerLifecycleCallback : MonoBehaviour
    {
        internal static System.Action<GameObject> OnEnabled;
        internal static System.Action<GameObject> OnDisabled;

        private void OnEnable() => OnEnabled?.Invoke(gameObject);

        private void OnDisable() => OnDisabled?.Invoke(gameObject);
    }

    /// <summary>
    /// Characterization coverage for MinimapHUD (SPEC 15, closing a P1 gap the technical review
    /// flagged): describes what the component already does, without changing its behaviour. Building
    /// a real PlayerController (required by MinimapHUD.Start's FindAnyObjectByType lookup) drags in
    /// its own RequireComponents; its Awake logs a benign "no InputActionAsset assigned" warning
    /// with no assigned asset, which these tests explicitly expect rather than suppress.
    /// </summary>
    internal class MinimapHUDTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, $"Field '{name}' was not found.");
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
            Assert.IsNotNull(method, $"Method '{name}' was not found.");
            method.Invoke(target, null);
        }

        private static MinimapHUD BuildHud()
        {
            var go = new GameObject("MinimapHUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            MinimapHUD hud = go.AddComponent<MinimapHUD>();

            var mapImageGo = new GameObject("MapImage", typeof(RawImage));
            mapImageGo.transform.SetParent(go.transform, false);
            var mapImage = mapImageGo.GetComponent<RawImage>();
            mapImage.rectTransform.sizeDelta = new Vector2(200f, 200f);

            var markerContainerGo = new GameObject("MarkerContainer", typeof(RectTransform));
            markerContainerGo.transform.SetParent(go.transform, false);
            RectTransform markerContainer = markerContainerGo.GetComponent<RectTransform>();
            markerContainer.sizeDelta = new Vector2(200f, 200f);

            SetField(hud, "mapImage", mapImage);
            SetField(hud, "poiMarkerContainer", markerContainer);

            return hud;
        }

        private static RectTransform MarkerContainer(MinimapHUD hud) =>
            (RectTransform)typeof(MinimapHUD).GetField("poiMarkerContainer", PrivateInstance).GetValue(hud);

        private static GameObject BuildTrackedObject(MinimapHUD hud, Vector3 position)
        {
            var tracked = new GameObject("Tracked");
            tracked.transform.position = position;
            SetField(hud, "trackedTransform", tracked.transform);

            var dataGo = new GameObject("MinimapData");
            dataGo.transform.SetParent(hud.transform, false);
            MinimapData data = dataGo.AddComponent<MinimapData>();
            data.worldOrigin = Vector2.zero;
            data.worldSize = new Vector2(200f, 200f);
            SetField(hud, "data", data);
            return tracked;
        }

        private static RectTransform BuildMarkerPrefab(Vector2 size, Vector2 pivot)
        {
            var go = new GameObject("MarkerPrefab", typeof(RectTransform));
            RectTransform prefab = go.GetComponent<RectTransform>();
            prefab.sizeDelta = size;
            prefab.pivot = pivot;
            return prefab;
        }

        private static (GameObject go, PlayerController controller) BuildPlayer()
        {
            var go = new GameObject("Player", typeof(CharacterController), typeof(Animator));
            LogAssert.Expect(LogType.Warning, "PlayerController: no InputActionAsset assigned.");
            PlayerController controller = go.AddComponent<PlayerController>();
            return (go, controller);
        }

        private static MinimapData BuildMinimapData()
        {
            var go = new GameObject("MinimapData");
            MinimapData data = go.AddComponent<MinimapData>();
            data.worldOrigin = Vector2.zero;
            data.worldSize = new Vector2(100f, 100f);
            return data;
        }

        [TearDown]
        public void ClearMarkerLifecycleCallbacks()
        {
            MinimapMarkerLifecycleCallback.OnEnabled = null;
            MinimapMarkerLifecycleCallback.OnDisabled = null;
        }

        [UnityTest]
        public IEnumerator Start_WithNoMinimapDataOrPlayer_DeactivatesItself()
        {
            MinimapHUD hud = BuildHud();

            yield return null;

            Assert.IsFalse(hud.gameObject.activeSelf, "MinimapHUD must deactivate itself when it can't find a MinimapData and a PlayerController in the scene.");

            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator Start_WithMinimapDataAndPlayer_StaysActive_AndAssignsSnapshotTexture()
        {
            MinimapHUD hud = BuildHud();
            MinimapData data = BuildMinimapData();
            data.snapshot = new Texture2D(4, 4);
            (GameObject playerGo, _) = BuildPlayer();

            yield return null;

            Assert.IsTrue(hud.gameObject.activeSelf);
            var mapImage = (RawImage)typeof(MinimapHUD)
                .GetField("mapImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(hud);
            Assert.AreEqual(data.snapshot, mapImage.texture);

            Object.DestroyImmediate(hud.gameObject);
            Object.DestroyImmediate(data.gameObject);
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(data.snapshot);
        }

        [Test]
        public void ViewRadiusMeters_RoundTripsThroughProperty()
        {
            MinimapHUD hud = BuildHud();

            hud.ViewRadiusMeters = 120f;

            Assert.AreEqual(120f, hud.ViewRadiusMeters);

            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void AddAndRemoveFixedMarker_UpdatesHandleValidity()
        {
            MinimapHUD hud = BuildHud();
            var prefabGo = new GameObject("MarkerPrefab", typeof(RectTransform));
            RectTransform prefab = prefabGo.GetComponent<RectTransform>();

            MinimapMarkerHandle handle = hud.AddMarker(new Vector3(10f, 2f, 20f), prefab, false);

            Assert.IsTrue(handle.IsValid);
            hud.RemoveMarker(handle);
            Assert.IsFalse(handle.IsValid);
            Assert.DoesNotThrow(() => hud.RemoveMarker(handle));

            Object.DestroyImmediate(prefabGo);
            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator AddMarker_WhenPrefabCallbackDestroysHud_ReturnsInvalidWithoutThrowing()
        {
            MinimapHUD hud = BuildHud();
            GameObject hudObject = hud.gameObject;
            RectTransform markerContainer = MarkerContainer(hud);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerLifecycleCallback.OnEnabled = _ => Object.DestroyImmediate(hud);
            MinimapMarkerHandle handle = default;

            Assert.DoesNotThrow(() => handle = hud.AddMarker(Vector3.zero, prefab, false));

            Assert.IsFalse(handle.IsValid);
            yield return null;
            Assert.AreEqual(0, markerContainer.childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hudObject);
        }

        [Test]
        public void AddMarker_WhenPrefabCallbackDestroysClone_ReturnsInvalidWithoutThrowing()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerLifecycleCallback.OnEnabled = clone => Object.DestroyImmediate(clone);
            MinimapMarkerHandle handle = default;

            Assert.DoesNotThrow(() => handle = hud.AddMarker(Vector3.zero, prefab, false));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, MarkerContainer(hud).childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator AddMarker_WhenCloneOnDisableDestroysHud_ReturnsInvalidAndLeavesNoClone()
        {
            MinimapHUD hud = BuildHud();
            GameObject hudObject = hud.gameObject;
            RectTransform markerContainer = MarkerContainer(hud);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerLifecycleCallback.OnDisabled = _ =>
            {
                MinimapMarkerLifecycleCallback.OnDisabled = null;
                Object.DestroyImmediate(hud);
            };
            MinimapMarkerHandle handle = default;

            Assert.DoesNotThrow(() => handle = hud.AddMarker(Vector3.zero, prefab, false));

            Assert.IsFalse(handle.IsValid);
            yield return null;
            Assert.AreEqual(0, markerContainer.childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hudObject);
        }

        [Test]
        public void AddMarker_WhenCloneOnDisableDestroysClone_ReturnsInvalidAndUnregistersIt()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerLifecycleCallback.OnDisabled = clone =>
            {
                MinimapMarkerLifecycleCallback.OnDisabled = null;
                Object.DestroyImmediate(clone);
            };
            MinimapMarkerHandle handle = default;

            Assert.DoesNotThrow(() => handle = hud.AddMarker(Vector3.zero, prefab, false));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, MarkerContainer(hud).childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void AddMarker_PrefabCallbackCanAddAnotherMarkerWithoutThrowing()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerHandle nested = default;
            MinimapMarkerLifecycleCallback.OnEnabled = _ =>
            {
                MinimapMarkerLifecycleCallback.OnEnabled = null;
                nested = hud.AddMarker(Vector3.one, prefab, false);
            };
            MinimapMarkerHandle outer = default;

            Assert.DoesNotThrow(() => outer = hud.AddMarker(Vector3.zero, prefab, false));

            Assert.IsTrue(outer.IsValid);
            Assert.IsTrue(nested.IsValid);
            Assert.AreEqual(2, MarkerContainer(hud).childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void RemovedHandle_DoesNotBecomeValidForLaterRegistration()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle removed = hud.AddMarker(Vector3.zero, prefab, false);

            hud.RemoveMarker(removed);
            MinimapMarkerHandle replacement = hud.AddMarker(Vector3.one, prefab, false);

            Assert.IsFalse(removed.IsValid);
            Assert.IsTrue(replacement.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void MarkerActivationCallback_CanAddMarkerDuringUpdate()
        {
            MinimapHUD hud = BuildHud();
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerHandle addedFromCallback = default;
            hud.AddMarker(Vector3.zero, prefab, false);
            MinimapMarkerLifecycleCallback.OnEnabled = _ =>
            {
                MinimapMarkerLifecycleCallback.OnEnabled = null;
                addedFromCallback = hud.AddMarker(Vector3.one, prefab, false);
            };

            Assert.DoesNotThrow(() => Invoke(hud, "LateUpdate"));

            Assert.IsTrue(addedFromCallback.IsValid);
            Assert.AreEqual(2, MarkerContainer(hud).childCount);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void MarkerActivationCallback_CanRemoveMarkerDuringUpdate()
        {
            MinimapHUD hud = BuildHud();
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerHandle handle = hud.AddMarker(Vector3.zero, prefab, false);
            MinimapMarkerLifecycleCallback.OnEnabled = _ => hud.RemoveMarker(handle);

            Assert.DoesNotThrow(() => Invoke(hud, "LateUpdate"));

            Assert.IsFalse(handle.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void AddMarker_WithInvalidArguments_ReturnsInvalidHandle()
        {
            MinimapHUD hud = BuildHud();
            var targetGo = new GameObject("Target");
            var prefabGo = new GameObject("MarkerPrefab", typeof(RectTransform));
            RectTransform prefab = prefabGo.GetComponent<RectTransform>();

            Assert.IsFalse(hud.AddMarker((Transform)null, prefab, false).IsValid);
            Assert.IsFalse(hud.AddMarker(targetGo.transform, null, false).IsValid);
            Assert.IsFalse(hud.AddMarker(Vector3.zero, null, false).IsValid);
            Assert.IsFalse(hud.AddMarker(new Vector3(float.PositiveInfinity, 0f, 0f), prefab, false).IsValid);
            Assert.IsFalse(default(MinimapMarkerHandle).IsValid);

            Object.DestroyImmediate(targetGo);
            Object.DestroyImmediate(prefabGo);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void PoiAndDynamicMarkerAtSamePositionShareProjectionResult()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, new Vector3(10f, 5f, 20f));
            RectTransform poiPrefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            poiPrefab.gameObject.SetActive(false);
            RectTransform dynamicPrefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            SetField(hud, "poiMarkerTemplate", poiPrefab);
            MinimapData data = hud.GetComponentInChildren<MinimapData>();
            data.pointsOfInterest.Add(new PointOfInterestEntry
            {
                title = "Shared projection",
                worldPosition = new Vector3(40f, -999f, 60f),
            });
            hud.AddMarker(new Vector3(40f, 999f, 60f), dynamicPrefab, false);

            Invoke(hud, "LateUpdate");

            RectTransform container = MarkerContainer(hud);
            RectTransform dynamicInstance = (RectTransform)container.GetChild(0);
            RectTransform poiInstance = (RectTransform)container.GetChild(1);
            Assert.That(dynamicInstance.anchoredPosition.x, Is.EqualTo(poiInstance.anchoredPosition.x).Within(0.0001f));
            Assert.That(dynamicInstance.anchoredPosition.y, Is.EqualTo(poiInstance.anchoredPosition.y).Within(0.0001f));
            Assert.That(poiInstance.anchoredPosition.x, Is.EqualTo(30f).Within(0.001f));
            Assert.That(poiInstance.anchoredPosition.y, Is.EqualTo(40f).Within(0.001f));

            Object.DestroyImmediate(poiPrefab.gameObject);
            Object.DestroyImmediate(dynamicPrefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void FixedMarker_UsesPoiProjection_AndIgnoresWorldHeight()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, new Vector3(10f, 5f, 20f));
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle handle = hud.AddMarker(new Vector3(40f, 999f, 60f), prefab, false);
            Invoke(hud, "LateUpdate");

            RectTransform instance = (RectTransform)MarkerContainer(hud).GetChild(0);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.That(instance.anchoredPosition.x, Is.EqualTo(30f).Within(0.001f));
            Assert.That(instance.anchoredPosition.y, Is.EqualTo(40f).Within(0.001f));

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void ClampedMarker_WithFiniteExtremeCoordinates_StaysFiniteAndPreservesDirection()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(
                hud,
                new Vector3(-float.MaxValue, 0f, float.MaxValue));
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            hud.AddMarker(new Vector3(float.MaxValue, 0f, -float.MaxValue), prefab, true);
            Invoke(hud, "LateUpdate");
            Vector2 position = ((RectTransform)MarkerContainer(hud).GetChild(0)).anchoredPosition;

            Assert.IsFalse(float.IsNaN(position.x) || float.IsInfinity(position.x));
            Assert.IsFalse(float.IsNaN(position.y) || float.IsInfinity(position.y));
            Assert.Greater(position.x, 0f);
            Assert.Less(position.y, 0f);
            Assert.That(Mathf.Abs(position.x), Is.EqualTo(Mathf.Abs(position.y)).Within(0.001f));

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void MarkerAtTinyPositiveRadius_ProjectsToFiniteMapEdge()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = float.Epsilon;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            hud.AddMarker(new Vector3(float.Epsilon, 0f, 0f), prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = (RectTransform)MarkerContainer(hud).GetChild(0);

            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.IsFalse(float.IsNaN(instance.anchoredPosition.x) || float.IsInfinity(instance.anchoredPosition.x));
            Assert.That(instance.anchoredPosition.x, Is.EqualTo(100f).Within(0.001f));
            Assert.That(instance.anchoredPosition.y, Is.EqualTo(0f).Within(0.001f));

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void TransformMarker_FollowsMovement_AndInactiveTargetIsTemporarilyHidden()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            target.transform.position = new Vector3(10f, 0f, 20f);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle handle = hud.AddMarker(target.transform, prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = (RectTransform)MarkerContainer(hud).GetChild(0);
            Assert.AreEqual(new Vector2(10f, 20f), instance.anchoredPosition);

            target.transform.position = new Vector3(25f, 500f, -15f);
            Invoke(hud, "LateUpdate");
            Assert.AreEqual(new Vector2(25f, -15f), instance.anchoredPosition);

            target.SetActive(false);
            Invoke(hud, "LateUpdate");
            Assert.IsFalse(instance.gameObject.activeSelf);
            Assert.IsTrue(handle.IsValid);

            target.SetActive(true);
            Invoke(hud, "LateUpdate");
            Assert.IsTrue(instance.gameObject.activeSelf);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void MarkerWithoutClamp_HidesOnlyBeyondViewRadius()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            target.transform.position = new Vector3(100f, 0f, 0f);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            hud.AddMarker(target.transform, prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = (RectTransform)MarkerContainer(hud).GetChild(0);
            Assert.IsTrue(instance.gameObject.activeSelf, "The existing POI rule includes the exact radius.");

            target.transform.position = new Vector3(100.01f, 0f, 0f);
            Invoke(hud, "LateUpdate");
            Assert.IsFalse(instance.gameObject.activeSelf);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [TestCase(200f, 0f)]
        [TestCase(200f, 200f)]
        public void ClampedMarker_KeepsEveryRootRectCornerInsideCircle(float x, float z)
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(30f, 10f), new Vector2(0.2f, 0.8f));

            hud.AddMarker(new Vector3(x, 0f, z), prefab, true);
            Invoke(hud, "LateUpdate");

            RectTransform container = MarkerContainer(hud);
            RectTransform instance = (RectTransform)container.GetChild(0);
            var corners = new Vector3[4];
            instance.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = container.InverseTransformPoint(corner);
                Assert.That(new Vector2(local.x, local.y).magnitude, Is.LessThanOrEqualTo(100.001f));
            }

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void DestroyedTarget_InvalidatesHandle_AndForeignHandleCannotRemoveMarker()
        {
            MinimapHUD hudA = BuildHud();
            MinimapHUD hudB = BuildHud();
            GameObject trackedA = BuildTrackedObject(hudA, Vector3.zero);
            GameObject trackedB = BuildTrackedObject(hudB, Vector3.zero);
            var target = new GameObject("Target");
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle handle = hudA.AddMarker(target.transform, prefab, false);
            hudB.RemoveMarker(handle);
            Assert.IsTrue(handle.IsValid);

            Object.DestroyImmediate(target);
            Invoke(hudA, "LateUpdate");
            Assert.IsFalse(handle.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(trackedA);
            Object.DestroyImmediate(trackedB);
            Object.DestroyImmediate(hudA.gameObject);
            Object.DestroyImmediate(hudB.gameObject);
        }

        [Test]
        public void OversizedClampedMarker_StaysFiniteAtTheCentre()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(500f, 500f), new Vector2(0.5f, 0.5f));

            hud.AddMarker(new Vector3(1000f, 0f, 0f), prefab, true);
            Invoke(hud, "LateUpdate");
            Vector2 position = ((RectTransform)MarkerContainer(hud).GetChild(0)).anchoredPosition;

            Assert.IsFalse(float.IsNaN(position.x) || float.IsInfinity(position.x));
            Assert.IsFalse(float.IsNaN(position.y) || float.IsInfinity(position.y));
            Assert.AreEqual(Vector2.zero, position);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void ReactivatingHiddenHud_PrunesDestroyedTargetBeforeShowingMarkers()
        {
            MinimapHUD hud = BuildHud();
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(target.transform, prefab, false);
            Invoke(hud, "LateUpdate");

            hud.gameObject.SetActive(false);
            Object.DestroyImmediate(target);
            hud.gameObject.SetActive(true);

            Assert.IsFalse(handle.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void LateUpdateWithoutRenderPrerequisites_PrunesDestroyedTarget()
        {
            MinimapHUD hud = BuildHud();
            var target = new GameObject("Target");
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(target.transform, prefab, false);

            Object.DestroyImmediate(target);
            Invoke(hud, "LateUpdate");

            Assert.IsFalse(handle.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void ReactivatingUninitializedHud_PrunesDestroyedTarget()
        {
            MinimapHUD hud = BuildHud();
            var target = new GameObject("Target");
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(target.transform, prefab, false);

            hud.gameObject.SetActive(false);
            Object.DestroyImmediate(target);
            hud.gameObject.SetActive(true);

            Assert.IsFalse(handle.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void DestroyingHud_RejectsAddAndToleratesRemoveFromMarkerCallbacks()
        {
            MinimapHUD hud = BuildHud();
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerHandle first = hud.AddMarker(Vector3.zero, prefab, false);
            MinimapMarkerHandle second = hud.AddMarker(Vector3.one, prefab, false);
            Invoke(hud, "LateUpdate");
            MinimapMarkerHandle addedDuringTeardown = default;
            MinimapMarkerLifecycleCallback.OnDisabled = _ =>
            {
                MinimapMarkerLifecycleCallback.OnDisabled = null;
                hud.RemoveMarker(second);
                addedDuringTeardown = hud.AddMarker(Vector3.up, prefab, false);
            };

            Assert.DoesNotThrow(() => Object.DestroyImmediate(hud.gameObject));

            Assert.IsFalse(first.IsValid);
            Assert.IsFalse(second.IsValid);
            Assert.IsFalse(addedDuringTeardown.IsValid);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
        }

        [Test]
        public void DestroyingHud_InvalidatesItsMarkerHandles()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(Vector3.zero, prefab, false);

            Object.DestroyImmediate(hud.gameObject);

            Assert.IsFalse(handle.IsValid);
            Object.DestroyImmediate(prefab.gameObject);
        }
    }
}
