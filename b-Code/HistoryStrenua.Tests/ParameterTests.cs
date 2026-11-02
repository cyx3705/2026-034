using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>带参数的快捷指令（1.20.0「视图」「标注」两类）。</summary>
internal static partial class Tests
{
    static readonly string[] ParameterClasses = ["prop", "view", "dim", "tol", "doc", "check"];

    static IEnumerable<QuickCommand> ParameterCommands()
        => QuickCommands.All.Where(command => command.Arguments.Count > 0 || command.Bind is not null);

    static void TestParameterRegistration()
    {
        var registry = Registry();
        var commands = ParameterCommands().ToList();
        Equal("check.svg,tech.add,tech.remove,view.list,view.move,dim.list,dim.delete,dim.move,dim.edit,dim.add,dim.hole,dim.center,tol.dim,tol.gtol,tol.datum,doc.open,doc.activate,doc.save,doc.close,prop.write",
            string.Join(",", commands.Select(command => command.CommandName["strenua.".Length..])));
        foreach (var command in commands)
        {
            True(registry.TryGet(command.CommandName, out var descriptor), $"未注册 {command.CommandName}");
            True(ParameterClasses.Contains(command.PageClass), $"{command.CommandName} 的类");
            Equal(command.Readonly, descriptor!.Readonly);
            True(descriptor.HiddenReason is null, $"{command.CommandName} 要能经 MCP 调");
            Equal(command.Arguments.Count, descriptor.Parameters.Count);
            True(descriptor.Summary.Length <= 120, $"{command.CommandName} Summary 超过 120 字：{descriptor.Summary.Length}");

            // 例子照抄能解析：只用声明过的参数、带全部必填参数，Bind 不抛。
            var example = descriptor.Example!;
            var values = ExampleValues(example, command.CommandName);
            var declared = command.Arguments.Select(parameter => parameter.Name).ToHashSet();
            True(values.Keys.All(declared.Contains), $"{example} 用了没声明的参数");
            True(command.Arguments.Where(parameter => parameter.Required).All(parameter => values.ContainsKey(parameter.Name)), $"{example} 缺必填参数");
            command.Bind!(new CommandContext(descriptor, values, "test", null, default));

            // 页面传上来的是全部输入框，没填的是空串：等于没给（列表类照样能跑）。
            var blank = command.Arguments.ToDictionary(parameter => parameter.Name, _ => string.Empty);
            foreach (var (key, value) in values)
                blank[key] = value;
            command.Bind(new CommandContext(descriptor, blank, "test", null, default));
        }

        True(registry.TryGet("strenua.view.list", out var list) && list!.Readonly, "视图列表只读");
        True(registry.TryGet("strenua.dim.delete", out var delete) && !delete!.Readonly, "删标注改图");
        True(registry.TryGet("strenua.quick.run", out var run) && run!.AllowUnspecifiedParameters, "quick.run 透传参数");
    }

