using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Binding;

/// <summary>
/// Provides the single stable construction order used by lowering and context-sensitive
/// semantic services. Independent members retain source order; DependsOn edges are applied
/// through a stable topological sort.
/// </summary>
public static class XamlBoundMemberOrdering
{
    public static IEnumerable<XamlBoundMember> Order(
        ImmutableArray<XamlBoundMember> members)
    {
        if (members.Length < 2) return members;
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < members.Length; index++)
            if (members[index].Member.Symbol != null)
                byName[members[index].Member.Symbol!.Name] = index;

        var indegree = new int[members.Length];
        var dependents = new List<int>?[members.Length];
        var seenEdges = new HashSet<long>();
        for (var index = 0; index < members.Length; index++)
        {
            var symbol = members[index].Member.Symbol;
            if (symbol == null) continue;
            foreach (var dependency in symbol.Dependencies)
            {
                if (!dependency.IsValid ||
                    !byName.TryGetValue(
                        dependency.Dependency!.Name,
                        out var dependencyIndex))
                {
                    continue;
                }

                var edge = ((long)dependencyIndex << 32) | (uint)index;
                if (!seenEdges.Add(edge)) continue;
                (dependents[dependencyIndex] ??= new List<int>()).Add(index);
                indegree[index]++;
            }
        }

        var ready = new SortedSet<int>();
        for (var index = 0; index < indegree.Length; index++)
            if (indegree[index] == 0) ready.Add(index);
        var result = new List<XamlBoundMember>(members.Length);
        while (ready.Count != 0)
        {
            var index = ready.Min;
            ready.Remove(index);
            result.Add(members[index]);
            if (dependents[index] == null) continue;
            foreach (var dependent in dependents[index]!)
                if (--indegree[dependent] == 0) ready.Add(dependent);
        }

        // Binding diagnoses cycles. Preserve every member deterministically for recovery.
        if (result.Count != members.Length)
            for (var index = 0; index < members.Length; index++)
                if (indegree[index] != 0) result.Add(members[index]);
        return result;
    }
}
