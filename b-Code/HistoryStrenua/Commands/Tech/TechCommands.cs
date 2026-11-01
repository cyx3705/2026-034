using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 不进登记表的两条技术要求指令：<c>tech.apply</c>（模板名是表格里点的，不是按钮，现做一条交给同一个执行器）、
/// <c>tech.default</c>（只改本机设置，不碰 SolidWorks）。
/// </summary>
internal static class TechCommands
{
    public static IEnumerable<CommandDescriptor> All(QuickCommandRunner runner, ICommandBus? bus)
    {
        yield return new CommandDescriptor
        {
            Name = TechApply.CommandName,
            Domain = StrenuaIdentity.Domain,
            CommandClass = TechApply.CommandClass,
            Summary = "PowerSW「技术要求」模板：" + TechApply.Summary,
            Example = TechApply.CommandName + " name=机加件-钢材",
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "name",
                    Description = "模板名（「通用技术要求」目录里的文件名去掉 " + TechTemplates.Extension + "，页面模板表格「技术要求」列）",
                    Required = true,
                    Position = 0,
                },
            ],
            Handler = context => runner.RunAsync(TechApply.Command(context.RequireString("name").Trim()), context),
        };

        yield return new CommandDescriptor
        {
            Name = TechApply.DefaultCommandName,
            Domain = StrenuaIdentity.Domain,
            CommandClass = TechApply.CommandClass,
            Summary = "PowerSW「技术要求」默认模板：" + TechApply.DefaultSummary,
            Example = TechApply.DefaultCommandName + " name=机加件-钢材",
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "name",
                    Description = "模板名（页面模板表格「技术要求」列）；省略时只报当前默认",
                    Required = false,
                    Position = 0,
                },
            ],
            Handler = context => TechApply.SetDefaultAsync(runner.Options, context.GetString("name"), bus),
        };
    }
}
