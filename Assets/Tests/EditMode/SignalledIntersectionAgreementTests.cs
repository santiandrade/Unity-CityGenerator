using System.Collections.Generic;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// Pins the contract the validator relies on: CityGeneratorTrafficBuilder.HasSignalledIntersection
    /// answers exactly whether BuildTrafficLights would instantiate anything. The two used to be
    /// derived independently, and a grid where they disagreed (1x2, 2x1, or a Custom shape with a
    /// T-intersection) passed validation with no Traffic Light prefab and then instantiated a null
    /// one mid-generation. Asserting the equivalence -- rather than each side's expected value --
    /// is what keeps a future change to the arm rule from reopening the gap.
    /// </summary>
    internal class SignalledIntersectionAgreementTests
    {
        private GameObject trafficLightPrefab;
        private GameObject leftTrafficLightPrefab;
        private GameObject group;

        [SetUp]
        public void SetUp()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            trafficLightPrefab = settings.props.rightTrafficLightPrefab;
            leftTrafficLightPrefab = settings.props.leftTrafficLightPrefab;
            Assert.IsNotNull(trafficLightPrefab, "The default assets must provide a Traffic Light prefab for this test.");
            group = new GameObject("TrafficLights");
        }

        [TearDown]
        public void TearDown()
        {
            if (group != null)
                Object.DestroyImmediate(group);
        }

        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        [TestCase(1, 4)]
        [TestCase(3, 2)]
        public void RectangularGrid_PredicateAgreesWithBuilder(int gridWidth, int gridHeight)
        {
            bool predicted = CityGeneratorTrafficBuilder.HasSignalledIntersection(gridWidth, gridHeight);
            List<GameObject> placed = CityGeneratorTrafficBuilder.BuildTrafficLights(
                trafficLightPrefab, TrafficLightSide.Right, group.transform, gridWidth, gridHeight, new System.Random(1));

            Assert.AreEqual(predicted, placed.Count > 0,
                $"Grid {gridWidth}x{gridHeight}: predicate says {predicted} but the builder placed {placed.Count} lights.");
        }

        private static IEnumerable<TestCaseData> CustomShapes()
        {
            yield return new TestCaseData(new List<Vector2Int> { new(5, 5) }).SetName("SingleCell");
            yield return new TestCaseData(new List<Vector2Int> { new(5, 5), new(6, 5) }).SetName("Domino");
            yield return new TestCaseData(new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) }).SetName("LTriomino");
            yield return new TestCaseData(new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6), new(6, 6) }).SetName("Square");
            yield return new TestCaseData(new List<Vector2Int> { new(5, 5), new(7, 7) }).SetName("TwoDisjointCells");
        }

        [TestCaseSource(nameof(CustomShapes))]
        public void CustomGrid_PredicateAgreesWithBuilder(List<Vector2Int> shape)
        {
            bool predicted = CityGeneratorTrafficBuilder.HasSignalledIntersection(shape);
            List<GameObject> placed = CityGeneratorTrafficBuilder.BuildTrafficLights(
                trafficLightPrefab, TrafficLightSide.Right, group.transform, shape, new System.Random(1));

            Assert.AreEqual(predicted, placed.Count > 0,
                $"Custom shape of {shape.Count} cells: predicate says {predicted} but the builder placed {placed.Count} lights.");
        }

        [Test]
        public void CustomGrid_EmptyShape_NeedsNoLights()
        {
            Assert.IsFalse(CityGeneratorTrafficBuilder.HasSignalledIntersection(new List<Vector2Int>()));
        }

        [TestCase(false, TrafficLightSide.Right)]
        [TestCase(true, TrafficLightSide.Right)]
        [TestCase(false, TrafficLightSide.Left)]
        [TestCase(true, TrafficLightSide.Left)]
        public void GeneratedLights_StandOnChosenSideAndRegulateTheirArrivalLane(bool customGrid, TrafficLightSide side)
        {
            group.AddComponent<CityGeneratorRoot>();
            var random = new System.Random(1);
            var cells = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6), new(6, 6) };
            bool left = side == TrafficLightSide.Left;
            GameObject prefab = left ? leftTrafficLightPrefab : trafficLightPrefab;
            Assert.AreEqual(left ? "TrafficLight_Left" : "TrafficLight_Right", prefab.name);
            float sideSign = left ? -1f : 1f;

            var network = CityGeneratorTrafficBuilder.AddNetworkComponent(group.transform, 2, 2);
            List<GameObject> placed = customGrid
                ? CityGeneratorTrafficBuilder.BuildTrafficLights(prefab, side, group.transform, cells, random)
                : CityGeneratorTrafficBuilder.BuildTrafficLights(prefab, side, group.transform, 2, 2, random);
            if (customGrid)
                network.BuildFromBlockCells(cells);
            else
                network.Build();

            Assert.AreEqual(16, placed.Count);
            Vector3[] arms = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            int gridSize = customGrid ? CityGeneratorConstants.MaxGridSize : 2;
            foreach (GameObject instance in placed)
            {
                Transform signal = instance.transform;
                string[] coordinates = signal.parent.name.Split('_');
                Vector3 centre = new(
                    CityGeneratorGrid.GetStreetAxisPosition(gridSize, int.Parse(coordinates[1])),
                    0f,
                    CityGeneratorGrid.GetStreetAxisPosition(gridSize, int.Parse(coordinates[2])));
                Vector3 approach = -arms[int.Parse(signal.name.Split('_')[1])];
                Vector3 right = Vector3.Cross(Vector3.up, approach);
                Vector3 offset = signal.position - centre;
                Assert.That(Vector3.Dot(offset, right), Is.EqualTo(6.2f * sideSign).Within(0.001f));
                Assert.That(Vector3.Dot(offset, approach), Is.EqualTo(-6.2f).Within(0.001f));
                Assert.That(Vector3.Dot(signal.forward, approach), Is.GreaterThan(0.999f));
                // The arm and housing reach over the road, i.e. toward the centre of the street.
                Assert.That(signal.Find("Arm").localPosition.x, Is.EqualTo(-1.45f * sideSign));
                Assert.That(signal.Find("Housing").localPosition.x, Is.EqualTo(-2.7f * sideSign));
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance), Is.EqualTo(prefab));

                TrafficNetwork.Node arrival = null;
                for (int index = 0; index < network.NodeCount; index++)
                {
                    TrafficNetwork.Node node = network.GetNode(index);
                    if (node.IsEntry && node.Direction == approach && (node.Position - centre).sqrMagnitude < 25f)
                    {
                        arrival = node;
                        break;
                    }
                }
                Assert.IsNotNull(arrival);
                Assert.AreSame(instance.GetComponent<TrafficLight>(), arrival.Light, signal.parent.name + "/" + signal.name);
            }
        }
    }
}
