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
