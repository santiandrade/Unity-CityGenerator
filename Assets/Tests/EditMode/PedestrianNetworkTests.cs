using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    internal class PedestrianNetworkTests
    {
        private GameObject go;
        private GameObject ground;
        private PedestrianNetwork network;

        // EditMode tests run against whatever scene is currently open in the Editor (this
        // project's own City.unity, with a real generated city), not an isolated empty scene.
        // Placing this fixture's synthetic ring far from the origin keeps PrunePlacedObstacles'
        // Physics queries (run at the end of every Build()) from colliding with real scene
        // geometry that happens to share the same layout coordinates.
        private const float Offset = 1000000f;

        [SetUp]
        public void SetUp()
        {
            // PrunePlacedObstacles (called at the end of every Build()) raycasts straight down
            // from each node to confirm it has ground under it; without any collider under this
            // fixture's own (far away) nodes that raycast always misses and every node ends up
            // wrongly Blocked.
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(Offset, -10f, Offset);
            ground.transform.localScale = new Vector3(500f, 20f, 500f);
            Physics.SyncTransforms();

            go = new GameObject("PedestrianNetwork");
            network = go.AddComponent<PedestrianNetwork>();
            // 2x2 blocks, no TrafficLightIntersection in the scene: each block's ring is its own
            // isolated connected component (no crossing chains link them), which is exactly what
            // BFS/connectivity needs to exercise.
            network.SetAxes(new float[] { Offset - 56f, Offset, Offset + 56f }, new float[] { Offset - 56f, Offset, Offset + 56f });
            network.Build();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        [Test]
        public void FindPath_BetweenTwoNodesOnTheSameRing_FindsAShortestPath()
        {
            // Ring nodes for the first block are added first, in a fixed 8-node cycle order:
            // sw(0) sMid(1) se(2) eMid(3) ne(4) nMid(5) nw(6) wMid(7). sw <-> se should be 2 hops
            // going either way around the 8-node ring.
            var path = new int[16];
            int length = network.FindPath(0, 2, path);

            Assert.Greater(length, 0);
            Assert.AreEqual(0, path[0]);
            Assert.AreEqual(2, path[length - 1]);
            Assert.AreEqual(3, length); // sw -> sMid -> se
        }

        [Test]
        public void FindPath_SameStartAndEnd_ReturnsSingleNodePath()
        {
            var path = new int[4];
            int length = network.FindPath(0, 0, path);

            Assert.AreEqual(1, length);
            Assert.AreEqual(0, path[0]);
        }

        [Test]
        public void FindPath_UnreachableNode_ReturnsZero()
        {
            // Node 0 (block (0,0) ring) and a node belonging to the diagonal block (1,1)'s ring
            // are not connected: no TrafficLightIntersection exists in this scene to wire a
            // crossing chain between blocks.
            var path = new int[64];
            int otherBlockRingStart = 8 * 3; // block index 3 in row-major (bi,bj) order for a 2x2 grid
            int length = network.FindPath(0, otherBlockRingStart, path);

            Assert.AreEqual(0, length);
        }

        [Test]
        public void FindPath_ThroughBlockedNode_IsRejected()
        {
            // sw(0) -> sMid(1) -> se(2) is the short way around; block sMid and force the long way.
            network.SetBlocked(1, true);
            var path = new int[16];
            int length = network.FindPath(0, 2, path);

            Assert.Greater(length, 0);
            CollectionAssert.DoesNotContain(path.GetRangeSubset(length), 1);
        }

        [Test]
        public void RegisterPointOfInterest_SurvivesRebuild()
        {
            int ringNode = 0; // sw corner of block (0,0)
            Vector3 poiPosition = network.GetNode(ringNode).Position + new Vector3(1f, 0f, 1f);
            int poiIndex = network.RegisterPointOfInterest(poiPosition, poiPosition, ringNode);

            // FindPath immediately after registering (before any rebuild) works.
            var path = new int[8];
            int length = network.FindPath(ringNode, poiIndex, path);
            Assert.Greater(length, 0);

            // Build() wipes `nodes` from scratch: the POI must be replayed from its serialized
            // descriptor, at a *new* index (nodes are rebuilt in the same deterministic order, so
            // it lands back at the same index here, but the test only relies on FindNearestNode).
            network.Build();

            int poiAfterRebuild = network.FindNearestNode(poiPosition, PedestrianNodeKind.PointOfInterest);
            Assert.GreaterOrEqual(poiAfterRebuild, 0);

            var pathAfterRebuild = new int[8];
            int lengthAfterRebuild = network.FindPath(0, poiAfterRebuild, pathAfterRebuild);
            Assert.Greater(lengthAfterRebuild, 0, "POI connection did not survive Build().");
        }

        [Test]
        public void ComponentOf_IsolatedRings_AreDifferentComponents()
        {
            // Node 0 belongs to block (0,0)'s ring; node 8 is the first node of block (0,1)'s ring
            // (see BuildBlockRing's fixed 8-nodes-per-block order) -- isolated from each other with
            // no TrafficLightIntersection to wire a crossing chain between them.
            Assert.AreNotEqual(network.ComponentOf(0), network.ComponentOf(8));
        }

        [Test]
        public void PickRandomDestination_WithRequiredComponent_NeverReturnsAnotherComponent()
        {
            int requiredComponent = network.ComponentOf(0);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                int candidate = network.PickRandomDestination(requiredComponent);
                if (candidate >= 0)
                    Assert.AreEqual(requiredComponent, network.ComponentOf(candidate));
            }
        }

        [Test]
        public void RegisterPointOfInterest_RepeatedlyAfterBuild_DoesNotThrow()
        {
            // Regression test: registering several POIs after Build() (as
            // CityGeneratorPedestrianBuilder.RegisterPointsOfInterest does once per plaza block)
            // used to grow `nodes` without keeping the ComponentOf/PickRandomDestination-backing
            // array in sync, throwing IndexOutOfRangeException the next time a pedestrian picked a
            // destination.
            for (int i = 0; i < 5; i++)
            {
                Vector3 poiPosition = network.GetNode(0).Position + new Vector3(i, 0f, i);
                int poiIndex = network.RegisterPointOfInterest(poiPosition, poiPosition, 0);
                Assert.AreEqual(network.ComponentOf(0), network.ComponentOf(poiIndex));
            }

            Assert.DoesNotThrow(() =>
            {
                for (int attempt = 0; attempt < 50; attempt++)
                    network.PickRandomDestination(network.ComponentOf(0));
            });
        }

        [Test]
        public void CanCross_NoIntersectionMatched_AlwaysAllowed()
        {
            // With no TrafficLightIntersection in the scene, no Crossing nodes exist at all in
            // this fixture's graph, so any node index queried here is a Ring node -- CanCross must
            // return true for anything that isn't a real Crossing node tied to an intersection.
            Assert.IsTrue(network.CanCross(0));
        }
    }

    internal static class PedestrianNetworkTestExtensions
    {
        public static int[] GetRangeSubset(this int[] array, int length)
        {
            var result = new int[length];
            System.Array.Copy(array, result, length);
            return result;
        }
    }
}
