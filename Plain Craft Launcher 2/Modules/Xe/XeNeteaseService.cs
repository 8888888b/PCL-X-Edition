using System.IO;
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
/// </summary>
public static class XeNeteaseService
{
    public sealed record Candidate(string Name, string Path, string Source);

    private static readonly string[] NearbyFolderNames =
    [
        ".minecraft", "Minecraft", "minecraft", "MC", "mc", "game", ".minecraft_netease", "MinecraftData"
    ];

    /// <summary>当前选中的 .minecraft 目录（由配置或自动探测决定）。</summary>
    public static string? ResolveMcDir()
    {
        var configured = Config.Xe.Netease.GameDir;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = NormalizeDir(configured);
            if (full is not null && LooksLikeMcDir(full)) return full;
        }

        return Detect().FirstOrDefault()?.Path;
    }

    /// <summary>扫描本机可能存在的中国版 .minecraft 目录。</summary>
    public static List<Candidate> Detect()
    {
        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string source)
        {
            var full = NormalizeDir(path);
            if (full is null || !LooksLikeMcDir(full) || !seen.Add(full)) return;
            result.Add(new Candidate(DisplayName(full), full, source));
        }

        // 1. 用户手动指定
        Add(Config.Xe.Netease.GameDir, Lang.Text("Xe.Netease.Source.Manual"));

        // 2. 官方启动器附近的 .minecraft
        var launcher = Config.Xe.Netease.LauncherPath;
        if (!string.IsNullOrWhiteSpace(launcher) && File.Exists(launcher))
        {
            var root = Path.GetDirectoryName(launcher);
            if (!string.IsNullOrEmpty(root))
            {
                Add(Path.Combine(root, ".minecraft"), Lang.Text("Xe.Netease.Source.NearLauncher"));
                Add(Path.Combine(root, "Minecraft"), Lang.Text("Xe.Netease.Source.NearLauncher"));
                Add(Path.Combine(root, "..", ".minecraft"), Lang.Text("Xe.Netease.Source.NearLauncher"));
                Add(Path.Combine(root, "..", "Minecraft"), Lang.Text("Xe.Netease.Source.NearLauncher"));
                Add(Path.Combine(root, "..", "game", ".minecraft"), Lang.Text("Xe.Netease.Source.NearLauncher"));
            }
        }

        // 3. 网易客户端常见数据目录
        foreach (var env in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE" })
        {
            var value = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrWhiteSpace(value)) continue;
            Add(Path.Combine(value, "Minecraft"), Lang.Text("Xe.Netease.Source.AppData"));
            Add(Path.Combine(value, ".minecraft"), Lang.Text("Xe.Netease.Source.AppData"));
            Add(Path.Combine(value, "Netease", "Minecraft"), Lang.Text("Xe.Netease.Source.AppData"));
        }

        // 4. 常见盘符根目录
        foreach (var drive in new[] { "C", "D", "E", "F" })
        foreach (var name in new[] { ".minecraft", "Minecraft", "Netease" })
            Add($@"{drive}:\{name}", Lang.Text("Xe.Netease.Source.CommonPath"));

        return result
            .OrderByDescending(c => Directory.Exists(Path.Combine(c.Path, "versions")))
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>判断目录是否像一个 Minecraft 根目录。</summary>
    private static bool LooksLikeMcDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            var versions = Path.Combine(dir, "versions");
            if (Directory.Exists(versions))
            {
                foreach (var sub in Directory.EnumerateDirectories(versions))
                    if (File.Exists(Path.Combine(sub, Path.GetFileName(sub) + ".json")))
                        return true;
            }

            return Directory.Exists(Path.Combine(dir, "libraries")) &&
                   Directory.Exists(Path.Combine(dir, "assets"));
        }
        catch
        {
            return false;
        }
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
