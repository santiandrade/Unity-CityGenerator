using System.Reflection;
using System.Text.RegularExpressions;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// Covers CityGeneratorAPI's registration/resolution lifecycle (SPEC 15). Cities are synthetic
    /// CityGeneratorInfo components built by hand, never generated through the pipeline -- what is
    /// under test here is resolution and lifecycle, not generation.
    /// </summary>
    internal class CityGeneratorAPITests
    {
        private static void SetStaticField(string field, object value)
        {
            FieldInfo info = typeof(CityGeneratorAPI).GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(info, $"Field '{field}' not found on CityGeneratorAPI");
            info.SetValue(null, value);
        }

        private static CityGeneratorInfo BuildCity(string name)
        {
            var go = new GameObject(name);
            return go.AddComponent<CityGeneratorInfo>();
        }

        [SetUp]
        public void ResetAmbiguousWarningFlag()
        {
            // Default's "warn once per session" flag is static and would otherwise leak between
            // tests -- reset it so each test that exercises the ambiguous-Default warning starts clean.
            SetStaticField("warnedAmbiguousDefault", false);
        }

        [TearDown]
        public void EnsureRegistryIsEmpty()
        {
            Assert.AreEqual(0, CityGeneratorAPI.Count, "A test left a city registered -- this contaminates every test that runs after it.");
        }

        [Test]
        public void Count_IsZero_WithNoCities()
        {
            Assert.AreEqual(0, CityGeneratorAPI.Count);
            Assert.IsNull(CityGeneratorAPI.Default);
            Assert.AreEqual(0, CityGeneratorAPI.All.Count);
        }

        [Test]
        public void Count_IsOne_AfterOneCityRegisters()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");

            Assert.AreEqual(1, CityGeneratorAPI.Count);

            Object.DestroyImmediate(cityInfo.gameObject);
        }

        [Test]
        public void DeactivatingRoot_RemovesCityFromAllDefaultAndInScene_ReactivatingRestoresIt()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");
            Scene scene = cityInfo.gameObject.scene;

            Assert.AreEqual(1, CityGeneratorAPI.Count);
            Assert.IsNotNull(CityGeneratorAPI.Default);
            Assert.IsNotNull(CityGeneratorAPI.InScene(scene));

            cityInfo.gameObject.SetActive(false);
            Assert.AreEqual(0, CityGeneratorAPI.Count);
            Assert.IsNull(CityGeneratorAPI.Default);
            Assert.IsNull(CityGeneratorAPI.InScene(scene));

            cityInfo.gameObject.SetActive(true);
            Assert.AreEqual(1, CityGeneratorAPI.Count);
            Assert.IsNotNull(CityGeneratorAPI.Default);
            Assert.IsNotNull(CityGeneratorAPI.InScene(scene));

            Object.DestroyImmediate(cityInfo.gameObject);
        }

        [Test]
        public void DestroyedCity_HandleBecomesInvalid_AndGettersReturnDefaultsWithoutThrowing()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");
            cityInfo.buildingCount = 42;
            CityGeneratorCity handle = CityGeneratorAPI.Default.Value;
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(42, handle.City.BuildingCount);

            Object.DestroyImmediate(cityInfo.gameObject);

            Assert.IsFalse(handle.IsValid);
            Assert.IsFalse(handle.IsActive);
            Assert.DoesNotThrow(() =>
            {
                Assert.AreEqual(0, handle.City.BuildingCount);
                Assert.AreEqual(Vector2Int.zero, handle.City.GridSize);
                Assert.AreEqual(Vector3.zero, handle.Player.Position);
                Assert.IsFalse(handle.Minimap.IsEnabled);
                handle.City.SetHour(12f);
                handle.Minimap.SetVisible(true);
            });
        }

        [Test]
        public void Default_ResolvesTheSingleRegisteredCity()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");

            CityGeneratorCity? handle = CityGeneratorAPI.Default;
            Assert.IsTrue(handle.HasValue);
            Assert.AreEqual(cityInfo, handle.Value.Info);

            Object.DestroyImmediate(cityInfo.gameObject);
        }

        [Test]
        public void Default_IsNull_WithZeroCities()
        {
            Assert.IsNull(CityGeneratorAPI.Default);
        }

        [Test]
        public void Default_IsAmbiguous_WithTwoCities_AndWarnsExactlyOnce()
        {
            CityGeneratorInfo cityA = BuildCity("CityA");
            CityGeneratorInfo cityB = BuildCity("CityB");

            LogAssert.Expect(LogType.Warning, new Regex(".*ambiguous.*"));
            Assert.IsNull(CityGeneratorAPI.Default);
            Assert.IsNull(CityGeneratorAPI.Default);
            Assert.IsNull(CityGeneratorAPI.Default);
            LogAssert.NoUnexpectedReceived();

            Assert.AreEqual(2, CityGeneratorAPI.All.Count);

            Object.DestroyImmediate(cityA.gameObject);
            Object.DestroyImmediate(cityB.gameObject);
        }

        [Test]
        public void InScene_And_For_ResolveEachCityIndependently()
        {
            CityGeneratorInfo cityA = BuildCity("CityA");
            CityGeneratorInfo cityB = BuildCity("CityB");

            CityGeneratorCity? resolvedA = CityGeneratorAPI.For(cityA);
            CityGeneratorCity? resolvedB = CityGeneratorAPI.For(cityB);

            Assert.AreEqual(cityA, resolvedA.Value.Info);
            Assert.AreEqual(cityB, resolvedB.Value.Info);
            Assert.AreEqual(cityA, CityGeneratorAPI.InScene(cityA.gameObject.scene).Value.Info);

            Object.DestroyImmediate(cityA.gameObject);
            Object.DestroyImmediate(cityB.gameObject);
        }

        [Test]
        public void For_ResolvesDeactivatedCity_WhichIsAbsentFromAll()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");
            cityInfo.gameObject.SetActive(false);

            CityGeneratorCity? handle = CityGeneratorAPI.For(cityInfo);
            Assert.IsTrue(handle.HasValue);
            Assert.IsTrue(handle.Value.IsValid);
            Assert.IsFalse(handle.Value.IsActive);
            Assert.AreEqual(0, CityGeneratorAPI.All.Count);

            Object.DestroyImmediate(cityInfo.gameObject);
        }

        [Test]
        public void For_ReturnsNull_ForNullOrDestroyedInfo()
        {
            Assert.IsNull(CityGeneratorAPI.For(null));

            CityGeneratorInfo cityInfo = BuildCity("City");
            Object.DestroyImmediate(cityInfo.gameObject);
            Assert.IsNull(CityGeneratorAPI.For(cityInfo));
        }

        [Test]
        public void TwoHandlesToSameCity_CompareEqual()
        {
            CityGeneratorInfo cityInfo = BuildCity("City");

            CityGeneratorCity handleA = CityGeneratorAPI.For(cityInfo).Value;
            CityGeneratorCity handleB = CityGeneratorAPI.For(cityInfo).Value;

            Assert.AreEqual(handleA, handleB);
            Assert.IsTrue(handleA == handleB);
            Assert.IsTrue(handleA.Equals(handleB));

            Object.DestroyImmediate(cityInfo.gameObject);
        }

        [Test]
        public void Default_BuildingCount_WithNoCity_CompilesAndReturnsZero()
        {
            int count = CityGeneratorAPI.Default?.City.BuildingCount ?? 0;
            Assert.AreEqual(0, count);
        }
    }
}
