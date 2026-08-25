using System.Collections.Generic;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Shared pool of <see cref="PedestrianNetwork.FindPath"/> output buffers, each sized to the
    /// graph's node count -- the worst case a path can ever be. Before item 9's staggered initial
    /// planning, every <see cref="PedestrianAgent"/> kept one such array alive for its whole
    /// lifetime (O(pedestrians x nodes) memory) even though it only actually needs the full-size
    /// buffer for the brief moment FindPath is filling it; the agent's own persistent state only
    /// ever needs however many nodes its *current* path actually has. A small pool, borrowed during
    /// planning and returned immediately after the used portion is copied out, replaces that.
    /// </summary>
    public sealed class PedestrianPathBufferPool
    {
        private readonly int nodeCount;
        private readonly Stack<int[]> buffers = new();

        public PedestrianPathBufferPool(int nodeCount)
        {
            this.nodeCount = nodeCount;
        }

        public int[] Rent() => buffers.Count > 0 ? buffers.Pop() : new int[nodeCount];

        public void Return(int[] buffer)
        {
            if (buffer != null && buffer.Length == nodeCount)
                buffers.Push(buffer);
        }
    }
}
