using System.Globalization;

namespace HistoryStrenua;

/// <summary>配合指令里指一个几何的写法：<c>零部件@F12</c>（面）、<c>零部件@前视 / 上视 / 右视 / 原点</c>、<c>@前视</c>（总装自己的）、只写零部件名（锁定用）。</summary>
internal sealed record MateTarget(string? Component, MateTarget.Kinds Kind, int Index)
{
    public enum Kinds
    {
        Face,
        Plane,
        Origin,
        Component,
    }

    public override string ToString() => Kind switch
    {
        Kinds.Face => $"{Component}@F{Index}",
        Kinds.Plane => $"{Component}@{MateSpec.PlaneNames[Index]}",
        Kinds.Origin => $"{Component}@原点",
        _ => Component ?? string.Empty,
    };
}

/// <summary>
/// 装配类配合指令（1.39.0，用户定：先摆位置后加配合）的参数写法，纯文本、离线可测。
/// </summary>
/// <remarks>
/// 配合种类值即 <c>swMateType_e</c>，对齐即 <c>swMateAlign_e</c>（本机 swconst.tlb 读出，SolidWorks 2025）。
/// 面编号与 <c>strenua.asm.contact</c> 回执里的 F 序号相同：零部件各实体 <c>GetFaces</c> 依次排下来，从 0 起。
/// </remarks>
internal static class MateSpec
{
    public static readonly string[] PlaneNames = ["前视", "上视", "右视"];

    /// <summary>(swMateType_e, 中文名, 要不要数值)。</summary>
    public static (int Type, string Name, string? Value) Type(string text) => text.Trim().ToLowerInvariant() switch
    {
        "重合" or "coincident" => (0, "重合", null),
        "同轴心" or "同轴" or "concentric" => (1, "同轴心", null),
        "垂直" or "perpendicular" => (2, "垂直", null),
        "平行" or "parallel" => (3, "平行", null),
        "相切" or "tangent" => (4, "相切", null),
        "距离" or "distance" => (5, "距离", "mm"),
        "角度" or "angle" => (6, "角度", "°"),
        "锁定" or "lock" => (16, "锁定", null),
        _ => throw new QuickArgumentException($"type={text}：可写 重合 / 同轴心 / 垂直 / 平行 / 相切 / 距离 / 角度 / 锁定（或英文）。"),
    };

    /// <summary>swMateType_e 写回中文（配合列表用）。</summary>
    public static string TypeName(int type) => type switch
    {
        0 => "重合",
        1 => "同轴心",
        2 => "垂直",
        3 => "平行",
        4 => "相切",
        5 => "距离",
        6 => "角度",
        8 => "对称",
        11 => "宽度",
        16 => "锁定",
        _ => $"其他({type})",
    };

    /// <summary>swAddMateError_e 写成人话（1 = 没错）。</summary>
    public static string Error(int code) => code switch
    {
        2 => "这两个几何不能加这种配合",
        3 => "对齐方式不对",
        4 => "选的几何不对",
        5 => "过定义（和已有配合或固定冲突）",
        _ => $"SolidWorks 没加上（错误码 {code}）",
    };

    /// <summary>对齐：同向 0、反向 1、最近 2（默认，跟着现在的摆位走）。</summary>
    public static int Align(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "最近" or "closest" => 2,
        "同向" or "aligned" => 0,
        "反向" or "anti" or "anti_aligned" => 1,
        _ => throw new QuickArgumentException($"align={text}：写 同向 / 反向 / 最近。"),
    };

    public static MateTarget Target(string text, string parameter)
    {
        var at = text.LastIndexOf('@');
        if (at < 0)
            return text.Trim().Length > 0 ? new MateTarget(text.Trim(), MateTarget.Kinds.Component, 0) : throw new QuickArgumentException($"{parameter}= 是空的。");
        var component = text[..at].Trim();
        var what = text[(at + 1)..].Trim();
        var owner = component.Length > 0 ? component : null;
        if (what.Length > 1 && (what[0] is 'F' or 'f') && int.TryParse(what[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return owner is null ? throw new QuickArgumentException($"{parameter}={text}：面要写零部件名，如 底板-1@F12。") : new MateTarget(owner, MateTarget.Kinds.Face, index);
        if (ModelSpec.StandardPlane(what) is { } plane)
            return new MateTarget(owner, MateTarget.Kinds.Plane, plane);
        if (what is "原点" or "origin")
            return new MateTarget(owner, MateTarget.Kinds.Origin, 0);
        throw new QuickArgumentException($"{parameter}={text}：@ 后写 F序号（接触面检测回执里的面）/ 前视 / 上视 / 右视 / 原点。");
    }
}
