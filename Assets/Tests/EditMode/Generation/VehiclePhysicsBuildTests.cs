using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 17: with Enable Physics off, a generated vehicle instance must be bit-for-bit
    /// identical to before this feature (no Rigidbody, no assigned physic material). With it on,
    /// every instance must carry a non-kinematic Rigidbody configured from the Traffic card's
    /// Mass/Drag/Angular Drag fields, the fixed constraints/interpolation/collision-detection
    /// CLAUDE.md documents as not user-exposed, and the configured Physic Material on its proxy
    /// collider when one is assigned.
    /// </summary>
    internal class VehiclePhysicsBuildTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 40000f;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in spawnedRoots)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
            spawnedRoots.Clear();
        }

        private Transform CreateOffsetCityRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(nextOffset, 0f, nextOffset);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        private static CityGeneratorSettings MakeSettings(int seed)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            settings.general.plazaCells.Clear();
            settings.customPlaces.Clear();
            settings.customPedestrians.Clear();
            settings.general.vehicleCount = 5;
            settings.general.pedestrianCount = 0;
            settings.general.includePedestrians = false;
            settings.general.playerEnabled = false;
            settings.audio.ambience.enabled = false;
            settings.audio.plazaAudio.enabled = false;
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            return settings;
        }

        private List<Rigidbody> GetVehicleRigidbodies(Transform root, out List<Collider> proxyColliders)
        {
            Transform vehicles = root.Find("Vehicles");
            Assert.IsNotNull(vehicles, "Expected a Vehicles group under the generated city root.");
            Assert.Greater(vehicles.childCount, 0, "Expected at least one generated vehicle instance.");

            var rigidbodies = new List<Rigidbody>();
            proxyColliders = new List<Collider>();
            foreach (Transform vehicle in vehicles)
            {
                rigidbodies.Add(vehicle.GetComponent<Rigidbody>());
                proxyColliders.Add(vehicle.GetComponent<Collider>());
            }
            return rigidbodies;
        }

        [Test]
        public void EnablePhysicsOff_NoVehicleGetsARigidbodyOrPhysicsMaterial()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 1);
            settings.general.enablePhysics = false;

            Transform root = CreateOffsetCityRoot("PhysicsOffCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            List<Rigidbody> rigidbodies = GetVehicleRigidbodies(root, out List<Collider> proxyColliders);
            foreach (Rigidbody rb in rigidbodies)
                Assert.IsNull(rb, "No vehicle may carry a Rigidbody when Enable Physics is off.");

            foreach (Collider proxy in proxyColliders)
                Assert.IsNull(proxy.sharedMaterial, "No proxy collider may have a physic material assigned when Enable Physics is off.");
        }

        [Test]
        public void EnablePhysicsOn_EveryVehicleGetsAConfiguredRigidbody()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 2);
            settings.general.enablePhysics = true;
            settings.vehiclePhysics.mass = 1500f;
            settings.vehiclePhysics.drag = 0.4f;
            settings.vehiclePhysics.angularDrag = 2f;
            settings.vehiclePhysics.physicMaterial = null;

            Transform root = CreateOffsetCityRoot("PhysicsOnCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            List<Rigidbody> rigidbodies = GetVehicleRigidbodies(root, out _);
            foreach (Rigidbody rb in rigidbodies)
            {
                Assert.IsNotNull(rb, "Every vehicle must carry a Rigidbody when Enable Physics is on.");
                Assert.IsFalse(rb.isKinematic, "The Rigidbody must be non-kinematic so it can respond to impacts.");
                Assert.AreEqual(1500f, rb.mass, 0.001f);
                Assert.AreEqual(0.4f, rb.linearDamping, 0.001f);
                Assert.AreEqual(2f, rb.angularDamping, 0.001f);
                Assert.IsFalse(rb.useGravity, "Gravity must be off at generation time.");
                Assert.AreEqual(RigidbodyInterpolation.Interpolate, rb.interpolation);
                Assert.AreEqual(CollisionDetectionMode.ContinuousDynamic, rb.collisionDetectionMode);
                Assert.AreEqual(
                    RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ,
                    rb.constraints,
                    "Only the rotation constraints are set at generation time; FreezePositionY is added by CarAgent once it starts driving.");
            }
        }

        [Test]
        public void EnablePhysicsOn_PhysicsMaterialIsAssignedToTheProxyColliderWhenConfigured()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 3);
            settings.general.enablePhysics = true;
            var material = new PhysicsMaterial("TestVehicleMaterial");
            settings.vehiclePhysics.physicMaterial = material;

            Transform root = CreateOffsetCityRoot("PhysicsOnMaterialCity");
            try
            {
                CityGeneratorContentAssembler.Assemble(settings, root);

                GetVehicleRigidbodies(root, out List<Collider> proxyColliders);
                foreach (Collider proxy in proxyColliders)
                    Assert.AreSame(material, proxy.sharedMaterial, "The configured Physic Material must be assigned to every vehicle's proxy collider.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void EnablePhysicsOn_EmptyPhysicsMaterialLeavesProxyColliderWithoutOne()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 4);
            settings.general.enablePhysics = true;
            settings.vehiclePhysics.physicMaterial = null;

            Transform root = CreateOffsetCityRoot("PhysicsOnNoMaterialCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            GetVehicleRigidbodies(root, out List<Collider> proxyColliders);
            foreach (Collider proxy in proxyColliders)
                Assert.IsNull(proxy.sharedMaterial, "Leaving Physic Material empty must not block generation, and the proxy collider keeps Unity's default material.");
        }
    }
}
