using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// Exercises PedestrianNetwork.CanCross against a directly-wired TrafficLightIntersection,
    /// bypassing the geometry-matching Build() normally performs (covered separately by the
    /// EditMode pathfinding tests) so the light-state logic itself can be tested in isolation.
    /// </summary>
    internal class PedestrianCanCrossTests
    {
        private GameObject networkGo;
        private PedestrianNetwork network;
        private GameObject intersectionGo;
        private TrafficLightIntersection intersection;
        private TrafficLight eastWestLight;
        private int crossingNodeIndex;

        [SetUp]
        public void SetUp()
        {
            networkGo = new GameObject("PedestrianNetwork");
            network = networkGo.AddComponent<PedestrianNetwork>();

            intersectionGo = new GameObject("Intersection");
            intersection = intersectionGo.AddComponent<TrafficLightIntersection>();
            var ewGo = new GameObject("EW");
            eastWestLight = ewGo.AddComponent<TrafficLight>();
            var nsGo = new GameObject("NS");
            TrafficLight nsLight = nsGo.AddComponent<TrafficLight>();
            SetPrivate(intersection, "eastWest", new List<TrafficLight> { eastWestLight });
            SetPrivate(intersection, "northSouth", new List<TrafficLight> { nsLight });

            var trafficNetworkGo = new GameObject("TrafficNetwork");
            TrafficNetwork trafficNetwork = trafficNetworkGo.AddComponent<TrafficNetwork>();
            SetPrivate(network, "trafficNetwork", trafficNetwork);

            crossingNodeIndex = network.AddNode(Vector3.zero, PedestrianNodeKind.Crossing);
            List<PedestrianNode> nodes = (List<PedestrianNode>)GetPrivate(network, "nodes");
            PedestrianNode node = nodes[crossingNodeIndex];
            node.Intersection = intersection;
            node.CrossingAxisIsX = true;
            nodes[crossingNodeIndex] = node;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(networkGo);
            Object.Destroy(intersectionGo);
            if (eastWestLight != null) Object.Destroy(eastWestLight.gameObject);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            info.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            return info.GetValue(target);
        }

        [Test]
        public void CanCross_WhenAxisIsRed_ReturnsTrue()
        {
            eastWestLight.SetState(TrafficLightState.Red);
            Assert.IsTrue(network.CanCross(crossingNodeIndex));
        }

        [Test]
        public void CanCross_WhenAxisIsGreen_ReturnsFalse()
        {
            eastWestLight.SetState(TrafficLightState.Green);
            Assert.IsFalse(network.CanCross(crossingNodeIndex));
        }

        [Test]
        public void CanCross_WhenAxisIsAmber_ReturnsFalse()
        {
            // Amber still has cars moving/braking through: only Red is safe to step onto.
            eastWestLight.SetState(TrafficLightState.Amber);
            Assert.IsFalse(network.CanCross(crossingNodeIndex));
        }
    }
}
