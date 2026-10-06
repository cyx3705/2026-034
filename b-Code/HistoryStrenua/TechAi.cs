using System.Text.Encodings.Web;
using System.Text.Json;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「AI 填写技术要求」（1.14.0，技术要求类，用户定）：AI 看当前图纸，从「通用技术要求」里选一份做基础（或判定没有合适的），
/// 再按这张图克制地增删几条，插进图框（标题栏上方或左侧，换掉原有的技术要求）。
/// </summary>
/// <remarks>
/// <para>两问，都经命令总线调 HistoryApollo 的 <c>apollo.chat.ask</c>（模块统一接口，本模块不引用 Apollo 的程序集）：</para>
/// <list type="number">
/// <item>选基础——输入：图纸截图、候选模板标题、图纸基本信息 JSON（<see cref="TechAiInfo"/>）；输出：哪份模板，或 null。</item>
/// <item>微调——输入：截图、基础的各条（带编号）、同一份 JSON；输出：删哪几条、在哪条后加哪几条（<see cref="TechAiEdit"/>，有上限）。</item>
/// </list>
/// <para>截图附在 <c>images=</c> 上：Apollo 0.5.0 起带图片的请求自动走识图供应商（DeepSeek 看不到图，见 Apollo 的现行约定）。
/// 用 <c>InvokeAsync</c>（安静执行）而不是 <c>ExecuteAsync</c>：提示词里整段 JSON 回显进控制台没人看得下去；
/// 模型怎么答的由 Apollo 自己逐轮写进控制台（它的进度通道在嵌套调用里也有），这里不复述。</para>
/// <para>页面「AI 填写技术要求」开关开着时，别处插技术要求也走这里：模板表格点哪一份都由 AI 重新选基础（用户选的），
/// 「一键出图」的技术要求一步也是。那两处 AI 没成（没配密钥、断网、答非所问）就退回原来那份模板，回执里说明——
/// 按钮本身没有可退的，直接失败。</para>
/// </remarks>
internal static class TechAi
{
    public static QuickCommand Command { get; } = new(
        Key: "tech-ai",
        CommandName: StrenuaIdentity.Domain + ".tech.ai",
        Title: "AI填写技术要求",
        Summary: "AI 看当前工程图选一份「通用技术要求」做基础（或判定没有合适的），再克制地增删几条，插到标题栏上方或左侧（换掉原有的）。",
        Usage: "切到要加技术要求的工程图再按：先把当前图纸页截一张图，连同图纸信息（材料、属性、特征类型、图幅比例）和模板标题交给 AI（经 HistoryApollo 的识图供应商）选基础；"
            + $"再让 AI 按这张图在基础上最多删 {TechAiEdit.MaxDelete} 条、加 {TechAiEdit.MaxAdd} 条（已有条目原样保留、不改写），没有合适基础时由 AI 写不超过 {TechAiEdit.MaxFresh} 条。"
            + "插的位置与换法同模板表格：标题栏上方或左侧，图框里已有技术要求就删掉换上。AI 的思考与答复逐轮进控制台。需要先在 HistoryApollo 配好识图供应商密钥（apollo.key.set provider=qwen token=…）。"
            + "下面开关「AI 填写技术要求」开着时，模板表格与「一键出图」插技术要求也走这条。",
        Run: context => Run(context));

    /// <summary>经总线调 Apollo 时的来源标签。</summary>
    public const string CommandSource = "module:" + StrenuaIdentity.Name;

    /// <summary>每一问的超时（秒）：识图模型看整页工程图偶尔要半分钟以上。</summary>
    private const int TimeoutSeconds = 180;

    private const string ChooseSystem =
        "你是机械设计工程师，负责给 SolidWorks 工程图选技术要求模板。看图纸截图与图纸基本信息 JSON，判断零件类别（机加件、焊接件、钣金、铝型材、装配图……）、"
        + "材料与工艺，从候选模板标题里选一份最贴近的作为基础；候选里没有这一类就选 null。"
        + "只输出一个 JSON 对象，不要别的文字：{\"base\": \"候选标题原文之一，或 null\", \"reason\": \"一句话理由\"}。";

