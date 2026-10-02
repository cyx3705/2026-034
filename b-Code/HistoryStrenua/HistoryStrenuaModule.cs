using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;

namespace HistoryStrenua;

/// <summary>
/// 模块装配入口：登记页面协议三条指令、每条快捷指令一条总线指令，外加列表、按 key 执行与取消。
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
        var runner = new QuickCommandRunner(context.Bus);
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
            Handler = CommandDescriptor.Sync(_ => CommandResult.Ok(ListText(runner), StrenuaPage.Rows(runner))),
        });

        registry.Register(new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".quick.run",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "quick",
            Summary = "按 key 执行一条 PowerSW 快捷指令（页面指令表点名称走的就是这条）",
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

        registry.Register(Internal("describe", "返回 PowerSW 页面描述", _ => Json(StrenuaPage.Describe())));
        registry.Register(Internal("actions", "返回 PowerSW 页面动作声明", _ => Json(StrenuaPage.Actions())));
        registry.Register(new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".ui.data",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "ui",
            Summary = "返回 PowerSW 指令表的行（query 搜索词、class 类名筛选）",
            Readonly = true,
            HiddenReason = Hidden,
            AllowUnspecifiedParameters = true,
            Handler = CommandDescriptor.Sync(context =>
                context.GetString("view")?.Trim().ToLowerInvariant() is null or "commands"
                    ? CommandResult.Ok(
                        "PowerSW 快捷指令",
                        StrenuaPage.Rows(runner, context.GetString("query"), context.GetString("class")))
                    : CommandResult.Fail("未知 view；支持 commands")),
        });
    }

    private static string ListText(QuickCommandRunner runner)
        => string.Join(
            Environment.NewLine,
            QuickCommands.All.Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                var result = status.Result.Length == 0 ? string.Empty : " — " + status.Result;
                return $"{command.CommandName}  {command.Title}  [{status.State}]{result}";
            }));

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
