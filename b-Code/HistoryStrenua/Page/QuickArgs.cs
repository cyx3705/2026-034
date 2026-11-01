using System.Globalization;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 带参数的快捷指令的一个参数（1.20.0）：总线上的参数说明，也是页面上这一类面板里的一个输入框（控件 id = 参数名，同类同名参数共用一个框）。
/// </summary>
/// <param name="Name">参数名，也是输入框的 id 与动作占位符 <c>{Name}</c>。</param>
/// <param name="Label">输入框左侧的标签。</param>
/// <param name="Description">参数说明（格式、单位、例子）。</param>
/// <param name="Required">必填。</param>
/// <param name="Group">输入框排在面板的第几行（0 = 对象：视图、名字；1 = 位置；2 = 文字、公差）。</param>
/// <param name="Options">有限取值：输入框渲染成选择框，第一项是缺省；null 为自由输入。</param>
/// <param name="OptionsView">候选实时取数（<c>strenua.ui.data view=</c>）的选择框（1.33.0）；取到的第一项是缺省。</param>
internal sealed record QuickParameter(string Name, string Label, string Description, bool Required, int Group = 0, string[]? Options = null, string? OptionsView = null)
{
    public ParameterSpec ToSpec(int position) => new()
    {
        Name = Name,
        Description = Description,
        Required = Required,
        Position = position,
    };
}

/// <summary>参数填错了（缺参数、数字写不对）。在附着 SolidWorks 之前抛，消息原样给调用方看。</summary>
internal sealed class QuickArgumentException(string message) : Exception(message);

/// <summary>
/// 带参数的快捷指令（1.20.0「视图」「标注」两类）的参数解析：纯文本，离线可测。
/// </summary>
/// <remarks>
/// 页面按钮会把这一类的输入框全部传上来，没填的是空串——所以<b>空串一律等于没给</b>；要清掉前后缀、公差写 <c>none</c>。
/// 坐标一律图纸 mm、左下角为原点，和截图对得上。
/// </remarks>
internal static class QuickArgs
{
    /// <summary>图纸 mm ↔ SolidWorks 的米。</summary>
    public const double Mm = 0.001;

    /// <summary>「清掉」的写法（前后缀、公差）。</summary>
    public const string None = "none";

    // 各指令共用的参数：同名同义，页面上同一类共用一个输入框。
    public static QuickParameter View(string description) => new("view", "视图", description, true);

    public static QuickParameter X(string description) => new("x", "X", description, false, 1);

    public static QuickParameter Y(string description) => new("y", "Y", description, false, 1);

    public static QuickParameter Dx(string description) => new("dx", "右移", description, false, 1);

    public static QuickParameter Dy(string description) => new("dy", "上移", description, false, 1);

    /// <summary>必填的名字（视图名、注解名）。</summary>
    public static string Name(CommandContext context, string parameter)
        => Optional(context, parameter) ?? throw new QuickArgumentException($"缺参数 {parameter}=");

    /// <summary>可省的文字：没给、空串都是 null。</summary>
    public static string? Optional(CommandContext context, string parameter)
        => context.GetString(parameter)?.Trim() is { Length: > 0 } text ? text : null;

    /// <summary>逗号 / 顿号 / 空白分开的一串名字，去空、去重、保持顺序；一个都没有抛错。</summary>
    public static IReadOnlyList<string> Names(string? text, string parameter)
    {
        var names = (text ?? string.Empty)
            .Split([',', '，', '、', ';', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count > 0 ? names : throw new QuickArgumentException($"缺参数 {parameter}=（逗号分开的名字，如 D1,D3）");
    }

    /// <summary>可省的 mm 数（允许负数、可带 mm）；写了但不是数抛错。</summary>
    public static double? Millimeters(string? text, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        if (trimmed.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2].TrimEnd();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : throw new QuickArgumentException($"{parameter}={text} 不是数（图纸 mm，如 120 或 -5.5）");
    }

    /// <summary>可省的是 / 否：true / false / 是 / 否，没给为 false。</summary>
    public static bool Flag(CommandContext context, string parameter)
        => Optional(context, parameter) switch
        {
            null => false,
            var text when text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "是" => true,
            var text when text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "否" => false,
            var text => throw new QuickArgumentException($"{parameter} 只能是 是 / 否（true / false）：{text}"),
        };

    /// <summary>
    /// 挪到哪：<c>x= y=</c> 给绝对位置（可只给一个，另一个不动），<c>dx= dy=</c> 给相对挪动，两种不能混用、也不能都不给。单位图纸 mm。
    /// </summary>
    public static MoveRequest Move(CommandContext context)
    {
        var x = Millimeters(context.GetString("x"), "x");
        var y = Millimeters(context.GetString("y"), "y");
        var dx = Millimeters(context.GetString("dx"), "dx");
        var dy = Millimeters(context.GetString("dy"), "dy");
        var absolute = x is not null || y is not null;
        var relative = dx is not null || dy is not null;
        if (absolute && relative)
            throw new QuickArgumentException("x/y（挪到）与 dx/dy（挪动）只能用一种。");
        if (!absolute && !relative)
            throw new QuickArgumentException("缺位置：给 x= y=（挪到，图纸 mm）或 dx= dy=（挪动多少 mm）。");
        return new MoveRequest(absolute, absolute ? x : dx, absolute ? y : dy);
    }

    /// <summary>按 <paramref name="request"/> 从 <paramref name="current"/>（米）算出目标（米）。</summary>
    public static SheetPoint Target(MoveRequest request, SheetPoint current)
        => request.Absolute
            ? new SheetPoint(request.X is { } x ? x * Mm : current.X, request.Y is { } y ? y * Mm : current.Y)
            : new SheetPoint(current.X + (request.X ?? 0) * Mm, current.Y + (request.Y ?? 0) * Mm);

    /// <summary>前后缀：没给为 null（不改），<c>none</c> 为空串（清掉），否则原样。</summary>
    public static string? Text(CommandContext context, string parameter)
        => Optional(context, parameter) is { } text
            ? text.Equals(None, StringComparison.OrdinalIgnoreCase) ? string.Empty : text
            : null;

    /// <summary>
    /// 公差：<c>none</c>（或「无」「0」）去掉，返回 null；一个正数 = 对称 ±（mm）。
    /// </summary>
    public static double? Tolerance(string text)
    {
        var trimmed = text.Trim().TrimStart('±', '+');
        if (trimmed.Equals(None, StringComparison.OrdinalIgnoreCase) || trimmed == "无" || trimmed == "0")
            return null;
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && double.IsFinite(value)
            ? value
            : throw new QuickArgumentException($"tol={text} 写不对：对称公差写正数 mm（如 0.02），去掉写 none");
    }

    /// <summary>米 → 回执里的 mm 文字（最多 3 位小数）。</summary>
    public static string Format(double meters) => (meters / Mm).ToString("0.###", CultureInfo.InvariantCulture);

    public static string Format(SheetPoint point) => $"({Format(point.X)}, {Format(point.Y)})";
}

/// <summary>一次挪动：绝对位置还是相对挪动；X / Y 为 null 的方向不动（单位图纸 mm）。</summary>
internal readonly record struct MoveRequest(bool Absolute, double? X, double? Y);