    private const string EditSystem =
        "你是机械设计工程师，按这张工程图克制地修订一份技术要求。原则：只做必要的增删，已有条目不改写、不润色、不重排；"
        + "与这张图明显不符的条目才删（例如图上没有螺纹却写螺纹要求、材料或工艺不符的表面处理），图纸明显需要而缺失的才加；拿不准就不动，不需要改就给空数组。"
        + "最多删 3 条、加 3 条。新条目沿用原文的口吻与标点，不带编号，一条一句。"
        + "只输出一个 JSON 对象，不要别的文字：{\"delete\": [原编号], \"add\": [{\"after\": 原编号（0 表示放最前）, \"text\": \"条目正文\"}], \"reason\": \"一句话说明改了什么、为什么\"}。";

    private const string FreshSystem =
        "你是机械设计工程师，给这张工程图写技术要求。现有模板都不合适，请按图纸截图与图纸基本信息写简短的技术要求，不超过 10 条，"
        + "每条一句、不带编号，只写这张图确实需要的（公差、去毛刺倒钝、表面处理、材料或工艺要求等）。"
        + "只输出一个 JSON 对象，不要别的文字：{\"items\": [\"条目正文\"], \"reason\": \"一句话说明\"}。";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="drawing">工程图；null 时取活动工程图（「一键出图」传刚建的那张）。</param>
    /// <param name="fallback">AI 没成时退回插的模板名（模板表格点的那份、一键出图的默认）；null 表示不退，直接失败（按钮）。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? drawing = null, string? fallback = null)
    {
        drawing ??= HoleScan.ActiveDrawing(context);
        TechPlan? plan;
        string failure;
        try
        {
            (plan, failure) = Plan(context, drawing);
        }
        catch (QuickCommandException ex) when (fallback is not null)
        {
            // 截图没存出来之类：有退路就退，不让一键出图的这一步因此空着。
            (plan, failure) = (null, ex.Message);
        }

        if (plan is null)
        {
            if (fallback is null)
                return QuickOutcome.Fail("AI 填写技术要求：" + failure);
            context.Report($"AI 填写技术要求没成（{failure}），改插模板「{fallback}」。");
            var outcome = TechApply.Run(context, fallback, drawing);
            return outcome with { Message = $"{outcome.Message}（AI 没成：{failure}）" };
        }

        return TechApply.Insert(context, drawing, plan.Label, plan.Items.Text, plan.Items.Items.Count, plan.Summary);
    }

    /// <summary>AI 定下来的技术要求：名字（写进回执）、条目、给人看的改动说明。</summary>
    internal sealed record TechPlan(string Label, TechItems Items, string Summary);

