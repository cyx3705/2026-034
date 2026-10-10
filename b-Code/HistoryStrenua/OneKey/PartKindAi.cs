using System.Text.Encodings.Web;
using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// 「一键出图」判零件类别（1.17.0，用户定：一键出图时直接让 AI 选这是钣金、框架还是加工件）。
/// </summary>
/// <remarks>
/// <para>输入与「AI 填写技术要求」同一套：刚建好的图纸截图 + 图纸基本信息 JSON（<see cref="TechAiInfo"/>，含材料、特征类型计数），
/// 经命令总线调 HistoryApollo 的 <c>apollo.chat.ask images=</c>（<see cref="TechAi.BuildCommand"/>）。一问，答 <c>{"kind", "reason"}</c>。</para>
/// <para>AI 没成（没配密钥、断网、答非所问、截图没存出来）不让一键出图停下：按特征树猜（<see cref="PartKindPlanner.Guess"/>），回执里说明。
/// 猜的结果也写进提示词，模型看得到「按特征树像什么」，但由它定。</para>
/// </remarks>
internal static class PartKindAi
{
    private const string System =
        "你是机械设计工程师，判断一个 SolidWorks 零件该按哪一类出工程图。三类：钣金（钣金件，板料折弯、冲切，特征类型里常有 SheetMetal）、"
        + "框架（铝型材搭成的骨架，常用焊件结构构件 WeldMemberFeat，材料多为 6061/6063 铝）、加工件（其他一切：机加件、铸件、焊接结构件等）。"
        + "看图纸截图与图纸基本信息 JSON 定。只输出一个 JSON 对象，不要别的文字：{\"kind\": \"钣金 或 框架 或 加工件\", \"reason\": \"一句话理由\"}。";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>判出的类别与写进回执的一句。</summary>
    internal static (PartKind Kind, string Message) Classify(QuickCommandContext context, object drawing)
    {
        context.SetState("AI 判类别");
        var info = TechAiInfo.Read(context, drawing);
        var guess = PartKindPlanner.Guess(info);
        string failure;
        try
        {
            var image = DrawingSnapshot.Save(context, drawing);
            var prompt = $"候选类别：{JsonSerializer.Serialize(PartKindPlanner.Titles, Json)}。按特征树看像「{PartKindPlanner.Title(guess)}」（仅供参考）。图纸基本信息 JSON：{info}";
            context.Cancellation.ThrowIfCancellationRequested();
            var reply = context.Invoke(TechAi.BuildCommand(System, prompt, image));
            if (!reply.Success)
                failure = "Apollo 失败：" + reply.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            else if (PartKindPlanner.Read(reply.Message) is { } read)
                return (read.Kind, $"零件类别：AI 判为{PartKindPlanner.Title(read.Kind)}" + (read.Reason.Length > 0 ? $"（{read.Reason}）" : string.Empty) + "。");
            else
                failure = "AI 的答复读不出 {\"kind\": …}";
        }
        catch (QuickCommandException ex)
        {
            failure = ex.Message;
        }

        context.Report($"AI 判零件类别没成（{failure}），按特征树当{PartKindPlanner.Title(guess)}。");
        return (guess, $"零件类别：AI 没成（{failure}），按特征树当{PartKindPlanner.Title(guess)}。");
    }
}
