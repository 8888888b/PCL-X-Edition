using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// PCL XE：网易版（我的世界中国版）客户端探测与启动。
///
/// 设计边界：本机中国版客户端是封闭客户端，其账号、版本与组件数据由官方启动器自行管理。
/// 本模块只负责「找到官方启动器并唤起它」，不读取账号、不注入进程、不改写游戏文件，
/// 也不尝试用 Java 版 / 基岩版的启动参数去启动中国版。
/// </summary>
public static class XeNeteaseService
{
    public sealed record Candidate(string Name, string Path, string Source);

    private static readonly string[] ExeNames =
    [
        "MinecraftLauncher.exe", "Minecraft.exe", "MCLauncher.exe", "mclauncher.exe",
        "Launcher.exe", "我的世界.exe", "我的世界启动器.exe", "NeteaseMinecraft.exe"
    ];

    private static readonly string[] VersionFolderNames =
    [
        "versions", ".minecraft", "minecraft", "game", "games", "MinecraftData", ".minecraft_netease"
    ];

    /// <summary>扫描本机可能存在的中国版启动器。</summary>
    public static List<Candidate> Detect()
    {
        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string name, string source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }
            if (!File.Exists(full) || !seen.Add(full)) return;
            result.Add(new Candidate(name, full, source));
        }

        // 1. 用户手动指定的路径优先
        Add(Config.Xe.Netease.LauncherPath, Lang.Text("Xe.Netease.Source.Manual"), Lang.Text("Xe.Netease.Source.Manual"));

        // 2. 注册表卸载项（含 32/64 位视图、HKCU/HKLM）
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var sub in new[]
                         {
                             @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                             @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                         })
                {
                    using var root = baseKey.OpenSubKey(sub);
                    if (root is null) continue;
                    foreach (var keyName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var item = root.OpenSubKey(keyName);
                            if (item is null) continue;
                            var display = item.GetValue("DisplayName")?.ToString() ?? "";
                            if (!IsNeteaseDisplayName(display)) continue;
                            var icon = item.GetValue("DisplayIcon")?.ToString() ?? "";
                            if (icon.Contains(',')) icon = icon[..icon.IndexOf(',')].Trim().Trim('"');
                            Add(icon, display, Lang.Text("Xe.Netease.Source.Registry"));
                            var install = item.GetValue("InstallLocation")?.ToString();
                            if (!string.IsNullOrWhiteSpace(install))
                                foreach (var exe in ExeNames) Add(Path.Combine(install, exe), display, Lang.Text("Xe.Netease.Source.Registry"));
                        }
                        catch { /* 单个卸载项损坏不应中断扫描 */ }
                    }
                }
            }
            catch { /* 无权限或视图不可用 */ }
        }

        // 3. 常见安装目录
        foreach (var root in CommonRoots())
        {
            foreach (var exe in ExeNames)
            {
                Add(Path.Combine(root, exe), Path.GetFileNameWithoutExtension(exe), Lang.Text("Xe.Netease.Source.CommonPath"));
                foreach (var sub in new[] { "Minecraft", "Netease", "MC", "bin", "launcher" })
                    Add(Path.Combine(root, sub, exe), Path.GetFileNameWithoutExtension(exe), Lang.Text("Xe.Netease.Source.CommonPath"));
            }
        }

        return result;
    }

    private static bool IsNeteaseDisplayName(string display)
    {
        if (string.IsNullOrWhiteSpace(display)) return false;
        return display.Contains("我的世界", StringComparison.OrdinalIgnoreCase) ||
               display.Contains("网易", StringComparison.OrdinalIgnoreCase) ||
               display.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
               display.Contains("中国版", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CommonRoots()
    {
        var roots = new List<string>();
        void AddEnv(string name, string? suffix = null)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) return;
            roots.Add(suffix is null ? value : Path.Combine(value, suffix));
        }

        AddEnv("ProgramFiles");
        AddEnv("ProgramFiles(x86)");
        AddEnv("LOCALAPPDATA");
        AddEnv("APPDATA");
        AddEnv("USERPROFILE", "Documents");
        AddEnv("USERPROFILE", "Desktop");
        foreach (var drive in new[] { "C", "D", "E", "F" })
        {
            roots.Add($@"{drive}:\");
            roots.Add($@"{drive}:\Games");
            roots.Add($@"{drive}:\Netease");
            roots.Add($@"{drive}:\Program Files");
            roots.Add($@"{drive}:\Program Files (x86)");
        }
        return roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>返回当前生效的启动器路径。</summary>
    public static string? ResolveLauncherPath()
    {
        var configured = Config.Xe.Netease.LauncherPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return Detect().FirstOrDefault()?.Path;
    }

    /// <summary>尝试推断中国版的游戏数据目录，仅供展示与打开文件夹，不写入任何内容。</summary>
    public static string? ResolveGameDir()
    {
        var configured = Config.Xe.Netease.GameDir;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        var launcher = ResolveLauncherPath();
        if (launcher is null) return null;
        var root = Path.GetDirectoryName(launcher);
        if (string.IsNullOrEmpty(root)) return null;
        foreach (var folder in VersionFolderNames)
        {
            var path = Path.Combine(root, folder);
            if (Directory.Exists(path)) return path;
        }
        return root;
    }

    /// <summary>列出游戏目录下的版本文件夹（若能被识别）。</summary>
    public static List<string> GetVersionNames()
    {
        var result = new List<string>();
        var dir = ResolveGameDir();
        if (string.IsNullOrEmpty(dir)) return result;
        try
        {
            var versions = Path.Combine(dir, "versions");
            var target = Directory.Exists(versions) ? versions : dir;
            foreach (var sub in Directory.EnumerateDirectories(target))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.')) continue;
                result.Add(name);
                if (result.Count >= 50) break;
            }
        }
        catch { /* 目录不可读 */ }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>唤起本机官方中国版启动器。</summary>
    public static bool Launch(out string message)
    {
        var path = ResolveLauncherPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            message = Lang.Text("Xe.Netease.Launch.NotFound");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                UseShellExecute = true
            };
            var args = Config.Xe.Netease.LaunchArgs;
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
            Process.Start(psi);
            message = Lang.Text("Xe.Netease.Launch.Success", Path.GetFileName(path));
            ModBase.Log($"[XE] 已唤起网易版启动器：{path}");
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "唤起网易版启动器失败", ModBase.LogLevel.Normal);
            message = Lang.Text("Xe.Netease.Launch.Failed", ex.Message);
            return false;
        }
    }

    /// <summary>打开游戏数据目录。</summary>
    public static void OpenGameDir()
    {
        var dir = ResolveGameDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            HintService.Hint(Lang.Text("Xe.Netease.OpenFolder.NotFound"), HintType.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (Exception ex) { HintService.Hint(ex.Message, HintType.Error); }
    }
}