    /// <summary>用户定：和以前的指令待遇一样——上页面（按钮 + 输入框）、进列表。</summary>
    static void TestParameterPage()
    {
        using var actions = JsonDocument.Parse(StrenuaPage.Actions());
        var declared = actions.RootElement.GetProperty("actions").EnumerateArray()
            .ToDictionary(action => action.GetProperty("id").GetString()!, action => action);
        var rows = QuickAdminCommands.ListRows(new QuickCommandRunner(new StrenuaOptions()));
        foreach (var command in ParameterCommands())
        {
            True(rows.Any(row => row["id"] == command.Key), $"{command.Title} 应进 quick.list");
            var action = declared[command.ActionId];
            if (command.Arguments.Count == 0)
                continue;
            var args = action.GetProperty("args");
            foreach (var parameter in command.Arguments)
                Equal("{" + parameter.Name + "}", args.GetProperty(parameter.Name).GetString()!);
        }

        using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
        string Inputs(string commandClass)
        {
            var panel = Descendants(description.RootElement)
                .Single(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.ClassPanelId(commandClass));
            return string.Join("|", panel.GetProperty("rows").EnumerateArray()
                .Select(row => row.GetProperty("widgets").EnumerateArray().ToList())
                .Where(widgets => widgets.All(widget => widget.GetProperty("kind").GetString() == "textbox"))
                .Select(widgets => string.Join(",", widgets.Select(widget => widget.GetProperty("label").GetString()))));
        }

        // 同名参数共用一个框，按组分行：对象 / 位置 / 文字。
        Equal("材料,表面处理,热处理|日期,设计,保存", Inputs("prop"));
        // 每个按钮的动作只引用本面板有的框（Aurora：取不到的占位符整条拒绝执行）。
        foreach (var commandClass in ParameterClasses.Where(item => !QuickClasses.Hidden.Contains(item)))
        {
            var panel = Descendants(description.RootElement)
                .Single(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.ClassPanelId(commandClass));
            var boxes = Descendants(panel).Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() == "textbox")
                .Select(node => node.GetProperty("id").GetString()!).ToHashSet();
            foreach (var command in QuickCommands.All.Where(command => command.PageClass == commandClass))
                True(command.Arguments.All(parameter => boxes.Contains(parameter.Name)), $"{command.Title} 的参数在面板上没有框");
        }

