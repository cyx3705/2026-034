using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>快捷指令执行时手里的东西。</summary>
internal sealed class QuickCommandContext(
    SolidWorksSession session,
    StrenuaOptions options,
    Action<string> report,
    Action<string> setState,
    CancellationToken cancellation,
    ICommandBus? bus = null,
    bool popups = true)
{
    public SolidWorksSession Session { get; } = session;

    public SolidWorksApi Api => Session.Api;

    /// <summary>本条命令开始处理的工程图。多步流程锁定这张图，避免 SW 激活引用零件后余下步骤跑错文档。</summary>
    public object? TargetDrawing { get; set; }

    /// <summary>只活一条指令的缓存，按类型取（如 <see cref="HoleScan.Scan"/> 的视图识别结果）。</summary>
    public T Scoped<T>() where T : class, new()
    {
        if (!_scoped.TryGetValue(typeof(T), out var item))
            _scoped[typeof(T)] = item = new T();
        return (T)item;
    }

    private readonly Dictionary<Type, object> _scoped = [];

    /// <summary>页面开关（避障、尺寸链模式）的当前值。</summary>
    public StrenuaOptions Options { get; } = options;

    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>一行进度，进宿主控制台。</summary>
    public void Report(string message) => report(message);

    /// <summary>改这条指令的状态（<c>strenua.quick.list</c> 里看得到），如「等待点选视图」。</summary>
    public void SetState(string state) => setState(state);

    /// <summary>
    /// 经命令总线安静执行一条别的模块的指令（1.14.0，调 HistoryApollo）并等它回来。在 SolidWorks 的 STA 线程上阻塞等待：
    /// 这条线程没有同步上下文，总线的续体落在线程池，不会互等；等待期间不碰 SolidWorks。没有总线（离线测试）时回失败回执。
    /// </summary>
    public CommandResult Invoke(string commandText)
        => bus is null
            ? CommandResult.Fail("没有命令总线（离线运行），调不到别的模块。")
            : Session.Yield(() => bus.InvokeAsync(commandText, StrenuaIdentity.CommandSource, Cancellation).GetAwaiter().GetResult());

    /// <summary>
    /// 经命令总线发一条别的模块的指令、不等它回来（1.16.0，检查结果弹窗 <c>aurora.ui.dialog</c>：它要等人关窗才回）。
    /// 失败只进控制台不影响本指令；没有总线（离线测试）或 AI 调用时（1.29.0，用户定：只有手动点才弹窗）什么都不做。
    /// </summary>
    public void Post(string commandText)
    {
        if (bus is null || !popups)
            return;
        _ = bus.InvokeAsync(commandText, StrenuaIdentity.CommandSource, CancellationToken.None).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully)
                report("弹窗没弹出来：" + (task.Exception?.GetBaseException().Message ?? "已取消"));
            else if (!task.Result.Success)
                report("弹窗没弹出来：" + task.Result.Message);
        }, TaskScheduler.Default);
    }
}
