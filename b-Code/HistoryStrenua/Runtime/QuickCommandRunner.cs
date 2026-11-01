using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 所有改图、读图指令的唯一入口：解析参数 → 排他 → 附着 SolidWorks → 执行 → 记状态与耗时 → 按来源加反馈、笔记、先回话。
/// </summary>
/// <remarks>
/// 一次只跑一条，是因为所有快捷指令都作用在用户同一个 SolidWorks 的同一个选择集上——
/// 两条同时跑，一条的 ClearSelection 会把另一条正在等的视图选择清掉。
/// </remarks>
/// <param name="options">页面开关与默认技术要求。</param>
/// <param name="bus">命令总线：指令经它调别的模块（如 HistoryApollo）；离线测试为 null。</param>
/// <param name="stepFeedback">AI 调用的改图指令外面包的回执反馈（模块入口给 <see cref="StepFeedback.Wrap"/>）；离线测试为 null。</param>
internal sealed class QuickCommandRunner(StrenuaOptions options, ICommandBus? bus = null,
    Func<Func<QuickCommandContext, QuickOutcome>, Func<QuickCommandContext, QuickOutcome>>? stepFeedback = null)
{
    /// <summary>页面开关（避障、尺寸链模式）。</summary>
    public StrenuaOptions Options { get; } = options;

    /// <summary>笔记区：经 MCP 的回执最上面带上。</summary>
    public WorkNotes Notes { get; } = new(options.DataDirectory);

    /// <summary>状态、回执历史与耗时。</summary>
    public QuickRunLog Log { get; } = new();

    private readonly object _gate = new();
    private CancellationTokenSource? _running;
    private string? _runningKey;
    private string? _runningTitle;
    private string? _runningProgress;
    private System.Diagnostics.Stopwatch? _runningClock;
    private TaskCompletionSource? _runningDone;

    /// <summary>MCP 调用最多等这么久就先回话（调用方有硬时限，超时结果就丢了）；指令在后台接着跑，用 <c>strenua.quick.last</c> 等结果。</summary>
    internal static readonly TimeSpan ReplyBudget = TimeSpan.FromSeconds(40);

    /// <summary><c>strenua.quick.last</c> 经 MCP 最多等多久。</summary>
    internal const int LastWaitSeconds = 40;

    public QuickCommandStatus StatusOf(string key) => Log.StatusOf(key);

    /// <summary>
    /// 执行一条指令。MCP 来的超过 <see cref="ReplyBudget"/> 还没完就先回「还在执行」，不让调用方撞时限、猜时间。
    /// </summary>
    public async Task<CommandResult> RunAsync(QuickCommand command, CommandContext context)
    {
        if (!CommandOrigin.IsAi(context.Source))
            return await RunCoreAsync(command, context, context.Cancellation).ConfigureAwait(false);

        // 先回话之后调用方的取消令牌就不算数了；要停用 strenua.quick.cancel。
        var task = RunCoreAsync(command, context, CancellationToken.None);
        var result = await Task.WhenAny(task, Task.Delay(ReplyBudget)).ConfigureAwait(false) == task
            ? await task.ConfigureAwait(false)
            : CommandResult.Ok(RunningText(command.Key));
        return Notes.Prepend(command.PageClass, result);
    }

    /// <summary>还在跑时给调用方的话：跑了多久、到哪一步、下一步做什么。</summary>
    private string RunningText(string key)
    {
        lock (_gate)
        {
            return _runningKey == key
                ? $"「{_runningTitle}」还在执行，已跑 {_runningClock?.Elapsed.TotalSeconds:0} 秒，当前：{_runningProgress ?? Log.StatusOf(key).State}。指令不会中断；调 strenua.quick.last key={key} 取结果，它完成就立刻返回、最多等 {LastWaitSeconds} 秒。"
                : $"key={key} 刚刚结束，调 strenua.quick.last key={key} 取结果。";
        }
    }

    /// <summary>等正在跑的那条结束，最多 <paramref name="seconds"/> 秒，再回 <see cref="Last"/>。</summary>
    /// <param name="key">指令 key。</param>
    /// <param name="back">往前第几次。</param>
    /// <param name="seconds">最多等几秒。</param>
    /// <param name="notesClass">最上面带这一类的笔记（经 MCP 来的）；null 不带。</param>
    public async Task<CommandResult> LastAsync(string key, int back, int seconds, string? notesClass = null)
    {
        Task? running;
        lock (_gate)
            running = _runningKey == key && back == 0 ? _runningDone?.Task : null;
        if (running is not null && seconds > 0)
            await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(seconds))).ConfigureAwait(false);
        var result = Last(key, back);
        return notesClass is null ? result : Notes.Prepend(notesClass, result);
    }

    private async Task<CommandResult> RunCoreAsync(QuickCommand command, CommandContext context, CancellationToken caller)
    {
        // 带参数的先解析参数，填错直接失败，不附着 SolidWorks。
        var run = command.Run;
        if (command.Bind is not null)
        {
            try
            {
                run = command.Bind(context);
            }
            catch (QuickArgumentException ex)
            {
                var message = $"{command.Title}：{ex.Message}";
                Finish(command.Key, "失败", message);
                return CommandResult.Fail(message);
            }
        }

        // MCP 来的改图指令，回执带本步改动的 SVG（检查类、只读的不改图，不带）。
        if (stepFeedback is not null && !command.Readonly && !command.SelfReport && command.CommandClass != "check" && CommandOrigin.IsAi(context.Source))
            run = stepFeedback(run);

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_runningKey is not null)
            {
                return CommandResult.Fail($"「{_runningTitle}」还在执行，等它结束或先按取消。");
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _runningClock = System.Diagnostics.Stopwatch.StartNew();
            _runningProgress = null;
            _runningDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _running = cancellation;
            _runningKey = command.Key;
            _runningTitle = command.Title;
            Log.SetState(command.Key, "附着 SolidWorks");
        }

        QuickOutcome outcome;
        string? lastProgress = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timeline = new List<string>();
        try
        {
            outcome = await SolidWorksSession.RunAsync(
                session =>
                {
                    var done = run(new QuickCommandContext(
                        session,
                        Options,
                        message =>
                        {
                            lastProgress = message;
                            lock (timeline)
                                timeline.Add($"{clock.Elapsed.TotalSeconds,7:0.0}s {message}");
                            lock (_gate)
                                _runningProgress = message;
                            context.Progress?.Report(message);
                        },
                        state => Log.SetState(command.Key, state),
                        cancellation.Token,
                        bus,
                        !CommandOrigin.IsAi(context.Source)));
                    Log.SaveProfile(command.Key, ProfileText(clock, timeline, session));
                    return done;
                },
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = new QuickOutcome(false, $"「{command.Title}」已取消。");
            Finish(command.Key, "已取消", outcome.Message);
            return CommandResult.Fail(outcome.Message);
        }
        catch (QuickCommandException ex)
        {
            outcome = QuickOutcome.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            // 非预期的 COM / 反射错误：带上类型、出错的 SolidWorks 调用、本模块调用位置和最后一条进度，不翻日志就知道是哪一步。
            outcome = QuickOutcome.Fail($"「{command.Title}」出错：{QuickError.Describe(ex)}"
                + (lastProgress is null ? string.Empty : $"{Environment.NewLine}出错前最后一步：{lastProgress}"));
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
                _runningKey = null;
                _runningTitle = null;
            }

            cancellation.Dispose();
        }

        Finish(command.Key, outcome.Success ? "完成" : "失败", outcome.Message, outcome.Data);
        return outcome.Success ? CommandResult.Ok(outcome.Message, outcome.Data) : CommandResult.Fail(outcome.Message);
    }

    /// <summary>一次执行的耗时：总用时、花在 SolidWorks 调用上的时间、进度时间线、最耗时的方法。</summary>
    private static string ProfileText(System.Diagnostics.Stopwatch clock, List<string> timeline, SolidWorksSession session)
        => $"总用时 {clock.Elapsed.TotalSeconds:0.0}s；SolidWorks 调用 {session.Api.CallCount} 次、{session.Api.CallSeconds:0.0}s"
            + Environment.NewLine + "时间线：" + Environment.NewLine + string.Join(Environment.NewLine, timeline)
            + Environment.NewLine + "最耗时的方法（秒 / 次数）：" + Environment.NewLine + session.Api.ProfileTable();

    /// <summary>取回一条指令上次执行的耗时明细。</summary>
    public CommandResult Perf(string key) => Log.Perf(key);

    /// <summary>取消正在跑的那条。没有在跑的也回成功——点「取消」不该让宿主抢控制台。</summary>
    public CommandResult Cancel()
    {
        lock (_gate)
        {
            if (_running is null)
                return CommandResult.Ok("PowerSW 当前没有正在执行的指令。");
            _running.Cancel();
            return CommandResult.Ok("已请求取消 PowerSW 当前指令。");
        }
    }

    private void Finish(string key, string state, string result, object? data = null)
    {
        Log.Finish(key, state, result, data);
        lock (_gate)
            _runningDone?.TrySetResult();
    }

    /// <summary>
    /// 上一次执行的结论：长指令（一键出图、孔标注全流程）经 MCP 转发时调用方先超时，指令照跑完、回执就丢了——
    /// 用 <c>strenua.quick.last</c> 取回原样的文字与数据（含本步反馈的 SVG）。还在跑回执「执行中」，没跑过回失败。
    /// </summary>
    /// <param name="key">指令 key。</param>
    /// <param name="back">往前第几次（0 = 最近一次）。</param>
    public CommandResult Last(string key, int back = 0)
    {
        if (back == 0)
        {
            lock (_gate)
            {
                if (_runningKey == key)
                    return CommandResult.Ok(RunningText(key));
            }
        }

        return Log.Last(key, back);
    }
}
