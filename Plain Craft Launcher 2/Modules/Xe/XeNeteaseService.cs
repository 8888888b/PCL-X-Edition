using System.IO;
using System.Text;
using Microsoft.Win32;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// PCL XE：网易版（我的世界中国版）客户端探测与启动。
///
/// 行为：不再唤起官方启动器，而是直接定位中国版客户端使用的 .minecraft 目录，
/// 把该目录注册为 PCL 的游戏文件夹，读取其中 versions 下的版本 JSON，
/// 复用 PCL 的 Java 版启动管线直接启动游戏。
/// 本模块不读取网易账号、不注入官方进程、不改写中国版游戏文件。
///
/// 探测原则：只收录**能确认是中国版**的目录。国际版（PCL / 官方启动器 / 第三方启动器）
/// 的 .minecraft 同样具备 versions / libraries / assets，因此不能仅凭结构判断，
/// 必须命中中国版特征（路径关键字、netease 相关文件或版本 JSON 中的 netease 字段）。
/// </summary>
public static class XeNeteaseService
{
    public sealed record Candidate(string Name, string Path, string Source, bool Confirmed = true);

    /// <summary>路径中出现的中国版特征（小写比较）。</summary>
    private static readonly string[] PathMarks =
        ["mcldownload", "netease", "我的世界", "mcchina", "minecraftchina", "minecraft-china"];

    /// <summary>目录内出现的中国版特征文件 / 文件夹（不区分大小写）。</summary>
    private static readonly string[] ContentMarks =
        ["netease", "netease_mc", "netease.json", ".netease", "mcl_download", "mcldownload"];

    /// <summary>当前选中的 .minecraft 目录（由配置或自动探测决定）。</summary>
    public static string? ResolveMcDir()
    {
        var configured = Config.Xe.Netease.GameDir;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = NormalizeDir(configured);
            if (full is not null && LooksLikeMcDir(full)) return full;
        }

