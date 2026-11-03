namespace HistoryStrenua;

/// <summary>配合里本零部件那一边的几何：平面（点 + 法向）、圆柱或轴线（轴上一点 + 轴向）、点。</summary>
internal enum MateGeometry
{
    Plane,
    Cylinder,
    Line,
    Point,
    Other,
}

/// <summary>
/// 一条配合对本零部件的约束：配合种类（swMateType_e）、本零部件这边的几何（总装 mm），<see cref="Partner"/> 是对方几何的方向（垂直、角度用）。
/// </summary>
internal sealed record MateConstraint(int Type, MateGeometry Geometry, Vec3 Point, Vec3 Vector, Vec3 Partner = default);

/// <summary>贴合关系图的一条边：两个顶层零部件、贴合面积（mm²）。</summary>
internal sealed record ContactEdge(string A, string B, double Area);

/// <summary>贴合树的一条边：<see cref="Child"/> 配到 <see cref="Parent"/> 上，<see cref="Depth"/> 是离根几层。</summary>
internal sealed record TreeEdge(string Parent, string Child, int Depth, double Area);

/// <summary>
/// 装配类自由度与贴合树（1.40.0，用户定：有加工件的装配体才上约束；先摆位置、解除固定再上约束）的纯规则。
/// </summary>
/// <remarks>
/// <para>自由度按约束力螺旋求秩：每条配合按几何给出几根约束（力 f 过点 p 记作 (f, p×f)，纯力矩记作 (0, m)），6 减秩就是剩下的自由度；
/// 对方零部件一律当作不动来算（它自己还能动的，在它那一行里看）。哪些方向能动：沿 X / Y / Z 平移、绕配合里的轴线和过原点的 X / Y / Z 转，逐个验与全部约束互易为零。</para>
/// <para>贴合树：从根（固定块）沿贴合关系广度优先往外长，同一层先走贴合面积大的；成环时已经走到的那条边不进树——广度优先下它就是离根最远的那条贴合关系（用户定：成环断最远处）。</para>
/// </remarks>
internal static class MateTreePlanner
{
    private const double Eps = 1e-6;

    /// <summary>一条配合给出的约束螺旋（6 维：力、力矩；位置相对 <paramref name="origin"/>，换成米免得量级悬殊）。认不出的给空。</summary>
    public static IReadOnlyList<double[]> Wrenches(MateConstraint mate, Vec3 origin)
    {
        var p = (mate.Point - origin) * 0.001;
        var n = mate.Vector.Normalized();
        var (u1, u2) = Perpendiculars(n);
        double[] Force(Vec3 f) => Row(f, p.Cross(f));
        double[] Moment(Vec3 m) => Row(default, m);
        switch (mate.Type)
        {
            case 16: // 锁定
                return [Force(new(1, 0, 0)), Force(new(0, 1, 0)), Force(new(0, 0, 1)), Moment(new(1, 0, 0)), Moment(new(0, 1, 0)), Moment(new(0, 0, 1))];
            case 0 or 5: // 重合、距离
                return mate.Geometry switch
                {
                    MateGeometry.Plane => [Force(n), Moment(u1), Moment(u2)],
                    MateGeometry.Line or MateGeometry.Cylinder => [Force(u1), Force(u2), Moment(u1), Moment(u2)],
                    MateGeometry.Point => [Force(new(1, 0, 0)), Force(new(0, 1, 0)), Force(new(0, 0, 1))],
                    _ => [],
                };
            case 1: // 同轴心
                return mate.Geometry is MateGeometry.Cylinder or MateGeometry.Line ? [Force(u1), Force(u2), Moment(u1), Moment(u2)] : [];
            case 3: // 平行
                return mate.Geometry is MateGeometry.Other or MateGeometry.Point ? [] : [Moment(u1), Moment(u2)];
            case 2 or 6: // 垂直、角度：绕两根方向的公垂线转会改夹角
            {
                var k = n.Cross(mate.Partner.Normalized());
                return k.Length > Eps ? [Moment(k.Normalized())] : [Moment(u1)];
            }
            case 4: // 相切：只算沿法向的一根
                return mate.Geometry == MateGeometry.Plane ? [Force(n)] : mate.Partner.Length > Eps ? [Force(mate.Partner.Normalized())] : [];
            default:
                return [];
        }
    }

    /// <summary>约束螺旋的秩（高斯消元，按最大主元）。</summary>
    public static int Rank(IEnumerable<double[]> rows)
    {
        var m = rows.Select(row => (double[])row.Clone()).ToList();
        var rank = 0;
        for (var col = 0; col < 6 && rank < m.Count; col++)
        {
            var pivot = Enumerable.Range(rank, m.Count - rank).OrderByDescending(r => Math.Abs(m[r][col])).First();
            if (Math.Abs(m[pivot][col]) < Eps)
                continue;
            (m[rank], m[pivot]) = (m[pivot], m[rank]);
            for (var r = 0; r < m.Count; r++)
            {
                if (r == rank)
                    continue;
                var k = m[r][col] / m[rank][col];
                for (var c = 0; c < 6; c++)
                    m[r][c] -= k * m[rank][c];
            }

            rank++;
        }

        return rank;
    }

