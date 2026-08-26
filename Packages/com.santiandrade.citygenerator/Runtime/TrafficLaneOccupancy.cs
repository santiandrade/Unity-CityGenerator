using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Resolves only the "is there a CarAgent immediately ahead on the same lane segment?" case
    /// without a physics query: a per-segment occupancy list, ordered by forward projection towards
    /// the querying car's own target node, that <see cref="CarAgent"/> consults first before
    /// falling back to its existing sensor for pedestrians, crossings and arbitrary obstacles (see
    /// the four "load-bearing" sensor rules in CLAUDE.md — this only ever replaces the cheap,
    /// common case, never the sensor itself).
    /// </summary>
    public sealed class TrafficLaneOccupancy : MonoBehaviour
    {
        // Key = directed edge (fromNode, toNode) of TrafficNetwork's graph -- a car "occupies" the
        // segment it is currently driving towards (its targetNode) coming from the node it last
        // departed.
        private readonly Dictionary<(int from, int to), List<CarAgent>> segmentOccupants = new();

        public void Enter(CarAgent agent, int fromNode, int toNode)
        {
            var key = (fromNode, toNode);
            if (!segmentOccupants.TryGetValue(key, out List<CarAgent> occupants))
            {
                occupants = new List<CarAgent>();
                segmentOccupants[key] = occupants;
            }

            if (!occupants.Contains(agent))
                occupants.Add(agent);
        }

        public void Leave(CarAgent agent, int fromNode, int toNode)
        {
            if (segmentOccupants.TryGetValue((fromNode, toNode), out List<CarAgent> occupants))
                occupants.Remove(agent);
        }

        /// <summary>
        /// True if another CarAgent on the same segment, geometrically ahead of
        /// <paramref name="agent"/>, was found -- false means "no answer from this index", not "the
        /// lane is clear": the caller should fall back to its own sensor (an empty/absent segment
        /// entry, a car at the very end of its segment about to turn, or a crossing are all cases
        /// this index doesn't attempt to resolve).
        ///
        /// Ordered by forward projection along <paramref name="forward"/> -- the direction towards
        /// <paramref name="agent"/>'s own target node, not its live transform.forward, because
        /// mid-corner the two can disagree enough to flip the sign (see the comment on the caller,
        /// <see cref="CarAgent.VehicleAheadClearance"/>) -- not by <see cref="CarAgent.DistanceTravelled"/>
        /// as this used to be: DistanceTravelled is a lifetime total, and two cars sharing a segment
        /// can have wildly different totals (one spawned recently, one mid-way through its Nth lap)
        /// with no relation to which one is physically closer to the stop line. Comparing totals
        /// could not just misorder a queue -- it could report "no one ahead" for a car with a lower
        /// total sitting right behind one with a higher total, skipping the sensor fallback's own
        /// detection entirely and letting it drive straight through the car actually ahead of it.
        /// </summary>
        public bool TryGetCarAhead(CarAgent agent, int fromNode, int toNode, Vector3 forward, out CarAgent ahead)
        {
            ahead = null;
            if (!segmentOccupants.TryGetValue((fromNode, toNode), out List<CarAgent> occupants))
                return false;

            Vector3 agentPosition = agent.transform.position;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < occupants.Count; i++)
            {
                CarAgent other = occupants[i];
                if (other == agent || other == null)
                    continue;

                float along = Vector3.Dot(other.transform.position - agentPosition, forward);
                if (along > 0f && along < bestDistance)
                {
                    bestDistance = along;
                    ahead = other;
                }
            }

            return ahead != null;
        }
    }
}
