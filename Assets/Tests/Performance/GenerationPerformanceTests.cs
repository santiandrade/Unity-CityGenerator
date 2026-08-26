using CityGenerator.Editor;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace CityGenerator.Tests.Performance
{
    /// <summary>
    /// Baseline generation-time/GC-alloc measurements for the grid sizes this spec's item 7
    /// (spatial hash) baseline/delta comparison uses. Purely informational (see the spec's
    /// "no numeric threshold" decision) -- these are read from the Test Runner's result window
    /// or its exported results, then copied into the spec/PR by hand.
    /// </summary>
    internal class GenerationPerformanceTests
    {
        private GameObject root;
        private float offset;

        private CityGeneratorSettings MakeSettings(int gridWidth, int gridHeight)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = gridWidth;
            settings.general.gridHeight = gridHeight;
            settings.general.useCustomSeed = true;
            settings.general.seed = 42;
            return settings;
        }

        private void SetUpRoot()
        {
            root = new GameObject("PerfCity");
            root.transform.position = new Vector3(offset, 0f, offset);
            offset += 5000f;
        }

        private void CleanUpRoot()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private void MeasureGeneration(int gridWidth, int gridHeight)
        {
            CityGeneratorSettings settings = MakeSettings(gridWidth, gridHeight);

            Measure.Method(() =>
                {
                    CityGeneratorContentAssembler.Assemble(settings, root.transform);
                })
                .SetUp(SetUpRoot)
                .CleanUp(CleanUpRoot)
                .WarmupCount(1)
                .MeasurementCount(5)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void GenerationTime_1x3() => MeasureGeneration(1, 3);

        [Test, Performance]
        public void GenerationTime_5x5() => MeasureGeneration(5, 5);

        [Test, Performance]
        public void GenerationTime_10x10() => MeasureGeneration(10, 10);

        /// <summary>
        /// Rough managed-memory footprint of a generated city: GC.GetTotalMemory before/after,
        /// forcing a collection first so the delta reflects genuinely new live objects rather than
        /// whatever garbage happened to be pending. Approximate by nature (doesn't account for
        /// native/graphics memory the instantiated GameObjects/meshes/materials also hold) -- see
        /// the spec's "informative data, not a blocking threshold" decision.
        /// </summary>
        [Test, Performance]
        public void ApproxManagedMemory_5x5()
        {
            SetUpRoot();
            CityGeneratorSettings settings = MakeSettings(5, 5);

            System.GC.Collect();
            long before = System.GC.GetTotalMemory(true);

            CityGeneratorContentAssembler.Assemble(settings, root.transform);

            long after = System.GC.GetTotalMemory(false);

            Measure.Custom(new SampleGroup("ApproxManagedMemoryDelta", SampleUnit.Megabyte), (after - before) / (1024.0 * 1024.0));

            CleanUpRoot();
        }
    }
}
