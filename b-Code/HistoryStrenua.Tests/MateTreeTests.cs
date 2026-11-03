using HistoryStrenua;

/// <summary>1.40.0：自由度求秩、贴合树、配合挑选、同轴孔、外购件判定。</summary>
internal static partial class Tests
{
    static readonly Vec3 Up = new(0, 1, 0);

    static void TestFreedom()
    {
        var origin = new Vec3(0, 0, 0);
        var plane = new MateConstraint(0, MateGeometry.Plane, new Vec3(0, 0, 0), Up);
        var (dof, free) = MateTreePlanner.Freedom([plane], origin);
        Equal(3, dof);
        True(free.Contains("沿 X 移") && free.Contains("沿 Z 移") && free.Any(item => item.StartsWith("绕 Y 转", StringComparison.Ordinal)), string.Join("、", free));

        var pin = new MateConstraint(1, MateGeometry.Cylinder, new Vec3(10, 0, 0), Up);
        (dof, free) = MateTreePlanner.Freedom([plane, pin], origin);
        Equal(1, dof);
        Equal("绕 Y 转（过 X10 Z0）", free.Single());

        var second = new MateConstraint(1, MateGeometry.Cylinder, new Vec3(-10, 0, 0), Up);
        Equal(0, MateTreePlanner.Freedom([plane, pin, second], origin).Dof);
        Equal(0, MateTreePlanner.Freedom([new MateConstraint(16, MateGeometry.Other, default, default)], origin).Dof);
        Equal(4, MateTreePlanner.Freedom([new MateConstraint(3, MateGeometry.Plane, default, Up)], origin).Dof);
        Equal(6, MateTreePlanner.Freedom([], origin).Dof);
        // 两个平行的面重合只算一个（第二个是多余的）。
        Equal(3, MateTreePlanner.Freedom([plane, new MateConstraint(0, MateGeometry.Plane, new Vec3(50, 0, 0), Up)], origin).Dof);
    }

    static void TestMateTree()
    {
        string[] nodes = ["A", "B", "C", "D", "E"];
        ContactEdge[] edges = [new("A", "B", 10), new("B", "C", 5), new("A", "C", 1), new("C", "D", 3)];
        var (tree, broken, unreached) = MateTreePlanner.Tree(nodes, edges, "A");
        Equal("A>B,A>C,C>D", string.Join(",", tree.Select(edge => $"{edge.Parent}>{edge.Child}")));
        Equal(2, tree.Single(edge => edge.Child == "D").Depth);
        Equal("B-C", string.Join(",", broken.Select(edge => $"{edge.A}-{edge.B}")));
        Equal("E", string.Join(",", unreached));

        Equal("C", MateTreePlanner.Root(nodes, edges, new HashSet<string>()));
        Equal("D", MateTreePlanner.Root(nodes, edges, new HashSet<string> { "D" }));

        True(MateTreePlanner.Purchased(@"C:\项目\b-Module-SW\外购件\AHS10-H4040-L700.SLDPRT"), "外购件文件夹");
        True(!MateTreePlanner.Purchased(@"C:\项目\b-Module-SW\LSQH-01-01 立柱底板.SLDPRT"), "加工件");
    }

    static void TestMateChoose()
    {
        // 板（子）贴在底座（父）顶面上，两个孔对着底座的两个螺纹孔。
        var plate = Square("板-1", 4, new Vec3(0, -1, 0), 100, -50, -20, 50, 20);
        var base_ = Square("底座-1", 9, new Vec3(0, 1, 0), 100, -80, -40, 80, 40);
        var contact = ContactPlanner.Match(plate, base_, 0.05, 0.05)!;
        var h1 = Tube("板-1", 6, 3.3, true, 100, 110, -40);
        var h2 = Tube("板-1", 7, 3.3, true, 100, 110, 40);
        var t1 = Tube("底座-1", 2, 2.5, true, 88, 100, -40);
        var t2 = Tube("底座-1", 3, 2.5, true, 88, 100, 40);
        var a1 = ContactPlanner.Coaxial(h1, t1, 1, 0.05);
        var a2 = ContactPlanner.Coaxial(h2, t2, 1, 0.05);
        True(a1 is not null && a2 is not null, "孔对螺纹孔同轴");
        True(ContactPlanner.Coaxial(Tube("x", 0, 3, false, 0, 10), Tube("y", 0, 3, false, 0, 10), 1, 0.05) is null, "两根轴不算");
        True(ContactPlanner.Coaxial(h1, Tube("底座-1", 5, 2.5, true, 80, 98, -40), 1, 0.05) is null, "轴向隔 2 mm 超出范围");
        True(ContactPlanner.Coaxial(h1, Tube("底座-1", 5, 2.5, true, 88, 100, -39), 1, 0.05) is null, "轴线偏 1 mm 不算");
        Equal(1, ContactPlanner.DistinctAxes([a1!, a1! with { Other = t1 with { Index = 8 } }]).Count);

        var chosen = MateTreePlanner.Choose([contact], [a1!, a2!], true, [], new Vec3(0, 100, 0));
        Equal("0:板-1@F4>底座-1@F9,1:板-1@F6>底座-1@F2,1:板-1@F7>底座-1@F3", string.Join(",", chosen.Select(c => $"{c.Type}:{c.Child}>{c.Parent}")));
        Equal(0, MateTreePlanner.Freedom(chosen.Select(c => c.Constraint).ToList(), new Vec3(0, 100, 0)).Dof);
        // 子零部件在 Other 那边时两头对调。
        var flipped = MateTreePlanner.Choose([contact], [a1!, a2!], false, [], default);
        True(flipped[0].Child == "底座-1@F9" && flipped[0].Parent == "板-1@F4", flipped[0].ToString());
        // 已经定死的不再加；只要同心的不加平面。
        Equal(0, MateTreePlanner.Choose([contact], [a1!, a2!], true, [new MateConstraint(16, MateGeometry.Other, default, default)], default).Count);
        Equal("1,1", string.Join(",", MateTreePlanner.Choose([], [a1!, a2!], true, [], new Vec3(0, 100, 0), concentricOnly: true).Select(c => c.Type)));
    }
}
