using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
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
        private GameObject group;

        [SetUp]
        public void SetUp()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            trafficLightPrefab = settings.props.trafficLightPrefab;
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
                trafficLightPrefab, group.transform, gridWidth, gridHeight, new System.Random(1));

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
                trafficLightPrefab, group.transform, shape, new System.Random(1));

            Assert.AreEqual(predicted, placed.Count > 0,
                $"Custom shape of {shape.Count} cells: predicate says {predicted} but the builder placed {placed.Count} lights.");
        }

        [Test]
        public void CustomGrid_EmptyShape_NeedsNoLights()
        {
            Assert.IsFalse(CityGeneratorTrafficBuilder.HasSignalledIntersection(new List<Vector2Int>()));
        }
    }
}
