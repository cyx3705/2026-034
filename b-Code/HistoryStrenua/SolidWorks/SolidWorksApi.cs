using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.Win32;

namespace HistoryStrenua.SolidWorks;

/// <summary>
/// 按接口名 + 方法名调用 SolidWorks API，并记下拿到手的每一个 COM 对象，收工时统一释放。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么走反射而不是编译期引用 Interop</b>：官方 Interop 只在装了 SolidWorks 的机器上有，
/// 编译期引用它，没装 SolidWorks 的机器连模块都构建不了。运行时从安装目录加载
/// （与 HistoryMinerva Worker 的做法一致）。
/// </para>
/// <para>
/// <b>为什么装进默认装载上下文</b>：宿主把模块装进可卸载的 <c>AssemblyLoadContext</c>，
/// 而可卸载程序集里的类型不能参与 COM 互操作。Interop 放进默认上下文，本模块自己一个
/// <c>[ComImport]</c> 类型都不定义——模块照样能热卸载，Interop 留在进程里无害。
/// </para>
/// <para>
/// <b>为什么自己重试</b>：没有 OLE 消息过滤器（它要求在本程序集里实现一个 COM 接口，
/// 见上一条），SolidWorks 忙（弹着模态框、正在重建）时调用会立即被拒。这里对
/// 「被拒 / 稍后重试」两种 HRESULT 限时重试，别的错误原样抛出。
/// </para>
/// <para>本类只能在创建它的那条 STA 线程上使用。</para>
/// </remarks>
internal sealed class SolidWorksApi : IDisposable
{
    private const string InteropName = "SolidWorks.Interop.sldworks";
    private const string InterfacePrefix = "SolidWorks.Interop.sldworks.";

    /// <summary>RPC_E_CALL_REJECTED：服务端拒绝了这次调用（通常正忙）。</summary>
    private const int CallRejected = unchecked((int)0x80010001);

    /// <summary>RPC_E_SERVERCALL_RETRYLATER：服务端要求稍后重试。</summary>
    private const int RetryLater = unchecked((int)0x8001010A);

    private static readonly TimeSpan RetryBudget = TimeSpan.FromSeconds(30);

    private readonly Assembly _interop;
    private readonly CancellationToken _cancellation;
    private readonly Dictionary<string, Type> _interfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Interface, string Method), MethodInfo> _methods = new();
    private readonly HashSet<object> _owned = new(ReferenceEqualityComparer.Instance);

    private SolidWorksApi(Assembly interop, CancellationToken cancellation)
    {
        _interop = interop;
        _cancellation = cancellation;
    }

    /// <summary>加载本机 SolidWorks 的官方 Interop。</summary>
    public static SolidWorksApi Load(Guid applicationClassId, CancellationToken cancellation)
        => new(LoadInterop(applicationClassId), cancellation);

    /// <summary>
    /// 调用 <c>I<paramref name="interfaceName"/></c> 上的方法（属性写 <c>get_X</c>）。
    /// 返回值里的 COM 对象（含数组元素）都记账，<see cref="Dispose"/> 时释放。
    /// </summary>
    public object? Call(object target, string interfaceName, string method, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(target);
        var info = Method(interfaceName, method);
        var deadline = DateTime.UtcNow + RetryBudget;
        while (true)
        {
            _cancellation.ThrowIfCancellationRequested();
            try
            {
                return Own(info.Invoke(target, arguments));
            }
            catch (TargetInvocationException ex) when (ex.InnerException is COMException com
                                                        && com.HResult is CallRejected or RetryLater
                                                        && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    public bool CallBool(object target, string interfaceName, string method, params object?[] arguments)
        => Convert.ToBoolean(Call(target, interfaceName, method, arguments));

    public int CallInt(object target, string interfaceName, string method, params object?[] arguments)
        => Convert.ToInt32(Call(target, interfaceName, method, arguments));

    public string CallString(object target, string interfaceName, string method, params object?[] arguments)
        => Convert.ToString(Call(target, interfaceName, method, arguments)) ?? string.Empty;

    /// <summary>返回 <c>VARIANT</c> 数组的调用。SolidWorks 用空值表示「一个都没有」，这里统一成空数组。</summary>
    public object[] CallArray(object target, string interfaceName, string method, params object?[] arguments)
        => Call(target, interfaceName, method, arguments) switch
        {
            object[] items => items,
            Array array => array.Cast<object>().ToArray(),
            _ => [],
        };

    public double[] CallDoubles(object target, string interfaceName, string method, params object?[] arguments)
        => Call(target, interfaceName, method, arguments) switch
        {
            double[] values => values,
            Array array => array.Cast<object>().Select(Convert.ToDouble).ToArray(),
            _ => [],
        };

    /// <summary>释放本次会话里经手的全部 COM 对象。不会关闭 SolidWorks，只放下我们的引用。</summary>
    public void Dispose()
    {
        foreach (var item in _owned)
        {
            try
            {
                Marshal.FinalReleaseComObject(item);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
            {
            }
        }

        _owned.Clear();
    }

    /// <summary>把外部拿到的 COM 对象（如运行对象表里的 Application）也纳入记账。</summary>
    public T Own<T>(T value)
    {
        switch (value)
        {
            case null:
                break;
            case object[] items:
                foreach (var item in items)
                    Own(item);
                break;
            default:
                if (Marshal.IsComObject(value))
                    _owned.Add(value);
                break;
        }

        return value;
    }

    private MethodInfo Method(string interfaceName, string method)
    {
        if (_methods.TryGetValue((interfaceName, method), out var cached))
            return cached;

        var type = Interface(interfaceName);
        var info = type.GetMethod(method)
            ?? throw new MissingMethodException(type.FullName, method);
        _methods[(interfaceName, method)] = info;
        return info;
    }

    private Type Interface(string name)
    {
        if (_interfaces.TryGetValue(name, out var cached))
            return cached;

        var type = _interop.GetType(InterfacePrefix + name, throwOnError: false)
            ?? throw new TypeLoadException($"SolidWorks Interop 里没有 {name}");
        _interfaces[name] = type;
        return type;
    }

    /// <summary>
    /// 默认上下文里已经有就复用；否则从 COM 注册的 sldworks.exe 所在目录加载 <c>api\redist</c> 下那份。
    /// 安装目录取自注册表，不猜路径。
    /// </summary>
    private static Assembly LoadInterop(Guid applicationClassId)
    {
        var loaded = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, InteropName, StringComparison.Ordinal));
        if (loaded is not null)
            return loaded;

        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{{{applicationClassId}}}\LocalServer32");
        var server = key?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(server))
            throw new QuickCommandException("无法从 COM 注册解析 SolidWorks 安装路径。");

        var executable = Environment.ExpandEnvironmentVariables(server.Trim().Trim('"'));
        var directory = Path.GetDirectoryName(executable)
            ?? throw new QuickCommandException("SolidWorks 的 COM 注册路径无效：" + executable);
        var path = Path.Combine(directory, "api", "redist", InteropName + ".dll");
        if (!File.Exists(path))
            throw new QuickCommandException("SolidWorks 安装目录里找不到官方 Interop：" + path);

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
}
