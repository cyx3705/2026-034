namespace HistoryStrenua;

/// <summary>命令类的显示名与页面顺序。命令类 = 指令名第二段，改显示名不改指令名，MCP 调用不受影响。</summary>
internal static class QuickClasses
{
    /// <summary>页面上类的顺序；登记表里出现了这里没有的类就排在最后。</summary>
    public static IReadOnlyList<string> Order { get; } = ["onekey", "hole", "fillet", "view", "dim", "tol", "doc", "tech", "prop", "check", "mymooo"];

    /// <summary>
    /// 不上页面的类（1.33.0，用户定：类太多，这几类是给 AI 用的；公差类 1.36.0、蚂蚁工场 1.37.0 同样）。指令照常注册，MCP 和控制台调用不变。
    /// </summary>
    public static IReadOnlySet<string> Hidden { get; } = new HashSet<string>(StringComparer.Ordinal) { "view", "dim", "tol", "doc", "mymooo" };

    /// <summary>命令类 → 页面上的类名。新类不登记也能用，只是显示成英文类名。</summary>
    private static readonly IReadOnlyDictionary<string, string> Titles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onekey"] = "一键",
            ["hole"] = "孔",
            ["drawing"] = "基础",
            ["fillet"] = "倒圆",
            ["view"] = "视图",
            ["dim"] = "标注",
            ["tol"] = "公差",
            ["doc"] = "文档",
            ["tech"] = "要求",
            ["prop"] = "属性",
            ["check"] = "检查",
            ["mymooo"] = "蚂蚁工场",
        };

    public static string Title(string commandClass)
        => Titles.TryGetValue(commandClass, out var title) ? title : commandClass;
}
