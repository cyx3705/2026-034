namespace HistoryStrenua;

/// <summary>「重标尺寸」的图算法部分：自环、找桥、分组、最大生成树、算式。</summary>
internal static partial class DimensionCheckPlanner
{
    private static List<RedundantLoop> Loops(List<DimensionEdge> allEdges, OutlineCoverage given)
    {
        var result = new List<RedundantLoop>();
        // 同一个 R / C 在几个视图、几段弧上连同一段只算一条；普通尺寸各是各的（标两遍就是重标）。R、C 两头已定的不报：它们是大小，不是位置。
        var edges = allEdges
            .Where(edge => !edge.Size || given.Component(edge.From) != given.Component(edge.To))
            .GroupBy(edge => edge.Size
                ? (edge.Member, Math.Min(given.Component(edge.From), given.Component(edge.To)), Math.Max(given.Component(edge.From), given.Component(edge.To)), 0)
                : (edge.Member, 0, 0, allEdges.IndexOf(edge)))
            .Select(group => group.First())
            .ToList();
        var ends = edges.Select(edge => (U: given.Component(edge.From), V: given.Component(edge.To))).ToList();

        // 两头已经定了的：单独一组，算式只有不用尺寸的那部分。
        for (var i = 0; i < edges.Count; i++)
        {
            if (ends[i].U == ends[i].V)
                result.Add(new RedundantLoop(edges[i].Horizontal, [edges[i].Member], 1, [new RedundantFormula(edges[i].Member, [], edges[i].Member.Value)]));
        }

        var live = Enumerable.Range(0, edges.Count).Where(i => ends[i].U != ends[i].V).ToList();
        var bridges = Bridges(live, ends);
        var inLoop = live.Where(i => !bridges.Contains(i)).ToList();

        // 去掉桥后的连通块就是一组。
        var parent = new Dictionary<int, int>();
        int Find(int x)
        {
            if (!parent.TryGetValue(x, out var p))
                return parent[x] = x;
            return p == x ? x : parent[x] = Find(p);
        }

        foreach (var i in inLoop)
            parent[Find(ends[i].U)] = Find(ends[i].V);
        foreach (var group in inLoop.GroupBy(i => Find(ends[i].U)))
        {
            var members = group.ToList();
            // 全是 R、C：几处圆角恰好等高等大（真机夹爪2 主视图、右视图的两个 R5），各管各的大小，不是重标。
            if (members.All(i => edges[i].Size))
                continue;
            var nodes = members.SelectMany(i => new[] { ends[i].U, ends[i].V }).Distinct().Count();
            result.Add(new RedundantLoop(edges[members[0]].Horizontal, members.Select(i => edges[i].Member).ToList(),
                members.Count - nodes + 1, Suggest(members, edges, ends)));
        }

        return result;
    }

    /// <summary>无向多重图找桥（Tarjan）：按边号跳过来时那条，两个尺寸量同一段时互不是桥。</summary>
    private static HashSet<int> Bridges(List<int> live, List<(int U, int V)> ends)
    {
        var adjacent = new Dictionary<int, List<(int To, int Edge)>>();
        void Add(int from, int to, int edge)
        {
            if (!adjacent.TryGetValue(from, out var list))
                adjacent[from] = list = [];
            list.Add((to, edge));
        }

        foreach (var i in live)
        {
            Add(ends[i].U, ends[i].V, i);
            Add(ends[i].V, ends[i].U, i);
        }

        var order = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        var bridges = new HashSet<int>();
        void Visit(int node, int cameBy)
        {
            order[node] = low[node] = order.Count;
            foreach (var (to, edge) in adjacent[node])
            {
                if (edge == cameBy)
                    continue;
                if (order.TryGetValue(to, out var seen))
                {
                    low[node] = Math.Min(low[node], seen);
                    continue;
                }

                Visit(to, edge);
                low[node] = Math.Min(low[node], low[to]);
                if (low[to] > order[node])
                    bridges.Add(edge);
            }
        }

        foreach (var node in adjacent.Keys)
        {
            if (!order.ContainsKey(node))
                Visit(node, -1);
        }

        return bridges;
    }

    /// <summary>保留优先级高的先进生成树；进不去的就是建议删的，算式走树上的路径。</summary>
    private static List<RedundantFormula> Suggest(List<int> members, List<DimensionEdge> edges, List<(int U, int V)> ends)
    {
        var largest = members.Max(i => Math.Abs(edges[i].Delta));
        double Score(int i)
        {
            var edge = edges[i];
            var fraction = Math.Abs(edge.Member.Value * 1000 - Math.Round(edge.Member.Value * 1000));
            return (edge.Member.Tolerance ? 1e6 : 0) + (edge.Array ? 1e5 : 0)
                + (Math.Abs(edge.Delta) >= largest - DimensionGeometry.ValueTolerance ? 1e4 : 0)
                + (edge.Size ? 5e3 : 0)
                + (fraction < 0.005 ? 100 : -100);
        }

        var tree = new Dictionary<int, List<(int To, int Edge, int Sign)>>();
        var result = new List<int>();
        foreach (var i in members.OrderByDescending(Score).ThenBy(i => edges[i].Member.View).ThenBy(i => edges[i].Member.Index))
        {
            var (u, v) = ends[i];
            if (PathBetween(tree, u, v) is not null)
            {
                result.Add(i);
                continue;
            }

            (tree.TryGetValue(u, out var forward) ? forward : tree[u] = []).Add((v, i, 1));
            (tree.TryGetValue(v, out var backward) ? backward : tree[v] = []).Add((u, i, -1));
        }

        return result.Select(i =>
        {
            var edge = edges[i];
            var direction = Math.Sign(edge.Delta);
            var terms = PathBetween(tree, ends[i].U, ends[i].V)!
                .Select(step => (Term: edges[step.Edge].Member, Sign: direction * step.Sign * Math.Sign(edges[step.Edge].Delta)))
                .ToList();
            var rest = edge.Member.Value - terms.Sum(term => term.Sign * term.Term.Value);
            return new RedundantFormula(edge.Member, terms, Math.Abs(rest) <= DimensionGeometry.ValueTolerance ? 0 : rest);
        }).ToList();
    }

    /// <summary>树上从 <paramref name="from"/> 到 <paramref name="to"/> 的路径；不通为 null。</summary>
    private static List<(int Edge, int Sign)>? PathBetween(Dictionary<int, List<(int To, int Edge, int Sign)>> tree, int from, int to)
    {
        var came = new Dictionary<int, (int Node, int Edge, int Sign)> { [from] = (from, -1, 0) };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node == to)
            {
                var path = new List<(int Edge, int Sign)>();
                for (var at = to; at != from; at = came[at].Node)
                    path.Insert(0, (came[at].Edge, came[at].Sign));
                return path;
            }

            if (!tree.TryGetValue(node, out var steps))
                continue;
            foreach (var (next, edge, sign) in steps)
            {
                if (came.TryAdd(next, (node, edge, sign)))
                    queue.Enqueue(next);
            }
        }

        return null;
    }
}
