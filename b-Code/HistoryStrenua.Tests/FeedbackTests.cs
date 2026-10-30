using HistoryStrenua;

/// <summary>离线测试：1.24.0 回执质量（报错定位、引线、无改动合并、列表过滤、未注倒角）。</summary>
internal static partial class Tests
{
    /// <summary>报错带 SolidWorks 调用与本模块位置；局部函数、lambda 只留外层方法名。</summary>
    static void TestQuickError()
    {
        const string stack = """
               at System.RuntimeMethodHandle.InvokeMethod(Object target, Span`1& arguments, Signature sig, Boolean isConstructor)
               at HistoryStrenua.SolidWorks.SolidWorksApi.CallCore(Object target, String interfaceName, String method, CancellationToken cancellation, Object[] arguments) in F:\x\SolidWorksApi.cs:line 80
               at HistoryStrenua.SymmetryAxes.DisplayLines(SolidWorksApi api, Object annotation) in F:\x\SymmetryAxes.cs:line 119
               at HistoryStrenua.SymmetryAxes.Insert(SolidWorksApi api, ScannedView scan, IReadOnlyList`1 axes)
               at HistoryStrenua.OneKeyDrawing.<>c__DisplayClass5_0.<Run>b__0()
               at HistoryStrenua.StepLog.Run(String title, Func`1 run)
            """;
        Equal("SymmetryAxes.DisplayLines:119 ← SymmetryAxes.Insert ← OneKeyDrawing.Run", string.Join(" ← ", QuickError.Frames(stack)));

        var ex = new System.Reflection.TargetException("Object does not match target type.");
        False(QuickError.TagCall(ex, "IAnnotation", "GetDisplayData"));
        QuickError.TagCall(ex, "IModelDoc2", "ClearSelection2");
        var text = QuickError.Describe(ex);
        True(text.StartsWith("TargetException: Object does not match target type.（调用 IAnnotation.GetDisplayData", StringComparison.Ordinal), text);
        True(QuickError.Unexpected(ex) && !QuickError.Unexpected(new OperationCanceledException()) && !QuickError.Unexpected(new QuickCommandException("x")), "哪些算非预期");
    }

    /// <summary>引线：认引线、量长度、交叉；注解重叠报过长与交叉，中心符号线不算。</summary>
    static void TestLeaders()
    {
        // 孔标注：水平下划线 + 斜引线 + 箭头短笔画。
        SheetSegment[] callout = [new(0.050, 0.090, 0.075, 0.090), new(0.075, 0.090, 0.0777, 0.1293), new(0.0777, 0.1293, 0.0770, 0.1283)];
        var leaders = LeaderPlanner.Leaders(callout);
        Equal(1, leaders.Count);
        True(Math.Abs(LeaderPlanner.Length(leaders) - Math.Sqrt(0.0027 * 0.0027 + 0.0393 * 0.0393)) < 1e-9, "引线长度只算斜的那段");
        True(LeaderPlanner.Cross(new(0, 0, 0.01, 0.01), new(0, 0.01, 0.01, 0)), "X 形相交");
        False(LeaderPlanner.Cross(new(0, 0, 0.01, 0.01), new(0.01, 0.01, 0.02, 0)), "首尾相接不算");
        False(LeaderPlanner.Cross(new(0, 0, 0.01, 0), new(0, 0.001, 0.01, 0.001)), "平行不算");

        CheckedAnnotation Callout(int index, string name, double x, double y, double tipX, double tipY)
            => new(index, "工程图视图1", name, AnnotationCheckPlanner.CalloutKind,
                [new SheetSegment(x, y, x + 0.025, y), new SheetSegment(x + 0.025, y, tipX, tipY)], [new TextBox(x, y, 0.025, 0.0035)]);

        // 两个孔标注：A 引线 55 mm 长且与 B 的引线交叉；B 的引线横穿一个尺寸的界线；中心符号线不算。
        var a = Callout(0, "RD8", 0.080, 0.075, 0.0597, 0.1293);
        var b = Callout(1, "RD10", 0.035, 0.075, 0.0797, 0.1203);
        var dimension = new CheckedAnnotation(2, "工程图视图1", "RD6", "尺寸", [new SheetSegment(0.070, 0.090, 0.070, 0.140)], [new TextBox(0.066, 0.140, 0.004, 0.0035)]);
        var mark = new CheckedAnnotation(3, "工程图视图1", "细节项目842", "中心符号线", [new SheetSegment(0.040, 0.100, 0.140, 0.100)], []);
        var issues = AnnotationCheckPlanner.Overlaps([a, b, dimension, mark], [], [], [], null);
        var all = string.Join(" | ", issues.Select(i => i.Index + ":" + i.Reason));
        True(issues.Any(i => i.Index == 0 && i.Reason.StartsWith("引线过长", StringComparison.Ordinal)), all);
        Equal(1, issues.Count(i => i.Reason.Contains("的引线交叉", StringComparison.Ordinal)));
        True(issues.Any(i => i.Index == 1 && i.Other == 2 && i.Reason.StartsWith("引线横穿", StringComparison.Ordinal)), all);
        True(!issues.Any(i => i.Other == 3), "中心符号线不算：" + all);

        // 孔旁的短引线：干净。
        var near = Callout(0, "RD9", 0.040, 0.120, 0.0672, 0.1293);
        True(AnnotationCheckPlanner.Overlaps([near], [], [], [], null).Count == 0, "短引线不报");
    }

