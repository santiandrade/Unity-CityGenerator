using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// <see cref="TrafficNetwork.FindNodeAhead"/> is what every <see cref="CarAgent"/> targets
    /// first, before it has ever routed through the graph (`fromNode == -1`). Unlike
    /// `PickNextNode` it is a blind spatial search, so it is the one place a vehicle can be aimed
    /// at a node the graph itself would never send it to — including an exit node facing off the
    /// city at its own boundary, which strands it there for good on arrival.
    /// </summary>
    internal class TrafficNetworkFindNodeAheadTests
    {
        private GameObject go;
        private TrafficNetwork network;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("TrafficNetwork");
            network = go.AddComponent<TrafficNetwork>();
            network.SetAxes(new float[] { -112f, -56f, 0f, 56f, 112f }, new float[] { -112f, -56f, 0f, 56f, 112f });
            network.Build();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void FindNodeAhead_NeverReturnsADeadEnd()
        {
            for (int i = 0; i < network.NodeCount; i++)
            {
                TrafficNetwork.Node spawn = network.GetNode(i);

                // Mirrors CityGeneratorTrafficBuilder.BuildVehicles: a vehicle starts exactly on a
                // node position, facing that node's own direction.
                int ahead = network.FindNodeAhead(spawn.Position, spawn.Direction);
                if (ahead < 0)
                    continue;

                Assert.Greater(network.GetNode(ahead).Exits.Count, 0,
                    $"Node {i} at {spawn.Position} heading {spawn.Direction} is aimed at dead-end node {ahead} at {network.GetNode(ahead).Position}.");
            }
        }

        [Test]
        public void FindNodeAhead_PerimeterEntryFacingOutOfTheCity_TargetsItsOwnNode()
        {
            // The east edge, middle: the entry approaching the boundary intersection head-on. The
            // street it is on ends there, so the only node "ahead" of it in its own direction is
            // the outward-facing exit past the intersection, which goes nowhere. Its own node is
            // the answer: it arrives immediately and PickNextNode turns it back into the city.
            int entry = -1;
            for (int i = 0; i < network.NodeCount; i++)
            {
                TrafficNetwork.Node node = network.GetNode(i);
                if (node.IsEntry && node.Direction == Vector3.right && Mathf.Approximately(node.Position.z, -2.6f) && node.Position.x > 100f)
                {
                    entry = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(entry, 0, "Expected an eastbound entry node on the city's east edge.");

            TrafficNetwork.Node spawn = network.GetNode(entry);
            Assert.AreEqual(entry, network.FindNodeAhead(spawn.Position, spawn.Direction));
            Assert.Greater(network.PickNextNode(entry), -1, "That entry must still have somewhere to go.");
        }
    }
}
