using System.Collections.Generic;
using CityGenerator.Editor;
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
            // crossing chain between blocks. Every normal block now also gets a 5-node Interior
            // cross right after its own 8-node ring (13 nodes/block total), so block index 3's
            // ring starts at 13 * 3, not 8 * 3.
            var path = new int[64];
            int otherBlockRingStart = 13 * 3; // block index 3 in row-major (bi,bj) order for a 2x2 grid
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
        public void ComponentOf_IsolatedRings_AreDifferentComponents()
        {
            // Node 0 belongs to block (0,0)'s ring; node 13 is the first node of block (0,1)'s
            // ring (8-node ring + 5-node Interior cross = 13 nodes/block, see BuildBlockRing/
            // BuildInteriorCross) -- isolated from each other with no TrafficLightIntersection to
            // wire a crossing chain between them.
            Assert.AreNotEqual(network.ComponentOf(0), network.ComponentOf(13));
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
        public void CanCross_NoIntersectionMatched_AlwaysAllowed()
        {
            // With no TrafficLightIntersection in the scene, no Crossing nodes exist at all in
            // this fixture's graph, so any node index queried here is a Ring node -- CanCross must
            // return true for anything that isn't a real Crossing node tied to an intersection.
            Assert.IsTrue(network.CanCross(0));
        }
    }

    /// <summary>
    /// SPEC 10: covers the three per-block outcomes CityGeneratorPedestrianBuilder.AddNetworkComponent
    /// drives Build() with -- a normal block, a plaza block, and a block with a full-block Custom
    /// Place -- using a 3x1 grid so all three sit in one row with no crossings to worry about
    /// (see PedestrianNetworkTests' own SetUp comment re: 1xN grids). Only a normal block gets an
    /// Interior cross: both a plaza block and a full-block Custom Place stay confined to their ring.
    /// </summary>
    internal class PedestrianNetworkInteriorTests
    {
        private GameObject networkGroupObject;
        private GameObject ground;
        private PedestrianNetwork network;

        private const float Offset = 2000000f;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(Offset, -10f, Offset);
            ground.transform.localScale = new Vector3(1000f, 20f, 1000f);
            Physics.SyncTransforms();

            networkGroupObject = new GameObject("PedestrianNetworkGroup");

            // Block (0,0) normal, block (1,0) a plaza, block (2,0) a full-block Custom Place.
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(3, 1, new[] { new Vector2Int(1, 0) });
            var reservedSlots = new HashSet<(int gridX, int gridY, int slot)> { (2, 0, -1) };

            network = CityGeneratorPedestrianBuilder.AddNetworkComponent(networkGroupObject.transform, 3, 1, blocks, reservedSlots);
            // Re-anchored away from the origin, same reasoning as PedestrianNetworkTests' Offset.
            network.SetAxes(
                new float[] { Offset - 84f, Offset - 28f, Offset + 28f, Offset + 84f },
                new float[] { Offset - 28f, Offset + 28f });
            network.Build();
        }

        [TearDown]
        public void TearDown()
        {
            if (networkGroupObject != null) Object.DestroyImmediate(networkGroupObject);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        [Test]
        public void Build_NormalBlock_GetsExactlyFiveInteriorNodesConnectedToItsRing()
        {
            Assert.AreEqual(5, CountKind(PedestrianNodeKind.Interior));

            // Block (0,0): ring 0-7 (sw,sMid,se,eMid,ne,nMid,nw,wMid), interior 8-12 (centre,armS,armE,armN,armW).
            const int centre = 8, armS = 9, armE = 10, armN = 11, armW = 12;
            const int sMid = 1, eMid = 3, nMid = 5, wMid = 7;

            Assert.AreEqual(PedestrianNodeKind.Interior, network.GetNode(centre).Kind);
            CollectionAssert.Contains(network.GetNode(centre).Neighbours, armS);
            CollectionAssert.Contains(network.GetNode(centre).Neighbours, armE);
            CollectionAssert.Contains(network.GetNode(centre).Neighbours, armN);
            CollectionAssert.Contains(network.GetNode(centre).Neighbours, armW);

            CollectionAssert.Contains(network.GetNode(armS).Neighbours, sMid);
            CollectionAssert.Contains(network.GetNode(armE).Neighbours, eMid);
            CollectionAssert.Contains(network.GetNode(armN).Neighbours, nMid);
            CollectionAssert.Contains(network.GetNode(armW).Neighbours, wMid);
        }

        [Test]
        public void Build_PlazaBlock_GetsNoInteriorNodes()
        {
            // Block (1,0) starts at index 13 (block (0,0)'s 8 ring + 5 interior nodes) and is only
            // its own 8-node ring -- a plaza block stays confined to its ring, same as a
            // full-block Custom Place.
            const int plazaBlockStart = 13;
            for (int i = plazaBlockStart; i < plazaBlockStart + 8; i++)
            {
                Assert.AreEqual(PedestrianNodeKind.Ring, network.GetNode(i).Kind);
            }
        }

        [Test]
        public void Build_FullBlockCustomPlace_GetsNoInteriorNodes()
        {
            // Block (2,0) starts at index 21 (13 + 8-node ring from block (1,0), the plaza block,
            // which itself got no Interior nodes) and is only its own 8-node ring.
            const int reservedBlockStart = 13 + 8;

            // The 3 blocks' own 29 nodes come first, then the 28-node walkway along the perimeter
            // sidewalk that closes this 3x1 city (3 nodes per block-length of contour, plus a
            // corner node at each of the 4 outer corners).
            Assert.AreEqual(29 + 28, network.NodeCount);
            for (int i = reservedBlockStart; i < reservedBlockStart + 8; i++)
            {
                Assert.AreEqual(PedestrianNodeKind.Ring, network.GetNode(i).Kind);
            }
        }

        private int CountKind(PedestrianNodeKind kind)
        {
            int count = 0;
            for (int i = 0; i < network.NodeCount; i++)
            {
                if (network.GetNode(i).Kind == kind)
                    count++;
            }
            return count;
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
