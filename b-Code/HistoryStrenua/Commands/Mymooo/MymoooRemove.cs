using System.Text;

namespace HistoryStrenua;

/// <summary>
/// 从当前总装删零部件（1.37.0，用户要的：测试插入时不用手动删）。零件、子装配都行；files=是 再删外购件里的文件。
/// </summary>
/// <remarks>
/// 只删顶层零部件，按 <c>IComponent2.Name2</c>（插入回执里的零部件名，如 SBH1-M8-L20-S11-1）。
/// 删文件只删总装目录「外购件」里的、且总装里已没有别的实例引用的；外购件以外的文件一律不删。不存总装。
/// </remarks>
internal static class MymoooRemove
{
    public static QuickCommand Command { get; } = new(
        Key: "mymooo-remove",
        CommandName: StrenuaIdentity.Domain + ".mymooo.remove",
        Title: "删零部件",
        Summary: "从当前总装删顶层零部件（零件或子装配，按零部件名）；files=是 再删「外购件」里不再被引用的文件。不存总装",
        Usage: "「零部件」填零部件名（插入回执里的名字，如 SBH1-M8-L20-S11-1），几个用逗号分开。「删文件」填是：删完把总装目录「外购件」里、总装已不再引用的那些文件也删掉（外购件以外的文件不删）。不存总装。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "顶层零部件名，逗号分开", true),
            new QuickParameter("files", "删文件", "是 = 再删外购件里不再被引用的文件；省略为否", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".mymooo.remove name=SBH1-M8-L20-S11-1",
        Bind: arguments =>
        {
            var names = QuickArgs.Names(QuickArgs.Optional(arguments, "name"), "name");
            var files = QuickArgs.Flag(arguments, "files");
            return context => Remove(context, names, files);
        },
        SelfReport: true);

    private static QuickOutcome Remove(QuickCommandContext context, IReadOnlyList<string> names, bool files)
    {
        var api = context.Api;
        var assembly = context.Session.ActiveDocument();
        if (assembly is null || api.CallInt(assembly, "IModelDoc2", "GetType") != SwConst.DocumentAssembly)
            throw new QuickCommandException("当前文档不是总装：先切到要删零部件的总装。");
        var title = api.CallString(assembly, "IModelDoc2", "GetTitle");

        var top = TopComponents(context, assembly);
        var missing = names.Where(name => !top.ContainsKey(name)).ToList();
        if (missing.Count > 0)
            throw new QuickCommandException($"「{title}」顶层没有 {string.Join("、", missing)}；有：{string.Join("、", top.Keys.Take(40))}{(top.Count > 40 ? " …" : string.Empty)}。");
        var paths = names.Select(name => api.CallString(top[name], "IComponent2", "GetPathName")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // EditDelete 没有返回值，偶有漏删：回读重删，最多三遍（同注解删除）。
        var left = names.ToList();
        for (var pass = 0; pass < 3 && left.Count > 0; pass++)
        {
            api.ClearSelection(assembly);
            for (var i = 0; i < left.Count; i++)
                api.Call(top[left[i]], "IComponent2", "Select4", i > 0, null, false);
            api.Call(assembly, "IModelDoc2", "EditDelete");
            top = TopComponents(context, assembly);
            left = left.Where(top.ContainsKey).ToList();
        }

        api.ClearSelection(assembly);
        var text = new StringBuilder($"删零部件：「{title}」删了 {names.Count - left.Count} 个（总装没存）。");
        if (left.Count > 0)
            text.Append($"没删掉：{string.Join("、", left)}。");
        if (files)
            text.AppendLine().Append(DeleteFiles(context, assembly, paths));
        return left.Count == 0 ? QuickOutcome.Ok(text.ToString()) : QuickOutcome.Fail(text.ToString());
    }

    /// <summary>删外购件里、总装已不再引用的文件。</summary>
    private static string DeleteFiles(QuickCommandContext context, object assembly, IReadOnlyList<string> paths)
    {
        var api = context.Api;
        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(api.CallString(assembly, "IModelDoc2", "GetPathName")) ?? string.Empty, MymoooRules.Folder)) + Path.DirectorySeparatorChar;
        var used = api.CallArray(assembly, "IAssemblyDoc", "GetComponents", false)
            .Select(component => api.CallString(component, "IComponent2", "GetPathName"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleted = new List<string>();
        var kept = new List<string>();
        foreach (var path in paths)
        {
            if (!Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add($"{Path.GetFileName(path)}（不在外购件里）");
                continue;
            }

            if (used.Contains(path))
            {
                kept.Add($"{Path.GetFileName(path)}（总装里还有实例）");
                continue;
            }

            Release(context, path);
            try
            {
                File.Delete(path);
                deleted.Add(Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept.Add($"{Path.GetFileName(path)}（删不掉：{ex.Message}）");
            }
        }

        return $"删文件：删了 {deleted.Count} 个{(deleted.Count > 0 ? "（" + string.Join("、", deleted) + "）" : string.Empty)}"
               + (kept.Count > 0 ? "；留着：" + string.Join("、", kept) : string.Empty) + "。";
    }

    /// <summary>
    /// 让 SolidWorks 放开文件：删了零部件，零件仍在内存里、带着写锁（真机：CloseDoc 后 ~$ 锁文件还在，File.Delete 报占用）。
    /// 先关，还在内存里就放掉文件锁（文档留在内存，没人引用）。
    /// </summary>
    private static void Release(QuickCommandContext context, string path)
    {
        var api = context.Api;
        var application = context.Session.Application;
        if (api.Call(application, "ISldWorks", "GetOpenDocumentByName", path) is not { } document)
            return;
        api.Call(application, "ISldWorks", "CloseDoc", api.CallString(document, "IModelDoc2", "GetTitle"));
        if (api.Call(application, "ISldWorks", "GetOpenDocumentByName", path) is { } still)
            api.Call(still, "IModelDoc2", "ForceReleaseLocks");
    }

    private static Dictionary<string, object> TopComponents(QuickCommandContext context, object assembly)
    {
        var api = context.Api;
        var top = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in api.CallArray(assembly, "IAssemblyDoc", "GetComponents", true))
            top.TryAdd(api.CallString(component, "IComponent2", "get_Name2"), component);
        return top;
    }
}