        // 无参的类不长输入框。
        Equal(string.Empty, Inputs("hole"));
    }

    static void TestSplitHoles()
    {
        // 同心同半径、背对背的两段半圆：一个整圆孔；另外一对真腰型孔不受影响。
        HoleEdge[] edges =
        [
            new(0, 0.10, 0.10, 0.0035, "/导入", BulgeX: 1),
            new(1, 0.10, 0.10, 0.0035, "/导入", BulgeX: -1),
            new(2, 0.20, 0.10, 0.003, "/腰", BulgeX: -1),
            new(3, 0.23, 0.10, 0.003, "/腰", BulgeX: 1),
        ];
        var holes = HoleCalloutPlanner.Recognize(edges);
        Equal(1, holes.Count(hole => hole.Split));
        Equal(0, holes.Single(hole => hole.Split).Index);
        Equal(2, holes.Count(hole => hole.Slot >= 0));
        // 同向的两段不是一个孔（配不上腰型孔就丢掉，和以前一样）。
        Equal(0, HoleCalloutPlanner.Recognize([new(0, 0.1, 0.1, 0.003, "/a", BulgeX: 1), new(1, 0.1, 0.1, 0.003, "/a", BulgeX: 1)]).Count);

        var axis = new ModelDirection(0, 0, 1);
        PartCylinder Half(double z1, double z2) => new(axis, default, 0.0035, true, false, "导入", [], z1, z2);
        var part = new PartGeometry(new ModelBox(0, 0, 0, 1, 1, 1), [Half(0, 0.02), Half(0, 0.02)]);
        Equal(1, part.Holes.Count);
        Equal("4 x <MOD-DIAM>", SplitHole.Prefix(4));
        Equal("<MOD-DIAM>", SplitHole.Prefix(1));
    }

    static void TestFineToolArguments()
    {
        Equal(new GeometryRef(false, 3), GeometryRef.Parse("E3", "from"));
        Equal(new GeometryRef(true, 26), GeometryRef.Parse(" h26 ", "from"));
        Throws(() => GeometryRef.Parse("D1", "from"), "只认 E / H");
        Equal(new GeometryRef(false, 7, 2), GeometryRef.Parse("e7.2", "to"));
        Equal("E7.2", new GeometryRef(false, 7, 2).ToString());
        Equal(new GeometryRef(false, 3, 0, true), GeometryRef.Parse("c3", "to"));
        Equal(new GeometryRef(false, 3, 1, true), GeometryRef.Parse("C3.1", "to"));
        Equal("C3.1", new GeometryRef(false, 3, 1, true).ToString());
        True(DimAdd.SameEdge(new GeometryRef(false, 3, 0, true), new GeometryRef(false, 3, 1, true)), "圆心和自己的端点");
        True(!DimAdd.SameEdge(new GeometryRef(false, 3, 0, true), new GeometryRef(false, 3)), "曲线和直边编号碰巧相同");
        SheetCurve arc = new([new(0, 0.01, 0.003, 0.003), new(0.003, 0.003, 0.01, 0)], new SheetPoint(0.01, 0.01), 0.01);
        Equal<double?>(0.01, DimAdd.CurveCoordinate(arc, 0, horizontal: true));
        Equal<double?>(0.01, DimAdd.CurveCoordinate(arc, 1, horizontal: false));
        Equal<double?>(0, DimAdd.CurveCoordinate(arc, 2, horizontal: false));
        Equal<double?>(null, DimAdd.CurveCoordinate(arc with { Center = null }, 0, horizontal: true));
        Equal<double?>(null, DimAdd.CurveCoordinate(new SheetCurve(ClearancePlanner.Octagon(new SheetPoint(0, 0), 0.01).ToList(), new SheetPoint(0, 0), 0.01), 1, horizontal: true));
        Throws(() => GeometryRef.Parse("E7.3", "to"), "端点只有 1、2");
        Throws(() => GeometryRef.Parse("H5.1", "to"), "孔没有端点");
        True(DimAdd.SameEdge(new GeometryRef(false, 7, 1), new GeometryRef(false, 7, 2)), "同一条边的两端");
        True(DimAdd.SameEdge(new GeometryRef(false, 7), new GeometryRef(false, 7, 2)), "边和它自己的端点");
        True(!DimAdd.SameEdge(new GeometryRef(false, 5), new GeometryRef(false, 7, 2)), "线到别的线的端点");
        True(!DimAdd.SameEdge(new GeometryRef(true, 7), new GeometryRef(false, 7)), "孔和直边编号碰巧相同");
        Throws(() => GeometryRef.Parse("H", "from"), "要带序号");
        // 直边要和量的方向垂直才有坐标：竖直边量水平有 X，量竖直没有。
        var vertical = new SheetSegment(0.06, 0.08, 0.06, 0.2);
        Equal<double?>(0.06, GeometryRef.Coordinate(vertical, horizontal: true));
        Equal<double?>(null, GeometryRef.Coordinate(vertical, horizontal: false));
        True(DimAdd.Axis("h") && !DimAdd.Axis("竖直"), "方向");
        Throws(() => DimAdd.Axis("x"), "方向写错");
        Equal(3, DocOpen.TypeOf(@"D:\a\图.SLDDRW"));
        Throws(() => DocOpen.TypeOf(@"D:\a\图.pdf"), "不是 SolidWorks 文档");
    }

    static void TestParameterArguments()
    {
        Equal(new MoveRequest(true, 120, null), Move(("x", "120")));
        Equal(new MoveRequest(false, -5.5, 3), Move(("dx", "-5.5"), ("dy", "3mm")));
        Equal(new MoveRequest(false, 4, null), Move(("x", ""), ("y", " "), ("dx", "4"), ("dy", "")));
        Throws(() => Move(("x", "1"), ("dy", "2")), "x/y 与 dx/dy 不能混用");
        Throws(() => Move(), "缺位置");
        Throws(() => Move(("x", "abc")), "不是数");

        var current = new SheetPoint(0.100, 0.200);
        Equal(new SheetPoint(0.120, 0.200), QuickArgs.Target(new MoveRequest(true, 120, null), current));
        var moved = QuickArgs.Target(new MoveRequest(false, -10, 5), current);
        True(Math.Abs(moved.X - 0.090) < 1e-12 && Math.Abs(moved.Y - 0.205) < 1e-12, "相对挪动");

        Equal<double?>(0.02, QuickArgs.Tolerance("0.02"));
        Equal<double?>(0.05, QuickArgs.Tolerance("±0.05"));
        Equal<double?>(null, QuickArgs.Tolerance("none"));
        Throws(() => QuickArgs.Tolerance("-1"), "负公差");

        Equal<string?>(null, QuickArgs.Text(Context(("prefix", "")), "prefix"));
        Equal<string?>(string.Empty, QuickArgs.Text(Context(("prefix", "none")), "prefix"));
        Equal<string?>("2 x C", QuickArgs.Text(Context(("prefix", "2 x C")), "prefix"));
        True(QuickArgs.Flag(Context(("unalign", "是")), "unalign") && !QuickArgs.Flag(Context(("unalign", "否")), "unalign")
             && !QuickArgs.Flag(Context(("unalign", "")), "unalign") && QuickArgs.Flag(Context(("unalign", "true")), "unalign"), "是 / 否");
        Throws(() => QuickArgs.Flag(Context(("unalign", "也许")), "unalign"), "是否以外的值");

        Equal("D1|D3|RD2", string.Join("|", QuickArgs.Names("D1, D3，RD2、D1", "name")));
        Throws(() => QuickArgs.Names(" , ", "name"), "空名字");
        Equal("12.5", QuickArgs.Format(0.0125));
        Equal("(100, 200)", QuickArgs.Format(current));

        Equal(0, SheetLookup.Missed(new SheetPoint(0.1, 0.2), new SheetPoint(0.100004, 0.2)).Count);
        Equal("Y", string.Join("", SheetLookup.Missed(new SheetPoint(0.1, 0.2), new SheetPoint(0.1, 0.15))));

        Equal("线性尺寸", AnnotationRows.DimensionKind(11));
        Equal("坐标尺寸", AnnotationRows.DimensionKind(1));
        Equal("技术要求 / 1. 未注圆角R1", AnnotationRows.Flatten("技术要求\r\n  1. 未注圆角R1\r\n"));
        Equal(81, AnnotationRows.Flatten(new string('字', 100)).Length);
    }

    /// <summary>参数填错时不附着 SolidWorks，直接回失败；经 quick.run 也一样。</summary>
    static void TestParameterBadArguments()
    {
        var registry = Registry();
        True(registry.TryGet("strenua.dim.move", out var move), "缺挪标注");
        var result = move!.Handler(new CommandContext(move, new Dictionary<string, string> { ["view"] = "工程图视图1", ["name"] = "D1" }, "test", null, default))
            .GetAwaiter().GetResult();
        True(!result.Success && result.Message.Contains("缺位置", StringComparison.Ordinal), result.Message);

        True(registry.TryGet("strenua.dim.edit", out var edit), "缺改尺寸");
        result = edit!.Handler(new CommandContext(edit, new Dictionary<string, string> { ["view"] = "V", ["name"] = "D1", ["prefix"] = "", ["tol"] = "" }, "test", null, default))
            .GetAwaiter().GetResult();
        True(!result.Success && result.Message.Contains("没有要改的", StringComparison.Ordinal), result.Message);

        True(registry.TryGet("strenua.quick.run", out var run), "缺 quick.run");
        result = run!.Handler(new CommandContext(run, new Dictionary<string, string> { ["key"] = "view-move", ["view"] = "工程图视图1" }, "test", null, default))
            .GetAwaiter().GetResult();
        True(!result.Success && result.Message.Contains("缺位置", StringComparison.Ordinal), result.Message);
    }

    static CommandContext Context(params (string Key, string Value)[] values)
    {
        var registry = Registry();
        registry.TryGet(ViewMove.Command.CommandName, out var descriptor);
        return new CommandContext(descriptor!, values.ToDictionary(pair => pair.Key, pair => pair.Value), "test", null, default);
    }

    static MoveRequest Move(params (string Key, string Value)[] values) => QuickArgs.Move(Context(values));

    static void Throws(Action action, string what)
    {
        try
        {
            action();
        }
        catch (QuickArgumentException)
        {
            return;
        }

        throw new InvalidOperationException($"应当拒绝：{what}");
    }

    static Dictionary<string, string> ExampleValues(string example, string name)
    {
        True(example.StartsWith(name, StringComparison.Ordinal), $"{example} 不以指令名开头");
        return example[name.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : string.Empty);
    }
}
