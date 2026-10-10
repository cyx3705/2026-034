namespace HistoryStrenua;

/// <summary>
/// 钣金折弯切线（1.17.0，用户定：折弯处那条线不能标）：钣金件视图里，平板面与折弯圆柱面相切的那条边会画成一条直线
/// （WTJYQ-04-02 右防护板：总宽 450 旁边多出一条 447.79，孔位从它量出 52.79、2.21）。它不是零件的边界，量到它的尺寸带两位小数也没用。
/// </summary>
/// <remarks>
/// <para>认法：直边旁边贴着一张圆柱面、边与圆柱轴线平行（直边落在圆柱面上只能是母线，即相切处），且这张面属于钣金体
/// （<c>IBody2.IsSheetMetal</c>）。孔壁也是圆柱面，但孔口是圆边、轴线垂直板面，不会对上；机加件的圆角切线不在这里管（不是钣金体）。</para>
/// <para>在 <see cref="HoleScan.Scan"/> 收直边时就去掉，所以基准、外轮廓、方形槽、孔位尺寸、未标尺寸都看不到它——
/// 外轮廓的射线也不再被它挡，但它本来就在零件轮廓里面，不影响判外轮廓。</para>
/// </remarks>
internal static class SheetMetal
{
    /// <summary>直边方向与圆柱轴线算平行的容差（夹角正弦，约 0.06°）。</summary>
    public const double ParallelTolerance = 1e-3;

    /// <summary>直边（方向 <paramref name="line"/>）是不是贴在这张圆柱面（轴线 <paramref name="axis"/>）上的切线：两者平行。</summary>
    public static bool AlongAxis(ModelDirection line, ModelDirection axis)
    {
        var (a, b) = (line.Normalized(), axis.Normalized());
        if (a.Dot(a) <= 0 || b.Dot(b) <= 0)
            return false;
        var (cx, cy, cz) = (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        return Math.Sqrt(cx * cx + cy * cy + cz * cz) <= ParallelTolerance;
    }
}