        return Detect().FirstOrDefault(c => c.Confirmed)?.Path;
    }

    /// <summary>扫描本机可能存在的中国版 .minecraft 目录（自动结果一律经过中国版特征校验）。</summary>
    public static List<Candidate> Detect()
    {
        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manual = Lang.Text("Xe.Netease.Source.Manual");
        var registry = Lang.Text("Xe.Netease.Source.Registry");
        var common = Lang.Text("Xe.Netease.Source.CommonPath");
        var near = Lang.Text("Xe.Netease.Source.NearLauncher");

        // 自动发现：必须命中中国版特征
        void AddStrict(string? path, string source)
        {
            var full = NormalizeDir(path);
            if (full is null || seen.Contains(full) || !IsChinaEditionDir(full)) return;
            seen.Add(full);
            result.Add(new Candidate(DisplayName(full), full, source));
        }

        // 手动指定：像 MC 根目录就列出来，但只有命中中国版特征才算“已确认”，
        // 未确认的候选会标注出来，且不会被自动选中，避免把国际版误当成网易版。
        var manualDir = NormalizeDir(Config.Xe.Netease.GameDir);
        if (manualDir is not null && LooksLikeMcDir(manualDir))
        {
            seen.Add(manualDir);
            var confirmed = IsChinaEditionDir(manualDir);
            result.Add(new Candidate(DisplayName(manualDir), manualDir,
                confirmed ? manual : Lang.Text("Xe.Netease.Source.ManualUnconfirmed"), confirmed));
        }

        // 1. 注册表中登记的中国版启动器
        foreach (var location in ReadNeteaseInstallLocations())
            AddLauncherNeighbourhoods(location, registry, AddStrict);

        // 2. 配置过的网易启动器路径附近（仅在路径本身带中国版特征时才会命中）
        var launcher = Config.Xe.Netease.LauncherPath;
        if (!string.IsNullOrWhiteSpace(launcher))
        {
            var launcherDir = TryGetDirectory(launcher);
            AddLauncherNeighbourhoods(launcherDir, near, AddStrict);
        }

        // 3. 中国版默认安装位置：MCLDownload 与 Netease 目录
        foreach (var root in EnumerateDriveRoots())
        {
            AddStrict(Path.Combine(root, "MCLDownload", "Game", ".minecraft"), common);
            AddStrict(Path.Combine(root, "MCLDownload", ".minecraft"), common);
            AddStrict(Path.Combine(root, "MCLDownload", "Game", "minecraft"), common);
            AddStrict(Path.Combine(root, "Netease", "MC", ".minecraft"), common);
            AddStrict(Path.Combine(root, "Netease", "Minecraft", ".minecraft"), common);
        }

        // 4. 用户数据目录下的网易相关位置
        foreach (var env in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE" })
        {
            var value = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrWhiteSpace(value)) continue;
            AddStrict(Path.Combine(value, "Netease", "Minecraft", ".minecraft"), common);
            AddStrict(Path.Combine(value, "Netease", "MC", ".minecraft"), common);
            AddStrict(Path.Combine(value, "MCLDownload", "Game", ".minecraft"), common);
            AddStrict(Path.Combine(value, "我的世界", ".minecraft"), common);
            AddStrict(Path.Combine(value, "Minecraft China", ".minecraft"), common);
        }

        return result
            .OrderByDescending(c => c.Confirmed)
            .ThenByDescending(c => Directory.Exists(Path.Combine(c.Path, "versions")))
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>判断目录是否可以确认属于「我的世界中国版」（用于界面提示与自动发现过滤）。</summary>
    public static bool IsChinaEditionDir(string? dir)
    {
        var full = NormalizeDir(dir);
        if (full is null) return false;
        if (!HasLaunchableVersion(full)) return false;

        var lower = full.ToLowerInvariant();
        if (PathMarks.Any(lower.Contains)) return true;

        foreach (var mark in ContentMarks)
        {
            if (Directory.Exists(Path.Combine(full, mark)) || File.Exists(Path.Combine(full, mark)))
                return true;
        }

        return VersionJsonHasMark(full);
    }

    /// <summary>列出指定 .minecraft 下可启动的版本（要求存在同名 JSON）。</summary>
    public static List<string> GetVersions(string? mcDir = null)
    {
        var dir = mcDir ?? ResolveMcDir();
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(dir)) return result;

        try
        {
            var versions = Path.Combine(dir, "versions");
            if (!Directory.Exists(versions)) return result;
            foreach (var sub in Directory.EnumerateDirectories(versions))
            {
                var name = Path.GetFileName(sub);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.')) continue;
                if (!File.Exists(Path.Combine(sub, name + ".json"))) continue;
                result.Add(name);
            }
        }
        catch
        {
            // 目录不可读时返回已收集到的结果
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>兼容旧调用：返回当前 .minecraft 下的版本名。</summary>
    public static List<string> GetVersionNames() => GetVersions();

    /// <summary>修改版本 JSON 的时间作为“最近使用”排序依据，取最新的版本。</summary>
    public static string? GetPreferredVersion(string? mcDir = null)
    {
        var dir = mcDir ?? ResolveMcDir();
        var versions = GetVersions(dir);
        if (versions.Count == 0) return null;
        var configured = Config.Xe.Netease.SelectedVersion;
        if (!string.IsNullOrWhiteSpace(configured) &&
            versions.Contains(configured, StringComparer.OrdinalIgnoreCase))
            return versions.First(v => string.Equals(v, configured, StringComparison.OrdinalIgnoreCase));

        try
        {
            var versionsRoot = Path.Combine(dir!, "versions");
            return versions
                .OrderByDescending(v => File.GetLastWriteTime(Path.Combine(versionsRoot, v, v + ".json")))
                .First();
        }
        catch
        {
            return versions[0];
        }
    }

    /// <summary>使用当前配置的版本启动中国版客户端。</summary>
    public static bool Launch(out string message) => Launch(null, out message);

    /// <summary>直接通过中国版 .minecraft 启动指定版本。</summary>
    public static bool Launch(string? version, out string message)
    {
        var dir = ResolveMcDir();
        if (string.IsNullOrWhiteSpace(dir))
        {
            message = Lang.Text("Xe.Netease.Launch.NotFound");
            return false;
        }

        var versions = GetVersions(dir);
        if (versions.Count == 0)
        {
            message = Lang.Text("Xe.Netease.Launch.NoVersion", dir);
            return false;
        }

        version = string.IsNullOrWhiteSpace(version) ? GetPreferredVersion(dir) : version;
        if (string.IsNullOrWhiteSpace(version) ||
            !versions.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            message = Lang.Text("Xe.Netease.Launch.NoVersion", dir);
            return false;
        }

        version = versions.First(v => string.Equals(v, version, StringComparison.OrdinalIgnoreCase));

        try
        {
            if (!ModBase.RunInUi())
            {
                message = Lang.Text("Xe.Netease.Launch.Failed", "必须在 UI 线程启动");
                return false;
            }

            RegisterFolder(dir);
            ModFolder.mcFolderSelected = dir;
            States.Game.SelectedFolder = dir.Replace(ModBase.exePath, "$");
            Config.Xe.Netease.GameDir = dir;
            Config.Xe.Netease.SelectedVersion = version;

            var versionDir = Path.Combine(dir, "versions", version) + @"\";
            // 中国版的库与资源都在 .minecraft 根目录下，必须关闭版本隔离才能让 PCL 找对路径
            Config.Instance.IndieV2[versionDir] = false;

            var instance = new McInstance(versionDir);
            instance.Load();
            if (instance.state == McInstanceState.Error)
            {
                message = Lang.Text("Xe.Netease.Launch.BadVersion", version, instance.Desc);
                return false;
            }

            var started = ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions { instance = instance });
            message = started
                ? Lang.Text("Xe.Netease.Launch.Success", version)
                : Lang.Text("Xe.Netease.Launch.Failed", Lang.Text("Xe.Netease.Launch.NotStarted"));
            if (started) ModBase.Log($"[XE] 已通过 .minecraft 启动网易版：{versionDir}");
            return started;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "通过 .minecraft 启动网易版失败", ModBase.LogLevel.Normal);
            message = Lang.Text("Xe.Netease.Launch.Failed", ex.Message);
            return false;
        }
    }

    /// <summary>把 .minecraft 注册进 PCL 的游戏文件夹列表（已存在则跳过）。</summary>
    private static void RegisterFolder(string dir)
    {
        var normalized = dir.EndsWith(@"\") ? dir : dir + @"\";
        var folders = (States.Game.Folders ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var exists = folders.Any(f =>
        {
            var index = f.IndexOf('>');
            return index >= 0 &&
                   string.Equals(f[(index + 1)..].TrimEnd('\\'), normalized.TrimEnd('\\'),
                       StringComparison.OrdinalIgnoreCase);
        });
        if (exists) return;

        folders.Add(Lang.Text("Xe.Netease.FolderName") + ">" + normalized);
        States.Game.Folders = string.Join("|", folders);
        // 异步刷新文件夹列表，供 PCL 的选择界面显示；启动本身只依赖 mcFolderSelected
        try { ModFolder.mcFolderListLoader.Start(); }
        catch (Exception ex) { ModBase.Log(ex, "刷新游戏文件夹列表失败", ModBase.LogLevel.Debug); }
    }

    /// <summary>打开当前 .minecraft 目录。</summary>
    public static void OpenGameDir()
    {
        var dir = ResolveMcDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            HintService.Hint(Lang.Text("Xe.Netease.OpenFolder.NotFound"), HintType.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    // ---- 探测辅助 ----

    /// <summary>从注册表卸载项中找出中国版启动器的安装目录。</summary>
    private static List<string> ReadNeteaseInstallLocations()
    {
        var locations = new List<string>();
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
        var roots = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var hive in roots)
        foreach (var view in views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(uninstall);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                    try
                    {
                        using var sub = key.OpenSubKey(name);
                        if (sub is null) continue;
                        var display = sub.GetValue("DisplayName") as string;
                        var publisher = sub.GetValue("Publisher") as string;
                        var install = sub.GetValue("InstallLocation") as string;
                        var icon = sub.GetValue("DisplayIcon") as string;
                        if (!LooksLikeNeteaseEntry(display, publisher, install, icon)) continue;
                        if (!string.IsNullOrWhiteSpace(install)) locations.Add(install);
                        var iconDir = TryGetDirectory(icon);
                        if (iconDir is not null) locations.Add(iconDir);
                    }
                    catch
                    {
                        // 忽略无法读取的单个项
                    }
            }
            catch
            {
                // 忽略无法读取的注册表视图
            }
        }

        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool LooksLikeNeteaseEntry(string? display, string? publisher, string? install, string? icon)
    {
        var text = $"{display}|{publisher}|{install}|{icon}".ToLowerInvariant();
        if (text.Contains("mcldownload") || text.Contains("netease") || text.Contains("网易")) return true;
        // “我的世界”条目本身也可能是网易中国版，但要有安装目录才推广候选
        return display?.Contains("我的世界") == true && !string.IsNullOrWhiteSpace(install);
    }

    /// <summary>按启动器所在目录推导可能的 .minecraft 位置。</summary>
    private static void AddLauncherNeighbourhoods(string? launcherDir, string source, Action<string?, string> add)
    {
        var dir = NormalizeDir(launcherDir);
        if (dir is null) return;
        add(Path.Combine(dir, ".minecraft"), source);
        add(Path.Combine(dir, "Game", ".minecraft"), source);
        add(Path.Combine(dir, "minecraft"), source);
        add(Path.Combine(dir, "..", ".minecraft"), source);
        add(Path.Combine(dir, "..", "Game", ".minecraft"), source);
        add(Path.Combine(dir, "..", "..", ".minecraft"), source);
        add(Path.Combine(dir, "..", "..", "Game", ".minecraft"), source);
    }

    private static IEnumerable<string> EnumerateDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
                catch
                {
                    // 忽略不可访问的驱动器
                }
        }
        catch
        {
            // 忽略枚举失败
        }

        return roots;
    }

    private static string? TryGetDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var trimmed = path.Trim().Trim('"');
            var index = trimmed.LastIndexOfAny(['\\', '/']);
            if (index <= 0) return null;
            return trimmed[..index];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>目录内是否有可启动版本，且目录结构像 Minecraft 根目录。</summary>
    private static bool LooksLikeMcDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            return HasLaunchableVersion(dir) ||
                   (Directory.Exists(Path.Combine(dir, "libraries")) &&
                    Directory.Exists(Path.Combine(dir, "assets")));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasLaunchableVersion(string dir)
    {
        try
        {
            var versions = Path.Combine(dir, "versions");
            if (!Directory.Exists(versions)) return false;
            foreach (var sub in Directory.EnumerateDirectories(versions))
                if (File.Exists(Path.Combine(sub, Path.GetFileName(sub) + ".json")))
                    return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>版本 JSON 中是否带 netease 相关字段（中国版会在版本元数据里写入网易信息）。</summary>
    private static bool VersionJsonHasMark(string dir)
    {
        try
        {
            var versions = Path.Combine(dir, "versions");
            if (!Directory.Exists(versions)) return false;
            foreach (var sub in Directory.EnumerateDirectories(versions).Take(4))
            {
                var json = Path.Combine(sub, Path.GetFileName(sub) + ".json");
                if (!File.Exists(json)) continue;
                using var stream = new FileStream(json, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[Math.Min(stream.Length, 16384)];
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0 &&
                    Encoding.UTF8.GetString(buffer, 0, read).ToLowerInvariant().Contains("netease"))
                    return true;
            }
        }
        catch
        {
            // 读不到就当没有特征
        }

        return false;
    }

    private static string? NormalizeDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return full.EndsWith(@"\") ? full : full + @"\";
        }
        catch
        {
            return null;
        }
    }

    private static string DisplayName(string dir)
    {
        var name = new DirectoryInfo(dir).Name;
        return string.IsNullOrWhiteSpace(name) ? dir : name;
    }
}
