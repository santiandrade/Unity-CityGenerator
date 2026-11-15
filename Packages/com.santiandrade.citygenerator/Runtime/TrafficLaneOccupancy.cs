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

        // SPEC 20 (b): every car registered on any segment ending at each node, whatever node it
        // comes from. One physical lane can hold cars under different keys -- a car that went
        // straight through a crossing is registered as (previous entry, next entry), one that
        // turned as (exit, next entry) -- so "nobody ahead on my key" is not "nobody ahead in my
        // lane". This index answers the latter for the mid-block shortcut.
        private readonly Dictionary<int, List<CarAgent>> byTarget = new();

        public void Enter(CarAgent agent, int fromNode, int toNode)
        {
            var key = (fromNode, toNode);
            if (!segmentOccupants.TryGetValue(key, out List<CarAgent> occupants))
            {
                occupants = new List<CarAgent>();
                segmentOccupants[key] = occupants;
            }

            if (!occupants.Contains(agent))
            {
                occupants.Add(agent);
                if (!byTarget.TryGetValue(toNode, out List<CarAgent> heading))
                {
                    heading = new List<CarAgent>();
                    byTarget[toNode] = heading;
                }
                heading.Add(agent);
            }
        }

        public void Leave(CarAgent agent, int fromNode, int toNode)
        {
            if (segmentOccupants.TryGetValue((fromNode, toNode), out List<CarAgent> occupants) && occupants.Remove(agent))
            {
                if (byTarget.TryGetValue(toNode, out List<CarAgent> heading))
                    heading.Remove(agent);
            }
        }

        /// <summary>
        /// SPEC 20: free distance, centre to centre along <paramref name="forward"/>, from
        /// <paramref name="position"/> to the nearest car ahead and the nearest car behind among
        /// every car heading to <paramref name="toNode"/> (any segment key). float.MaxValue when
        /// there is none. Meant for choosing where a relocated car fits on a street segment; cars
        /// still inside the crossing behind the segment (heading to its from node) are not counted.
        /// </summary>
        public void GetGap(int toNode, Vector3 position, Vector3 forward, out float ahead, out float behind)
        {
            ahead = float.MaxValue;
            behind = float.MaxValue;
            if (!byTarget.TryGetValue(toNode, out List<CarAgent> heading))
                return;

            for (int i = 0; i < heading.Count; i++)
            {
                CarAgent other = heading[i];
                if (other == null)
                    continue;

                float along = Vector3.Dot(other.transform.position - position, forward);
                if (along >= 0f)
                    ahead = Mathf.Min(ahead, along);
                else
                    behind = Mathf.Min(behind, -along);
            }
        }

        /// <summary>
        /// SPEC 20 (b): whether any CarAgent other than <paramref name="agent"/> heading to
        /// <paramref name="toNode"/> -- from any node, (-1, toNode) included -- is ahead of it
        /// along <paramref name="forward"/>. Deliberately coarse (no lateral check): a true answer
        /// only sends the caller back to its physics sensor.
        /// </summary>
        public bool HasCarAheadHeadingTo(CarAgent agent, int toNode, Vector3 forward)
        {
            if (!byTarget.TryGetValue(toNode, out List<CarAgent> heading))
                return false;

            Vector3 agentPosition = agent.transform.position;
            for (int i = 0; i < heading.Count; i++)
            {
                CarAgent other = heading[i];
                if (other == agent || other == null)
                    continue;

                if (Vector3.Dot(other.transform.position - agentPosition, forward) > 0f)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// SPEC 20: whether any CarAgent other than <paramref name="agent"/> is registered on the
        /// directed segment (<paramref name="fromNode"/>, <paramref name="toNode"/>). Used with
        /// fromNode = -1 to find cars that share a physical lane segment with a car under another
        /// key: a freshly spawned or re-attached car registers as (-1, target) until it reaches its
        /// first node.
        /// </summary>
        public bool HasOtherOccupant(CarAgent agent, int fromNode, int toNode)
        {
            if (!segmentOccupants.TryGetValue((fromNode, toNode), out List<CarAgent> occupants))
                return false;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != agent && occupants[i] != null)
                    return true;
            }

            return false;
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
