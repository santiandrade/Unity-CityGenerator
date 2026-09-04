using System.Collections;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// Characterization coverage for MinimapHUD (SPEC 15, closing a P1 gap the technical review
    /// flagged): describes what the component already does, without changing its behaviour. Building
    /// a real PlayerController (required by MinimapHUD.Start's FindAnyObjectByType lookup) drags in
    /// its own RequireComponents; its Awake logs a benign "no InputActionAsset assigned" warning
    /// with no assigned asset, which these tests explicitly expect rather than suppress.
    /// </summary>
    internal class MinimapHUDTests
    {
        private static MinimapHUD BuildHud()
        {
            var go = new GameObject("MinimapHUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            MinimapHUD hud = go.AddComponent<MinimapHUD>();

            var mapImageGo = new GameObject("MapImage", typeof(RawImage));
            mapImageGo.transform.SetParent(go.transform, false);
            var mapImage = mapImageGo.GetComponent<RawImage>();

            var field = typeof(MinimapHUD).GetField("mapImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(hud, mapImage);

            return hud;
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
    }
}
