namespace HistoryStrenua;

/// <summary>
/// 多处要用的 SolidWorks 枚举值，只留这一份（不编译期引用 Interop，所以自己写）。只用一处的仍写在调用处。
/// </summary>
internal static class SwConst
{
    public const int DocumentPart = 1;
    public const int DocumentAssembly = 2;
    public const int DocumentDrawing = 3;

    public const int AnnotationDatum = 2;

    public const int AnnotationGtol = 5;

    public const int AnnotationDimension = 4;
    public const int AnnotationNote = 6;
    public const int AnnotationDowel = 10;
    public const int AnnotationCenterMark = 13;
    public const int AnnotationCenterLine = 15;

    public const int AngularDimension = 3;
    public const int RadialDimension = 5;
    public const int DiameterDimension = 6;

    // swDimensionTextParts_e（SetText / GetText 的段）
    public const int TextPrefix = 1;
    public const int TextSuffix = 2;

    public const int ViewSheet = 1;
    public const int ProjectedView = 4;

    public const int SaveSilent = 1;
}
