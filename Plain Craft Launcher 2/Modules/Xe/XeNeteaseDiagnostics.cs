using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// PCL XE：网易版启动诊断会话。
///
/// 每次启动都会新建一个会话，把每一步（目录探测、版本检查、账号、实例加载、启动管线）写入
/// 独立日志文件（PCL\Log\XeNetease\），失败时据此生成可直接复制的错误报告，
/// 让用户能自己定位问题，也能把报告贴给开发者。
/// </summary>
public sealed class XeNeteaseDiagnostics
{
    /// <summary>保留的历史日志数量，超出后删除最旧的。</summary>
    private const int MaxLogFiles = 30;

    private readonly List<string> _steps = new();
    private readonly object _lock = new();

    private XeNeteaseDiagnostics()
    {
        StartedAt = DateTime.Now;
        LogPath = CreateLogFile();
    }

    /// <summary>最近一次启动会话（启动器运行期间始终保留）。</summary>
    public static XeNeteaseDiagnostics? Current { get; private set; }

    /// <summary>网易版诊断日志目录。</summary>
    public static string LogDirectory => Path.Combine(ModBase.exePath, "PCL", "Log", "XeNetease");

    public DateTime StartedAt { get; }

    /// <summary>本次会话的日志文件；无法写入时为 null。</summary>
    public string? LogPath { get; }

    /// <summary>本次启动使用的 .minecraft 目录。</summary>
    public string? McDir { get; set; }

    /// <summary>本次启动的版本名。</summary>
    public string? Version { get; set; }

    /// <summary>失败阶段（本地化后的名称），成功时为 null。</summary>
    public string? Stage { get; private set; }

    /// <summary>失败原因，成功时为 null。</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>失败时捕获的异常。</summary>
    public Exception? Error { get; private set; }

    public bool Succeeded { get; private set; }

    /// <summary>启动流程是否已经结束（成功、失败或交给管线异步处理）。</summary>
    public bool Finished { get; private set; }

    public bool IsFailed => FailureMessage is not null;

    /// <summary>按时间顺序记录的所有步骤。</summary>
    public IReadOnlyList<string> Steps
    {
        get
        {
            lock (_lock) return _steps.ToArray();
        }
    }

    #region 会话生命周期

    /// <summary>开始一次新的启动会话。</summary>
    public static XeNeteaseDiagnostics Begin()
    {
        var session = new XeNeteaseDiagnostics();
        Current = session;
        session.Step("================ 网易版启动开始 ================");
        session.Step($"启动器：{ModBase.versionBaseName}（{ModBase.versionCode}）");
        session.Step($"系统：{RuntimeInformation.OSDescription.Trim()} / {RuntimeInformation.OSArchitecture}" +
                     $" / {RuntimeInformation.FrameworkDescription} / {(Environment.Is64BitProcess ? "x64" : "x86")}");
        session.Step(session.LogPath is null
            ? "日志文件：写入失败，以下内容仅在启动器日志中可见"
            : "日志文件：" + session.LogPath);
        return session;
    }

    /// <summary>记录一个步骤。</summary>
    public void Step(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}";
        lock (_lock)
        {
            _steps.Add(line);
            AppendToFile(line);
        }

        ModBase.Log("[XE/网易版] " + text);
    }

    /// <summary>记录失败原因。</summary>
    public void Fail(string stage, string message, Exception? ex = null)
    {
        Stage = stage;
        FailureMessage = message;
        Error = ex;
        // 启动管线可能在会话已“完成”后才报错，允许重新收尾以写入结尾信息
        Finished = false;
        Step($"❌ 失败（{stage}）：{message}");
        if (ex is not null && !string.Equals(ex.Message, message, StringComparison.Ordinal))
            Step("异常详情：" + ex);
    }

    /// <summary>记录成功。</summary>
    public void Succeed(string detail)
    {
        Succeeded = true;
        Step("✅ 已交给启动管线：" + detail);
    }

    /// <summary>结束会话（写入结尾分隔符）。</summary>
    public void Complete()
    {
        if (Finished) return;
        Finished = true;
        Step(Succeeded
            ? "================ 网易版启动已发起 ================"
            : "================ 网易版启动结束 ================");
        if (IsFailed) Step("完整日志：" + (LogPath ?? "（无法写入日志文件）"));
    }

    #endregion

    #region 错误报告

    /// <summary>生成用户可直接复制/粘贴的错误报告。</summary>
    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.Text("Xe.Netease.Report.Header"));
        sb.AppendLine("----------------------------------------------");
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Time")}：{StartedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Launcher")}：{ModBase.versionBaseName}（{ModBase.versionCode}）");
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.System")}：{RuntimeInformation.OSDescription.Trim()} " +
                      $"{RuntimeInformation.OSArchitecture}，{RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.GameDir")}：{McDir ?? "（未确定）"}");
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Version")}：{Version ?? "（未确定）"}");
        if (IsFailed)
        {
            sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Stage")}：{Stage}");
            sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Reason")}：{FailureMessage}");
        }
        else
        {
            sb.AppendLine($"{Lang.Text("Xe.Netease.Report.Stage")}：" + Lang.Text("Xe.Netease.Report.NoFailure"));
        }

        if (Error is not null)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.Text("Xe.Netease.Report.Exception") + "：");
            sb.AppendLine(Error.ToString());
        }

        sb.AppendLine();
        sb.AppendLine(Lang.Text("Xe.Netease.Report.Steps") + "：");
        foreach (var step in Steps) sb.AppendLine(step);

        sb.AppendLine();
        sb.AppendLine($"{Lang.Text("Xe.Netease.Report.LogFile")}：{LogPath ?? Lang.Text("Xe.Netease.Report.LogUnavailable")}");
        return sb.ToString();
    }

    #endregion

    #region 日志文件

    private static string? CreateLogFile()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            CleanupOldLogs();

            var path = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
            File.WriteAllText(path, "", Encoding.UTF8);
            return path;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "创建网易版启动日志失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private void AppendToFile(string line)
    {
        if (LogPath is null) return;
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "写入网易版启动日志失败", ModBase.LogLevel.Debug);
        }
    }

    private static void CleanupOldLogs()
    {
        try
        {
            var files = new DirectoryInfo(LogDirectory).GetFiles("*.log");
            if (files.Length <= MaxLogFiles) return;
            foreach (var file in files.OrderBy(f => f.CreationTimeUtc).Take(files.Length - MaxLogFiles))
                try
                {
                    file.Delete();
                }
                catch
                {
                    // 忽略被占用的旧日志
                }
        }
        catch
        {
            // 清理失败不影响启动
        }
    }

    #endregion
}
