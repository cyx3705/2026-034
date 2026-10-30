using System.Globalization;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 细粒度指令（1.20.0，用户定：把 Strenua 做成 AI 操作工程图的 MCP，大粒度之外要能单独删、挪、改一个尺寸，单独挪一个视图）。
/// </summary>
/// <remarks>
/// <para>和快捷指令不同：带参数、不上页面（人手工删一个尺寸比点按钮快，这些是给 AI 经 MCP 用的），也不进 <c>strenua.quick.list</c>。
/// 执行仍交给同一个 <see cref="QuickCommandRunner"/>：同一条 STA 线程、一次一条、可取消。</para>
/// <para>参数在附着 SolidWorks 之前就解析完（<see cref="Bind"/>），填错直接回失败、不碰图。坐标一律是图纸 mm、左下角为原点，和截图对得上；
/// 对象按名字找：视图名（<c>strenua.view.list</c>）、注解名（<c>strenua.dim.list</c>，在视图内唯一）。</para>
/// <para>细粒度指令不看避障开关：AI 说放哪就放哪，压没压到别的只在回执里说。</para>
/// </remarks>
/// <param name="Key">执行器里的状态键。</param>
/// <param name="CommandName">总线指令名 <c>strenua.&lt;类&gt;.&lt;方法&gt;</c>，类是 <c>view</c> / <c>dim</c>。</param>
/// <param name="Title">回执里的名字。</param>
/// <param name="Summary">指令自描述的 Summary（120 字内）。</param>
/// <param name="Example">照抄能跑的例子（带全部必填参数）。</param>
/// <param name="Parameters">参数表。</param>
/// <param name="Readonly">不改图（只读回）。</param>
/// <param name="Bind">解析参数，返回在 STA 线程上执行的那一段；参数不对抛 <see cref="FineArgumentException"/>。</param>
internal sealed record FineCommand(
    string Key,
    string CommandName,
    string Title,
    string Summary,
    string Example,
    IReadOnlyList<ParameterSpec> Parameters,
    bool Readonly,
    Func<CommandContext, Func<QuickCommandContext, QuickOutcome>> Bind)
{
    /// <summary>指令名的第二段，作为命令类。</summary>
    public string CommandClass => CommandName.Split('.')[1];

    /// <summary>解析参数并交给执行器；参数不对直接回失败。</summary>
    public Task<CommandResult> RunAsync(QuickCommandRunner runner, CommandContext context)
    {
        Func<QuickCommandContext, QuickOutcome> run;
        try
        {
            run = Bind(context);
        }
        catch (FineArgumentException ex)
        {
            return Task.FromResult(CommandResult.Fail($"{Title}：{ex.Message}"));
        }

        return runner.RunAsync(new QuickCommand(Key, CommandName, Title, Summary, Summary, run), context);
    }
}

/// <summary>细粒度指令的参数填错了（缺参数、数字写不对）。消息原样给调用方看。</summary>
internal sealed class FineArgumentException(string message) : Exception(message);

/// <summary>细粒度指令登记表。</summary>
internal static class FineCommands
{
    public static IReadOnlyList<FineCommand> All { get; } =
    [
        ViewList.Command,
        MoveView.Command,
        DimList.Command,
        DimDelete.Command,
        DimMove.Command,
        DimEdit.Command,
    ];
}

/// <summary>细粒度指令的参数解析（纯文本，离线可测）。</summary>
internal static class FineArgs
{
    /// <summary>图纸 mm ↔ SolidWorks 的米。</summary>
    public const double Mm = 0.001;

    public static ParameterSpec Text(string name, string description, bool required, int position, string[]? allowed = null)
        => new()
        {
            Name = name,
            Description = description,
            Required = required,
            Position = position,
            AllowedValues = allowed,
        };

    /// <summary>必填的名字（视图名、注解名）。</summary>
    public static string Name(CommandContext context, string parameter)
        => context.GetString(parameter)?.Trim() is { Length: > 0 } text
            ? text
            : throw new FineArgumentException($"缺参数 {parameter}=");

    /// <summary>逗号 / 顿号 / 空白分开的一串名字，去空、去重、保持顺序；一个都没有抛错。</summary>
    public static IReadOnlyList<string> Names(string? text, string parameter)
    {
        var names = (text ?? string.Empty)
            .Split([',', '，', '、', ';', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count > 0 ? names : throw new FineArgumentException($"缺参数 {parameter}=（逗号分开的名字，如 D1,D3）");
    }

    /// <summary>可省的 mm 数（允许负数）；写了但不是数抛错。</summary>
    public static double? Millimeters(string? text, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        if (trimmed.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2].TrimEnd();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : throw new FineArgumentException($"{parameter}={text} 不是数（图纸 mm，如 120 或 -5.5）");
    }

    /// <summary>
    /// 挪到哪：<c>x= y=</c> 给绝对位置（可只给一个，另一个不动），<c>dx= dy=</c> 给相对挪动，两种不能混用、也不能都不给。
    /// 返回值是给 <see cref="Target"/> 的指令，单位图纸 mm。
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
            throw new FineArgumentException("x/y（挪到）与 dx/dy（挪动）只能用一种。");
        if (!absolute && !relative)
            throw new FineArgumentException("缺位置：给 x= y=（挪到，图纸 mm）或 dx= dy=（挪动多少 mm）。");
        return new MoveRequest(absolute, absolute ? x : dx, absolute ? y : dy);
    }

    /// <summary>按 <paramref name="request"/> 从 <paramref name="current"/>（米）算出目标（米）。</summary>
    public static SheetPoint Target(MoveRequest request, SheetPoint current)
        => request.Absolute
            ? new SheetPoint(request.X is { } x ? x * Mm : current.X, request.Y is { } y ? y * Mm : current.Y)
            : new SheetPoint(current.X + (request.X ?? 0) * Mm, current.Y + (request.Y ?? 0) * Mm);

    /// <summary>
    /// 公差：<c>none</c>（或「无」）去掉；一个正数 = 对称 ±（mm）。返回 null 表示去掉。
    /// </summary>
    public static double? Tolerance(string text)
    {
        var trimmed = text.Trim().TrimStart('±', '+');
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) || trimmed == "无" || trimmed == "0")
            return null;
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && double.IsFinite(value)
            ? value
            : throw new FineArgumentException($"tol={text} 写不对：对称公差写正数 mm（如 0.02），去掉写 none");
    }

    /// <summary>米 → 回执里的 mm 文字（最多 3 位小数）。</summary>
    public static string Format(double meters) => (meters / Mm).ToString("0.###", CultureInfo.InvariantCulture);

    public static string Format(SheetPoint point) => $"({Format(point.X)}, {Format(point.Y)})";
}

/// <summary>一次挪动：绝对位置还是相对挪动；X / Y 为 null 的方向不动（单位图纸 mm）。</summary>
internal readonly record struct MoveRequest(bool Absolute, double? X, double? Y);