    /// <summary>
    /// 剩下几个自由度、能动的方向（「沿 X 移」「绕 Y 转（过 X-920 Z-300）」）。<paramref name="origin"/> 是零部件原点（总装 mm）。
    /// </summary>
    public static (int Dof, IReadOnlyList<string> Free) Freedom(IReadOnlyList<MateConstraint> mates, Vec3 origin)
    {
        var rows = mates.SelectMany(mate => Wrenches(mate, origin)).ToList();
        var dof = 6 - Rank(rows);
        if (dof == 0)
            return (0, []);
        bool Allowed(Vec3 omega, Vec3 v) => rows.All(w => Math.Abs(w[0] * v.X + w[1] * v.Y + w[2] * v.Z + w[3] * omega.X + w[4] * omega.Y + w[5] * omega.Z) < 1e-6);
        var axes = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) };
        var free = new List<string>();
        var basis = new List<double[]>();
        void Take(Vec3 omega, Vec3 v, string text)
        {
            var row = Row(v, omega);
            if (Rank(basis.Append(row)) > basis.Count)
            {
                basis.Add(row);
                free.Add(text);
            }
        }

        foreach (var axis in axes.Where(axis => Allowed(default, axis)))
            Take(default, axis, $"沿 {ContactPlanner.Direction(axis)[1]} 移");
        var pivots = mates.Where(mate => mate.Geometry is MateGeometry.Cylinder or MateGeometry.Line).Select(mate => (mate.Point, Axis: mate.Vector.Normalized()))
            .Concat(axes.Select(axis => (Point: origin, Axis: axis)));
        foreach (var (point, axis) in pivots)
        {
            var q = (point - origin) * 0.001;
            var v0 = -axis.Cross(q);
            if (!Allowed(axis, v0))
                continue;
            var (u, v, un, vn) = ContactPlanner.Frame(axis);
            var name = ContactPlanner.Direction(axis.Canonical());
            Take(axis, v0, $"绕 {(name.Length == 2 ? name[1].ToString() : name)} 转（过 {un}{F(point.Dot(u))} {vn}{F(point.Dot(v))}）");
        }

        if (free.Count < dof)
            free.Add($"另有 {dof - free.Count} 个组合运动");
        return (dof, free);
    }

    /// <summary>根：优先固定着的，贴合关系最多的，再比贴合面积。</summary>
    public static string Root(IReadOnlyList<string> nodes, IReadOnlyList<ContactEdge> edges, IReadOnlySet<string> fixedNodes)
    {
        int Degree(string node) => edges.Count(edge => edge.A == node || edge.B == node);
        double Area(string node) => edges.Where(edge => edge.A == node || edge.B == node).Sum(edge => edge.Area);
        return nodes.OrderByDescending(fixedNodes.Contains).ThenByDescending(Degree).ThenByDescending(Area).ThenBy(node => node, StringComparer.Ordinal).First();
    }

    /// <summary>从 <paramref name="root"/> 广度优先长树；返回树边（按配的先后）、成环断开的边、连不上的零部件。</summary>
    public static (IReadOnlyList<TreeEdge> Tree, IReadOnlyList<ContactEdge> Broken, IReadOnlyList<string> Unreached) Tree(IReadOnlyList<string> nodes, IReadOnlyList<ContactEdge> edges, string root)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal) { [root] = 0 };
        var tree = new List<TreeEdge>();
        var used = new HashSet<ContactEdge>();
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var edge in edges.Where(edge => edge.A == node || edge.B == node).OrderByDescending(edge => edge.Area).ThenBy(edge => edge.A + edge.B, StringComparer.Ordinal))
            {
                var other = edge.A == node ? edge.B : edge.A;
                if (depth.ContainsKey(other))
                    continue;
                depth[other] = depth[node] + 1;
                tree.Add(new TreeEdge(node, other, depth[other], edge.Area));
                used.Add(edge);
                queue.Enqueue(other);
            }
        }

        var broken = edges.Where(edge => !used.Contains(edge) && depth.ContainsKey(edge.A) && depth.ContainsKey(edge.B)).ToList();
        var unreached = nodes.Where(node => !depth.ContainsKey(node)).ToList();
        return (tree, broken, unreached);
    }

    /// <summary>零件文件在任何「外购件」文件夹下就是外购件（蚂蚁工场插入的都放那），其余是加工件。</summary>
    public static bool Purchased(string path)
        => path.Replace('/', '\\').Split('\\').Any(part => part.Equals("外购件", StringComparison.Ordinal));

    private static double[] Row(Vec3 a, Vec3 b) => [a.X, a.Y, a.Z, b.X, b.Y, b.Z];

    private static (Vec3, Vec3) Perpendiculars(Vec3 n)
    {
        var seed = Math.Abs(n.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var u = n.Cross(seed).Normalized();
        return (u, n.Cross(u).Normalized());
    }

    private static string F(double value) => ModelSpec.Format(value);

    /// <summary>
    /// 子零部件配到父零部件上该加哪几条配合（<paramref name="childIsMine"/>：子零部件的面在 <c>ContactPair.Mine</c> 那边）。
    /// 先后：贴合面积最大的那个面重合 → 有两根以上同轴就取相距最远的两根同轴心（用户定：两同心自动上两同心）→ 别的方向的贴合面重合 → 只有一根同轴就同轴心。
    /// 每条只有让子零部件少一个以上自由度才要（对方当作不动；<paramref name="existing"/> 是子零部件已挂的约束），去掉多余的免得过定义；到 0 就停。
    /// </summary>
    public static IReadOnlyList<MateCandidate> Choose(IReadOnlyList<ContactPair> planes, IReadOnlyList<ContactPair> axes, bool childIsMine, IReadOnlyList<MateConstraint> existing, Vec3 origin, bool concentricOnly = false)
    {
        ContactFace Child(ContactPair pair) => childIsMine ? pair.Mine : pair.Other;
        ContactFace Parent(ContactPair pair) => childIsMine ? pair.Other : pair.Mine;
        MateCandidate Plane(ContactPair pair) => new(0, Child(pair).Ref, Parent(pair).Ref, new MateConstraint(0, MateGeometry.Plane, Child(pair).Point, Child(pair).Normal));
        MateCandidate Axis(ContactPair pair) => new(1, Child(pair).Ref, Parent(pair).Ref, new MateConstraint(1, MateGeometry.Cylinder, Child(pair).Point, Child(pair).Normal));

        // 同一平面（子零部件这边）的贴合合成一组，组按面积从大到小，组里挑面积最大的那对当代表。
        var groups = planes.GroupBy(pair => (Normal: Round(Child(pair).Normal), Offset: Math.Round(Child(pair).Point.Dot(Child(pair).Normal.Normalized()), 2)))
            .Select(group => (Area: group.Sum(pair => pair.Amount), Best: group.OrderByDescending(pair => pair.Amount).First()))
            .OrderByDescending(group => group.Area)
            .Select(group => group.Best)
            .ToList();
        var ordered = new List<MateCandidate>();
        if (!concentricOnly && groups.Count > 0)
            ordered.Add(Plane(groups[0]));
        if (axes.Count >= 2)
        {
            var (i, j, best) = (0, 1, -1.0);
            for (var x = 0; x < axes.Count; x++)
            {
                for (var y = x + 1; y < axes.Count; y++)
                {
                    var d = (Child(axes[x]).Point - Child(axes[y]).Point).Length;
                    var parallel = Math.Abs(Child(axes[x]).Normal.Normalized().Dot(Child(axes[y]).Normal.Normalized())) > 0.99985;
                    var apart = parallel ? ContactPlanner.Offset(Child(axes[x]).Point, Child(axes[y]).Point, Child(axes[x]).Normal.Normalized()) : d;
                    if (apart > best)
                        (i, j, best) = (x, y, apart);
                }
            }

            ordered.Add(Axis(axes[i]));
            ordered.Add(Axis(axes[j]));
        }

        if (!concentricOnly)
            ordered.AddRange(groups.Skip(1).Select(Plane));
        if (axes.Count == 1)
            ordered.Add(Axis(axes[0]));

        var rows = existing.SelectMany(c => Wrenches(c, origin)).ToList();
        var chosen = new List<MateCandidate>();
        foreach (var candidate in ordered)
        {
            if (Rank(rows) == 6)
                break;
            var more = rows.Concat(Wrenches(candidate.Constraint, origin)).ToList();
            if (Rank(more) <= Rank(rows))
                continue;
            rows = more;
            chosen.Add(candidate);
        }

        return chosen;
    }

    private static (double, double, double) Round(Vec3 v) => (Math.Round(v.X, 3), Math.Round(v.Y, 3), Math.Round(v.Z, 3));
}

/// <summary>一条要加的配合：种类（swMateType_e，0 重合、1 同轴心）、子零部件的面「名@F序号」、父零部件的面、子零部件这边的约束。</summary>
internal sealed record MateCandidate(int Type, string Child, string Parent, MateConstraint Constraint);
