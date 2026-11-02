using HistoryStrenua;

internal static partial class Tests
{
    static void TestToleranceSpec()
    {
        var symmetric = ToleranceSpec.Dimension("对称", 0.02, null, null, null);
        Equal(4, symmetric.Type);
        Equal<double?>(-0.02, symmetric.Lower);
        Equal(2, ToleranceSpec.Dimension("bilateral", 0.02, -0.01, null, null).Type);
        Equal(3, ToleranceSpec.Dimension("limit", 0.02, 0.01, null, null).Type);
        Equal(1, ToleranceSpec.Dimension("basic", null, null, null, null).Type);
        Equal(0, ToleranceSpec.Dimension("none", null, null, null, null).Type);
        Equal("H7", ToleranceSpec.Dimension("fittol", null, null, "H7", null).HoleFit);
        Throws(() => ToleranceSpec.Dimension("symmetric", null, null, null, null), "对称缺值");
        Throws(() => ToleranceSpec.Dimension("bilateral", 0.01, 0.02, null, null), "上偏差小于下偏差");
        Throws(() => ToleranceSpec.Dimension("fit", null, null, null, null), "配合缺公差带");
        Throws(() => ToleranceSpec.Dimension("fit", null, null, "七", null), "公差带写错");
        Throws(() => ToleranceSpec.Dimension("别的", null, null, null, null), "种类写错");

        var position = ToleranceSpec.Frames("位置度 Ø0.1(M) A B(M) C-D")[0];
        Equal("<IGTOL-POSI>", position.Symbol);
        True(position.Diameter, "Ø");
        Equal("0.1", position.Value);
        Equal("<MOD-MMC>", position.Modifiers);
        Equal("A,B<MOD-MMC>,C-D", string.Join(",", position.Datums));
        Equal("50", ToleranceSpec.Frames("垂直度 0.2(P50) A")[0].ProjectedZone);
        Equal("0.05/100", ToleranceSpec.Frames("直线度 0.05/100")[0].Value);
        Equal(2, ToleranceSpec.Frames("位置度 0.2 A; 位置度 0.05 A").Count);
        foreach (var form in new[] { "直线度 0.1", "平面度 0.1", "圆度 0.1", "圆柱度 0.1", "线轮廓度 0.1", "面轮廓度 0.1 A", "平行度 0.1 A", "倾斜度 0.1 A", "同轴度 Ø0.1 A", "对称度 0.1 A", "圆跳动 0.1 A", "全跳动 0.1 A-B" })
            Equal(1, ToleranceSpec.Frames(form).Count);
        Throws(() => ToleranceSpec.Frames("平面度 0.02 A"), "形状公差带基准");
        Throws(() => ToleranceSpec.Frames("平行度 0.02"), "方向公差缺基准");
        Throws(() => ToleranceSpec.Frames("位置度 0.02 A B C D"), "基准超三个");
        Throws(() => ToleranceSpec.Frames("圆满度 0.02"), "符号写错");
        Throws(() => ToleranceSpec.Frames("位置度 abc"), "公差值写错");
        Throws(() => ToleranceSpec.Frames("位置度 0.1(X)"), "修饰符写错");
        Equal("双向 +0.02/-0.01", ToleranceSpec.Describe(2, 0.02, -0.01, "", ""));
        Equal("配合 H7/g6", ToleranceSpec.Describe(7, 0, 0, "H7", "g6"));
        Equal("", ToleranceSpec.Describe(0, 0, 0, "", ""));
        Equal("E0", ToleranceSpec.Locate(new SheetPoint(0.1, 0.1002), [new SheetSegment(0, 0.1, 0.2, 0.1)], []));
        Equal("", ToleranceSpec.Locate(new SheetPoint(0.1, 0.11), [new SheetSegment(0, 0.1, 0.2, 0.1)], []));
        Equal("40 +0.02/-0.01", AnnotationRows.Readable("40 + - 0.02 0.01"));
        Equal("A", ToleranceSpec.DatumLabel("a"));
        Throws(() => ToleranceSpec.DatumLabel("A1"), "基准字母写错");
    }
}