    /// <summary>SVG：孔标注画引线、给总长（挪过的也给）；中心符号线只给外框。</summary>
    static void TestSvgLeaders()
    {
        var view = new SvgView("工程图视图1", "模型", "", "1:2", new SheetRect(0.05, 0.1, 0.15, 0.2), [], [], [],
        [
            new SvgAnnotation("RD8", "孔标注", "4 x Ø 11", "", new SheetRect(0.05, 0.09, 0.08, 0.13), new SheetPoint(0.05, 0.09), 0.0035, "H5 (59.7,129.3)",
                [new SheetSegment(0.075, 0.090, 0.075, 0.120)], new SheetRect(0.05, 0.09, 0.075, 0.0935)),
            new SvgAnnotation("RD9", "孔标注", "2 x Ø 6", "", new SheetRect(0.05, 0.09, 0.08, 0.13), null, 0, "",
                [new SheetSegment(0.075, 0.090, 0.075, 0.110)], new SheetRect(0.06, 0.09, 0.075, 0.0935), Change: "moved"),
            new SvgAnnotation("细节项目842", "中心符号线", "", "", null, null, 0, "",
                [new SheetSegment(0.0597, 0.1293, 0.0622, 0.1293), new SheetSegment(0.0597, 0.1268, 0.0597, 0.1318), new SheetSegment(0.0647, 0.1293, 0.0797, 0.1293)]),
        ]);
        var svg = DrawingSvgWriter.Write(new SvgSheet("t", "图纸1", 0.297, 0.210, null, [], [view]));
        True(svg.Contains("id=\"RD8\" data-kind=\"孔标注\" data-leader=\"30\"", StringComparison.Ordinal), svg);
        True(svg.Contains("<path d=\"M75 90L75 120\"/>", StringComparison.Ordinal), "引线画出来");
        True(svg.Contains("id=\"RD9\" data-kind=\"孔标注\" data-change=\"moved\" data-leader=\"20\"><rect", StringComparison.Ordinal)
             && svg.Contains("<path d=\"M75 90L75 110\"/></g>", StringComparison.Ordinal), "挪过的给新框与引线：" + svg);
        True(svg.Contains("id=\"细节项目842\" data-kind=\"中心符号线\" data-box=\"59.7,126.8,79.7,131.8\"/>", StringComparison.Ordinal), "中心符号线只给外框：" + svg);
    }

