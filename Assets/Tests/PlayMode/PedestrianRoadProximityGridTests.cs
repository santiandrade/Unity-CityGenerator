using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// Regression coverage for a real bug: once PedestrianRoadProximityGrid.HasEnoughAgents goes
    /// true, CarAgent.PedestrianAheadClearance stops SphereCasting and answers from this grid
    /// alone -- but the player is on the same Pedestrian layer as every NPC (and the SphereCast
    /// sensor always saw it) without ever being a PedestrianAgent itself, so it could never be one
    /// of the grid's bucketed entries. Cars stopped detecting/braking for the player entirely once
    /// enough pedestrians were spawned. Fixed by tracking the player separately (see
    /// TryGetPlayerPosition) and having CarAgent check it explicitly alongside the grid query.
    /// </summary>
    internal class PedestrianRoadProximityGridTests
    {
        private GameObject gridGo;
        private PedestrianRoadProximityGrid grid;

        [SetUp]
        public void SetUp()
        {
            gridGo = new GameObject("RoadProximityGrid");
            grid = gridGo.AddComponent<PedestrianRoadProximityGrid>();
        }

        [TearDown]
        public void TearDown()
        {
            if (gridGo != null) Object.Destroy(gridGo);
        }

        [Test]
        public void Rebuild_WithPlayerTransform_TryGetPlayerPositionReturnsIt()
        {
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(5f, 0f, 7f);

            grid.Rebuild(new List<PedestrianAgent>(), minAgentCountToUse: 0, player: playerGo.transform);

            Assert.IsTrue(grid.TryGetPlayerPosition(out Vector3 position));
            Assert.AreEqual(playerGo.transform.position, position);

            Object.Destroy(playerGo);
        }

        [Test]
        public void Rebuild_WithNoPlayerTransform_TryGetPlayerPositionReturnsFalse()
        {
            grid.Rebuild(new List<PedestrianAgent>(), minAgentCountToUse: 0, player: null);

            Assert.IsFalse(grid.TryGetPlayerPosition(out _));
        }

        [Test]
        public void CarAgent_WithHasEnoughAgentsButNoPedestrianAgents_StillDetectsPlayer()
        {
            // No PedestrianAgent instances registered at all (0 NPCs, e.g. Include Pedestrians
            // off) but the grid still reports HasEnoughAgents once minAgentCountToUse is 0 -- the
            // exact scenario that silently blinded CarAgent to the player before this fix.
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0f, 0f, 5f);
            // HasEnoughAgents is `agents.Count > minAgentCountToUse`: -1 forces it true even with
            // zero registered PedestrianAgents (0 NPCs is exactly the scenario that hid this bug).
            grid.Rebuild(new List<PedestrianAgent>(), minAgentCountToUse: -1, player: playerGo.transform);
            Assert.IsTrue(grid.HasEnoughAgents);

            var carGo = new GameObject("Car");
            CarAgent car = carGo.AddComponent<CarAgent>();
            typeof(CarAgent).GetField("pedestrianRoadProximity", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(car, grid);
            typeof(CarAgent).GetField("sensorRange", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(car, 12f);
            typeof(CarAgent).GetField("minGap", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(car, 2.2f);

            MethodInfo method = typeof(CarAgent).GetMethod("PedestrianAheadClearance", BindingFlags.NonPublic | BindingFlags.Instance);
            float clearance = (float)method.Invoke(car, null);

            Assert.Less(clearance, float.MaxValue, "CarAgent must still detect the player via the grid path, not report an unobstructed lane.");

            Object.Destroy(carGo);
            Object.Destroy(playerGo);
        }
    }
}
