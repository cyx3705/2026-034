using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>两个顶层零部件之间的关系：贴合的平面对、同轴的圆柱对（<c>Mine</c> 都在 <see cref="A"/> 那边）。</summary>
internal sealed record NodeContacts(string A, string B, IReadOnlyList<ContactPair> Planes, IReadOnlyList<ContactPair> Axes)
{
    public double Area => Planes.Sum(pair => pair.Amount);
}

/// <summary>
/// 自由度、双同心、贴合树共用（1.40.0）：整个总装按顶层零部件（子装配当一整块）两两扫贴合面与同轴孔；加工件判定；零部件的顶层归属。
/// </summary>
internal static class AssemblyContacts
{
    /// <summary>没压缩的顶层零部件。</summary>
    public static IReadOnlyList<object> Tops(SolidWorksApi api, object assembly)
        => api.CallArray(assembly, "IAssemblyDoc", "GetComponents", true).Where(component => !api.CallBool(component, "IComponent2", "IsSuppressed")).ToList();

    /// <summary>零部件所属的顶层零部件（子装配里的零件往上找到顶）。</summary>
    public static object TopOf(SolidWorksApi api, object component)
    {
        var current = component;
        while (api.Call(current, "IComponent2", "GetParent") is { } parent)
            current = parent;
        return current;
    }

    /// <summary>总装里有没有加工件（零件文件不在「外购件」文件夹下），有才上约束（用户定）。</summary>
    public static IReadOnlyList<string> Machined(SolidWorksApi api, object assembly)
        => AssemblyGeometry.Leaves(api, assembly)
            .Where(leaf => !MateTreePlanner.Purchased(api.CallString(leaf, "IComponent2", "GetPathName")))
            .Select(leaf => api.CallString(leaf, "IComponent2", "get_Name2"))
            .ToList();

    /// <summary>没有加工件就抛可纠正的错（约束原则）。</summary>
    public static void RequireMachined(SolidWorksApi api, object assembly, string what)
    {
        if (Machined(api, assembly).Count == 0)
            throw new QuickCommandException($"{what}：「{api.CallString(assembly, "IModelDoc2", "GetTitle")}」里全是外购件（零件都在「外购件」文件夹下），按约束原则不上约束。");
    }

    /// <summary>
    /// 顶层零部件两两扫：包围盒挨着的，读双方靠近对方的面，留贴合（距离不到 <paramref name="tol"/>）的平面对与同轴的圆柱对。
    /// <paramref name="only"/> 给了就只扫至少一边名字在里面的对。
    /// </summary>
    public static IReadOnlyList<NodeContacts> Scan(QuickCommandContext context, object assembly, double tol, double near, IReadOnlySet<string>? only = null)
    {
        var api = context.Api;
        var tops = Tops(api, assembly)
            .Select(top => (Name: api.CallString(top, "IComponent2", "get_Name2"), Component: top, Box: AssemblyGeometry.Box(api, top), Leaves: AssemblyGeometry.LeavesOf(api, top)))
            .Where(top => top.Box is not null)
            .ToList();
        var result = new List<NodeContacts>();
        for (var i = 0; i < tops.Count; i++)
        {
            for (var j = i + 1; j < tops.Count; j++)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var (a, b) = (tops[i], tops[j]);
                if (only is not null && !only.Contains(a.Name) && !only.Contains(b.Name))
                    continue;
                if (!AssemblyGeometry.Touches([b.Box!.Value.Min, b.Box.Value.Max], a.Box!.Value, near))
                    continue;
                var mine = a.Leaves.SelectMany(leaf => AssemblyGeometry.Faces(api, leaf, b.Box, near)).ToList();
                if (mine.Count == 0)
                    continue;
                var theirs = b.Leaves.SelectMany(leaf => AssemblyGeometry.Faces(api, leaf, a.Box, near)).ToList();
                var planes = new List<ContactPair>();
                var axes = new List<ContactPair>();
                foreach (var own in mine)
                {
                    var box = own.Box;
                    foreach (var face in theirs)
                    {
                        var fb = face.Box;
                        if (!AssemblyGeometry.Touches([fb.Min, fb.Max], box, near))
                            continue;
                        if (own.Surface == ContactSurface.Plane)
                        {
                            if (ContactPlanner.Match(own, face, tol, tol) is { } pair)
                                planes.Add(pair);
                        }
                        else if (ContactPlanner.Coaxial(own, face, near, tol) is { } coaxial)
                        {
                            axes.Add(coaxial);
                        }
                    }
                }

                if (planes.Count > 0 || axes.Count > 0)
                    result.Add(new NodeContacts(a.Name, b.Name, planes, ContactPlanner.DistinctAxes(axes)));
            }
        }

        return result;
    }
}
