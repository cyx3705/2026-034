namespace HistoryStrenua;

/// <summary>要加的一个孔位尺寸。</summary>
/// <param name="Axis">方向。</param>
/// <param name="FromEdgeIndex">起点孔边的下标；null 表示从基准边量。</param>
/// <param name="ToEdgeIndex">终点孔边的下标。</param>
/// <param name="TextAt">尺寸文字放在图纸上的位置（米）。</param>
/// <param name="Prefix">尺寸值前面的文字；阵列标法是「(N-1) x 间距 =」，其余为空。</param>
/// <param name="Expected">规划的尺寸值（模型，米），建好后读回核对；0 表示不核对。</param>
internal sealed record PositionDimension(PositionAxis Axis, int? FromEdgeIndex, int ToEdgeIndex, SheetPoint TextAt, string Prefix, double Expected = 0);

/// <summary>
/// 尺寸链模式下一个方向的一组 SolidWorks「尺寸链」（坐标尺寸，<c>swOrdinateDimension</c>）：
/// 0 点是基准边（水平组用左侧直边、竖直组用上侧直边），其后依次是 <paramref name="EdgeIndexes"/> 的孔，在 <paramref name="At"/> 一次建成整组。
/// </summary>
/// <param name="Axis">方向：水平组在视图上方量各列的 X，竖直组在视图左侧量各行的 Y。</param>
/// <param name="EdgeIndexes">每一列（行）一个孔边下标，按离基准由近到远（不含 0 点）。</param>
/// <param name="Values">每一站离基准的距离（模型长度，米），与 <paramref name="EdgeIndexes"/> 一一对应，用来核对建出来的值。</param>
/// <param name="At">0 点那个尺寸文字的位置（图纸坐标，米）；整组文字排成一列，SolidWorks 自己折弯错开。</param>
internal sealed record OrdinateGroup(PositionAxis Axis, IReadOnlyList<int> EdgeIndexes, IReadOnlyList<double> Values, SheetPoint At)
{
    /// <summary>这组的尺寸个数：0 点一个，加每站一个。</summary>
    public int Count => EdgeIndexes.Count + 1;
}

/// <summary>视图里已有的一个尺寸：它的类型（<c>swDimensionType_e</c>）与它连着的圆边圆心、中心线。</summary>
/// <param name="Index">回指调用方手里那个尺寸。</param>
/// <param name="Type"><c>IDisplayDimension.Type2</c>。</param>
/// <param name="Centers">它附着的圆边的圆心（图纸坐标）。</param>
/// <param name="Lines">它附着的视图草图线（中心符号线的线、中心线），图纸坐标。</param>
/// <param name="Dangling">悬空：至少一头的附着丢了（<c>IAnnotation.IsDangling</c>，或附着对象读出来是空的）。</param>
/// <param name="Attached">坐标尺寸每个附着对象的几何写成的键（直边两端、圆心或草图线）；同组的坐标尺寸都含组的 0 点那个键。其余尺寸为 null。</param>
internal sealed record ExistingDimension(
    int Index, int Type, IReadOnlyList<SheetPoint> Centers, IReadOnlyList<SheetSegment>? Lines = null, bool Dangling = false,
    IReadOnlyList<string>? Attached = null);

/// <summary>规划结果。</summary>
/// <param name="Dimensions">要加的线性尺寸，先水平后竖直，同方向按层由里到外；尺寸链模式下为空。</param>
/// <param name="HoleCount">视图里认出的孔数（同心的算一个，一个腰型孔算一个）。</param>
/// <param name="KindCount">孔的种数。</param>
/// <param name="PatternCount">用了阵列标法的尺寸个数。</param>
/// <param name="SlotCount">其中腰型孔的个数。</param>
/// <param name="Ordinates">尺寸链模式（1.7.0）的坐标尺寸组，先水平后竖直；其余模式为空。</param>
internal sealed record HolePositionPlan(
    IReadOnlyList<PositionDimension> Dimensions, int HoleCount, int KindCount, int PatternCount, int SlotCount = 0,
    IReadOnlyList<OrdinateGroup>? Ordinates = null)
{
    /// <summary>回执里的「N 个孔（含 M 个腰型孔）共 K 种」。</summary>
    public string Summary => HoleCalloutPlanner.Summary(HoleCount, SlotCount, KindCount);

    /// <summary>坐标尺寸组（没有时为空表）。</summary>
    public IReadOnlyList<OrdinateGroup> OrdinateGroups => Ordinates ?? [];

    /// <summary>要加的尺寸总数：线性尺寸，加上坐标尺寸组里每一站（含 0 点）一个。</summary>
    public int Count => Dimensions.Count + OrdinateGroups.Sum(group => group.Count);
}

/// <summary>
/// 孔位尺寸避开图纸禁区用的（1.16.0）：视图（零件轮廓）最右、最下沿，图纸上不许压的地方（标题栏、修改栏、图号框、图纸注解、别的视图），
/// 图框（尺寸出了图框也算压）。图纸坐标，米。
/// </summary>
internal sealed record PositionSpace(double Right, double Bottom, IReadOnlyList<SheetRect> Blocked, SheetRect? Frame = null);
