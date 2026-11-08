using HistoryStrenua;

/// <summary>1.43.0：异形孔向导的类型、规格写法与 HoleWizard5 数值段（真机试出来的规律）。</summary>
internal static partial class Tests
{
    static void TestHoleSpec()
    {
        Equal("沉头", HoleKind.Parse("沉孔").Name);
        Equal("锥形沉头", HoleKind.Parse("埋头孔").Name);
        Equal(359, HoleKind.Parse("螺纹孔").Fastener);
        Equal(708, HoleKind.Parse("销孔").Fastener);
        Throws(() => HoleKind.Parse("腰型孔"), "不认识的类型");
        // 锥形沉头先 ISO 10642（GB 沉头 M3–M5 孔库里没有），其余先 GB 再退 ISO。
        Equal("8:140,13:362", string.Join(",", HoleKind.Parse("锥形沉头").Candidates.Select(c => $"{c.Standard}:{c.Fastener}")));
        Equal("13:361,8:139", string.Join(",", HoleKind.Parse("沉头").Candidates.Select(c => $"{c.Standard}:{c.Fastener}")));

        Equal("M12", HoleSpec.NormalizeSize(HoleKind.Parse("沉头"), "m12"));
        Equal("M6", HoleSpec.NormalizeSize(HoleKind.Parse("通孔"), "6"));
        Equal("Ø6.0", HoleSpec.NormalizeSize(HoleKind.Parse("销孔"), "6"));
        Equal("Ø4.5", HoleSpec.NormalizeSize(HoleKind.Parse("钻孔"), "Ø4.5"));
        Throws(() => HoleSpec.NormalizeSize(HoleKind.Parse("螺纹"), "Mx"), "螺纹规格不对");

        var points = HoleSpec.ParsePoints("-20,-50,20; 20,-50,20；0,0,20");
        Equal(3, points.Count);
        Throws(() => HoleSpec.ParsePoints("1,2,3;1,2,3"), "重合孔心");
        Throws(() => HoleSpec.ParsePoints("1,2"), "少坐标");

        // 沉头：主孔、沉孔按标准（-1），配合写在 Value4。
        var cbore = new HoleSpec(HoleKind.Parse("沉头"), "M12", null, points).WizardValues();
        Equal(-1.0, cbore[0]);
        Equal(-1.0, cbore[1]);
        Equal(-1.0, cbore[3]);
        Equal(1.0, cbore[6]);
        var custom = new HoleSpec(HoleKind.Parse("沉头"), "M4", null, points, HoleFit.Normal, null, 8, 4.5).WizardValues();
        Equal(0.008, Math.Round(custom[3], 6));
        Equal(0.0045, Math.Round(custom[4], 6));

        // 普通孔：锥孔值必须是 0（-1 建不出特征），钻孔 / 销孔直径直接给。
        var dowel = new HoleSpec(HoleKind.Parse("销孔"), "Ø6.0", 12, points).WizardValues();
        Equal(0.006, dowel[0]);
        Equal(0.012, dowel[1]);
        Equal(1.0, dowel[3]);
        Equal(-1.0, dowel[4]);
        Equal(0.0, dowel[5]);
        Equal(0.0, dowel[9]);
        Equal(-1.0, dowel[10]);
        Equal(-1.0, new HoleSpec(HoleKind.Parse("通孔"), "M6", null, points).WizardValues()[0]);

        // 螺纹：盲孔螺纹深默认 2 倍公称直径（不超过底孔深），装饰螺纹 1，终止 0 盲 / 1 贯穿。
        var tap = new HoleSpec(HoleKind.Parse("螺纹"), "M6", 15, points).WizardValues();
        Equal(0.012, tap[3]);
        Equal(1.0, tap[9]);
        Equal(0.0, tap[10]);
        Equal(0.008, new HoleSpec(HoleKind.Parse("螺纹"), "M6", 8, points).WizardValues()[3]);
        var tapThrough = new HoleSpec(HoleKind.Parse("螺纹"), "M6", null, points);
        Equal(1, tapThrough.EndCondition);
        Equal(1.0, tapThrough.WizardValues()[10]);
        Equal("M6 螺纹（深 15）", new HoleSpec(HoleKind.Parse("螺纹"), "M6", 15, points).Title);
    }
}
