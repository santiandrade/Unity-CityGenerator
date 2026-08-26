using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// TrafficNetwork.RouteWeight/Ring are private (no generation-time need to expose them), so
    /// this test drives them by reflection rather than widening their visibility just for tests.
    /// </summary>
    internal class TrafficNetworkRouteWeightTests
    {
        private GameObject go;
        private TrafficNetwork network;
        private MethodInfo ringMethod;
        private MethodInfo routeWeightMethod;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("TrafficNetwork");
            network = go.AddComponent<TrafficNetwork>();

            // 5x5 axes: index 2 is the exact centre (interior), 0/4 are the perimeter.
            network.SetAxes(new float[] { -112f, -56f, 0f, 56f, 112f }, new float[] { -112f, -56f, 0f, 56f, 112f });
            network.Build();

            ringMethod = typeof(TrafficNetwork).GetMethod("Ring", BindingFlags.NonPublic | BindingFlags.Instance);
            routeWeightMethod = typeof(TrafficNetwork).GetMethod("RouteWeight", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        private float Ring(int i, int j) => (float)ringMethod.Invoke(network, new object[] { i, j });

        private float RouteWeight(float baseWeight, int fromI, int fromJ, int toI, int toJ)
            => (float)routeWeightMethod.Invoke(network, new object[] { baseWeight, fromI, fromJ, toI, toJ });

        [Test]
        public void Ring_CentreIntersection_IsZero()
        {
            Assert.AreEqual(0f, Ring(2, 2), 0.0001f);
        }

        [Test]
        public void Ring_CornerIntersection_IsMaximal()
        {
            Assert.AreEqual(2f, Ring(0, 0), 0.0001f);
            Assert.AreEqual(2f, Ring(4, 4), 0.0001f);
        }

        [Test]
        public void RouteWeight_MovingTowardsInterior_AppliesInteriorBias()
        {
            float weight = RouteWeight(1f, 0, 2, 1, 2); // perimeter -> next ring in
            Assert.Greater(weight, 1f);
        }

        [Test]
        public void RouteWeight_MovingTowardsPerimeter_KeepsBaseWeight()
        {
            float weight = RouteWeight(1f, 1, 2, 0, 2); // interior -> perimeter
            Assert.AreEqual(1f, weight, 0.0001f);
        }

        [Test]
        public void RouteWeight_StayingOnPerimeterRing_IsPenalized()
        {
            float weight = RouteWeight(1f, 0, 1, 0, 2); // both on the outermost ring
            Assert.Less(weight, 1f);
        }

        [Test]
        public void RouteWeight_StayingOnInteriorRing_KeepsBaseWeight()
        {
            float weight = RouteWeight(1f, 1, 2, 2, 1); // both ring distance 1 (interior), not perimeter
            Assert.AreEqual(1f, weight, 0.0001f);
        }
    }
}
