using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// SPEC 04 fixed CityGeneratorColliderUtility so a prefab whose only Collider lives on a
    /// child (never the root) still ends up with a root-level, non-trigger proxy -- required for
    /// CarAgent's ColliderRegistry-based sensor identity check (CarAgent.OnEnable does a
    /// root-only GetComponent&lt;Collider&gt;()). This was previously verified manually; this test
    /// automates it.
    /// </summary>
    internal class ColliderOnChildDetectionTests
    {
        private static object GetStatic(System.Type type, string field)
        {
            FieldInfo info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(info, $"Static field '{field}' not found on {type}");
            return info.GetValue(null);
        }

        [Test]
        public void EnsureNonTriggerCollider_WithColliderOnlyOnChild_AddsRootProxy_LeavesChildUntouched()
        {
            var root = new GameObject("VehicleLikePrefabInstance");
            var child = new GameObject("Body");
            child.transform.SetParent(root.transform);
            BoxCollider childCollider = child.AddComponent<BoxCollider>();
            childCollider.isTrigger = true; // simulate a purely physical/decorative child collider
            child.AddComponent<MeshRenderer>();
            child.AddComponent<MeshFilter>();

            Assert.IsNull(root.GetComponent<Collider>(), "Fixture invariant: root must start with no collider.");

            Collider proxy = CityGeneratorColliderUtility.EnsureNonTriggerCollider(root);

            Assert.AreSame(root, proxy.gameObject, "The proxy collider must live on the instance root.");
            Assert.IsFalse(proxy.isTrigger);
            Assert.IsTrue(childCollider.isTrigger, "A collider deeper in the hierarchy must be left completely untouched.");

            Object.Destroy(root);
        }

        [Test]
        public void CarAgent_WithColliderOnlyOnChildBeforeProxyAssignment_RegistersItsRootProxy()
        {
            // Own TrafficNetwork/Manager pair, wired before the agent is ever enabled: otherwise
            // CarAgent.OnEnable's FindAnyObjectByType<TrafficNetwork> fallback could resolve the
            // real generated city in this project's own currently-open scene instead.
            var networkGo = new GameObject("TrafficNetwork");
            TrafficNetwork network = networkGo.AddComponent<TrafficNetwork>();
            TrafficManager manager = networkGo.AddComponent<TrafficManager>();
            typeof(TrafficNetwork).GetField("manager", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(network, manager);

            var root = new GameObject("VehicleLikePrefabInstance");
            root.SetActive(false);
            var child = new GameObject("Body");
            child.transform.SetParent(root.transform);
            child.AddComponent<BoxCollider>();

            // Mirrors CityGeneratorTrafficBuilder.BuildVehicles: the collider policy runs before
            // CarAgent is ever added/enabled.
            CityGeneratorColliderUtility.EnsureNonTriggerCollider(root);
            CarAgent agent = root.AddComponent<CarAgent>();
            typeof(CarAgent).GetField("network", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(agent, network);

            root.SetActive(true); // triggers OnEnable: GetComponent<Collider>() must now find the root proxy

            var registry = (System.Collections.IDictionary)GetStatic(typeof(CarAgent), "ColliderRegistry");
            bool found = false;
            foreach (System.Collections.DictionaryEntry entry in registry)
            {
                if (ReferenceEquals(entry.Value, agent))
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "CarAgent must register under its root proxy collider's entity id, even though the prefab's only original collider was on a child.");

            Object.Destroy(root);
            Object.Destroy(networkGo);
        }
    }
}