    /// <summary>截图 + 读信息 + 两问。失败时返回原因（不抛 <see cref="QuickCommandException"/> 以外的东西）。</summary>
    private static (TechPlan? Plan, string Failure) Plan(QuickCommandContext context, object drawing)
    {
        var templates = TechTemplates.Load();
        if (templates.Count == 0)
            return (null, $"找不到模板目录或目录里没有 {TechTemplates.Extension}：{TechTemplates.Folder}");

        context.SetState("读图纸");
        var info = TechAiInfo.Read(context, drawing);
        var image = DrawingSnapshot.Save(context, drawing);
        context.Report($"AI 填写技术要求：图纸截图 {image}；图纸信息 {info}");

        context.SetState("AI 选基础");
        var names = templates.Select(template => template.Name).ToList();
        var choose = Ask(context, ChooseSystem, $"候选技术要求标题：{JsonSerializer.Serialize(names, Json)}。图纸基本信息 JSON：{info}", image);
        if (!choose.Success)
            return (null, "选基础时 Apollo 失败：" + FirstLine(choose.Message));
        var (valid, baseName, chooseReason) = TechAiEdit.ReadChoice(choose.Message, names);
        if (!valid)
            return (null, baseName is null ? "AI 选基础的答复读不出 {\"base\": …}：" + Clip(choose.Message) : $"AI 选的「{baseName}」不在模板里");

        var template = baseName is null ? null : templates.First(item => item.Name == baseName);
        var based = template is null ? TechItems.Empty : TechAiEdit.Parse(template.Text);
        context.SetState("AI 微调");
        var edit = template is null
            ? Ask(context, FreshSystem, $"图纸基本信息 JSON：{info}", image)
            : Ask(context, EditSystem, $"基础技术要求「{template.Name}」（编号：正文）：{JsonSerializer.Serialize(TechAiEdit.Numbered(based), Json)}。图纸基本信息 JSON：{info}", image);
        if (!edit.Success)
            return (null, "微调时 Apollo 失败：" + FirstLine(edit.Message));
        if (TechAiEdit.ReadEdit(edit.Message, based, fresh: template is null) is not { } read)
            return (null, "AI 微调的答复读不出 JSON：" + Clip(edit.Message));

        var (outcome, editReason) = read;
        if (outcome.Result.Items.Count == 0)
            return (null, template is null ? "没有合适的模板，AI 也没写出条目" : $"AI 把「{template.Name}」删空了");

        var label = template is null ? "AI 撰写" : $"{template.Name}（AI 微调）";
        return (new TechPlan(label, outcome.Result, Describe(template?.Name, chooseReason, outcome, editReason)), string.Empty);
    }

    /// <summary>回执里的改动说明：基础与理由、删了哪几条、加了哪几条、没采用几条。</summary>
    internal static string Describe(string? baseName, string chooseReason, TechEditOutcome outcome, string editReason)
    {
        var parts = new List<string>
        {
            baseName is null ? "没有合适的模板，由 AI 撰写" : $"以「{baseName}」为基础",
        };
        if (chooseReason.Length > 0)
            parts[0] += $"（{chooseReason}）";
        if (baseName is not null && outcome.Deleted.Count == 0 && outcome.Added.Count == 0)
            parts.Add("未增删");
        if (outcome.Deleted.Count > 0)
            parts.Add("删 " + string.Join("、", outcome.Deleted.Select(item => $"第 {item.Number} 条「{TechAiEdit.Plain(item.Text)}」")));
        if (outcome.Added.Count > 0)
        {
            parts.Add(baseName is null
                ? $"写了 {outcome.Added.Count} 条"
                : "加" + string.Join("、", outcome.Added.Select(item => $"「{item.Text}」（{(item.After == 0 ? "最前" : $"原第 {item.After} 条后")}）")));
        }

        if (outcome.Dropped > 0)
            parts.Add($"AI 另给的 {outcome.Dropped} 处超出上限或不合规，没采用");
        if (editReason.Length > 0)
            parts.Add($"AI 说明：{editReason}");
        return string.Join("；", parts);
    }

    /// <summary>发给总线的那一行：参数值一律经 <see cref="CommandParser.QuoteArg"/> 编码。</summary>
    internal static string BuildCommand(string system, string prompt, string image)
        => $"apollo.chat.ask maxtokens=1500 temperature=0.2 timeout={TimeoutSeconds}"
           + " images=" + CommandParser.QuoteArg(image)
           + " system=" + CommandParser.QuoteArg(system)
           + " prompt=" + CommandParser.QuoteArg(prompt.Replace("\r", " ").Replace("\n", " "));

    private static CommandResult Ask(QuickCommandContext context, string system, string prompt, string image)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        return context.Invoke(BuildCommand(system, prompt, image));
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

    private static string Clip(string text)
    {
        var line = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 120 ? line : line[..120] + "…";
    }
}
