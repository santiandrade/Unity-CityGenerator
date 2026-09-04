using System.Collections.Generic;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 16: pins the two invariants multiple coexisting cities depend on.
    /// (1) TrafficNetwork/PedestrianNetwork build their nodes relative to their own root's
    /// transform (TransformPoint/TransformDirection), not in absolute world space -- verified by
    /// comparing an origin network against an identically-laid-out shifted one, so the assertion
    /// never has to duplicate the classes' own placement formulas.
    /// (2) AssignTrafficLights/PedestrianNetwork.Build only match TrafficLight/TrafficLightIntersection
    /// instances under their own CityGeneratorRoot ancestor -- verified with two cities placed at the
    /// exact same world position (so a distance-only match could not tell them apart), falling back
    /// to a scene-wide search when there is no CityGeneratorRoot ancestor at all.
    /// </summary>
    internal class MultiCityNetworkTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: false);
            spawned.Add(go);
            return go;
        }

        private TrafficNetwork CreateTrafficNetwork(Transform parent, float[] axesX, float[] axesZ)
        {
            GameObject go = Create("TrafficNetwork", parent);
            TrafficNetwork network = go.AddComponent<TrafficNetwork>();
            network.SetAxes(axesX, axesZ);
            return network;
        }

        private PedestrianNetwork CreatePedestrianNetwork(Transform parent, float[] axesX, float[] axesZ)
        {
            GameObject go = Create("PedestrianNetwork", parent);
            PedestrianNetwork network = go.AddComponent<PedestrianNetwork>();
            network.SetAxes(axesX, axesZ);
            return network;
        }

        private TrafficLight CreateTrafficLight(Transform parent, Vector3 worldPosition, Vector3 forward)
        {
            GameObject go = Create("TrafficLight", parent);
            go.transform.position = worldPosition;
            go.transform.rotation = Quaternion.LookRotation(forward);
            return go.AddComponent<TrafficLight>();
        }

        private TrafficLightIntersection CreateIntersection(Transform parent, Vector3 worldPosition)
        {
            GameObject go = Create("Intersection", parent);
            TrafficLightIntersection intersection = go.AddComponent<TrafficLightIntersection>();
            CreateTrafficLight(go.transform, worldPosition, Vector3.left);
            return intersection;
        }

        // --- Coordinates relative to the root -----------------------------------------------

        [Test]
        public void TrafficNetwork_NodePositions_AreOffsetByRootShift()
        {
            var originRoot = Create("CityOrigin");
            var shiftedRoot = Create("CityShifted");
            Vector3 shift = new(200f, 0f, 150f);
            shiftedRoot.transform.position = shift;

            float[] axesX = { -28f, 28f };
            float[] axesZ = { -28f, 28f };

            TrafficNetwork origin = CreateTrafficNetwork(originRoot.transform, axesX, axesZ);
            TrafficNetwork shifted = CreateTrafficNetwork(shiftedRoot.transform, axesX, axesZ);
            origin.Build();
            shifted.Build();

            Assert.AreEqual(origin.NodeCount, shifted.NodeCount);
            for (int i = 0; i < origin.NodeCount; i++)
            {
                Vector3 expected = origin.GetNode(i).Position + shift;
                Vector3 actual = shifted.GetNode(i).Position;
                Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.001f),
                    $"Node {i}: expected {expected}, got {actual}");
            }
        }

        [Test]
        public void PedestrianNetwork_NodePositions_AreOffsetByRootShift()
        {
            var originRoot = Create("CityOrigin");
            var shiftedRoot = Create("CityShifted");
            Vector3 shift = new(300f, 0f, -120f);
            shiftedRoot.transform.position = shift;

            float[] axesX = { 0f, 50f };
            float[] axesZ = { 0f, 50f };

            PedestrianNetwork origin = CreatePedestrianNetwork(originRoot.transform, axesX, axesZ);
            PedestrianNetwork shifted = CreatePedestrianNetwork(shiftedRoot.transform, axesX, axesZ);
            origin.Build();
            shifted.Build();

            Assert.AreEqual(origin.NodeCount, shifted.NodeCount);
            Assert.Greater(origin.NodeCount, 0);
            for (int i = 0; i < origin.NodeCount; i++)
            {
                Vector3 expected = origin.GetNode(i).Position + shift;
                Vector3 actual = shifted.GetNode(i).Position;
                Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.001f),
                    $"Node {i}: expected {expected}, got {actual}");
            }
        }

        // --- Hierarchy scoping, no leaks between cities -------------------------------------

        [Test]
        public void TrafficNetwork_AssignTrafficLights_OnlyMatchesOwnHierarchy()
        {
            // Both cities sit at the exact same world position: a plain distance/facing match
            // (the pre-SPEC-16 behaviour) could pick either light, so only the hierarchy scoping
            // itself can guarantee each network gets its own.
            var rootA = Create("CityA");
            rootA.AddComponent<CityGeneratorRoot>();
            var rootB = Create("CityB");
            rootB.AddComponent<CityGeneratorRoot>();

            float[] axesX = { 0f };
            float[] axesZ = { 0f };

            TrafficNetwork networkA = CreateTrafficNetwork(rootA.transform, axesX, axesZ);
            TrafficLight lightA = CreateTrafficLight(rootA.transform, new Vector3(5f, 0f, 0f), Vector3.left);

            TrafficNetwork networkB = CreateTrafficNetwork(rootB.transform, axesX, axesZ);
            TrafficLight lightB = CreateTrafficLight(rootB.transform, new Vector3(5f, 0f, 0f), Vector3.left);

            networkA.Build();
            networkB.Build();

            TrafficLight matchedA = FindTheOnlyMatchedLight(networkA);
            TrafficLight matchedB = FindTheOnlyMatchedLight(networkB);

            Assert.AreSame(lightA, matchedA, "Network A must match its own city's light, not city B's.");
            Assert.AreSame(lightB, matchedB, "Network B must match its own city's light, not city A's.");
        }

        [Test]
        public void PedestrianNetwork_Build_OnlyMatchesOwnHierarchyIntersections()
        {
            var rootA = Create("CityA");
            rootA.AddComponent<CityGeneratorRoot>();
            var rootB = Create("CityB");
            rootB.AddComponent<CityGeneratorRoot>();

            float[] axesX = { 0f, 50f };
            float[] axesZ = { 0f, 50f };

            PedestrianNetwork networkA = CreatePedestrianNetwork(rootA.transform, axesX, axesZ);
            TrafficLightIntersection intersectionA = CreateIntersection(rootA.transform, Vector3.zero);

            PedestrianNetwork networkB = CreatePedestrianNetwork(rootB.transform, axesX, axesZ);
            TrafficLightIntersection intersectionB = CreateIntersection(rootB.transform, Vector3.zero);

            networkA.Build();
            networkB.Build();

            Assert.IsTrue(HasCrossingMatching(networkA, intersectionA), "Network A should have a crossing matched to its own intersection.");
            Assert.IsFalse(HasCrossingMatching(networkA, intersectionB), "Network A must never match city B's intersection.");
            Assert.IsTrue(HasCrossingMatching(networkB, intersectionB), "Network B should have a crossing matched to its own intersection.");
            Assert.IsFalse(HasCrossingMatching(networkB, intersectionA), "Network B must never match city A's intersection.");
        }

        // --- Fallback to global search when there is no CityGeneratorRoot ancestor ----------

        [Test]
        public void TrafficNetwork_WithoutRootAncestor_FallsBackToGlobalSearch()
        {
            float[] axesX = { 0f };
            float[] axesZ = { 0f };

            // No CityGeneratorRoot anywhere in this test's hierarchy.
            TrafficNetwork network = CreateTrafficNetwork(null, axesX, axesZ);
            TrafficLight light = CreateTrafficLight(null, new Vector3(5f, 0f, 0f), Vector3.left);

            network.Build();

            Assert.AreSame(light, FindTheOnlyMatchedLight(network));
        }

        [Test]
        public void PedestrianNetwork_WithoutRootAncestor_FallsBackToGlobalSearch()
        {
            float[] axesX = { 0f, 50f };
            float[] axesZ = { 0f, 50f };

            PedestrianNetwork network = CreatePedestrianNetwork(null, axesX, axesZ);
            TrafficLightIntersection intersection = CreateIntersection(null, Vector3.zero);

            network.Build();

            Assert.IsTrue(HasCrossingMatching(network, intersection));
        }

        private static TrafficLight FindTheOnlyMatchedLight(TrafficNetwork network)
        {
            TrafficLight found = null;
            for (int i = 0; i < network.NodeCount; i++)
            {
                TrafficNetwork.Node node = network.GetNode(i);
                if (node.Light != null)
                {
                    Assert.IsNull(found, "More than one node matched a light; test setup should only produce one match.");
                    found = node.Light;
                }
            }

            return found;
        }

        private static bool HasCrossingMatching(PedestrianNetwork network, TrafficLightIntersection intersection)
        {
            for (int i = 0; i < network.NodeCount; i++)
            {
                PedestrianNode node = network.GetNode(i);
                if (node.Kind == PedestrianNodeKind.Crossing && node.Intersection == intersection)
                    return true;
            }

            return false;
        }
    }
}
