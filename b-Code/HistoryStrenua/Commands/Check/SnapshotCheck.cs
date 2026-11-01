namespace HistoryStrenua;

/// <summary>快捷指令「图纸截图」<c>strenua.check.snapshot</c>：存图见 <see cref="DrawingSnapshot"/>。不改图，只改了窗口的缩放。</summary>
internal static class SnapshotCheck
{
    private static QuickOutcome Run(QuickCommandContext context)
    {
        var document = DrawingAccess.ActiveDrawing(context);
        var title = context.Api.CallString(document, "IModelDoc2", "GetTitle");
        var file = DrawingSnapshot.Save(context, document);
        return QuickOutcome.Ok($"图纸截图：「{title}」当前图纸页已存成图片 {file}");
    }

    public static QuickCommand Command { get; } = new(
        Key: "snapshot",
        CommandName: StrenuaIdentity.Domain + ".check.snapshot",
        Title: "图纸截图",
        Summary: "把当前工程图的当前图纸页整页存成 PNG 并回执文件路径（给 AI 看图验收用），不改图。",
        Usage: "不用点视图：按当前工程图当前纸面打印捕获黑白 200 DPI PNG，不含图框外备用注释（副本，不改原图、不改保存状态，导出设置用后还原）。存在模块数据目录的 snapshots 里（只留最近 30 张），控制台回执给出文件路径。AI 经 MCP 调完出图、标注指令后调它，读这张图就能自己验收。",
        Run: Run);
}
