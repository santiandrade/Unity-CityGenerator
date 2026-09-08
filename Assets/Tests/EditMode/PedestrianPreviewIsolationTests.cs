using System.Reflection;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// Pins the isolation contract of the Custom Pedestrians graph preview. The preview instantiates
    /// the user's real Traffic Light Prefab at every intersection, and it once did so under a merely
    /// HideFlags.HideAndDontSave root -- which hides an object from the Hierarchy but still renders
    /// it and still registers its colliders. Editing Grid Width/Height therefore painted a grid of
    /// traffic lights across the Scene view that the Hierarchy could not account for.
    ///
    /// The assertions below cover the three halves of the fix that a future refactor could silently
    /// undo: the root is inactive (nothing renders), it carries its own CityGeneratorRoot (so
    /// PedestrianNetwork scopes its TrafficLightIntersection search to the preview instead of
    /// falling back to a scene-wide search, which under an inactive root sees nothing -- and which
    /// used to cross-match against a real generated city's intersections), and the graph itself is
    /// unchanged by being built inactive.
    /// </summary>
    internal class PedestrianPreviewIsolationTests
    {
        private const string PreviewRootName = "CityGeneratorPedestrianPreview (temporary)";

        private static CityGeneratorSettings BuildSettings(int gridWidth, int gridHeight)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            Assert.IsNotNull(settings.props.trafficLightPrefab, "The default assets must provide a Traffic Light prefab for this test.");
            settings.general.useCustomGrid = false;
            settings.general.gridWidth = gridWidth;
            settings.general.gridHeight = gridHeight;
            settings.general.plazaCells.Clear();
            settings.customPlaces.Clear();
            return settings;
        }

        private static GameObject GetRoot(CityGeneratorPedestrianPreview preview)
        {
            return (GameObject)typeof(CityGeneratorPedestrianPreview)
                .GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(preview);
        }

        [TestCase(3, 3)]
        [TestCase(4, 2)]
        public void PreviewNeverRendersIntoTheScene(int gridWidth, int gridHeight)
        {
            using var preview = CityGeneratorPedestrianPreview.Build(BuildSettings(gridWidth, gridHeight));
            GameObject root = GetRoot(preview);

            Assert.IsFalse(root.activeInHierarchy, "The preview root must be inactive; HideAndDontSave alone does not stop it rendering.");
            Assert.Greater(root.GetComponentsInChildren<TrafficLight>(true).Length, 0, "Sanity: the preview really does instantiate the Traffic Light prefab, which is why isolation matters.");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsFalse(renderer.gameObject.activeInHierarchy, "A preview renderer would be visible in the Scene view: " + renderer.name);
            }
        }

        [Test]
        public void PreviewCarriesItsOwnCityGeneratorRootSoTheGraphStaysScoped()
        {
            using var preview = CityGeneratorPedestrianPreview.Build(BuildSettings(3, 3));
            GameObject root = GetRoot(preview);

            Assert.IsNotNull(root.GetComponent<CityGeneratorRoot>(), "Without the marker, PedestrianNetwork falls back to a scene-wide intersection search.");
            Assert.IsFalse(root.scene.IsValid(), "A HideAndDontSave root belongs to no scene, which is what keeps the Scene-level CityGeneratorRoot searches from ever seeing it.");

            int crossings = 0;
            for (int i = 0; i < preview.NodeCount; i++)
            {
                if (preview.GetNode(i).Kind == PedestrianNodeKind.Crossing)
                    crossings++;
            }

            Assert.Greater(crossings, 0, "The scoped search must still match the preview's own intersections, or every Curb/Crossing node disappears.");
        }

        [TestCase(3, 3)]
        [TestCase(4, 2)]
        public void BuildingInactiveDoesNotChangeTheGraph(int gridWidth, int gridHeight)
        {
            CityGeneratorSettings settings = BuildSettings(gridWidth, gridHeight);

            using var preview = CityGeneratorPedestrianPreview.Build(settings);
            int inactiveNodeCount = preview.NodeCount;
            int inactiveFingerprint = preview.Fingerprint();

            // The same graph built the way the preview would be if its root were left active: same
            // builders, same order, same fixed seed -- only activeSelf differs.
            var activeRoot = new GameObject("PedestrianPreviewIsolationTests active reference");
            try
            {
                activeRoot.AddComponent<CityGeneratorRoot>();
                var lightsGroup = new GameObject("TrafficLights").transform;
                lightsGroup.SetParent(activeRoot.transform);
                var networkGroup = new GameObject("PedestrianNetwork").transform;
                networkGroup.SetParent(activeRoot.transform);

                var blocks = CityGeneratorGrid.BuildBlocks(gridWidth, gridHeight, settings.general.plazaCells);
                var reservedSlots = CityGeneratorCustomPlaceBuilder.ResolveReservedSlots(settings.customPlaces, blocks);
                CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, lightsGroup, gridWidth, gridHeight, new System.Random(0));
                PedestrianNetwork network = CityGeneratorPedestrianBuilder.AddNetworkComponent(networkGroup, gridWidth, gridHeight, blocks, reservedSlots);
                network.Build();

                Assert.AreEqual(network.NodeCount, inactiveNodeCount, "Building the preview inactive changed how many nodes it has.");

                unchecked
                {
                    int fingerprint = network.NodeCount;
                    for (int i = 0; i < network.NodeCount; i++)
                    {
                        Vector3 position = network.GetNode(i).Position;
                        fingerprint = fingerprint * 31 + Mathf.RoundToInt(position.x * 10f);
                        fingerprint = fingerprint * 31 + Mathf.RoundToInt(position.z * 10f);
                    }

                    Assert.AreEqual(fingerprint, inactiveFingerprint, "Building the preview inactive moved its nodes, which would invalidate every saved Custom Pedestrian route.");
                }
            }
            finally
            {
                Object.DestroyImmediate(activeRoot);
            }
        }

        [Test]
        public void BuildReapsALeakedRootButNeverALivePreview()
        {
            // A HideAndDontSave root survives a domain reload while the window's reference to it
            // does not, so one stranded that way stays invisible and unreachable by its owner.
            var leaked = new GameObject(PreviewRootName) { hideFlags = HideFlags.HideAndDontSave };
            CityGeneratorPedestrianPreview live = CityGeneratorPedestrianPreview.Build(BuildSettings(2, 2));
            try
            {
                GameObject liveRoot = GetRoot(live);
                using var second = CityGeneratorPedestrianPreview.Build(BuildSettings(3, 3));

                Assert.IsTrue(leaked == null, "The orphaned root from a previous session should have been destroyed.");
                Assert.IsTrue(liveRoot != null, "A preview that is still alive must never be reaped -- CityGeneratorValidator can hold one alongside the window's.");
            }
            finally
            {
                live.Dispose();
                if (leaked != null)
                    Object.DestroyImmediate(leaked);
            }
        }
    }
}
