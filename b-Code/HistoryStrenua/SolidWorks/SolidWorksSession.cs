using System.Runtime.InteropServices;

namespace HistoryStrenua.SolidWorks;

/// <summary>
/// 附着到用户**正在用**的那个 SolidWorks。
/// </summary>
/// <remarks>
/// PowerSW 与 HistoryMinerva 的转换管线用 SolidWorks 的方式正好相反：转换要么自己起一个专属进程，
/// 要么附着后把界面藏起来、关掉用户交互、做完还可能退出。这里一样都不许做——
/// 不启动（没开就报「没在运行」）、不改 <c>Visible</c> / <c>UserControl</c>、不 <c>ExitApp</c>，
/// 收工只放下自己手里的引用。
/// </remarks>
internal sealed class SolidWorksSession : IDisposable
{
    private const string ProgId = "SldWorks.Application";

    /// <summary>MK_E_UNAVAILABLE：运行对象表里没有这个类（SolidWorks 没在运行）。</summary>
    private const int NotRunning = unchecked((int)0x800401E3);

    private SolidWorksSession(SolidWorksApi api, object application)
    {
        Api = api;
        Application = application;
    }

    public SolidWorksApi Api { get; }

    /// <summary><c>ISldWorks</c>。</summary>
    public object Application { get; }

    /// <summary>必须在 STA 线程上调用，之后整个会话都留在这条线程上。</summary>
    public static SolidWorksSession AttachRunning(CancellationToken cancellation)
    {
        var type = Type.GetTypeFromProgID(ProgId, throwOnError: false)
            ?? throw new QuickCommandException("本机没有注册 SolidWorks（SldWorks.Application）。");

        object application;
        try
        {
            var classId = type.GUID;
            GetActiveObject(ref classId, IntPtr.Zero, out application);
        }
        catch (COMException ex) when (ex.HResult == NotRunning)
        {
            throw new QuickCommandException("SolidWorks 没有在运行。PowerSW 只作用于你已经打开的 SolidWorks，不会替你启动它。");
        }

        var api = SolidWorksApi.Load(type.GUID, cancellation);
        api.Own(application);
        return new SolidWorksSession(api, application);
    }

    /// <summary>当前活动文档（<c>IModelDoc2</c>），没有打开任何文档时为 null。</summary>
    public object? ActiveDocument()
        => Api.Call(Application, "ISldWorks", "get_ActiveDoc");

    public void Dispose() => Api.Dispose();

    /// <summary>
    /// 在一条新的 STA 线程上附着 SolidWorks 并执行 <paramref name="work"/>。
    /// SolidWorks 的 COM 对象属于单线程套间，拿到它们的线程也必须是用它们的线程。
    /// </summary>
    public static Task<T> RunAsync<T>(Func<SolidWorksSession, T> work, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var session = AttachRunning(cancellation);
                completion.SetResult(work(session));
            }
            catch (OperationCanceledException)
            {
                completion.SetCanceled(cancellation);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "HistoryStrenua SolidWorks",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid classId,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
