using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode
{
    internal class TrafficLightIntersectionCycleTests
    {
        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            info.SetValue(target, value);
        }

        [UnityTest]
        public IEnumerator RunCycle_AlternatesGreenBetweenAxes_WithAmberAndAllRedPhases()
        {
            var go = new GameObject("Intersection");
            var intersection = go.AddComponent<TrafficLightIntersection>();

            var ewGo = new GameObject("EW");
            TrafficLight ew = ewGo.AddComponent<TrafficLight>();
            var nsGo = new GameObject("NS");
            TrafficLight ns = nsGo.AddComponent<TrafficLight>();

            // Each phase boundary is checked shortly after it should have started, with enough
            // headroom before the *next* boundary that frame-timing jitter can't spill the check
            // into the following phase (each cumulative wait lands comfortably inside its target
            // phase's window, well short of the next one).
            const float phase = 1f;
            const float stepWait = phase + 0.2f;
            SetPrivate(intersection, "eastWest", new List<TrafficLight> { ew });
            SetPrivate(intersection, "northSouth", new List<TrafficLight> { ns });
            SetPrivate(intersection, "greenDuration", phase);
            SetPrivate(intersection, "amberDuration", phase);
            SetPrivate(intersection, "allRedDuration", phase);

            // Start() runs on the next frame after AddComponent, kicking off RunCycle(), which
            // sets both Red then immediately (no startOffset) sets eastWest Green -- all
            // synchronous within that same frame, before RunCycle's first yield.
            yield return null;
            Assert.AreEqual(TrafficLightState.Green, ew.State);
            Assert.AreEqual(TrafficLightState.Red, ns.State);

            yield return new WaitForSeconds(stepWait); // ~1.2s: green (0-1s) has ended, now in amber (1-2s)
            Assert.AreEqual(TrafficLightState.Amber, ew.State, "Expected amber after the green phase.");

            yield return new WaitForSeconds(stepWait); // ~2.4s: amber (1-2s) has ended, now in all-red (2-3s)
            Assert.AreEqual(TrafficLightState.Red, ew.State, "Expected all-red after the amber phase.");
            Assert.AreEqual(TrafficLightState.Red, ns.State);

            yield return new WaitForSeconds(stepWait); // ~3.6s: all-red (2-3s) has ended, now north-south green (3-4s)
            Assert.AreEqual(TrafficLightState.Green, ns.State, "Expected the north-south axis to get green next.");
            Assert.AreEqual(TrafficLightState.Red, ew.State);

            Object.Destroy(go);
            Object.Destroy(ewGo);
            Object.Destroy(nsGo);
        }

        [UnityTest]
        public IEnumerator StartOffset_DelaysTheFirstCycle()
        {
            var go = new GameObject("Intersection");
            var intersection = go.AddComponent<TrafficLightIntersection>();
            var ewGo = new GameObject("EW");
            TrafficLight ew = ewGo.AddComponent<TrafficLight>();
            var nsGo = new GameObject("NS");
            TrafficLight ns = nsGo.AddComponent<TrafficLight>();

            SetPrivate(intersection, "eastWest", new List<TrafficLight> { ew });
            SetPrivate(intersection, "northSouth", new List<TrafficLight> { ns });
            SetPrivate(intersection, "greenDuration", 0.15f);
            SetPrivate(intersection, "amberDuration", 0.15f);
            SetPrivate(intersection, "allRedDuration", 0.15f);
            SetPrivate(intersection, "startOffset", 0.2f);

            yield return null;
            // Still within the offset window: both red.
            Assert.AreEqual(TrafficLightState.Red, ew.State);
            Assert.AreEqual(TrafficLightState.Red, ns.State);

            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(TrafficLightState.Green, ew.State, "Cycle should have started after the offset elapsed.");

            Object.Destroy(go);
            Object.Destroy(ewGo);
            Object.Destroy(nsGo);
        }
    }
}
