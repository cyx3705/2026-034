using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>细粒度指令（1.20.0）：视图、单个注解。</summary>
internal static partial class Tests
{
    static void TestFineRegistration()
    {
        var registry = Registry();
        foreach (var command in FineCommands.All)
        {
            True(registry.TryGet(command.CommandName, out var descriptor), $"未注册 {command.CommandName}");
            Equal(command.Readonly, descriptor!.Readonly);
            True(descriptor.HiddenReason is null, $"{command.CommandName} 要能经 MCP 调");
            True(command.CommandClass is "view" or "dim", $"{command.CommandName} 的类");
            True(command.Summary.Length <= 120, $"{command.CommandName} Summary 超过 120 字");
            True(QuickCommands.All.All(quick => quick.CommandName != command.CommandName), "细粒度指令不上页面");

            // 例子照抄能解析：只用声明过的参数、带全部必填参数，Bind 不抛。
            var values = ExampleValues(command.Example, command.CommandName);
            var declared = command.Parameters.Select(parameter => parameter.Name).ToHashSet();
            True(values.Keys.All(declared.Contains), $"{command.Example} 用了没声明的参数");
            True(command.Parameters.Where(parameter => parameter.Required).All(parameter => values.ContainsKey(parameter.Name)), $"{command.Example} 缺必填参数");
            for (var i = 0; i < command.Parameters.Count; i++)
                Equal(i, command.Parameters[i].Position);
            command.Bind(new CommandContext(descriptor, values, "test", null, default));
        }

        True(registry.TryGet("strenua.view.list", out var list) && list!.Readonly, "视图列表只读");
        True(registry.TryGet("strenua.dim.delete", out var delete) && !delete!.Readonly, "删注解改图");
    }

    static void TestFineArguments()
    {
        Equal(new MoveRequest(true, 120, null), Move(("x", "120")));
        Equal(new MoveRequest(false, -5.5, 3), Move(("dx", "-5.5"), ("dy", "3mm")));
        Throws(() => Move(("x", "1"), ("dy", "2")), "x/y 与 dx/dy 不能混用");
        Throws(() => Move(), "缺位置");
        Throws(() => Move(("x", "abc")), "不是数");

        var current = new SheetPoint(0.100, 0.200);
        Equal(new SheetPoint(0.120, 0.200), FineArgs.Target(new MoveRequest(true, 120, null), current));
        var moved = FineArgs.Target(new MoveRequest(false, -10, 5), current);
        True(Math.Abs(moved.X - 0.090) < 1e-12 && Math.Abs(moved.Y - 0.205) < 1e-12, "相对挪动");

        Equal<double?>(0.02, FineArgs.Tolerance("0.02"));
        Equal<double?>(0.05, FineArgs.Tolerance("±0.05"));
        Equal<double?>(null, FineArgs.Tolerance("none"));
        Throws(() => FineArgs.Tolerance("-1"), "负公差");

        Equal("D1|D3|RD2", string.Join("|", FineArgs.Names("D1, D3，RD2、D1", "names")));
        Throws(() => FineArgs.Names(" , ", "names"), "空名字");
        Equal("12.5", FineArgs.Format(0.0125));
        Equal("(100, 200)", FineArgs.Format(current));

        Equal(0, MoveView.Missed(new SheetPoint(0.1, 0.2), new SheetPoint(0.100004, 0.2)).Count);
        Equal("Y", string.Join("", MoveView.Missed(new SheetPoint(0.1, 0.2), new SheetPoint(0.1, 0.15))));

        Equal("线性尺寸", DimList.DimensionKind(11));
        Equal("坐标尺寸", DimList.DimensionKind(1));
        Equal("技术要求 / 1. 未注圆角R1", DimList.Flatten("技术要求\r\n  1. 未注圆角R1\r\n"));
        Equal(81, DimList.Flatten(new string('字', 100)).Length);
    }

    /// <summary>参数填错时不附着 SolidWorks，直接回失败。</summary>
    static void TestFineBadArguments()
    {
        var registry = Registry();
        True(registry.TryGet("strenua.dim.move", out var move), "缺挪注解");
        var result = move!.Handler(new CommandContext(move, new Dictionary<string, string> { ["view"] = "工程图视图1", ["name"] = "D1" }, "test", null, default))
            .GetAwaiter().GetResult();
        True(!result.Success && result.Message.Contains("缺位置", StringComparison.Ordinal), result.Message);

        True(registry.TryGet("strenua.dim.edit", out var edit), "缺改尺寸");
        result = edit!.Handler(new CommandContext(edit, new Dictionary<string, string> { ["view"] = "V", ["name"] = "D1" }, "test", null, default))
            .GetAwaiter().GetResult();
        True(!result.Success && result.Message.Contains("没有要改的", StringComparison.Ordinal), result.Message);
    }

    static MoveRequest Move(params (string Key, string Value)[] values)
    {
        var descriptor = MoveView.Command;
        var registry = Registry();
        registry.TryGet(descriptor.CommandName, out var command);
        return FineArgs.Move(new CommandContext(command!, values.ToDictionary(pair => pair.Key, pair => pair.Value), "test", null, default));
    }

    static void Throws(Action action, string what)
    {
        try
        {
            action();
        }
        catch (FineArgumentException)
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
