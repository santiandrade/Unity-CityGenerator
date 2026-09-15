using System.Collections;
using System.Collections.Generic;
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

        private static RectTransform MarkerInstance(MinimapHUD hud, int index) =>
            (RectTransform)MarkerContainer(hud).GetChild(index);

        private static RawImage MapImage(MinimapHUD hud) =>
            (RawImage)typeof(MinimapHUD).GetField("mapImage", PrivateInstance).GetValue(hud);

        private static void AssertPosition(Vector2 expected, RectTransform instance)
        {
            Assert.IsTrue(instance.gameObject.activeSelf, $"{instance.name} should be visible.");
            Assert.That(instance.anchoredPosition.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(instance.anchoredPosition.y, Is.EqualTo(expected.y).Within(0.001f));
        }

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

        [Test]
        public void AddMarker_RunsNoPrefabCallbacks_UntilTheHudFirstShowsTheMarker()
        {
            MinimapHUD hud = BuildHud();
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            int enabledCount = 0;
            int disabledCount = 0;
            MinimapMarkerLifecycleCallback.OnEnabled = go =>
            {
                if (go != prefab.gameObject)
                    enabledCount++;
            };
            MinimapMarkerLifecycleCallback.OnDisabled = go =>
            {
                if (go != prefab.gameObject)
                    disabledCount++;
            };

            MinimapMarkerHandle handle = hud.AddMarker(Vector3.zero, prefab, false);

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(0, enabledCount, "No prefab callback may run inside AddMarker.");
            Assert.AreEqual(0, disabledCount, "No prefab callback may run inside AddMarker.");
            RectTransform instance = MarkerInstance(hud, 0);
            Assert.IsFalse(instance.gameObject.activeSelf);

            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            Invoke(hud, "LateUpdate");

            Assert.AreEqual(1, enabledCount);
            Assert.IsTrue(instance.gameObject.activeSelf);

            MinimapMarkerLifecycleCallback.OnEnabled = null;
            MinimapMarkerLifecycleCallback.OnDisabled = null;
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator ShowingMarker_WhenPrefabCallbackDestroysHud_StopsWithoutThrowing()
        {
            MinimapHUD hud = BuildHud();
            GameObject hudObject = hud.gameObject;
            RectTransform markerContainer = MarkerContainer(hud);
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            prefab.gameObject.AddComponent<MinimapMarkerLifecycleCallback>();
            MinimapMarkerHandle first = hud.AddMarker(Vector3.zero, prefab, false);
            MinimapMarkerHandle second = hud.AddMarker(Vector3.one, prefab, true);
            MinimapMarkerLifecycleCallback.OnEnabled = _ =>
            {
                MinimapMarkerLifecycleCallback.OnEnabled = null;
                Object.DestroyImmediate(hud);
            };

            Assert.DoesNotThrow(() => Invoke(hud, "LateUpdate"));

            Assert.IsFalse(first.IsValid);
            Assert.IsFalse(second.IsValid);
            yield return null;
            Assert.AreEqual(0, markerContainer.childCount);
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hudObject);
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

        // Cardinal and diagonal directions in every quadrant, far targets, a target inside the world
        // radius whose icon would still be cut by the edge (95, 0), one away from the edge that
        // must match the plain projection (-3, -4), and several sizes / off-centre pivots.
        [TestCase(200f, 0f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(0f, 200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(-200f, 0f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(0f, -200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(200f, 200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(-200f, 200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(-200f, -200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(200f, -200f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(95f, 0f, 30f, 10f, 0.2f, 0.8f)]
        [TestCase(-60f, 70f, 10f, 40f, 1f, 0f)]
        [TestCase(0f, -5000f, 24f, 24f, 0.5f, 0.5f)]
        [TestCase(-3f, -4f, 60f, 20f, 0f, 1f)]
        public void ClampedMarker_KeepsEveryRootRectCornerInsideCircle(
            float x,
            float z,
            float width,
            float height,
            float pivotX,
            float pivotY)
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(width, height), new Vector2(pivotX, pivotY));

            hud.AddMarker(new Vector3(x, 0f, z), prefab, true);
            Invoke(hud, "LateUpdate");

            RectTransform container = MarkerContainer(hud);
            RectTransform instance = MarkerInstance(hud, 0);
            var corners = new Vector3[4];
            instance.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = container.InverseTransformPoint(corner);
                Assert.That(new Vector2(local.x, local.y).magnitude, Is.LessThanOrEqualTo(100.001f));
            }

            // Map radius 100 at one UI unit per meter: the projection is (x, z) itself.
            var projected = new Vector2(x, z);
            float cornerRadius = new Vector2(
                Mathf.Max(width * pivotX, width * (1f - pivotX)),
                Mathf.Max(height * pivotY, height * (1f - pivotY))).magnitude;
            float safeRadius = 100f - cornerRadius;
            Vector2 expected = projected.magnitude > safeRadius ? projected.normalized * safeRadius : projected;
            AssertPosition(expected, instance);
            Assert.That(Vector2.Dot(instance.anchoredPosition.normalized, projected.normalized),
                Is.GreaterThan(0.9999f), "Clamping must never change the marker's direction.");

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

        [UnityTest]
        public IEnumerator DestroyingHudComponent_DestroysItsMarkerInstances()
        {
            MinimapHUD hud = BuildHud();
            GameObject hudObject = hud.gameObject;
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(Vector3.zero, prefab, false);
            RectTransform instance = MarkerInstance(hud, 0);

            Object.DestroyImmediate(hud);

            Assert.IsFalse(handle.IsValid);
            yield return null;
            Assert.IsTrue(instance == null, "Destroying the HUD must destroy the instances it created.");

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hudObject);
        }

        [UnityTest]
        public IEnumerator RemoveMarker_HidesTheInstanceAtOnce_AndDestroysItByFrameEnd()
        {
            MinimapHUD hud = BuildHud();
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(Vector3.zero, prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = MarkerInstance(hud, 0);
            Assert.IsTrue(instance.gameObject.activeSelf);

            hud.RemoveMarker(handle);

            Assert.IsFalse(handle.IsValid);
            Assert.IsFalse(instance.gameObject.activeSelf);
            yield return null;
            Assert.IsTrue(instance == null);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator RepeatedTargetAndPrefab_CreateIndependentRegistrations()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            target.transform.position = new Vector3(10f, 0f, 20f);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle first = hud.AddMarker(target.transform, prefab, false);
            MinimapMarkerHandle second = hud.AddMarker(target.transform, prefab, false);
            Invoke(hud, "LateUpdate");

            Assert.IsTrue(first.IsValid);
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(2, MarkerContainer(hud).childCount);
            RectTransform secondInstance = MarkerInstance(hud, 1);

            hud.RemoveMarker(first);
            Invoke(hud, "LateUpdate");
            yield return null;

            Assert.IsFalse(first.IsValid);
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(1, MarkerContainer(hud).childCount);
            AssertPosition(new Vector2(10f, 20f), secondInstance);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void FixedMarker_DoesNotFollowTheObjectUsedToComputeIt()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var source = new GameObject("Source");
            source.transform.position = new Vector3(10f, 0f, 20f);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            hud.AddMarker(source.transform.position, prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = MarkerInstance(hud, 0);
            AssertPosition(new Vector2(10f, 20f), instance);

            source.transform.position = new Vector3(-40f, 0f, 30f);
            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(10f, 20f), instance);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void ClampedMarker_AtTheCentre_StaysAtTheCentre()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, new Vector3(7f, 3f, -2f));
            RectTransform prefab = BuildMarkerPrefab(new Vector2(30f, 10f), new Vector2(0.2f, 0.8f));

            hud.AddMarker(new Vector3(7f, 50f, -2f), prefab, true);
            Invoke(hud, "LateUpdate");

            AssertPosition(Vector2.zero, MarkerInstance(hud, 0));

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void Markers_FollowViewRadiusAndUiDimensionChanges()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            hud.AddMarker(new Vector3(50f, 0f, 0f), prefab, true);
            hud.AddMarker(new Vector3(50f, 0f, 0f), prefab, false);
            RectTransform clamped = MarkerInstance(hud, 0);
            RectTransform unclamped = MarkerInstance(hud, 1);
            float smallIconRadius = Mathf.Sqrt(50f);

            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(50f, 0f), clamped);
            AssertPosition(new Vector2(50f, 0f), unclamped);

            // 200 / 80 = 2.5 units per meter: 125 units, past the 100-unit map radius.
            hud.ViewRadiusMeters = 40f;
            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(100f - smallIconRadius, 0f), clamped);
            Assert.IsFalse(unclamped.gameObject.activeSelf);

            // A wider map: 400 / 80 = 5 units per meter, 250 units against a 200-unit radius.
            MapImage(hud).rectTransform.sizeDelta = new Vector2(400f, 400f);
            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(200f - smallIconRadius, 0f), clamped);

            // A bigger icon needs a deeper inset.
            clamped.sizeDelta = new Vector2(40f, 40f);
            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(200f - Mathf.Sqrt(800f), 0f), clamped);

            // 400 / 120 units per meter: both back inside, both on the plain projection.
            hud.ViewRadiusMeters = 60f;
            Invoke(hud, "LateUpdate");
            AssertPosition(new Vector2(50f * 400f / 120f, 0f), clamped);
            AssertPosition(new Vector2(50f * 400f / 120f, 0f), unclamped);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void HidingAndShowingHud_KeepsMarkers_AndRepositionsThemBeforeShowing()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            target.transform.position = new Vector3(10f, 0f, 20f);
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle handle = hud.AddMarker(target.transform, prefab, false);
            Invoke(hud, "LateUpdate");
            RectTransform instance = MarkerInstance(hud, 0);
            AssertPosition(new Vector2(10f, 20f), instance);

            hud.gameObject.SetActive(false);
            Assert.IsTrue(handle.IsValid);
            Assert.IsFalse(instance.gameObject.activeInHierarchy);

            target.transform.position = new Vector3(-30f, 0f, 5f);
            hud.gameObject.SetActive(true);

            // No LateUpdate in between: OnEnable alone must already have moved it.
            Assert.IsTrue(handle.IsValid);
            AssertPosition(new Vector2(-30f, 5f), instance);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [UnityTest]
        public IEnumerator AddAndRemove_OnHiddenHudBeforeStart_LeaveNoOrphans()
        {
            MinimapHUD hud = BuildHud();
            hud.gameObject.SetActive(false);
            RectTransform container = MarkerContainer(hud);
            var target = new GameObject("Target");
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle fixedMarker = hud.AddMarker(new Vector3(1f, 0f, 1f), prefab, true);
            MinimapMarkerHandle tracking = hud.AddMarker(target.transform, prefab, false);

            Assert.IsTrue(fixedMarker.IsValid);
            Assert.IsTrue(tracking.IsValid);
            Assert.AreEqual(2, container.childCount);

            hud.RemoveMarker(fixedMarker);
            Object.DestroyImmediate(target);
            hud.gameObject.SetActive(true);

            Assert.IsFalse(fixedMarker.IsValid);
            Assert.IsFalse(tracking.IsValid);
            yield return null;
            Assert.AreEqual(0, container.childCount);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void TwoHuds_SharingMinimapData_KeepTheirMarkersIsolated()
        {
            MinimapHUD hudA = BuildHud();
            MinimapHUD hudB = BuildHud();
            hudA.ViewRadiusMeters = 100f;
            hudB.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hudA, Vector3.zero);
            SetField(hudB, "trackedTransform", tracked.transform);
            SetField(hudB, "data", hudA.GetComponentInChildren<MinimapData>());
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));

            MinimapMarkerHandle onA = hudA.AddMarker(new Vector3(10f, 0f, 20f), prefab, false);
            MinimapMarkerHandle onB = hudB.AddMarker(new Vector3(-10f, 0f, -20f), prefab, false);
            Invoke(hudA, "LateUpdate");
            Invoke(hudB, "LateUpdate");

            Assert.AreEqual(1, MarkerContainer(hudA).childCount);
            Assert.AreEqual(1, MarkerContainer(hudB).childCount);
            AssertPosition(new Vector2(10f, 20f), MarkerInstance(hudA, 0));
            AssertPosition(new Vector2(-10f, -20f), MarkerInstance(hudB, 0));

            hudB.RemoveMarker(onA);
            hudA.RemoveMarker(onB);
            Assert.IsTrue(onA.IsValid);
            Assert.IsTrue(onB.IsValid);

            hudA.RemoveMarker(onA);
            Invoke(hudB, "LateUpdate");
            Assert.IsFalse(onA.IsValid);
            Assert.IsTrue(onB.IsValid);
            AssertPosition(new Vector2(-10f, -20f), MarkerInstance(hudB, 0));

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hudB.gameObject);
            Object.DestroyImmediate(hudA.gameObject);
        }

        [Test]
        public void SourcePrefab_IsUnchangedByAddTrackingAndRemove()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            var target = new GameObject("Target");
            var prefabGo = new GameObject("StretchMarkerPrefab", typeof(RectTransform));
            RectTransform prefab = prefabGo.GetComponent<RectTransform>();
            prefab.anchorMin = Vector2.zero;
            prefab.anchorMax = Vector2.one;
            prefab.sizeDelta = new Vector2(12f, 6f);
            prefab.pivot = new Vector2(0.3f, 0.7f);
            prefab.anchoredPosition = new Vector2(5f, -5f);
            prefab.localScale = new Vector3(2f, 2f, 1f);
            new GameObject("Icon", typeof(RectTransform)).transform.SetParent(prefab, false);

            MinimapMarkerHandle tracking = hud.AddMarker(target.transform, prefab, true);
            MinimapMarkerHandle fixedMarker = hud.AddMarker(new Vector3(500f, 0f, 0f), prefab, true);
            for (int i = 0; i < 3; i++)
            {
                target.transform.position = new Vector3(i * 40f, 0f, -i * 30f);
                Invoke(hud, "LateUpdate");
            }

            RectTransform clone = MarkerInstance(hud, 0);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), clone.anchorMin, "The documented anchor override applies to the clone.");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), clone.anchorMax);
            hud.RemoveMarker(tracking);
            hud.RemoveMarker(fixedMarker);

            Assert.AreEqual("StretchMarkerPrefab", prefabGo.name);
            Assert.IsTrue(prefabGo.activeSelf);
            Assert.IsNull(prefab.parent);
            Assert.AreEqual(1, prefab.childCount);
            Assert.AreEqual(Vector2.zero, prefab.anchorMin);
            Assert.AreEqual(Vector2.one, prefab.anchorMax);
            Assert.AreEqual(new Vector2(12f, 6f), prefab.sizeDelta);
            Assert.AreEqual(new Vector2(0.3f, 0.7f), prefab.pivot);
            Assert.AreEqual(new Vector2(5f, -5f), prefab.anchoredPosition);
            Assert.AreEqual(new Vector3(2f, 2f, 1f), prefab.localScale);

            Object.DestroyImmediate(prefabGo);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void PoiPool_IsUnaffectedByDynamicMarkers()
        {
            MinimapHUD hud = BuildHud();
            hud.ViewRadiusMeters = 100f;
            GameObject tracked = BuildTrackedObject(hud, Vector3.zero);
            RectTransform template = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            new GameObject("Label", typeof(Text)).transform.SetParent(template, false);
            template.gameObject.SetActive(false);
            SetField(hud, "poiMarkerTemplate", template);
            MinimapData data = hud.GetComponentInChildren<MinimapData>();
            data.pointsOfInterest.Add(new PointOfInterestEntry
            {
                title = "Plaza",
                worldPosition = new Vector3(-20f, 0f, 35f),
            });
            RectTransform prefab = BuildMarkerPrefab(new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
            MinimapMarkerHandle first = hud.AddMarker(new Vector3(-20f, 0f, 35f), prefab, false);
            MinimapMarkerHandle second = hud.AddMarker(new Vector3(500f, 0f, 0f), prefab, true);

            Invoke(hud, "LateUpdate");

            var pool = (List<RectTransform>)typeof(MinimapHUD).GetField("poiMarkerPool", PrivateInstance).GetValue(hud);
            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual("Plaza", pool[0].GetComponentInChildren<Text>(true).text);
            AssertPosition(new Vector2(-20f, 35f), pool[0]);

            hud.RemoveMarker(first);
            hud.RemoveMarker(second);
            Invoke(hud, "LateUpdate");

            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(1, data.pointsOfInterest.Count);
            AssertPosition(new Vector2(-20f, 35f), pool[0]);

            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(template.gameObject);
            Object.DestroyImmediate(tracked);
            Object.DestroyImmediate(hud.gameObject);
        }
    }
}
