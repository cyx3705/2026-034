using HistoryStrenua;

/// <summary>1.44.0：一键螺丝的类型、孔口认法、长度挑法、摆位（数值取自真机 GB 沉孔 / ISO 锥孔与蚂蚁工场下载件现量）。</summary>
internal static partial class Tests
{
    static void TestScrewKinds()
    {
        Equal("内六角圆柱头", ScrewKind.Parse("杯头").Name);
        Equal("弹垫组合", ScrewKind.Parse("SZA").Name);
        Equal("十字盘头", ScrewKind.Parse("盘头").Name);
        Equal(302188L, ScrewKind.Parse("沉头").Product);
        Throws(() => ScrewKind.Parse("紧定"), "紧定螺钉没有头");

        var sizes = ScrewSpec.Sizes("M3=十字盘头,m8=弹垫组合");
        Equal("十字盘头", sizes[3].Name);
        Equal("弹垫组合", sizes[8].Name);
        Throws(() => ScrewSpec.Sizes("M3=盘头,3=沉头"), "同一规格写两次");
        Throws(() => ScrewSpec.Sizes("粗=盘头"), "规格写错");

        // 按沉头自动：锥孔一律沉头（十字系十字沉头）、沉孔圆柱头一类、平面照选的（选沉头换圆柱头）。
        Equal("内六角沉头", ScrewSpec.Choose(ScrewKind.Cap, ScrewSeat.Countersink, true).Kind!.Name);
        Equal("十字沉头", ScrewSpec.Choose(ScrewKind.Pan, ScrewSeat.Countersink, true).Kind!.Name);
        Equal("弹垫组合", ScrewSpec.Choose(ScrewKind.Spring, ScrewSeat.Counterbore, true).Kind!.Name);
        Equal("内六角圆柱头", ScrewSpec.Choose(ScrewKind.Button, ScrewSeat.Counterbore, true).Kind!.Name);
        Equal("十字盘头", ScrewSpec.Choose(ScrewKind.Pan, ScrewSeat.Surface, true).Kind!.Name);
        var flat = ScrewSpec.Choose(ScrewKind.Countersunk, ScrewSeat.Surface, true);
        Equal("内六角圆柱头", flat.Kind!.Name);
        True(flat.Note is not null, "平面孔口换类型要说明");
        // 不自动：装不上就跳过。
        True(ScrewSpec.Choose(ScrewKind.Cap, ScrewSeat.Countersink, false).Kind is null, "圆柱头进锥孔");
        True(ScrewSpec.Choose(ScrewKind.Countersunk, ScrewSeat.Surface, false).Kind is null, "沉头贴平面");
        Equal("内六角半圆头", ScrewSpec.Choose(ScrewKind.Button, ScrewSeat.Counterbore, false).Kind!.Name);
    }

    static void TestScrewStack()
    {
        // GB M6 沉孔盖板（厚 10，沉孔 Ø11 深 6.8，过孔 Ø6.6）。
        var cbore = ScrewSpec.Stack(
        [
            new AxialFace("盖板-1", false, true, 0, 3.2, 3.3, 3.3),
            new AxialFace("盖板-1", false, true, 3.2, 10, 5.5, 5.5),
        ], 6).Stack!;
        Equal(ScrewSeat.Counterbore, cbore.Seat);
        Equal(3.2, cbore.SeatAt);
        Equal(5.5, cbore.SeatRadius);

        // ISO M4 锥形沉头孔（Ø8.96 90°，过孔 Ø4.5）。
        var sink = ScrewSpec.Stack(
        [
            new AxialFace("盖板-1", false, true, 0, 7.77, 2.25, 2.25),
            new AxialFace("盖板-1", true, true, 7.77, 10, 2.25, 4.48),
        ], 4).Stack!;
        Equal(ScrewSeat.Countersink, sink.Seat);
        Equal(10.0, sink.SeatAt);
        Equal(45.0, Math.Round(sink.ConeHalfAngle * 180 / Math.PI));

        // 两块板叠着，头贴最外那块表面；挨着的第三块离太远不算。
        var two = ScrewSpec.Stack(
        [
            new AxialFace("垫块-1", false, true, 0, 5, 4.5, 4.5),
            new AxialFace("盖板-1", false, true, 5.2, 15, 4.5, 4.5),
            new AxialFace("远处-1", false, true, 40, 50, 4.5, 4.5),
        ], 8).Stack!;
        Equal(ScrewSeat.Surface, two.Seat);
        Equal(15.0, two.SeatAt);
        Equal("垫块-1,盖板-1", string.Join(",", two.Owners));

        // 过孔没贴上 / 对面的孔比螺纹还小 / 什么都没有。
        True(ScrewSpec.Stack([new AxialFace("盖板-1", false, true, 2, 12, 3.3, 3.3)], 6).Reason!.Contains("没贴上"), "离孔口 2 mm");
        True(ScrewSpec.Stack([new AxialFace("盖板-1", false, true, 0, 12, 2.5, 2.5)], 6).Reason!.Contains("还小"), "对面是底孔");
        True(ScrewSpec.Stack([], 6).Reason!.Contains("没有对上"), "空");
        // 杆（凸圆柱）不算过孔。
        True(ScrewSpec.Stack([new AxialFace("螺钉-1", false, false, -10, 10, 3, 3)], 6).Stack is null, "杆");
    }

