using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>页面协议指令（取数、描述、动作）与三个页面开关 <c>strenua.option.*</c>。</summary>
internal static class PageCommands
{
    private const string Hidden = "Aurora 页面内部协议，不对远程消费面暴露";

    public static IEnumerable<CommandDescriptor> All(StrenuaOptions options)
    {
        yield return new CommandDescriptor
        {
            Name = StrenuaPage.DataCommand,
            Domain = StrenuaIdentity.Domain,
            CommandClass = "ui",
            Summary = "返回 PowerSW 技术要求模板表格的行（view=tech）",
            Readonly = true,
            HiddenReason = Hidden,
            AllowUnspecifiedParameters = true,
            Handler = CommandDescriptor.Sync(context =>
            {
                var view = context.GetString("view")?.Trim().ToLowerInvariant();
                if (view is null or StrenuaPage.TechView)
                    return CommandResult.Ok("PowerSW 技术要求模板", TechApply.Rows(options.TechDefault));
                return PartProperties.OptionRows(view) is { } rows
                    ? CommandResult.Ok("PowerSW 属性候选", rows)
                    : CommandResult.Fail("未知 view；支持 tech、materials、surfaces、heats");
            }),
        };

        yield return Internal("describe", "返回 PowerSW 页面描述", _ => Json(StrenuaPage.Describe(options)));
        yield return Internal("actions", "返回 PowerSW 页面动作声明", _ => Json(StrenuaPage.Actions()));
        yield return Option(StrenuaOption.Clearance, "clearance", "避障",
            "页面「避障」开关：" + StrenuaPage.ClearanceSummary, options);
        yield return Option(StrenuaOption.Chain, "chain", "尺寸链",
            "页面「尺寸链」开关：" + StrenuaPage.ChainSummary, options);
        yield return Option(StrenuaOption.TechAi, "techai", "AI 填写技术要求",
            "页面「AI 填写技术要求」开关：" + StrenuaPage.TechAiSummary, options);
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
