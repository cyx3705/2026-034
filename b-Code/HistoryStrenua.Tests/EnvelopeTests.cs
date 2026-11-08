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
