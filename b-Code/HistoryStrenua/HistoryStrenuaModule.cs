using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;

namespace HistoryStrenua;

/// <summary>
/// 模块装配入口：登记页面协议两条指令、每条快捷指令一条总线指令，外加列表、按 key 执行、取消与两个页面开关。
/// </summary>
/// <remarks>
/// 判断一段代码该不该进这个仓，用这条：它是否作用于用户**正在用**的 SolidWorks、
/// 让一次手工操作变成一下点击。批量转换、离线处理文件属于 HistoryMinerva。
/// </remarks>
public sealed class HistoryStrenuaModule : IModuleContextAware
{
    private const string Hidden = "Aurora 页面内部协议，不对远程消费面暴露";

    /// <summary>宿主在装载时注入权威指令总线与命令注册器。</summary>
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new StrenuaOptions(Path.Combine(context.Environment.DataDirectory, StrenuaOptions.FileName));
        var runner = new QuickCommandRunner(options);
        context.RegisterCommands(registry => Register(registry, runner));
    }

    internal static void Register(ICommandRegistrar registry, QuickCommandRunner runner)
    {
        foreach (var command in QuickCommands.All)
        {
            registry.Register(new CommandDescriptor
            {
                Name = command.CommandName,
                Domain = StrenuaIdentity.Domain,
                CommandClass = command.CommandClass,
                Summary = $"PowerSW「{command.Title}」：{command.Summary}",
                Example = command.CommandName,
                Level = CommandLevel.Run,
                Handler = context => runner.RunAsync(command, context),
            });
        }

        registry.Register(new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.list",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "列出 PowerSW 全部快捷指令及其上次执行结果",
            Example = StrenuaIdentity.Domain + ".quick.list",
            Readonly = true,
            Handler = CommandDescriptor.Sync(_ => CommandResult.Ok(ListText(runner), ListRows(runner))),
        });

        registry.Register(new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.run",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "按 key 执行一条 PowerSW 快捷指令（key 见 strenua.quick.list）",
            Example = StrenuaIdentity.Domain + ".quick.run key=" + QuickCommands.All[0].Key,
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "key",
                    Description = "快捷指令的 key（strenua.quick.list 返回行的 id 列）",
                    Required = true,
                    Position = 0,
                    AllowedValues = QuickCommands.All.Select(command => command.Key).ToArray(),
                },
            ],
            Handler = context =>
            {
                var key = context.RequireString("key").Trim();
                var command = QuickCommands.All.FirstOrDefault(item => item.Key == key);
                return command is null
                    ? Task.FromResult(CommandResult.Fail($"没有 key={key} 的快捷指令"))
                    : runner.RunAsync(command, context);
            },
        });

        registry.Register(new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.cancel",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "取消正在执行的 PowerSW 快捷指令（包括正在等你点视图的那一条）",
            Example = StrenuaIdentity.Domain + ".quick.cancel",
            Handler = CommandDescriptor.Sync(_ => runner.Cancel()),
        });

        registry.Register(Internal("describe", "返回 PowerSW 页面描述", _ => Json(StrenuaPage.Describe(runner.Options))));
        registry.Register(Internal("actions", "返回 PowerSW 页面动作声明", _ => Json(StrenuaPage.Actions())));
        registry.Register(Option(StrenuaOption.Clearance, "clearance", "避障",
            "页面「避障」开关：开着时孔标注、孔位尺寸加完后把压在别的孔相关注解上的文字挪开（默认开）", runner.Options));
        registry.Register(Option(StrenuaOption.Chain, "chain", "尺寸链",
            "页面「尺寸链」开关：开着时孔位尺寸改用 SW 原生尺寸链，每个方向全部孔一条链，不分种、不用阵列（默认关）", runner.Options));
    }

    /// <summary>
    /// 一个页面开关的指令：带 value 就改并记到本机，不带就报当前值。页面开关拨动走的就是它。
    /// </summary>
    private static CommandDescriptor Option(StrenuaOption option, string method, string title, string summary, StrenuaOptions options)
        => new()
        {
            Name = StrenuaIdentity.Domain + ".option." + method,
            Domain = StrenuaIdentity.Domain,
            CommandClass = "option",
            Summary = summary,
            Example = StrenuaIdentity.Domain + ".option." + method + " value=true",
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "value",
                    Description = "true 开、false 关；省略时只报当前值",
                    Required = false,
                    Position = 0,
                    AllowedValues = ["true", "false"],
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var text = context.GetString("value")?.Trim();
                if (string.IsNullOrEmpty(text))
                    return CommandResult.Ok($"PowerSW「{title}」当前{(options.Get(option) ? "开" : "关")}。");
                if (!bool.TryParse(text, out var value))
                    return CommandResult.Fail($"value 只能是 true 或 false：{text}");
                var failure = options.Set(option, value);
                var message = $"PowerSW「{title}」已{(value ? "打开" : "关闭")}。";
                return CommandResult.Ok(failure is null ? message : message + $"（未能记到本机，下次启动回到上次的值：{failure}）");
            }),
        };

    /// <summary><c>strenua.quick.list</c> 的数据：一条快捷指令一行。</summary>
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
                    ["usage"] = command.Usage,
                    ["state"] = status.State,
                    ["result"] = status.Result,
                    ["time"] = QuickCommandRunner.FormatTime(status.FinishedAt),
                };
            })
            .ToList();

    private static string ListText(QuickCommandRunner runner)
        => string.Join(
            Environment.NewLine,
            QuickCommands.All.Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                var result = status.Result.Length == 0 ? string.Empty : " — " + status.Result;
                return $"{command.CommandName}  {command.Title}  [{status.State}]{result}";
            })
            .Append($"避障：{(runner.Options.Clearance ? "开" : "关")}；尺寸链：{(runner.Options.Chain ? "开" : "关")}"));

    private static CommandResult Json(string json) => CommandResult.Ok(json, json);

    private static CommandDescriptor Internal(string method, string summary, Func<CommandContext, CommandResult> handler)
        => new()
        {
            Name = StrenuaIdentity.Domain + ".ui." + method,
            Domain = StrenuaIdentity.Domain,
            CommandClass = "ui",
            Summary = summary,
            Readonly = true,
            HiddenReason = Hidden,
            Handler = CommandDescriptor.Sync(handler),
        };
}
