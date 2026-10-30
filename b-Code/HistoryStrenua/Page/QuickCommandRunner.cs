using System.Globalization;
using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 执行快捷指令：一次只跑一条、可取消，并记下每条指令的状态给 <c>strenua.quick.list</c>。
/// </summary>
/// <remarks>
/// 一次只跑一条，是因为所有快捷指令都作用在用户同一个 SolidWorks 的同一个选择集上——
/// 两条同时跑，一条的 ClearSelection 会把另一条正在等的视图选择清掉。
/// </remarks>
/// <param name="options">页面开关与默认技术要求。</param>
/// <param name="bus">命令总线：「AI 填写技术要求」经它调 HistoryApollo（1.14.0）；离线测试为 null。</param>
internal sealed class QuickCommandRunner(StrenuaOptions options, ICommandBus? bus = null)
{
    /// <summary>页面开关（避障、尺寸链模式）。</summary>
    public StrenuaOptions Options { get; } = options;

    private readonly object _gate = new();
    private readonly Dictionary<string, QuickCommandStatus> _status = new(StringComparer.Ordinal);
    private CancellationTokenSource? _running;
    private string? _runningKey;
    private string? _runningTitle;

    /// <summary>一条指令的状态；<paramref name="Data"/> 是上次回执带的数据（1.22.0，<c>strenua.quick.last</c> 取回用）。</summary>
    internal sealed record QuickCommandStatus(string State, string Result, DateTime? FinishedAt, object? Data = null);

    public QuickCommandStatus StatusOf(string key)
    {
        lock (_gate)
            return _status.TryGetValue(key, out var status) ? status : new QuickCommandStatus("就绪", string.Empty, null);
    }

    public async Task<CommandResult> RunAsync(QuickCommand command, CommandContext context)
    {
        // 1.20.0：带参数的先解析参数，填错直接失败，不附着 SolidWorks。
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

        // 1.21.0：MCP 来的改图指令，回执带本步改动的 SVG（检查类、只读的不改图，不带）。
        if (!command.Readonly && !command.SelfReport && command.CommandClass != "check" && StepFeedback.Wanted(context.Source))
            run = StepFeedback.Wrap(run);

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_runningKey is not null)
            {
                return CommandResult.Fail($"「{_runningTitle}」还在执行，等它结束或先按取消。");
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation);
            _running = cancellation;
            _runningKey = command.Key;
            _runningTitle = command.Title;
            _status[command.Key] = new QuickCommandStatus("附着 SolidWorks", string.Empty, null);
        }

        QuickOutcome outcome;
        string? lastProgress = null;
        try
        {
            outcome = await SolidWorksSession.RunAsync(
                session => run(new QuickCommandContext(
                    session,
                    Options,
                    message =>
                    {
                        lastProgress = message;
                        context.Progress?.Report(message);
                    },
                    state => SetState(command.Key, state),
                    cancellation.Token,
                    bus)),
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
            // 非预期的 COM / 反射错误：带上类型、出错的 SolidWorks 调用、本模块调用位置和最后一条进度（1.24.0），不翻日志就知道是哪一步。
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

    private void SetState(string key, string state)
    {
        lock (_gate)
            _status[key] = new QuickCommandStatus(state, string.Empty, null);
    }

    private void Finish(string key, string state, string result, object? data = null)
    {
        lock (_gate)
            _status[key] = new QuickCommandStatus(state, result, DateTime.Now, data);
    }

    /// <summary>
    /// 上一次执行的结论（1.22.0）：长指令（一键出图、孔标注全流程）经 MCP 转发时调用方先超时，指令照跑完、回执就丢了——
    /// 用 <c>strenua.quick.last</c> 取回原样的文字与数据（含本步反馈的 SVG）。还在跑回执「执行中」，没跑过回失败。
    /// </summary>
    public CommandResult Last(string key)
    {
        lock (_gate)
        {
            if (_runningKey == key)
                return CommandResult.Ok($"「{_runningTitle}」还在执行（{StatusOf(key).State}），等一会儿再取。");
            if (!_status.TryGetValue(key, out var status) || status.FinishedAt is null)
                return CommandResult.Fail($"key={key} 这次启动后还没执行过。");
            var head = $"[{status.State} {FormatTime(status.FinishedAt)}] ";
            return status.Data is null ? CommandResult.Ok(head + status.Result) : CommandResult.Ok(head + status.Result, status.Data);
        }
    }

    internal static string FormatTime(DateTime? time)
        => time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
}
