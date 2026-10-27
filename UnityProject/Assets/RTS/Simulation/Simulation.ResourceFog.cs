using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private readonly ResourceView[][] resourceViewBases = { new ResourceView[0], new ResourceView[0] };
        private readonly int[][] resourceViewNodeIndices = { new int[0], new int[0] };
        private readonly List<int>[] resourceViewReplacedIndices = { new List<int>(), new List<int>() };
        private readonly List<ResourceView>[] resourceViewReplacedItems = { new List<ResourceView>(), new List<ResourceView>() };

        /// <summary>Updates each faction's remembered amount only for resource cells visible in this observation.</summary>
        private void UpdateResourceObservations()
        {
            if (!ResourceFogOn) return;
            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var faction = ref world.Factions[f];
                for (int i = 0; i < world.Nodes.Length; i++)
                {
                    int cell = world.Map.Cell(world.Nodes[i].Definition.Position);
                    if (cell < 0 || cell >= faction.VisibleCells.Length || !faction.VisibleCells[cell]) continue;
                    faction.ResourceSeen[i] = true;
                    faction.ResourceRemaining[i] = world.Nodes[i].Remaining;
                }
            }
        }

        private bool ResourceKnownToFaction(uint faction, int nodeIndex)
        {
            if (!ResourceFogOn) return true;
            if (nodeIndex < 0 || nodeIndex >= world.Nodes.Length) return false;
            int cell = world.Map.Cell(world.Nodes[nodeIndex].Definition.Position);
            var explored = world.Factions[faction - 1].ExploredCells;
            return cell >= 0 && cell < explored.Length && explored[cell];
        }

        private IReadOnlyList<ResourceView> ResourceViewsFor(uint faction)
        {
            var memory = world.Factions[faction - 1];
            int count = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
                if (memory.ResourceSeen[i] && memory.ResourceRemaining[i] > 0) count++;

            var oldIndices = resourceViewNodeIndices[faction - 1];
            bool shapeChanged = oldIndices.Length != count;
            if (!shapeChanged)
            {
                int at = 0;
                for (int i = 0; i < world.Nodes.Length; i++)
                    if (memory.ResourceSeen[i] && memory.ResourceRemaining[i] > 0 && oldIndices[at++] != i)
                    {
                        shapeChanged = true;
                        break;
                    }
            }

            if (shapeChanged)
            {
                var indices = new int[count];
                var views = new ResourceView[count];
                int at = 0;
                for (int i = 0; i < world.Nodes.Length; i++)
                    if (memory.ResourceSeen[i] && memory.ResourceRemaining[i] > 0)
                    {
                        indices[at] = i;
                        views[at] = ResourceViewFor(i, memory.ResourceRemaining[i]);
                        at++;
                    }
                resourceViewNodeIndices[faction - 1] = indices;
                resourceViewBases[faction - 1] = views;
            }

            var baseViews = resourceViewBases[faction - 1];
            var replacedIndices = resourceViewReplacedIndices[faction - 1];
            var replacedItems = resourceViewReplacedItems[faction - 1];
            replacedIndices.Clear();
            replacedItems.Clear();
            for (int i = 0; i < baseViews.Length; i++)
            {
                var current = ResourceViewFor(resourceViewNodeIndices[faction - 1][i], memory.ResourceRemaining[resourceViewNodeIndices[faction - 1][i]]);
                if (current.Remaining == baseViews[i].Remaining) continue;
                replacedIndices.Add(i);
                replacedItems.Add(current);
            }
            return new SharedPrefixList<ResourceView>(baseViews, baseViews.Length, replacedIndices, replacedItems);
        }

        private ResourceView ResourceViewFor(int nodeIndex, int remaining)
        {
            var node = world.Nodes[nodeIndex];
            return new ResourceView(node.Definition.Id, node.Definition.Kind, node.Definition.Position, remaining);
        }
    }
}
