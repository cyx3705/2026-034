using System.Text;

namespace HistoryStrenua;

/// <summary>
/// 装配类「干涉检查」<c>strenua.asm.interference</c>（1.38.0）：列出活动总装里实体互相重叠的零部件对、体积与位置。
/// </summary>
/// <remarks>
/// 贴合面不算干涉（<c>TreatCoincidenceAsInterference=false</c>）；子装配拆到零件比（模组内部的问题也会报，厂家模型常有，回执按体积排序，
/// 用 <c>ignore=</c> 排除名字含某段的）。螺纹件（脚杯、螺钉）在模型里常是实体螺杆插进底孔，也会报，属正常。
/// </remarks>
internal static class AsmInterference
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-interference",
        CommandName: StrenuaIdentity.Domain + ".asm.interference",
        Title: "干涉检查",
        Summary: "检查活动总装的实体干涉：每对零部件、干涉体积（mm³）、干涉区包围盒；贴合面不算，可按名字排除。",
        Usage: "不带参数检查整个总装。「排除」写名字片段，逗号分开（如 RQBET,螺钉），零部件名含其中任一段的干涉对不报。按体积从大到小，最多列 40 对。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("ignore", "排除", "零部件名片段，逗号分开；省略全报", false)],
        Example: StrenuaIdentity.Domain + ".asm.interference ignore=RQBET",
        Bind: arguments =>
        {
            var ignore = QuickArgs.Optional(arguments, "ignore") is { } text ? QuickArgs.Names(text, "ignore") : [];
            return context => Check(context, ignore);
        },
        Readonly: true,
        SelfReport: true);

    private const int Shown = 40;

    private static QuickOutcome Check(QuickCommandContext context, IReadOnlyList<string> ignore)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        context.SetState("干涉检查");
        var manager = api.Call(assembly, "IAssemblyDoc", "get_InterferenceDetectionManager")
                      ?? throw new QuickCommandException("SolidWorks 没有返回干涉检查管理器。");
        var found = new List<(double Volume, string A, string B, string Box)>();
        try
        {
            api.Call(manager, "IInterferenceDetectionMgr", "set_TreatCoincidenceAsInterference", false);
            api.Call(manager, "IInterferenceDetectionMgr", "set_TreatSubAssembliesAsComponents", false);
            api.Call(manager, "IInterferenceDetectionMgr", "set_IncludeMultibodyPartInterferences", false);
            foreach (var interference in api.CallArray(manager, "IInterferenceDetectionMgr", "GetInterferences"))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var names = api.CallArray(interference, "IInterference", "get_Components")
                    .Select(component => api.CallString(component, "IComponent2", "get_Name2")).ToList();
                if (names.Count < 2 || names.Any(name => ignore.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))))
                    continue;
                var volume = api.Call(interference, "IInterference", "get_Volume") is { } v ? Convert.ToDouble(v) * 1e9 : 0;
                var box = api.Call(interference, "IInterference", "GetInterferenceBody") is { } body
                    ? string.Join(",", api.CallDoubles(body, "IBody2", "GetBodyBox").Select(value => ModelSpec.Format(value * 1000, "0.#")))
                    : string.Empty;
                found.Add((volume, names[0], names[1], box));
            }
        }
        finally
        {
            api.CallForCleanup(manager, "IInterferenceDetectionMgr", "Done");
        }

        if (found.Count == 0)
            return QuickOutcome.Ok($"干涉检查：「{api.CallString(assembly, "IModelDoc2", "GetTitle")}」没有实体干涉{(ignore.Count > 0 ? $"（已排除含 {string.Join("、", ignore)} 的）" : string.Empty)}。");
        var text = new StringBuilder($"干涉检查：{found.Count} 对干涉，按体积从大到小（总装 mm）：").AppendLine();
        foreach (var (volume, a, b, box) in found.OrderByDescending(item => item.Volume).Take(Shown))
            text.AppendLine($"· {ModelSpec.Format(volume, "0.#")} mm³  {Short(a)} ↔ {Short(b)}  区域[{box}]");
        if (found.Count > Shown)
            text.AppendLine($"…另有 {found.Count - Shown} 对。");
        return QuickOutcome.Fail(text.ToString().TrimEnd());
    }

    /// <summary>子装配里的零部件名是「子装配-1/零件-1」，太长时只留头尾两段。</summary>
    private static string Short(string name)
    {
        var parts = name.Split('/');
        return parts.Length <= 2 ? name : parts[0] + "/…/" + parts[^1];
    }
}
