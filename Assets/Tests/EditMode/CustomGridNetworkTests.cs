using System.Collections.Generic;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// SPEC 11: covers TrafficNetwork.BuildFromBlockCells/PedestrianNetwork.BuildFromBlockCells
    /// over a small irregular (L-triomino) shape on the fixed MaxGridSize canvas.
    /// </summary>
    internal class TrafficNetworkBuildFromBlockCellsTests
    {
        private GameObject go;
        private TrafficNetwork network;

        // MaxGridSize (10) + 1 street axes -- TrafficNetwork.BuildFromBlockCells always builds
        // over this fixed canvas, matching CityGeneratorConstants.MaxGridSize (Editor-only, so
        // duplicated here the same way TrafficNetwork itself keeps its own copy).
        private const int AxisCount = 11;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("TrafficNetwork");
            network = go.AddComponent<TrafficNetwork>();

            // L-triomino near canvas centre: (5,5)-(6,5)-(5,6). No island, orthogonally contiguous.
            var shape = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            network.BuildFromBlockCells(shape);
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        private static int NodeIndex(int i, int j, int k, bool entry) => (((i * AxisCount + j) * 4 + k) * 2) + (entry ? 0 : 1);

        [Test]
        public void BuildFromBlockCells_NodeCountMatchesFixedCanvas()
        {
            Assert.AreEqual(AxisCount * AxisCount * 4 * 2, network.NodeCount);
        }

        [Test]
        public void BuildFromBlockCells_StreetAdjacentToRealBlock_HasAStraightExit()
        {
            // Direction 0 (+X) from (5,5): adjacent blocks are (5,4)/(5,5) -- (5,5) is real.
            TrafficNetwork.Node entry = network.GetNode(NodeIndex(5, 5, 0, true));
            Assert.IsTrue(entry.Exits.Exists(e => e.Node == NodeIndex(5, 5, 0, false)));
        }

        [Test]
        public void BuildFromBlockCells_StreetWithNoAdjacentRealBlock_HasNoStraightExit()
        {
            // Direction 0 (+X) from (0,0): adjacent blocks are (0,-1)/(0,0) -- neither is in the shape.
            TrafficNetwork.Node entry = network.GetNode(NodeIndex(0, 0, 0, true));
            Assert.IsFalse(entry.Exits.Exists(e => e.Node == NodeIndex(0, 0, 0, false)));
        }

        [Test]
        public void BuildFromBlockCells_IntersectionTouchingNoRealBlock_IsNotAValidSpawnEntry()
        {
            // (0,0)'s four corner blocks -- (-1,-1),(0,-1),(-1,0),(0,0) -- are all outside the shape.
            for (int k = 0; k < 4; k++)
            {
                Assert.IsFalse(network.GetNode(NodeIndex(0, 0, k, true)).IsEntry, $"direction {k}");
            }
        }

        [Test]
        public void BuildFromBlockCells_IntersectionTouchingARealBlock_KeepsUsableEntries()
        {
            // (6,5)'s corner blocks include (5,5) and (6,5), both real.
            for (int k = 0; k < 4; k++)
            {
                Assert.IsTrue(network.GetNode(NodeIndex(6, 5, k, true)).IsEntry, $"direction {k}");
            }
        }
    }

    internal class PedestrianNetworkBuildFromBlockCellsTests
    {
        private GameObject go;
        private PedestrianNetwork network;

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
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

        // Centre of a Custom Grid cell on the fixed MaxGridSize (10) canvas, mirroring
        // CityGeneratorGrid.GetBlockCenter (Editor-only, so not callable from here).
        private static Vector3 CellCentre(Vector2Int cell)
            => new((cell.x - 4.5f) * 56f, 0f, (cell.y - 4.5f) * 56f);

        // Ring covers both a block's own 8-node ring and the walkway along the city's perimeter
        // sidewalk, which sits a whole street away from any block centre. Only the former is
        // counted here.
        private int CountBlockRingNodes(IEnumerable<Vector2Int> shape)
        {
            int count = 0;
            for (int i = 0; i < network.NodeCount; i++)
            {
                PedestrianNode node = network.GetNode(i);
                if (node.Kind != PedestrianNodeKind.Ring)
                    continue;

                foreach (Vector2Int cell in shape)
                {
                    Vector3 centre = CellCentre(cell);
                    if (Mathf.Abs(node.Position.x - centre.x) <= 19.6f && Mathf.Abs(node.Position.z - centre.z) <= 19.6f)
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        [Test]
        public void BuildFromBlockCells_LShape_EveryRealBlockGetsARingAndInteriorCross()
        {
            go = new GameObject("PedestrianNetwork");
            network = go.AddComponent<PedestrianNetwork>();

            var shape = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            network.BuildFromBlockCells(shape, new List<Vector2Int>(), new List<Vector2Int>());

            // 3 real blocks, none plaza/fully-reserved: 8-node ring + 5-node Interior cross each.
            Assert.AreEqual(3 * 8, CountBlockRingNodes(shape));
            Assert.AreEqual(3 * 5, CountKind(PedestrianNodeKind.Interior));
        }

        [Test]
        public void BuildFromBlockCells_PlazaCell_GetsRingButNoInteriorCross()
        {
            go = new GameObject("PedestrianNetwork");
            network = go.AddComponent<PedestrianNetwork>();

            var shape = new List<Vector2Int> { new(5, 5), new(6, 5) };
            var plazas = new List<Vector2Int> { new(6, 5) };
            network.BuildFromBlockCells(shape, plazas, new List<Vector2Int>());

            Assert.AreEqual(2 * 8, CountBlockRingNodes(shape));
            Assert.AreEqual(1 * 5, CountKind(PedestrianNodeKind.Interior));
        }

        [Test]
        public void BuildFromBlockCells_FullyReservedCell_GetsRingButNoInteriorCross()
        {
            go = new GameObject("PedestrianNetwork");
            network = go.AddComponent<PedestrianNetwork>();

            var shape = new List<Vector2Int> { new(5, 5), new(6, 5) };
            var fullyReserved = new List<Vector2Int> { new(5, 5) };
            network.BuildFromBlockCells(shape, new List<Vector2Int>(), fullyReserved);

            Assert.AreEqual(2 * 8, CountBlockRingNodes(shape));
            Assert.AreEqual(1 * 5, CountKind(PedestrianNodeKind.Interior));
        }

        [Test]
        public void BuildFromBlockCells_CellOutsideShape_GetsNoNodesAtAll()
        {
            go = new GameObject("PedestrianNetwork");
            network = go.AddComponent<PedestrianNetwork>();

            var shape = new List<Vector2Int> { new(5, 5) };
            network.BuildFromBlockCells(shape, new List<Vector2Int>(), new List<Vector2Int>());

            // Only the single real block's own ring + interior cross: 8 + 5 = 13 nodes, none of
            // them belonging to any other canvas cell.
            Assert.AreEqual(8, CountBlockRingNodes(shape));
            Assert.AreEqual(5, CountKind(PedestrianNodeKind.Interior));

            // Everything else is the perimeter walkway hugging this one block: 3 nodes per side
            // plus a corner node at each of the 4 outer corners. Nothing reaches a further cell.
            Assert.AreEqual(13 + 4 * 3 + 4, network.NodeCount);
            Vector3 centre = CellCentre(shape[0]);
            for (int i = 0; i < network.NodeCount; i++)
            {
                Vector3 position = network.GetNode(i).Position;
                Assert.LessOrEqual(Mathf.Abs(position.x - centre.x), 36.1f);
                Assert.LessOrEqual(Mathf.Abs(position.z - centre.z), 36.1f);
            }
        }
    }
}
