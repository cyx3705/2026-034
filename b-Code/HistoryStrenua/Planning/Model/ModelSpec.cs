using System.Globalization;

namespace HistoryStrenua;

/// <summary>草图里的一个封闭图形（草图坐标 mm）。</summary>
internal abstract record SketchShape
{
    /// <summary>中心矩形：中心、宽、高。</summary>
    public sealed record Rect(double X, double Y, double Width, double Height) : SketchShape;

    /// <summary>两角矩形。</summary>
    public sealed record Box(double X1, double Y1, double X2, double Y2) : SketchShape;

    /// <summary>圆：圆心、直径。</summary>
    public sealed record Circle(double X, double Y, double Diameter) : SketchShape;

    /// <summary>封闭折线：顶点按顺序，首尾自动连上。</summary>
    public sealed record Polygon(IReadOnlyList<(double X, double Y)> Points) : SketchShape;

    /// <summary>直槽（腰型孔）：两端圆心、宽。</summary>
    public sealed record Slot(double X1, double Y1, double X2, double Y2, double Width) : SketchShape;
}

/// <summary>
/// 建模类（1.38.0，用户定：不再依赖 SolidWorks MCP，建模、装配并进 Strenua）的参数写法，纯文本、离线可测。
/// </summary>
/// <remarks>
/// <para>草图图形一次给全（AI 一条指令画完一张草图，不逐条线来回）：<c>rect:中心x,中心y,宽,高</c>、<c>box:x1,y1,x2,y2</c>、
/// <c>circle:x,y,直径</c>、<c>circles:直径,x1,y1,x2,y2,…</c>（同径多孔）、<c>poly:x1,y1,x2,y2,…</c>、<c>slot:x1,y1,x2,y2,宽</c>，分号隔开。</para>
/// <para>摆位用「局部轴在总装里朝哪」写旋转（<c>axes=X,-Z,Y</c>：零件 x 轴朝总装 +X、y 轴朝 −Z、z 轴朝 +Y），比欧拉角好核对；必须是右手系。</para>
/// </remarks>
internal static class ModelSpec
{
    public static IReadOnlyList<SketchShape> Shapes(string text)
    {
        var shapes = new List<SketchShape>();
        foreach (var item in text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = item.IndexOfAny([':', '：']);
            if (colon <= 0)
                throw new QuickArgumentException($"图形「{item}」要写成 种类:数,数,…（rect / box / circle / circles / poly / slot）。");
            var kind = item[..colon].Trim().ToLowerInvariant();
            var numbers = Numbers(item[(colon + 1)..], item);
            switch (kind)
            {
                case "rect":
                    Need(numbers, 4, item);
                    Positive(numbers[2], item);
                    Positive(numbers[3], item);
                    shapes.Add(new SketchShape.Rect(numbers[0], numbers[1], numbers[2], numbers[3]));
                    break;
                case "box":
                    Need(numbers, 4, item);
                    if (numbers[0] == numbers[2] || numbers[1] == numbers[3])
                        throw new QuickArgumentException($"图形「{item}」两角在一条线上。");
                    shapes.Add(new SketchShape.Box(numbers[0], numbers[1], numbers[2], numbers[3]));
                    break;
                case "circle":
                    Need(numbers, 3, item);
                    Positive(numbers[2], item);
                    shapes.Add(new SketchShape.Circle(numbers[0], numbers[1], numbers[2]));
                    break;
                case "circles":
                    if (numbers.Count < 3 || numbers.Count % 2 != 1)
                        throw new QuickArgumentException($"图形「{item}」写成 circles:直径,x1,y1,x2,y2,…");
                    Positive(numbers[0], item);
                    for (var i = 1; i < numbers.Count; i += 2)
                        shapes.Add(new SketchShape.Circle(numbers[i], numbers[i + 1], numbers[0]));
                    break;
                case "poly":
                    if (numbers.Count < 6 || numbers.Count % 2 != 0)
                        throw new QuickArgumentException($"图形「{item}」至少三个顶点：poly:x1,y1,x2,y2,x3,y3,…");
                    shapes.Add(new SketchShape.Polygon(Enumerable.Range(0, numbers.Count / 2).Select(i => (numbers[2 * i], numbers[2 * i + 1])).ToList()));
                    break;
                case "slot":
                    Need(numbers, 5, item);
                    Positive(numbers[4], item);
                    if (numbers[0] == numbers[2] && numbers[1] == numbers[3])
                        throw new QuickArgumentException($"图形「{item}」两端圆心重合，写成 circle。");
                    shapes.Add(new SketchShape.Slot(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4]));
                    break;
                default:
                    throw new QuickArgumentException($"不认识图形「{kind}」：可写 rect / box / circle / circles / poly / slot。");
            }
        }

