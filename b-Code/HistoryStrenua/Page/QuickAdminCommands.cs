using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>登记表到总线的转换，以及管指令本身的 <c>strenua.quick.*</c>：列表、取回执、耗时、按 key 执行、取消。</summary>
internal static class QuickAdminCommands
{
    /// <summary>登记表一行对应的总线指令。</summary>
    public static CommandDescriptor Descriptor(QuickCommand command, QuickCommandRunner runner) => new()
    {
        Name = command.CommandName,
        Domain = StrenuaIdentity.Domain,
        CommandClass = command.CommandClass,
        Summary = $"PowerSW「{command.Title}」：{command.Summary}",
        Example = command.Example ?? command.CommandName,
        Parameters = command.Arguments.Select((parameter, position) => parameter.ToSpec(position)).ToArray(),
        Level = CommandLevel.Run,
        Readonly = command.Readonly,
        Handler = context => runner.RunAsync(command, context),
    };

    public static IEnumerable<CommandDescriptor> All(QuickCommandRunner runner)
    {
        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.list",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "列出 PowerSW 全部快捷指令的 key、状态与上次结果的第一行（全文用 strenua.quick.last）",
            Example = StrenuaIdentity.Domain + ".quick.list",
            Readonly = true,
            Handler = CommandDescriptor.Sync(_ => CommandResult.Ok(ListText(runner), ListRows(runner))),
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.last",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "取回一条快捷指令上次执行的完整回执（文字与数据，含本步反馈 SVG）；长指令经 MCP 转发超时后用它补拿结果",
            Example = StrenuaIdentity.Domain + ".quick.last key=" + QuickCommands.All[0].Key,
            Readonly = true,
            Parameters =
            [
                KeyParameter("快捷指令的 key（strenua.quick.list 的 id 列），如 hole-flow"),
                new ParameterSpec
                {
                    Name = "back",
                    Description = "往前第几次的回执：省略或 0 为最近一次，1 为上一次；每条指令留最近 20 次",
                    Required = false,
                    Position = 1,
                },
            ],
            // 经 MCP 来的还在跑就等它（完成立刻返回），调用方不用自己估时间、反复轮询。
            Handler = context =>
            {
                var key = context.RequireString("key").Trim();
                var ai = CommandOrigin.IsAi(context.Source);
                return runner.LastAsync(key,
                    int.TryParse(context.GetString("back"), out var back) && back > 0 ? back : 0,
                    ai ? QuickCommandRunner.LastWaitSeconds : 0,
                    ai ? QuickCommands.All.FirstOrDefault(command => command.Key == key)?.PageClass : null);
            },
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.perf",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "取回一条快捷指令上次执行的耗时明细：总用时、SolidWorks 调用次数、进度时间线、最耗时的方法",
            Example = StrenuaIdentity.Domain + ".quick.perf key=" + QuickCommands.All[0].Key,
            Readonly = true,
            Parameters = [KeyParameter("快捷指令的 key（strenua.quick.list 的 id 列），如 hole-flow")],
            Handler = CommandDescriptor.Sync(context => runner.Perf(context.RequireString("key").Trim())),
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.run",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "按 key 执行一条 PowerSW 快捷指令（key 见 strenua.quick.list）；带参数的照写它的参数，如 key=view-move view=工程图视图1 dx=10",
            Example = StrenuaIdentity.Domain + ".quick.run key=" + QuickCommands.All[0].Key,
            Level = CommandLevel.Run,
            // 带参数的快捷指令经这里执行时，参数原样透传给它（各自解析、各自报错）。
            AllowUnspecifiedParameters = true,
            Parameters = [KeyParameter("快捷指令的 key（strenua.quick.list 返回行的 id 列）")],
            Handler = context =>
            {
                var key = context.RequireString("key").Trim();
                var command = QuickCommands.All.FirstOrDefault(item => item.Key == key);
                return command is null
                    ? Task.FromResult(CommandResult.Fail($"没有 key={key} 的快捷指令"))
                    : runner.RunAsync(command, context);
            },
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.cancel",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "取消正在执行的 PowerSW 快捷指令（包括正在等你点视图的那一条）",
            Example = StrenuaIdentity.Domain + ".quick.cancel",
            Handler = CommandDescriptor.Sync(_ => runner.Cancel()),
        };
    }

    private static ParameterSpec KeyParameter(string description) => new()
    {
        Name = "key",
        Description = description,
        Required = true,
        Position = 0,
        AllowedValues = QuickCommands.All.Select(command => command.Key).ToArray(),
    };

    /// <summary>
    /// <c>strenua.quick.list</c> 的数据：一条快捷指令一行。不带用法和整段结果（真机一次调用上万字全是噪声）——
    /// 用法看自描述（<c>diana.docs.read domain=strenua</c>），结果只留第一行，全文用 <c>strenua.quick.last key=</c> 取。
    /// </summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> ListRows(QuickCommandRunner runner)
        => QuickCommands.All
            .Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["id"] = command.Key,
                    ["title"] = command.Title,
                    ["class"] = command.ClassTitle,
                    ["state"] = status.State,
                    ["result"] = FirstLine(status.Result),
                    ["time"] = QuickRunLog.FormatTime(status.FinishedAt),
                };
            })
            .ToList();

    /// <summary>结果的第一行，最多 60 字。</summary>
    internal static string FirstLine(string result)
    {
        var line = result.Split(['\r', '\n'], 2)[0];
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    private static string ListText(QuickCommandRunner runner)
        => string.Join(
            Environment.NewLine,
            QuickCommands.All.Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                var result = status.Result.Length == 0 ? string.Empty : " — " + FirstLine(status.Result);
                return $"{command.Key}  {command.Title}  [{status.State}]{result}";
            })
            .Append($"避障：{(runner.Options.Clearance ? "开" : "关")}；尺寸链：{(runner.Options.Chain ? "开" : "关")}；默认技术要求：{runner.Options.TechDefault}；"
                + $"AI 填写技术要求：{(runner.Options.TechAi ? "开" : "关")}"));
}
