using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

/// <summary>分层门禁：依赖只能朝下，切面只有一份。按源码文本查，命名空间不跟文件夹走所以看不了程序集。</summary>
internal static partial class Tests
{
    /// <summary>从下到上；下层的代码不得点名上层的类型。</summary>
    private static readonly string[] Layers = ["Planning", "SolidWorks", "Runtime", "Sheet", "Commands", "Page"];

    static string ModuleRoot([CallerFilePath] string self = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "HistoryStrenua"));

    /// <summary>源文件：(路径, 层, 命令类, 去掉注释和字符串的正文)。</summary>
    static List<(string Path, string Layer, string? Class, string Code)> ModuleSources()
    {
        var root = ModuleRoot();
        var list = new List<(string, string, string?, string)>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
            if (parts[0] is "bin" or "obj" || parts.Length == 1)
                continue;
            var code = File.ReadAllText(path);
            code = Regex.Replace(code, @"//.*", string.Empty);
            code = Regex.Replace(code, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
            list.Add((path, parts[0], parts[0] == "Commands" ? parts[1] : null, code));
        }

        return list;
    }

    /// <summary>每个文件声明的类型名（只看顶层）。</summary>
    static IEnumerable<string> DeclaredTypes(string code)
        => Regex.Matches(code, @"^(?:(?:internal|public|sealed|static|abstract|readonly|partial)\s+)*(?:class|record struct|record|struct|enum|interface)\s+([A-Z]\w+)", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value);

    /// <summary>代码里以静态方式点名的类型（<c>Foo.Bar</c>、<c>new Foo(</c>、<c>Foo&lt;</c> 不算属性访问）。</summary>
    static HashSet<string> Named(string code)
        => Regex.Matches(code, @"(?<![.\w])([A-Z]\w+)\.[A-Z]").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    static void TestArchitectureLayers()
    {
        var sources = ModuleSources();
        True(Layers.All(layer => sources.Any(source => source.Layer == layer)), "六层文件夹都要有文件");
        True(sources.All(source => Array.IndexOf(Layers, source.Layer) >= 0), "模块根下只有六层文件夹：" + string.Join("、", sources.Select(source => source.Layer).Distinct().Except(Layers)));
        var owners = new Dictionary<string, (string Layer, string? Class)>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var type in DeclaredTypes(source.Code))
                owners[type] = (source.Layer, source.Class);
        }

        var faults = new List<string>();
        foreach (var source in sources)
        {
            foreach (var name in Named(source.Code))
            {
                if (!owners.TryGetValue(name, out var owner))
                    continue;
                if (Array.IndexOf(Layers, owner.Layer) > Array.IndexOf(Layers, source.Layer))
                    faults.Add($"{Path.GetFileName(source.Path)}（{source.Layer}）点名了上层 {name}（{owner.Layer}）");
                // 命令类之间不互相调，共用的下沉到 Sheet；只有一键类负责编排别的类。
                else if (source.Class is not null && owner.Class is not null && owner.Class != source.Class && source.Class != "OneKey")
                    faults.Add($"{Path.GetFileName(source.Path)}（{source.Class} 类）点名了别的类 {name}（{owner.Class} 类）");
            }
        }

        True(faults.Count == 0, string.Join(Environment.NewLine, faults));
    }

    static void TestPlanningIsPure()
    {
        var faults = ModuleSources()
            .Where(source => source.Layer == "Planning" && Regex.IsMatch(source.Code, @"\bSolidWorksApi\b|\bQuickCommandContext\b|\.Call(?:Bool|Int|String|Array|Doubles)?\("))
            .Select(source => Path.GetFileName(source.Path))
            .ToList();
        True(faults.Count == 0, "Planning 不碰 SolidWorks：" + string.Join("、", faults));
    }

    static void TestCommandFolders()
    {
        var faults = new List<string>();
        foreach (var source in ModuleSources().Where(source => source.Layer == "Commands"))
        {
            var text = File.ReadAllText(source.Path);
            foreach (Match match in Regex.Matches(text, @"CommandName: StrenuaIdentity\.Domain \+ ""\.(\w+)\."))
            {
                if (!match.Groups[1].Value.Equals(source.Class, StringComparison.OrdinalIgnoreCase))
                    faults.Add($"{Path.GetFileName(source.Path)} 在 {source.Class}/，指令却是 {match.Groups[1].Value} 类");
            }
        }

        True(faults.Count == 0, "文件夹名 = 指令名第二段：" + string.Join("；", faults));
    }

    static void TestConstantsAndGuardsSingleSource()
    {
        var names = Regex.Matches(File.ReadAllText(Path.Combine(ModuleRoot(), "Planning", "Common", "SwConst.cs")), @"const int (\w+) =")
            .Select(match => match.Groups[1].Value)
            .ToList();
        var faults = new List<string>();
        foreach (var source in ModuleSources().Where(source => Path.GetFileName(source.Path) != "SwConst.cs"))
        {
            faults.AddRange(names.Where(name => Regex.IsMatch(source.Code, $@"const int {name}\b")).Select(name => $"{Path.GetFileName(source.Path)} 又定义了 {name}"));
            if (Path.GetFileName(source.Path) != "CommandOrigin.cs" && source.Code.Contains("IsAi", StringComparison.Ordinal) && Regex.IsMatch(source.Code, @"\bbool IsAi\b"))
                faults.Add($"{Path.GetFileName(source.Path)} 自己判断来源");
            if (source.Layer != "SolidWorks" && Regex.IsMatch(File.ReadAllText(source.Path), @"\.Contains\(""MCP"""))
                if (Path.GetFileName(source.Path) != "CommandOrigin.cs")
                    faults.Add($"{Path.GetFileName(source.Path)} 自己判断 MCP 来源");
        }

        True(faults.Count == 0, string.Join("；", faults));
    }
}
