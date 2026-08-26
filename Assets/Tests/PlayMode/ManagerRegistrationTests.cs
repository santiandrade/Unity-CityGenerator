using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// TrafficManager/PedestrianManager registration is driven entirely from CarAgent/PedestrianAgent
    /// OnEnable/OnDisable, which run synchronously on SetActive (not deferred like Start()) -- no
    /// need to wait a frame for these tests. Every agent here is built while its GameObject is
    /// inactive so its `network` field can be wired by reflection *before* OnEnable runs (fired by
    /// the SetActive(true) that follows) -- otherwise OnEnable's FindAnyObjectByType<TrafficNetwork>
    /// fallback could resolve the real generated city in this project's own currently-open scene
    /// instead of this test's fixture.
    /// </summary>
    internal class ManagerRegistrationTests
    {
        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            return info.GetValue(target);
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            info.SetValue(target, value);
        }

        private static GameObject BuildInactiveCarAgent(string name, TrafficNetwork network, out CarAgent agent)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            agent = go.AddComponent<CarAgent>();
            SetField(agent, "network", network);
            return go;
        }

        private static GameObject BuildInactivePedestrianAgent(string name, PedestrianNetwork network, out PedestrianAgent agent)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.AddComponent<Animator>();
            agent = go.AddComponent<PedestrianAgent>();
            SetField(agent, "network", network);
            return go;
        }

        private static (TrafficNetwork network, TrafficManager manager) BuildTrafficNetworkWithManager(string name)
        {
            var go = new GameObject(name);
            TrafficNetwork network = go.AddComponent<TrafficNetwork>();
            TrafficManager manager = go.AddComponent<TrafficManager>();
            SetField(network, "manager", manager);
            return (network, manager);
        }

        [Test]
        public void CarAgent_RegistersOnEnable_AndUnregistersOnDisable()
        {
            (TrafficNetwork network, TrafficManager manager) = BuildTrafficNetworkWithManager("TrafficNetwork");
            GameObject carGo = BuildInactiveCarAgent("Car", network, out _);
            var agents = (HashSet<CarAgent>)GetPrivate(manager, "agents");

            carGo.SetActive(true);
            Assert.AreEqual(1, agents.Count);

            carGo.SetActive(false);
            Assert.AreEqual(0, agents.Count);

            carGo.SetActive(true);
            Assert.AreEqual(1, agents.Count, "Re-enabling must re-register exactly once.");

            Object.Destroy(carGo);
            Object.Destroy(network.gameObject);
        }

        [Test]
        public void PedestrianAgent_RegistersOnEnable_AndUnregistersOnDisable()
        {
            var networkGo = new GameObject("PedestrianNetwork");
            PedestrianNetwork network = networkGo.AddComponent<PedestrianNetwork>();
            PedestrianManager manager = networkGo.AddComponent<PedestrianManager>();
            SetField(network, "manager", manager);

            GameObject pedGo = BuildInactivePedestrianAgent("Pedestrian", network, out _);
            var agents = (List<PedestrianAgent>)GetPrivate(manager, "agents");

            pedGo.SetActive(true);
            Assert.AreEqual(1, agents.Count);

            pedGo.SetActive(false);
            Assert.AreEqual(0, agents.Count);

            pedGo.SetActive(true);
            Assert.AreEqual(1, agents.Count, "Re-enabling must re-register exactly once.");

            Object.Destroy(pedGo);
            Object.Destroy(networkGo);
        }

        [UnityTest]
        public IEnumerator TrafficManager_WithNoRegisteredAgents_SkipsUpdateEntirely()
        {
            // Item 8 (stages 1-2): Physics.SyncTransforms() moved from TrafficNetwork.LateUpdate
            // into TrafficManager.Update, called only when there's at least one registered agent.
            // frameIndex only advances past the early-return guard, so watching it never move is a
            // faithful proxy for "Update (and the SyncTransforms call within it) never really ran".
            var networkGo = new GameObject("TrafficNetwork");
            TrafficNetwork network = networkGo.AddComponent<TrafficNetwork>();
            TrafficManager manager = networkGo.AddComponent<TrafficManager>();
            SetField(network, "manager", manager);

            yield return null;
            yield return null;

            object frameIndex = GetPrivate(manager, "frameIndex");
            Assert.AreEqual(0, frameIndex, "TrafficManager.Update must return before advancing frameIndex when no CarAgent is registered.");

            Object.Destroy(networkGo);
        }

        [Test]
        public void TwoIndependentNetworks_EachManagerOnlyTicksItsOwnAgents()
        {
            (TrafficNetwork networkA, TrafficManager managerA) = BuildTrafficNetworkWithManager("CityA");
            (TrafficNetwork networkB, TrafficManager managerB) = BuildTrafficNetworkWithManager("CityB");

            GameObject carA = BuildInactiveCarAgent("CarA", networkA, out CarAgent agentA);
            GameObject carB = BuildInactiveCarAgent("CarB", networkB, out CarAgent agentB);
            carA.SetActive(true);
            carB.SetActive(true);

            var agentsA = (HashSet<CarAgent>)GetPrivate(managerA, "agents");
            var agentsB = (HashSet<CarAgent>)GetPrivate(managerB, "agents");

            Assert.AreEqual(1, agentsA.Count);
            Assert.AreEqual(1, agentsB.Count);
            CollectionAssert.DoesNotContain(agentsA, agentB);
            CollectionAssert.DoesNotContain(agentsB, agentA);

            Object.Destroy(carA);
            Object.Destroy(carB);
            Object.Destroy(networkA.gameObject);
            Object.Destroy(networkB.gameObject);
        }
    }
}
