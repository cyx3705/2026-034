using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>一键类按零件类别出图（1.17.0，用户定）：钣金、框架（铝型材骨架）、加工件（其他情况）。</summary>
internal enum PartKind
{
    /// <summary>钣金：标孔（含方形槽）与外轮廓，折弯处的线不标、不当基准。</summary>
    SheetMetal,

    /// <summary>框架：只标外轮廓，轴测图上写「采用 xx 铝型材」。</summary>
    Frame,

    /// <summary>加工件：其他情况，孔、外轮廓、倒圆倒角全套。</summary>
    Machined,
}

/// <summary>
/// 零件类别的纯逻辑部分（1.17.0）：类别叫法、型材规格怎么写。
/// </summary>
internal static partial class PartKindPlanner
{
    /// <summary>类别在页面、回执里的叫法。</summary>
    public static string Title(PartKind kind) => kind switch
    {
        PartKind.SheetMetal => "钣金",
        PartKind.Frame => "框架",
        _ => "加工件",
    };

    /// <summary>
    /// 焊件型材文件名 → 规格（「采用 xx 铝型材」的 xx）：「20x20」「20×20」写成「2020」，别的（「2020欧标」「4040」）原样，
    /// 去掉扩展名与末尾的「铝型材」「型材」。空名返回空串。
    /// </summary>
    public static string ProfileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;
        if (SizePair().Match(name) is { Success: true } pair)
            name = pair.Groups[1].Value + pair.Groups[2].Value + name[(pair.Index + pair.Length)..].Trim();
        foreach (var tail in new[] { "铝型材", "型材" })
        {
            if (name.EndsWith(tail, StringComparison.Ordinal))
            {
                name = name[..^tail.Length].Trim();
                break;
            }
        }

        return name;
    }

    [GeneratedRegex(@"^(\d+)\s*[xX×*]\s*(\d+)")]
    private static partial Regex SizePair();

    /// <summary>
    /// 没有焊件型材文件时按实体包围盒猜规格：每根型材一个实体，包围盒最小的两个边长就是截面（毫米取整）；按出现次数排，
    /// 写成「2020」「4040」，几种就用「、」连起来。截面两边差三倍以上（不是方型材）、边长超过 200 mm（不像一根型材）的不算。
    /// </summary>
    /// <param name="boxes">每个实体的包围盒三个边长（米）。</param>
    public static string ProfileFromBodies(IEnumerable<(double A, double B, double C)> boxes)
    {
        var sections = new List<(int A, int B)>();
        foreach (var (a, b, c) in boxes)
        {
            var sides = new[] { a, b, c }.Order().Select(v => (int)Math.Round(v * 1000)).ToArray();
            var (small, middle) = (sides[0], sides[1]);
            if (small <= 0 || middle > 200 || middle > small * 3)
                continue;
            sections.Add((small, middle));
        }

        return string.Join("、", sections.GroupBy(section => section)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key.A)
            .Select(group => $"{group.Key.A}{group.Key.B}"));
    }

    /// <summary>规格名开头的截面宽（「2020」「2020欧标」→ 20 mm、「4080」→ 40 mm，米）；读不出返回 0。外轮廓筛站用（<see cref="OutlinePlanner.Sparse"/>）。</summary>
    public static double ProfileWidth(string name)
    {
        var digits = new string((name ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length is not (4 or 6))
            return 0;
        var half = digits.Length / 2;
        return Math.Min(int.Parse(digits[..half]), int.Parse(digits[half..])) / 1000.0;
    }

    /// <summary>轴测图上的说明文字：「采用2020铝型材」（用户样图「采用2020欧标铝型材」：规格名里带「欧标」就原样带上）。</summary>
    public static string ProfileNote(string name) => name.Length == 0 ? "采用铝型材" : $"采用{name}铝型材";
}
