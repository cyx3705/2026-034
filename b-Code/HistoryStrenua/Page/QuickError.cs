using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>
/// 非预期异常（COM / 反射错误）的回执写法（1.24.0）：类型 + 消息 + 出错的 SolidWorks 调用 + 本模块里的调用位置，
/// 让 AI 和人不翻日志也知道是哪一步、哪个 API 出的错。
/// </summary>
internal static partial class QuickError
{
    private const string CallKey = "strenua.call";

    /// <summary>
    /// 异常过滤器里用：记下是哪个 <c>I接口.方法</c> 抛的（反射包的那层与里面的 COM 异常都记），恒回 false 不接住。
    /// 只记第一次，内层调用记下的不被外层盖掉。
    /// </summary>
    public static bool TagCall(Exception ex, string interfaceName, string method)
    {
        var call = $"{interfaceName}.{method}";
        foreach (var target in new[] { ex, ex.InnerException })
        {
            if (target is not null && !target.Data.Contains(CallKey))
                target.Data[CallKey] = call;
        }

        return false;
    }

    /// <summary>多步流程里该记成「这一步没成」接着做的异常：不是取消，也不是已写好中文原因的 <see cref="QuickCommandException"/>。</summary>
    public static bool Unexpected(Exception ex) => ex is not (OperationCanceledException or QuickCommandException);

    /// <summary>例：<c>TargetException: Object does not match target type.（调用 IAnnotation.GetDisplayData；位置 SymmetryAxes.DisplayLines ← SymmetryAxes.Insert ← SymmetryAxes.RunView）</c></summary>
    public static string Describe(Exception ex)
    {
        var notes = new List<string>();
        if (ex.Data[CallKey] is string call)
            notes.Add("调用 " + call);
        var frames = Frames(ex.StackTrace);
        if (frames.Count > 0)
            notes.Add("位置 " + string.Join(" ← ", frames));
        return $"{ex.GetType().Name}: {ex.Message}" + (notes.Count > 0 ? $"（{string.Join("；", notes)}）" : string.Empty);
    }

    /// <summary>调用栈里本模块的前三层（略过 SolidWorksApi 的转发层），写成 <c>类.方法[:行]</c>。</summary>
    internal static List<string> Frames(string? stackTrace)
    {
        var frames = new List<string>();
        foreach (var line in (stackTrace ?? string.Empty).Split('\n'))
        {
            var match = FrameLine().Match(line);
            if (!match.Success || match.Groups["type"].Value.EndsWith("SolidWorksApi", StringComparison.Ordinal))
                continue;
            var type = match.Groups["type"].Value;
            type = type[(type.LastIndexOf('.') + 1)..];
            var method = match.Groups["method"].Value;
            // 局部函数、lambda 编译成 <外层>g__名|… / <外层>b__…，只留外层方法名。
            if (method.StartsWith('<') && method.IndexOf('>') is > 1 and var close)
                method = method[1..close];
            var frame = $"{type}.{method}" + (match.Groups["line"].Success ? ":" + match.Groups["line"].Value : string.Empty);
            if (frames.Count == 0 || frames[^1] != frame)
                frames.Add(frame);
            if (frames.Count == 3)
                break;
        }

        return frames;
    }

    [GeneratedRegex(@"at HistoryStrenua\.(?<type>[\w\.]+?)(?:[\.+]<>c(?:__DisplayClass\w+)?)?\.(?<method>[\w<>|`]+)\(.*?\)(?: in .*:line (?<line>\d+))?")]
    private static partial Regex FrameLine();
}
