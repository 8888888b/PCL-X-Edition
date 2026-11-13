using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace PCL;

/// <summary>
/// PCL XE：运行内存优化。
///
/// 启动器把每个访问过的页面都缓存在静态字段里，用得越久占用越高；.NET 的工作站 GC
/// 又倾向于把已释放的空闲页留在工作集里备用，于是任务管理器里的「内存」会一直偏高。
/// 这里按「人不在看启动器」的时机回收，避免影响交互：
/// 1) 窗口最小化时立即回收；
/// 2) 启动器连续 45 秒不在前台（多半在玩游戏或做别的事）时回收一次；
/// 3) 回收时压紧大对象堆，并把工作集归还给系统，内存数字会立刻下降。
/// </summary>
internal static class XeMemoryService
{
    private const long TrimThresholdMb = 200;
    private static readonly TimeSpan BackgroundDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private static Timer? _timer;
    private static IntPtr _hwnd;
    private static DateTime? _backgroundSince;
    private static int _pending;

    public static long WorkingSetMb()
    {
        try
        {
            return Process.GetCurrentProcess().WorkingSet64 >> 20;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>记录主窗口句柄，用于判断启动器是否在前台。</summary>
    public static void AttachWindow(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public static void Start()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        ModBase.Log($"[Memory] 内存回收已启用，当前工作集 {WorkingSetMb()} MB", ModBase.LogLevel.Debug);
    }

    private static bool IsLauncherForeground()
    {
        if (_hwnd == IntPtr.Zero) return true; // 拿不到句柄时不主动回收，宁可不省也别打扰
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        // 启动器弹出的子窗口、菜单归属同一个根窗口
        return GetAncestor(foreground, 2 /* GA_ROOT */) == _hwnd || foreground == _hwnd;
    }

    private static void Poll()
    {
        try
        {
            if (IsLauncherForeground())
            {
                _backgroundSince = null;
                return;
            }

            _backgroundSince ??= DateTime.Now;
            if (DateTime.Now - _backgroundSince.Value < BackgroundDelay) return;
            if (WorkingSetMb() < TrimThresholdMb) return;
            Trim(false);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Memory] 检查运行内存失败", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>最小化等确定要离开的时机调用：稍等片刻再回收，避免和收尾动画抢时间。</summary>
    public static void ScheduleTrim(int delayMs = 1200)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                Trim(true);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Memory] 延迟回收失败", ModBase.LogLevel.Debug);
            }
            finally
            {
                Interlocked.Exchange(ref _pending, 0);
            }
        });
    }

    /// <summary>压缩托管堆并把工作集归还给系统。</summary>
    public static void Trim(bool force)
    {
        var before = WorkingSetMb();
        if (!force && before < TrimThresholdMb) return;
        try
        {
            // 先整堆回收（含大对象堆压紧），再让系统收回已释放的物理页
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, false, true);
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Memory] 回收运行内存失败", ModBase.LogLevel.Debug);
            return;
        }

        _backgroundSince = DateTime.Now;
        var after = WorkingSetMb();
        if (before - after >= 16)
            ModBase.Log($"[Memory] 已回收运行内存：{before} MB → {after} MB", ModBase.LogLevel.Debug);
    }
}