    /// <summary>「没有」合并：多视图汇总里全无改动只一行；标注列表滤掉图框格子与图框外。</summary>
    static void TestIdleAndSheetPlace()
    {
        var idle = QuickOutcome.Nothing("视图「V」里没有孔。");
        True(idle.Success && idle.Idle, "Nothing 是成功且无改动");
        False(QuickOutcome.Ok("x").Idle, "Ok 不是无改动");

        var space = SheetSpace.Standard(0.297, 0.210);
        Dictionary<string, string> Row(string text, double x, double y) => new()
        {
            ["text"] = text, ["x"] = QuickArgs.Format(x), ["y"] = QuickArgs.Format(y),
            ["left"] = string.Empty, ["bottom"] = string.Empty, ["right"] = string.Empty, ["top"] = string.Empty,
        };
        var titleBlock = space.KeepOuts[0].Center;
        Equal("标题栏等格子里的", DimList.SheetPlace(Row("±0.5", titleBlock.X, titleBlock.Y), space));
        Equal("图框外的", DimList.SheetPlace(Row("技术要求 / 1、…", 0.350, 0.180), space));
        Equal("空注释", DimList.SheetPlace(Row(string.Empty, 0.100, 0.100), space));
        Equal<string?>(null, DimList.SheetPlace(Row("技术要求 / 1、…", 0.150, 0.150), space));
    }

    /// <summary>未标尺寸：技术要求里的未注倒角 / 圆角；C1 倒角的内边不报。</summary>
    static void TestUnnotedChamfer()
    {
        var (chamfer, fillet) = DimensionCheckPlanner.Unnoted("技术要求\n5、未注倒角C1，未注圆角R1，螺纹孔及沉孔顶部倒角C0.5~C1；");
        Near(0.00105, Math.Round(chamfer!.Value, 9));
        Near(0.00105, Math.Round(fillet!.Value, 9));
        Near(0.00055, Math.Round(DimensionCheckPlanner.Unnoted("2.未注倒角0.5*45°,未注圆角R1").Chamfer!.Value, 9));
        True(DimensionCheckPlanner.Unnoted("技术要求 1、去毛刺").Chamfer is null, "没写不猜");

        // 侧视：板厚 15，上边 C1 倒角（斜边 1×1），厚度尺寸量的是倒角外的那条上边，倒角内边（y = 14）不该报。
        var frame = new ViewFrame(new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1), new SheetPoint(0, 0), 1);
        SheetSegment[] lines =
        [
            new(0, 0, 0.2, 0),            // 0 下
            new(0, 0, 0, 0.014),          // 1 左
            new(0.2, 0, 0.2, 0.014),      // 2 右
            new(0, 0.014, 0.2, 0.014),    // 3 倒角内边
            new(0.001, 0.015, 0.199, 0.015), // 4 上
            new(0, 0.014, 0.001, 0.015),  // 5 倒角
            new(0.2, 0.014, 0.199, 0.015),// 6 倒角
        ];
        var dimensions = new List<ViewDimension>
        {
            new(0, 2, false, 0.015, [DimensionAnchor.Line(lines[0], true), DimensionAnchor.Line(lines[4], true)]),
            new(1, 2, false, 0.2, [DimensionAnchor.Line(lines[1], true), DimensionAnchor.Line(lines[2], true)]),
        };
        var view = new CheckView("下视", "PART|默认", frame, lines, [], [], dimensions, []);
        var missing = DimensionCheckPlanner.Check([view]);
        True(missing.Count == 0, string.Join(" | ", missing.Select(item => item.Text)));
        var strict = DimensionCheckPlanner.Check([view], chamfer: 0.0005);
        True(strict.Any(item => item.Text.StartsWith("水平边", StringComparison.Ordinal) && item.Indexes.Contains(3)), "未注倒角写 C0.5 时 C1 倒角的内边要报");
    }

    static void False(bool condition, string message = "期望为假")
        => True(!condition, message);
}