    static void TestScrewLength()
    {
        // SSC1-M6 现量：头顶→头下 6，L20 头顶→螺尾 26；沉孔底在 3.2，螺纹深 12。
        var cap = new ScrewProfile(new Vec3(0, 0, -1), default, 20, 26, 6, 5, 5);
        var hole = new TappedHole("底座-1", "HoleWzd1", new Vec3(-40, 20, -20), new Vec3(0, 1, 0), 6, 12, false);
        var stack = new ScrewStack(["盖板-1"], ScrewSeat.Counterbore, 3.2, 5.5, 0);
        var top = ScrewSpec.TopAt(stack, cap, ScrewHead.Cap);
        Equal(9.2, Math.Round(top, 3));
        var window = ScrewSpec.Engagement(hole);
        Equal((6.0, 9.0, 12.0), window);
        double[] table = [6, 8, 10, 12, 14, 16, 18, 20];
        Equal(12.0, ScrewSpec.Length(table, length => cap.TipFor(length) - top, window));

        // SSK1-M4 沉头头半径 4，ISO 锥孔 R4.48 → 头顶沉 0.48；螺纹深 8 → L16（旋入 6.48）。
        var countersunk = new ScrewProfile(new Vec3(0, 0, -1), default, 16, 16, 0, 4, 4);
        var sink = new ScrewStack(["盖板-1"], ScrewSeat.Countersink, 10, 4.48, Math.PI / 4);
        var sinkTop = ScrewSpec.TopAt(sink, countersunk, ScrewHead.Countersunk);
        Equal(9.52, Math.Round(sinkTop, 3));
        var m4 = hole with { Diameter = 4, Thread = 8 };
        Equal(16.0, ScrewSpec.Length([6, 8, 10, 12, 14, 16, 18, 20], length => countersunk.TipFor(length) - sinkTop, ScrewSpec.Engagement(m4)));

        // 螺纹太浅 / 夹太厚都挑不到，说清楚。
        var shallow = hole with { Thread = 3 };
        True(ScrewSpec.Length([20, 25], length => cap.TipFor(length) - top, ScrewSpec.Engagement(shallow)) is null, "太浅");
        True(ScrewSpec.NoLength([20, 25], length => cap.TipFor(length) - top, ScrewSpec.Engagement(shallow)).Contains("太浅"), "太浅说明");
        True(ScrewSpec.NoLength([6, 8], length => cap.TipFor(length) - 30, window).Contains("太厚"), "太厚说明");
    }

    static void TestScrewPlace()
    {
        // 杆沿 −Z 的件（圆柱头）：孔朝 +Y，零件 z 轴应朝 +Y，原点 = 头顶。
        var cap = new ScrewProfile(new Vec3(0, 0, -1), default, 20, 26, 6, 5, 5);
        var (axes, origin) = ScrewSpec.Place(cap, new Vec3(0, 1, 0), new Vec3(-40, 29.2, -20));
        Equal("0,1,0", Row(axes, 2));
        Equal((-40.0, 29.2, -20.0), (Math.Round(origin.X, 6), Math.Round(origin.Y, 6), Math.Round(origin.Z, 6)));
        True(Math.Abs(Det(axes) - 1) < 1e-9, "右手系");

        // 杆沿 +X、头顶不在原点的件（组合螺丝 / 十字沉头）：x 轴朝孔里，原点补回头顶偏移。
        var spring = new ScrewProfile(new Vec3(1, 0, 0), new Vec3(0.5, 0, 0), 20, 26, 9.11, 5, 5.97);
        (axes, origin) = ScrewSpec.Place(spring, new Vec3(0, 0, 1), new Vec3(10, 10, 10));
        Equal("0,0,-1", Row(axes, 0));
        Equal((10.0, 10.0, 10.5), (Math.Round(origin.X, 6), Math.Round(origin.Y, 6), Math.Round(origin.Z, 6)));
        True(Math.Abs(Det(axes) - 1) < 1e-9, "右手系");
    }

    private static string Row(double[,] axes, int row)
        => string.Join(",", Enumerable.Range(0, 3).Select(column => Math.Round(axes[row, column], 9) is var value && value == 0 ? "0" : value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static double Det(double[,] m)
        => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
}
