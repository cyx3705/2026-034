namespace HistoryStrenua;

/// <summary>这条指令是谁发来的。回执带不带本步反馈与笔记、检查类弹不弹窗、要不要先回话，都只看这一处。</summary>
internal static class CommandOrigin
{
    /// <summary>AI 经 MCP 调用（来源串里带「MCP」）；页面上人点按钮不算。</summary>
    public static bool IsAi(string? source) => source?.Contains("MCP", StringComparison.OrdinalIgnoreCase) == true;
}
