namespace Orchestrator.Infrastructure.Configuration;

public static class FallbackGraphValidator
{
    public static bool TryGetFirstCyclePath(IReadOnlyDictionary<string, List<string>> graph, out string cyclePath)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        foreach (var node in graph.Keys)
            if (!state.ContainsKey(node) && HasCycle(node, graph, state, stack, out cyclePath)) return true;
        cyclePath = string.Empty;
        return false;
    }

    private static bool HasCycle(string node, IReadOnlyDictionary<string, List<string>> graph, Dictionary<string, int> state, Stack<string> stack, out string cyclePath)
    {
        state[node] = 1;
        stack.Push(node);
        if (graph.TryGetValue(node, out var nextNodes))
            foreach (var next in nextNodes)
            {
                if (!state.TryGetValue(next, out var nextState)) { if (HasCycle(next, graph, state, stack, out cyclePath)) return true; }
                else if (nextState == 1) { var ordered = stack.Reverse().ToList(); var start = ordered.FindIndex(n => string.Equals(n, next, StringComparison.OrdinalIgnoreCase)); var cycle = ordered.Skip(start).ToList(); cycle.Add(next); cyclePath = string.Join(" -> ", cycle); return true; }
            }
        stack.Pop();
        state[node] = 2;
        cyclePath = string.Empty;
        return false;
    }
}