        return shapes.Count > 0 ? shapes : throw new QuickArgumentException("shapes= 是空的。");
    }

    /// <summary>「x,y,z」mm。</summary>
    public static (double X, double Y, double Z) Point(string text, string parameter)
    {
        var numbers = Numbers(text, parameter + "=" + text);
        return numbers.Count == 3 ? (numbers[0], numbers[1], numbers[2]) : throw new QuickArgumentException($"{parameter}={text} 要写 x,y,z（mm）。");
    }

    /// <summary>
    /// 「X,-Z,Y」：零件 x、y、z 轴各朝总装哪根轴（可带负号）。返回三根轴的方向向量（行 = 零件轴）。
    /// 也收 9 个数（三根单位向量，可以是斜的）。
    /// </summary>
    public static double[,] Axes(string text)
    {
        var parts = text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new double[3, 3];
        if (parts.Length == 3 && parts.All(part => part.TrimStart('+', '-').Length == 1))
        {
            for (var i = 0; i < 3; i++)
            {
                var sign = parts[i].StartsWith('-') ? -1 : 1;
                var axis = char.ToUpperInvariant(parts[i].TrimStart('+', '-')[0]) switch
                {
                    'X' => 0,
                    'Y' => 1,
                    'Z' => 2,
                    _ => throw new QuickArgumentException($"axes={text}：每项写 X / Y / Z，可带负号。"),
                };
                rows[i, axis] = sign;
            }
        }
        else if (parts.Length == 9)
        {
            var numbers = Numbers(text, "axes=" + text);
            for (var i = 0; i < 3; i++)
            {
                var length = Math.Sqrt(numbers[3 * i] * numbers[3 * i] + numbers[3 * i + 1] * numbers[3 * i + 1] + numbers[3 * i + 2] * numbers[3 * i + 2]);
                if (length < 1e-9)
                    throw new QuickArgumentException($"axes={text}：第 {i + 1} 根轴是零向量。");
                for (var j = 0; j < 3; j++)
                    rows[i, j] = numbers[3 * i + j] / length;
            }
        }
        else
        {
            throw new QuickArgumentException($"axes={text}：写零件 x,y,z 轴各朝总装哪根轴，如 X,-Z,Y。");
        }

        for (var i = 0; i < 3; i++)
        {
            for (var j = i + 1; j < 3; j++)
            {
                if (Math.Abs(rows[i, 0] * rows[j, 0] + rows[i, 1] * rows[j, 1] + rows[i, 2] * rows[j, 2]) > 1e-6)
                    throw new QuickArgumentException($"axes={text}：三根轴要两两垂直。");
            }
        }

        if (Determinant(rows) < 0)
            throw new QuickArgumentException($"axes={text} 是左手系（镜像），零件摆不出来：把其中一根轴取反。");
        return rows;
    }

    /// <summary>三根轴写回「X,-Z,Y」；斜的写成 9 个数。</summary>
    public static string AxesText(double[,] rows)
    {
        var names = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var hit = Enumerable.Range(0, 3).Where(j => Math.Abs(Math.Abs(rows[i, j]) - 1) < 1e-6).ToList();
            if (hit.Count != 1)
                return string.Join(",", Enumerable.Range(0, 9).Select(k => Format(rows[k / 3, k % 3], "0.####")));
            names.Add((rows[i, hit[0]] < 0 ? "-" : string.Empty) + "XYZ"[hit[0]]);
        }

        return string.Join(",", names);
    }

    /// <summary>SolidWorks 变换数组（16 个数：旋转 9、平移 3（米）、比例 1、空 3）。</summary>
    public static double[] TransformData(double[,] axes, (double X, double Y, double Z) origin)
        =>
        [
            axes[0, 0], axes[0, 1], axes[0, 2], axes[1, 0], axes[1, 1], axes[1, 2], axes[2, 0], axes[2, 1], axes[2, 2],
            origin.X / 1000, origin.Y / 1000, origin.Z / 1000, 1, 0, 0, 0,
        ];

    /// <summary>草图平面的写法：前视 / 上视 / 右视（也认 front / top / right），其余当基准面名。</summary>
    public static int? StandardPlane(string text) => text.Trim().ToLowerInvariant() switch
    {
        "前视" or "前视基准面" or "front" or "f" => 0,
        "上视" or "上视基准面" or "top" or "t" => 1,
        "右视" or "右视基准面" or "right" or "r" => 2,
        _ => null,
    };

    /// <summary>三个标准基准面上草图坐标与零件坐标的对应（回执里写给 AI）。</summary>
    public static string PlaneAxes(int standard) => standard switch
    {
        0 => "草图 x = 零件 X，草图 y = 零件 Y，拉伸朝 +Z",
        1 => "草图 x = 零件 X，草图 y = 零件 −Z，拉伸朝 +Y",
        _ => "草图 x = 零件 −Z，草图 y = 零件 Y，拉伸朝 +X",
    };

    private static double Determinant(double[,] m)
        => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
           - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
           + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    private static List<double> Numbers(string text, string item)
    {
        var numbers = new List<double>();
        foreach (var part in text.Split([',', '，', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new QuickArgumentException($"「{item}」里的「{part}」不是数。");
            numbers.Add(value);
        }

        return numbers;
    }

    private static void Need(List<double> numbers, int count, string item)
    {
        if (numbers.Count != count)
            throw new QuickArgumentException($"图形「{item}」要 {count} 个数，给了 {numbers.Count} 个。");
    }

    private static void Positive(double value, string item)
    {
        if (value <= 0)
            throw new QuickArgumentException($"图形「{item}」的尺寸要大于 0。");
    }

    public static string Format(double value, string format = "0.##") => value.ToString(format, CultureInfo.InvariantCulture);
}
