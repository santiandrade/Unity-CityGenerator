using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Resolves only the "is there a CarAgent immediately ahead on the same lane segment?" case
    /// without a <c>SphereCast</c>: a per-segment occupancy list, ordered by
    /// <see cref="CarAgent.DistanceTravelled"/>, that <see cref="CarAgent"/> consults first before
    /// falling back to its existing forward sensor for pedestrians, crossings and arbitrary
    /// obstacles (see the four "load-bearing" sensor rules in CLAUDE.md — this only ever replaces
    /// the cheap, common case, never the sensor itself).
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
        /// True if another CarAgent on the same segment, strictly ahead of <paramref name="agent"/>
        /// by <see cref="CarAgent.DistanceTravelled"/>, was found -- false means "no answer from
        /// this index", not "the lane is clear": the caller should fall back to its own sensor
        /// (an empty/absent segment entry, a car at the very end of its segment about to turn, or a
        /// crossing are all cases this index doesn't attempt to resolve).
        /// </summary>
        public bool TryGetCarAhead(CarAgent agent, int fromNode, int toNode, out CarAgent ahead)
        {
            ahead = null;
            if (!segmentOccupants.TryGetValue((fromNode, toNode), out List<CarAgent> occupants))
                return false;

            float bestDistance = float.MaxValue;
            for (int i = 0; i < occupants.Count; i++)
            {
                CarAgent other = occupants[i];
                if (other == agent || other == null)
                    continue;

                float delta = other.DistanceTravelled - agent.DistanceTravelled;
                if (delta > 0f && delta < bestDistance)
                {
                    bestDistance = delta;
                    ahead = other;
                }
            }

            return ahead != null;
        }
    }
}
