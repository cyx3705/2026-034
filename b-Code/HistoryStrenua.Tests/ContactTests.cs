using HistoryStrenua;

/// <summary>1.39.0：接触面判定、按面分组、截面 SVG、配合与矩阵写法。</summary>
internal static partial class Tests
{
    /// <summary>平面上的矩形（两个三角形），法向朝 <paramref name="normal"/>；u、v 按 <see cref="ContactPlanner.Frame"/>。</summary>
    static ContactFace Square(string owner, int index, Vec3 normal, double offset, double u0, double v0, double u1, double v1)
    {
        var (u, v, _, _) = ContactPlanner.Frame(normal);
        var n = normal.Normalized().Canonical();
        Vec3 P(double a, double b) => u * a + v * b + n * offset;
        var tris = new List<Vec3> { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v0), P(u1, v1), P(u0, v1) };
        return new ContactFace(owner, index, ContactSurface.Plane, normal, tris[0], 0, false, tris);
    }

    static ContactFace Tube(string owner, int index, double radius, bool hole, double from, double to, double x = 0)
    {
        // 轴沿 Y，过 (x, *, 0)；面片只要轴向两端的点。
        var tris = new List<Vec3> { new(x + radius, from, 0), new(x + radius, to, 0), new(x - radius, to, 0) };
        return new ContactFace(owner, index, ContactSurface.Cylinder, new Vec3(0, 1, 0), new Vec3(x, 0, 0), radius, hole, tris);
    }

    static void TestContactPlanes()
    {
        // 型材顶面 Y=145 朝 +Y，压在上面的板底面 Y=145 朝 −Y，错开一半：重叠 20×40。
        var top = Square("型材-1", 7, new Vec3(0, 1, 0), 145, -20, -20, 20, 20);
        var plate = Square("底板-1", 3, new Vec3(0, -1, 0), 145, 0, -20, 60, 20);
        var pair = ContactPlanner.Match(top, plate, 1, 0.05);
        True(pair is not null, "相对贴合的两面要接触");
        Equal(800.0, Math.Round(pair!.Amount, 6));
        Equal("贴合", ContactPlanner.State(pair, 0.05));

        var lifted = Square("底板-1", 3, new Vec3(0, -1, 0), 145.5, 0, -20, 60, 20);
        var gap = ContactPlanner.Match(top, lifted, 1, 0.05)!;
        Equal("差 0.5 没贴上", ContactPlanner.State(gap, 0.05));
        True(ContactPlanner.Match(top, lifted, 0.2, 0.05) is null, "超出范围不报");
        True(ContactPlanner.Match(top, Square("x", 0, new Vec3(0, 1, 0), 145, 0, 0, 10, 10), 1, 0.05) is null, "同向的面不是接触");
        True(ContactPlanner.Match(top, Square("x", 0, new Vec3(0, -1, 0), 145, 30, 30, 40, 40), 1, 0.05) is null, "投影不重叠不报");

        var pressed = ContactPlanner.Match(top, Square("x", 0, new Vec3(0, -1, 0), 144.8, 0, 0, 10, 10), 1, 0.05)!;
        Equal("压进 0.2", ContactPlanner.State(pressed, 0.05));

        // 同一平面上两块对方并成一个对象；另一个方向单独一个。
        var second = ContactPlanner.Match(top, Square("立柱-1", 0, new Vec3(0, -1, 0), 145, -20, -20, -10, 20), 1, 0.05)!;
        var side = Square("型材-1", 2, new Vec3(1, 0, 0), 20, 145, -20, 185, 20);
        var end = ContactPlanner.Match(side, Square("挡块-1", 1, new Vec3(-1, 0, 0), 20, 150, -10, 160, 10), 1, 0.05)!;
        var groups = ContactPlanner.Group([pair, second, end], 0.05);
        Equal(2, groups.Count);
        Equal("P1", groups[0].Id);
        Equal(2, groups[0].Pairs.Count);
        Equal("平面 Y=145 朝 +Y", ContactPlanner.Describe(groups[0]));
        Equal("平面 X=20 朝 +X", ContactPlanner.Describe(groups[1]));
        True(ContactPlanner.Line(pair, 0.05).Contains("[F7 ↔ 底板-1@F3]"), ContactPlanner.Line(pair, 0.05));
        True(ContactPlanner.Line(pair, 0.05).Contains("X 0~20"), ContactPlanner.Line(pair, 0.05));

        // 对方的面已和我贴合，它和我凹进去 1 mm 的面不再报；我这边两块面对同一个对方面合成一行。
        var recess = ContactPlanner.Match(Square("型材-1", 9, new Vec3(0, 1, 0), 144, 30, -20, 40, 20), plate, 2, 0.05)!;
        var split = ContactPlanner.Match(Square("型材-1", 8, new Vec3(0, 1, 0), 145, 40, -20, 50, 20), plate, 2, 0.05)!;
        var merged = ContactPlanner.Group([pair, recess, split], 0.05);
        Equal(1, merged.Count);
        var lines = ContactPlanner.Lines(merged[0], 0.05);
        Equal(1, lines.Count);
        True(lines[0].Contains("[F7,F8 ↔ 底板-1@F3]") && lines[0].Contains("重叠 1200 mm²"), lines[0]);
        Equal(1, ContactPlanner.Group([recess], 0.05).Count);
        // 同一平面上已贴合的零件，它另一个凹进的面也不报。
        var pocket = ContactPlanner.Match(Square("型材-1", 7, new Vec3(0, 1, 0), 145, -20, -20, 20, 20), Square("底板-1", 5, new Vec3(0, -1, 0), 146, -10, -10, 0, 0), 2, 0.05)!;
        Equal(1, ContactPlanner.Group([pair, pocket], 0.05)[0].Pairs.Count);
    }

    static void TestContactCylinders()
    {
        var hole = Tube("底板-1", 4, 3.3, true, 0, 20);
        var bolt = Tube("螺钉-1", 0, 3, false, 10, 40);
        var pair = ContactPlanner.Match(hole, bolt, 1, 0.05)!;
        Equal("直径差 0.6", ContactPlanner.State(pair, 0.05));
        Equal(10.0, Math.Round(pair.Amount, 6));
        True(ContactPlanner.Match(hole, Tube("x", 0, 3.3, true, 0, 20), 1, 0.05) is null, "两个孔不算");
        True(ContactPlanner.Match(hole, Tube("x", 0, 3, false, 0, 20, 5), 1, 0.05) is null, "不同轴不算");
        Equal("同径", ContactPlanner.State(ContactPlanner.Match(hole, Tube("销-1", 0, 3.3, false, 0, 20), 1, 0.05)!, 0.05));
        var group = ContactPlanner.Group([pair], 0.05).Single();
        Equal("A1", group.Id);
        Equal("孔 Ø6.6 轴线 Y 向，过 X0 Z0", ContactPlanner.Describe(group));
    }

    static void TestContactGeometry()
    {
        var clipped = ContactPlanner.Clip([(0, 0), (2, 0), (2, 2), (0, 2)], [(1, 1), (3, 1), (3, 3), (1, 3)]);
        Equal(1.0, Math.Round(ContactPlanner.Area(clipped), 9));
        var (u, v, un, vn) = ContactPlanner.Frame(new Vec3(0, -1, 0));
        Equal(("X", "Z"), (un, vn));
        Equal("+X", ContactPlanner.Direction(u));
        Equal("+Z", ContactPlanner.Direction(v));
        // 两个三角形拼的方块：外轮廓 4 条边一个环，对角线不算。
        var square = Square("a", 0, new Vec3(0, 0, 1), 0, 0, 0, 10, 10);
        var loops = ContactPlanner.Outline(square.Triangles, new Vec3(1, 0, 0), new Vec3(0, 1, 0));
        Equal(1, loops.Count);
        Equal(4, loops[0].Count);

        var top = Square("型材-1", 7, new Vec3(0, 1, 0), 145, -20, -20, 20, 20);
        var pair = ContactPlanner.Match(top, Square("底板-1", 3, new Vec3(0, -1, 0), 145, 0, -20, 60, 20), 1, 0.05)!;
        var svg = ContactPlanner.Svg("总装 · 型材-1", ContactPlanner.Group([pair], 0.05), 0.05);
        True(svg.StartsWith("<svg") && svg.EndsWith("</svg>"), "SVG 首尾");
        True(svg.Contains("id=\"P1\"") && svg.Contains("data-ref=\"底板-1@F3\"") && svg.Contains("data-state=\"贴合\""), svg);
        True(System.Xml.Linq.XDocument.Parse(svg).Root is not null, "SVG 是合法 XML");
    }

    static void TestMateSpec()
    {
        Equal((0, "重合", (string?)null), MateSpec.Type("重合"));
        Equal(5, MateSpec.Type("distance").Type);
        Equal(16, MateSpec.Type("锁定").Type);
        Throws(() => MateSpec.Type("焊接"), "不认识的配合");
        Equal(2, MateSpec.Align(null));
        Equal(1, MateSpec.Align("反向"));

        var face = MateSpec.Target("LSQH-01-01 立柱底板-1@F12", "a");
        Equal(("LSQH-01-01 立柱底板-1", MateTarget.Kinds.Face, 12), (face.Component, face.Kind, face.Index));
        var sub = MateSpec.Target("FBT134-1/滑块-1@上视", "b");
        Equal(("FBT134-1/滑块-1", MateTarget.Kinds.Plane, 1), (sub.Component, sub.Kind, sub.Index));
        var own = MateSpec.Target("@前视", "b");
        Equal((null, MateTarget.Kinds.Plane), (own.Component, own.Kind));
        Equal(MateTarget.Kinds.Origin, MateSpec.Target("底板-1@原点", "a").Kind);
        Equal(MateTarget.Kinds.Component, MateSpec.Target("底板-1", "a").Kind);
        Throws(() => MateSpec.Target("@F3", "a"), "面要写零部件");
        Throws(() => MateSpec.Target("底板-1@孔", "a"), "@ 后不认识");
    }

    static void TestMatrixSpec()
    {
        var (axes, origin) = ModelSpec.Matrix("1,0,0, 0,0,-1, 0,1,0, -600,262,0");
        Equal("X,-Z,Y", ModelSpec.AxesText(axes));
        Equal((-600.0, 262.0, 0.0), origin);
        Equal("1,0,0,0,0,-1,0,1,0,-600,262,0", ModelSpec.MatrixText(axes, origin));
        // 4×4 列向量约定：列是零件轴，同一个摆位。
        var (axes4, origin4) = ModelSpec.Matrix("1,0,0,-600, 0,0,1,262, 0,-1,0,0, 0,0,0,1");
        Equal("X,-Z,Y", ModelSpec.AxesText(axes4));
        Equal((-600.0, 262.0, 0.0), origin4);
        Throws(() => ModelSpec.Matrix("1,0,0,0,1,0,0,0,1"), "只有旋转");
        Throws(() => ModelSpec.Matrix("1,0,0,0, 0,1,0,0, 0,0,1,0, 1,0,0,1"), "末行不是 0,0,0,1");
        Throws(() => ModelSpec.Matrix("1,0,0, 0,1,0, 0,0,-1, 0,0,0"), "左手系");
    }
}
