using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;

namespace HistoryStrenua;

/// <summary>模块装配入口：造执行器，把各处声明的指令按顺序登记到宿主。本身不含指令逻辑。</summary>
/// <remarks>
/// 判断一段代码该不该进这个仓，用这条：它是否作用于用户**正在用**的 SolidWorks、
/// 让一次手工操作变成一下点击。批量转换、离线处理文件属于 HistoryMinerva。
/// </remarks>
public sealed class HistoryStrenuaModule : IModuleContextAware
{
    /// <summary>宿主在装载时注入权威指令总线与命令注册器。</summary>
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new StrenuaOptions(Path.Combine(context.Environment.DataDirectory, StrenuaOptions.FileName));
        TechTemplates.Bus = context.Bus; // 1.41.0：技术要求模板经 diana.z 读源码区的 z 级文件夹
        var runner = new QuickCommandRunner(options, context.Bus, StepFeedback.Wrap);
        context.RegisterCommands(registry => Register(registry, runner, context.Bus));
    }

    /// <param name="registry">注册器。</param>
    /// <param name="runner">执行器（带页面开关与默认技术要求）。</param>
    /// <param name="bus">命令总线：设默认技术要求后让 Aurora 刷新模板表格；离线测试为 null。</param>
    internal static void Register(ICommandRegistrar registry, QuickCommandRunner runner, ICommandBus? bus = null)
    {
        IEnumerable<CommandDescriptor> commands =
        [
            .. QuickCommands.All.Select(command => QuickAdminCommands.Descriptor(command, runner)),
            .. QuickAdminCommands.All(runner),
            .. TechCommands.All(runner, bus),
            PartProperties.DefaultDescriptor(runner.Options),
            .. WorkNoteCommands.All(runner.Notes),
            .. MymoooCatalog.All(),
            .. PageCommands.All(runner.Options),
        ];
        foreach (var command in commands)
            registry.Register(command);
    }
}
