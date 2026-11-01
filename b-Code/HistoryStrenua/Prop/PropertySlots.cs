using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 零件属性标签模板里的槽名与写法（1.33.0，从 HistoryMinerva 属性整备迁来，用户定：单件属性全归这里）。纯文本，离线可测。
/// </summary>
/// <remarks>
/// 槽名取模板的 <c>PropName</c>：<c>Add3</c> 按名字找槽，差一个字不报错，而是新建重名槽，标题栏照样取不到值。
/// 模板控件全是 <c>ApplyTo="Config"</c>，所以写配置特定属性。
/// </remarks>
internal static class PropertySlots
{
    public const string DrawingNumber = "图号";
    public const string Name = "名称";
    public const string Date = "日期";
    public const string Designer = "设计";
    public const string Material = "材料";
    public const string SurfaceTreatment = "表面处理";
    public const string HeatTreatment = "热处理";

    /// <summary>文本型日期的格式。</summary>
    public const string DateFormat = "yyyy/MM/dd";

    /// <summary>选择框第一项：这一槽本轮不动。</summary>
    public const string KeepOption = "不改";

    /// <summary>日期选择框：当日。</summary>
    public const string TodayOption = "今天";

    /// <summary>读属性时列出的槽。</summary>
    public static IReadOnlyList<string> All { get; } = [DrawingNumber, Name, Material, SurfaceTreatment, HeatTreatment, Date, Designer];

    /// <summary>「材料」槽的链接表达式，两侧英文双引号是表达式的一部分；写裸的 SW-Material 会被当成文字。</summary>
    public static string MaterialLink(string configuration, string fileName)
        => "\"SW-Material@@" + configuration + "@" + fileName + "\"";

    /// <summary>按第一个空格切成图号与名称，没有空格时图号为空。与 HistoryMinerva 批量改名同一条规则。</summary>
    public static (string DrawingNumber, string Name) SplitFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).Trim();
        var space = stem.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
            return (string.Empty, stem);
        var name = stem[(space + 1)..].Trim();
        return name.Length == 0 ? (string.Empty, stem) : (stem[..space].Trim(), name);
    }

    /// <summary>没填：空串或 <see cref="KeepOption"/>。不填就不动。</summary>
    public static bool IsUnset(string? text) => string.IsNullOrWhiteSpace(text) || text.Trim() == KeepOption;

    /// <summary>日期参数 → 槽里的文字：没给是 null，今天 / today 是当日，其余必须是 <see cref="DateFormat"/>。</summary>
    public static string? ResolveDate(string? text, DateTime today)
    {
        if (IsUnset(text))
            return null;
        var trimmed = text!.Trim();
        if (trimmed == TodayOption || string.Equals(trimmed, "today", StringComparison.OrdinalIgnoreCase))
            return today.ToString(DateFormat, CultureInfo.InvariantCulture);
        return DateTime.TryParseExact(trimmed, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString(DateFormat, CultureInfo.InvariantCulture)
            : throw new QuickArgumentException($"date={text} 写不对：写「今天」或 {DateFormat}。");
    }

    /// <summary>排出这一轮写哪些槽。图号、名称恒从文件名来（没有图号段就不碰图号）；材料另走 <see cref="WriteRequest.Material"/>。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Plan(string fileName, WriteRequest request)
    {
        var (number, name) = SplitFileName(fileName);
        var candidates = new (string Slot, string? Value)[]
        {
            (DrawingNumber, number),
            (Name, name),
            (Date, request.Date),
            (Designer, request.Designer),
            (SurfaceTreatment, request.SurfaceTreatment),
            (HeatTreatment, request.HeatTreatment),
        };
        return candidates.Where(item => !string.IsNullOrEmpty(item.Value))
            .Select(item => new KeyValuePair<string, string>(item.Slot, item.Value!))
            .ToList();
    }
}

/// <summary>记到本机的属性默认值，存选择框里的字（日期可以是「今天」，写入时才算成当日）；null 表示不改。</summary>
internal sealed record PropertyDefaults(
    string? Material = null,
    string? Surface = null,
    string? Heat = null,
    string? Date = null,
    string? Designer = null)
{
    /// <summary>按参数名取值（页面输入框的初值）。</summary>
    public string? ValueOf(string parameter) => parameter switch
    {
        "material" => Material,
        "surface" => Surface,
        "heat" => Heat,
        "date" => Date,
        "designer" => Designer,
        _ => null,
    };
}

/// <summary>一次写属性填了什么；null 的不动。</summary>
internal sealed record WriteRequest(
    FavoriteMaterial? Material,
    string? SurfaceTreatment,
    string? HeatTreatment,
    string? Date,
    string? Designer,
    bool Save);
