using System.Globalization;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HistoryStrenua;

/// <summary>用户在 SolidWorks 材料对话框里收藏的一种材质。</summary>
/// <param name="Label">选择框里显示的字；重名时带材料库。</param>
/// <param name="Database">材料库，原样来自注册表：库名或 <c>.sldmat</c> 全路径，两种 <c>SetMaterialPropertyName2</c> 都认。</param>
/// <param name="Name">材质名。</param>
internal readonly record struct FavoriteMaterial(string Label, string Database, string Name);

/// <summary>
/// 属性选择框的候选（1.33.0，从 HistoryMinerva 迁来）：材料取 SolidWorks 收藏材料，表面处理、热处理取属性标签模板里的下拉项。
/// </summary>
/// <remarks>
/// 不写死在代码里：用户改一次模板、收藏一种新材料，这里就该跟着变。两处来源都记在当前用户的 SolidWorks 系统选项（注册表）里，所以仓库里没有本机路径；
/// 读不到时表面处理、热处理退回 <see cref="PropertyFallbacks"/>，材料给空表（凭空编材质名比不给更糟）。
/// </remarks>
internal static class PropertyOptions
{
    private const string SolidWorksRoot = @"Software\SolidWorks";
    private const string VersionKeyPrefix = "SOLIDWORKS ";

    public static IReadOnlyList<FavoriteMaterial> Materials()
    {
        try
        {
            return ReadFavoriteMaterials();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> SurfaceTreatments() => ComboOptions(PropertySlots.SurfaceTreatment, PropertyFallbacks.SurfaceTreatments);

    public static IReadOnlyList<string> HeatTreatments() => ComboOptions(PropertySlots.HeatTreatment, PropertyFallbacks.HeatTreatments);

    /// <summary>参数 → 收藏材料：没填是 null，按显示字或材质名找，收藏里没有抛错（不能当没填放过，那是「选了但没生效」）。</summary>
    public static FavoriteMaterial? ResolveMaterial(string? text, IReadOnlyList<FavoriteMaterial> favorites)
    {
        if (PropertySlots.IsUnset(text))
            return null;
        var trimmed = text!.Trim();
        foreach (var item in favorites)
        {
            if (item.Label == trimmed || item.Name == trimmed)
                return item;
        }

        throw new QuickArgumentException($"收藏材料里没有「{trimmed}」：请在 SolidWorks 材料对话框里收藏它，或改选别的。"
            + (favorites.Count == 0 ? string.Empty : "可选：" + string.Join("、", favorites.Select(item => item.Label)) + "。"));
    }

    /// <summary>
    /// 把 <c>_FavMaterialN</c> 的值解析成收藏材料：形如 <c>库|材质名</c> 或 <c>库|材质名|序号</c>。同名材质分属不同库时才带库名。
    /// </summary>
    internal static IReadOnlyList<FavoriteMaterial> ParseFavorites(IEnumerable<string> records)
    {
        var entries = new List<(string Database, string Name)>();
        foreach (var raw in records)
        {
            var parts = raw.Split('|');
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
                entries.Add((parts[0], parts[1]));
        }

        var duplicated = entries.GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        return entries
            .Select(entry => new FavoriteMaterial(
                duplicated.Contains(entry.Name) ? $"{entry.Name}（{DatabaseLabel(entry.Database)}）" : entry.Name,
                entry.Database,
                entry.Name))
            .ToList();
    }

    /// <summary>从属性标签模板 XML 收某个槽的下拉项。按 <c>PropName</c> 找：它与 <c>Label</c> 是两个字段，可以不同。</summary>
    internal static void CollectOptions(XDocument document, string propertyName, List<string> options, HashSet<string> seen)
    {
        foreach (var control in document.Descendants("Control"))
        {
            if (!string.Equals((string?)control.Attribute("PropName"), propertyName, StringComparison.Ordinal))
                continue;
            foreach (var item in control.Elements("Data").Elements("Item"))
            {
                var value = item.Value.Trim();
                if (value.Length != 0 && seen.Add(value))
                    options.Add(value);
            }
        }
    }

    /// <summary>
    /// 当前用户最新一版 SolidWorks 的注册表键名。不能按名字倒序取第一个：同一层还有 <c>SOLIDWORKS CAM</c>，字符串序排在 2025 后面。只认四位年份。
    /// </summary>
    private static string? ResolveVersionKey()
    {
        using var root = Registry.CurrentUser.OpenSubKey(SolidWorksRoot);
        if (root is null)
            return null;
        string? best = null;
        var bestYear = 0;
        foreach (var name in root.GetSubKeyNames())
        {
            if (!name.StartsWith(VersionKeyPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(name[VersionKeyPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 2000 && year > bestYear)
            {
                bestYear = year;
                best = name;
            }
        }

        return best;
    }

    private static IReadOnlyList<FavoriteMaterial> ReadFavoriteMaterials()
    {
        if (ResolveVersionKey() is not { } version)
            return [];
        using var key = Registry.CurrentUser.OpenSubKey($@"{SolidWorksRoot}\{version}\Material");
        if (key is null)
            return [];
        // __NumOfFavs 是当前收藏条数；_FavMaterialN 会残留超出这个数的旧条目，全读会把取消收藏的又摆回来。
        var count = key.GetValue("__NumOfFavs") switch
        {
            int value => value,
            string text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
        var records = new List<string>();
        for (var index = 1; index <= count; index++)
        {
            if (key.GetValue($"_FavMaterial{index}") is string raw)
                records.Add(raw);
        }

        return ParseFavorites(records);
    }

    private static string DatabaseLabel(string database)
        => database.Contains('/') || database.Contains('\\') ? Path.GetFileNameWithoutExtension(database) : database;

    private static IReadOnlyList<string> ComboOptions(string propertyName, IReadOnlyList<string> fallback)
    {
        try
        {
            var parsed = ReadTemplateOptions(propertyName);
            return parsed.Count == 0 ? fallback : parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or XmlException)
        {
            return fallback;
        }
    }

    private static IReadOnlyList<string> ReadTemplateOptions(string propertyName)
    {
        if (ResolveVersionKey() is not { } version)
            return [];
        using var key = Registry.CurrentUser.OpenSubKey($@"{SolidWorksRoot}\{version}\ExtReferences");
        if (key?.GetValue("Custom Property Folders") is not string folders)
            return [];
        var options = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.prtprp"))
                CollectOptions(XDocument.Load(file), propertyName, options, seen);
        }

        return options;
    }
}

/// <summary>读不到模板时的兜底副本，抄自 <c>精密零件属性.prtprp</c>。不是权威：模板改了它不跟着改。</summary>
internal static class PropertyFallbacks
{
    public static IReadOnlyList<string> SurfaceTreatments { get; } =
        ["无", "本色阳极氧化", "镀硬铬", "镀化学镍", "镀白锌", "喷塑 RAL9003", "喷塑 RAL7035", "喷塑 RAL1012", "喷塑 RAL1013", "喷塑 RAL1018", "喷塑 RAL3020", "表面拉丝"];

    public static IReadOnlyList<string> HeatTreatments { get; } = ["无", "退火", "淬火HRC58-63", "调质处理"];
}
