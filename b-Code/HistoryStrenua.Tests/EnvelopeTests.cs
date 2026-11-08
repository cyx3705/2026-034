using HistoryStrenua;

/// <summary>1.42.0：装配包络的属性写法、面检查、合成与换到总装。</summary>
internal static partial class Tests
{
    static void TestEnvelopeSpec()
    {
        // 侧号写法。
        Equal(1, EnvelopeSpec.Side("+X"));
        Equal(1, EnvelopeSpec.Side("x"));
        Equal(4, EnvelopeSpec.Side("-z"));
        Equal(2, EnvelopeSpec.Side("Y-"));
        Throws(() => EnvelopeSpec.Side("+W"), "side=+W");

        // 属性文本来回一致；没写的侧按包围盒。
        const string text = "-X:盒;+X:盒;-Y:F12@0;+Y:F3+1.5@40;-Z:F7-2/轴@25;+Z:=120";
        var sides = EnvelopeSpec.Parse(text);
        Equal(text, EnvelopeSpec.Format(sides));
        Equal(EnvelopeSource.Face, sides[3].Source);
        Equal(3, sides[3].Face);
        Equal(1.5, sides[3].Offset);
        Equal(-2.0, sides[4].Offset);
        True(sides[4].Axis, "-Z 取轴线");
        Equal(EnvelopeSource.Manual, sides[5].Source);
        Equal(120.0, sides[5].Value);
        Equal(EnvelopeSource.Box, EnvelopeSpec.Parse("+Z:=5")[0].Source);
        Equal(EnvelopeSpec.Format(EnvelopeSpec.Default()), EnvelopeSpec.Format(EnvelopeSpec.Parse(null)));
        True(Rejects(() => EnvelopeSpec.Parse("+Y:G3")), "写坏的属性要报");

        // 平面：法向定侧，侧给错轴拒绝，斜面拒绝。
        var top = new EnvelopeFaceGeometry(true, new Vec3(0, 1, 0), new Vec3(5, 40, 9), 0, false);
        Equal(3, EnvelopeSpec.Check(top, null, 3));
        Equal(2, EnvelopeSpec.Check(top, 2, 3)); // 同一根轴的另一侧可以（凹进去的装配面）
        True(Rejects(() => EnvelopeSpec.Check(top, 0, 3)), "法向 +Y 的面定不了 -X");
        True(Rejects(() => EnvelopeSpec.Check(new EnvelopeFaceGeometry(true, new Vec3(0, 1, 1), default, 0, false), null, 1)), "斜面拒绝");
        Equal(40.0, EnvelopeSpec.Locate(top, 3, null).Value);

        // 圆柱：必须给侧、轴线要垂直；孔默认轴线，轴默认外侧切线。
        var shaft = new EnvelopeFaceGeometry(false, new Vec3(1, 0, 0), new Vec3(0, 0, 30), 10, false);
        var hole = shaft with { Hole = true };
        Throws(() => EnvelopeSpec.Check(shaft, null, 7), "圆柱不给侧");
        True(Rejects(() => EnvelopeSpec.Check(shaft, 1, 7)), "轴线 X 定不了 +X");
        Equal(5, EnvelopeSpec.Check(shaft, 5, 7));
        Equal(40.0, EnvelopeSpec.Locate(shaft, 5, null).Value);
        Equal(20.0, EnvelopeSpec.Locate(shaft, 4, null).Value);
        Equal(30.0, EnvelopeSpec.Locate(hole, 5, null).Value);
        Equal(40.0, EnvelopeSpec.Locate(hole, 5, false).Value);
        Equal(30.0, EnvelopeSpec.Locate(shaft, 5, true).Value);

        // 偏移往外为正。
        Equal(41.5, EnvelopeSpec.Outward(3, 40, 1.5));
        Equal(-1.5, EnvelopeSpec.Outward(2, 0, 1.5));

        // 合成：只改的面变，交叉报轴。
        var box = (new Vec3(-50, 0, 0), new Vec3(50, 100, 20));
        var (min, max, crossed) = EnvelopeSpec.Compose(box, new Dictionary<int, double> { [3] = 40 });
        Equal(new Vec3(-50, 0, 0), min);
        Equal(new Vec3(50, 40, 20), max);
        Equal<string?>(null, crossed);
        Equal<string?>("Y", EnvelopeSpec.Compose(box, new Dictionary<int, double> { [2] = 60, [3] = 40 }).Crossed);

        // 换到总装：零件 x→总装 X、y→总装 -Z、z→总装 Y，原点 (100,0,0)。
        var axes = new double[3, 3] { { 1, 0, 0 }, { 0, 0, -1 }, { 0, 1, 0 } };
        var (amin, amax) = EnvelopeSpec.ToAssembly((min, max), axes, (100, 0, 0));
        Equal(new Vec3(50, 0, -40), amin);
        Equal(new Vec3(150, 20, 0), amax);
        Equal("50,0,-40,150,20,0", EnvelopeSpec.BoxText((amin, amax)));

        // 不写「-0」，盒保留 3 位（146.95 不能写成 147）。
        Equal("0", EnvelopeSpec.Num(-0.0000001));
        Equal("146.95", EnvelopeSpec.Num(146.95));
        Equal("0,146.95,0,1,2,3", EnvelopeSpec.BoxText((new Vec3(-1e-9, 146.95, 0), new Vec3(1, 2, 3))));

        // 贴面摆位：只沿目标法向挪，间隙往外。
        var identity = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var cube = (new Vec3(0, 0, 0), new Vec3(10, 10, 10));
        var (snapped, moved) = EnvelopeSpec.Snap(identity, (5, 5, 5), cube, 2, new Vec3(0, 100, 0), new Vec3(0, 1, 0), 0);
        Equal((5.0, 100.0, 5.0), snapped);
        Equal(95.0, moved);
        Equal((5.0, 101.0, 5.0), EnvelopeSpec.Snap(identity, (5, 5, 5), cube, 2, new Vec3(0, 100, 0), new Vec3(0, 1, 0), 1).Origin);
        // 角码那种：零件 z 朝总装 Y（axes=X,-Z,Y），包络 -Z 改到装配面 0（包围盒 -2 是定位凸台），贴梁顶 145。
        var turned = new double[3, 3] { { 1, 0, 0 }, { 0, 0, -1 }, { 0, 1, 0 } };
        var corner = (new Vec3(0, 0, 0), new Vec3(38.5, 38.5, 38.5));
        Equal(145.0, EnvelopeSpec.Snap(turned, (920, 100, 118), corner, 4, new Vec3(0, 145, 0), new Vec3(0, 1, 0), 0).Origin.Y);
        True(Rejects(() => EnvelopeSpec.Snap(turned, (0, 0, 0), corner, 2, new Vec3(0, 145, 0), new Vec3(0, 1, 0), 0)), "-Y 朝 +Z 贴不上朝 +Y 的面");
        try
        {
            EnvelopeSpec.Snap(turned, (0, 0, 0), corner, 2, new Vec3(0, 145, 0), new Vec3(0, 1, 0), 0);
        }
        catch (QuickCommandException error)
        {
            True(error.Message.Contains("现在正对目标的是 -Z", StringComparison.Ordinal), error.Message);
        }

        // 盒差（用户的算法）：角码零件 y 朝总装 Y、z 朝总装 -X（axes=-Z,Y,X 的转置写法这里直接给行），包围盒 -Y=-2 / -X=-2，包络都改到 0。
        var bracket = new double[3, 3] { { 0, 0, -1 }, { 0, 1, 0 }, { 1, 0, 0 } };
        var bracketBox = (new Vec3(-2, -2, -17.25), new Vec3(38.5, 38.5, 17.25));
        var bracketEnvelope = (new Vec3(0, 0, -17.25), new Vec3(38.5, 38.5, 17.25));
        var delta = EnvelopeSpec.BoxDelta(bracket, bracketBox, bracketEnvelope, [2, 0]);
        Equal(new Vec3(0, -2, 2), delta); // 零件 -Y 外移 2 = 总装往下 2；零件 -X 外移 2 = 总装 +Z 2
        Equal(new Vec3(0, 0, 0), EnvelopeSpec.BoxDelta(bracket, bracketBox, bracketBox, [2, 0]));

        // 批量贴面找目标：法向正对、投影重叠、范围内最近；背对的、错开的、太远的不要。
        ContactFace Plane(string owner, int index, double y, Vec3 normal, double x0, double x1)
            => new(owner, index, ContactSurface.Plane, normal, new Vec3(x0, y, 0), 0, false,
                [new Vec3(x0, y, -5), new Vec3(x1, y, -5), new Vec3(x1, y, 5), new Vec3(x0, y, 5)]);
        var up = new Vec3(0, 1, 0);
        // 盒 0..10，原点 (0, 1.95, 0)：底面在 y=1.95，-Y 侧。
        ContactFace[] faces =
        [
            Plane("梁-1", 94, 0, up, -20, 20),          // 正下方，差 1.95 → 选它
            Plane("梁-2", 3, 1, up, 30, 40),            // 更近但投影不重叠
            Plane("梁-3", 7, -1, new Vec3(0, -1, 0), -20, 20), // 背对
            Plane("梁-4", 8, -10, up, -20, 20),         // 太远
        ];
        var pick = EnvelopeSpec.Nearest(identity, (0, 1.95, 0), cube, 2, faces, 5);
        True(pick is not null && pick.Value.Face.Ref == "梁-1@F94", pick?.Face.Ref ?? "没找到");
        Equal(1.95, Math.Round(pick!.Value.Distance, 6));
        // 压进去的也找（距离为负）。
        Equal(-0.5, Math.Round(EnvelopeSpec.Nearest(identity, (0, -0.5, 0), cube, 2, faces, 5)!.Value.Distance, 6));
        True(EnvelopeSpec.Nearest(identity, (0, 1.95, 0), cube, 2, faces[1..], 5) is null, "只剩错开、背对、太远的就没有目标");

        // 线框 12 条棱、每条只沿一根轴。
        var edges = EnvelopeSpec.Edges((min, max));
        Equal(12, edges.Count);
        True(edges.All(edge => new[] { edge.A.X != edge.B.X, edge.A.Y != edge.B.Y, edge.A.Z != edge.B.Z }.Count(changed => changed) == 1), "棱只沿一根轴");
    }

    static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (QuickCommandException)
        {
            return true;
        }
    }
}
