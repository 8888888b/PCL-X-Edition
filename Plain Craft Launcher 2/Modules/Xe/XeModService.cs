using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>模组管理页的「当前管理目标」。</summary>
public enum XeModTargetMode
{
    /// <summary>本地 Java 版客户端实例，可以管理模组。</summary>
    Client = 0,

    /// <summary>服务端目录，模组属于服务端，客户端不可管理。</summary>
    Server = 1,

    /// <summary>网易版（我的世界中国版）客户端，封闭客户端不可管理。</summary>
    NeteaseClient = 2
}

public sealed class XeModEntry
{
    public string FileName { get; init; } = "";
    public string RawFileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool Enabled { get; init; }
    public long Size { get; init; }
    public string DisplayName { get; init; } = "";
    public bool ServerSideOnly { get; init; }
    public string Note { get; init; } = "";
    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024d / 1024d:F1} MB"
        : $"{Math.Max(1, Size / 1024)} KB";
}

/// <summary>
/// PCL XE：模组管理页的数据层。
/// 与上游「实例 → Mod 管理」页不同，这里是独立的顶层分页，并且会显式区分
/// 「本地客户端实例 / 服务端 / 网易版客户端」三种目标，后两种一律提示不可管理模组。
/// </summary>
public static class XeModService
{
    private static readonly string[] ModExtensions = [".jar", ".zip"];
    private static readonly Regex DisplayNameRegex = new(@"displayName\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ServerSideRegex = new(@"side\s*=\s*""server""|serverSideOnly\s*=\s*true", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static XeModTargetMode CurrentMode
    {
        get => Config.Xe.Mods.TargetMode switch
        {
            1 => XeModTargetMode.Server,
            2 => XeModTargetMode.NeteaseClient,
            _ => XeModTargetMode.Client
        };
        set => Config.Xe.Mods.TargetMode = (int)value;
    }

    /// <summary>列出启动器已识别的全部游戏实例。</summary>
    public static List<McInstance> GetInstances()
    {
        var result = new List<McInstance>();
        try
        {
            foreach (var pair in ModInstanceList.mcInstanceList)
                foreach (var instance in pair.Value)
                    if (instance is not null && !result.Contains(instance)) result.Add(instance);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "读取游戏实例列表失败", ModBase.LogLevel.Debug);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static McInstance? FindInstance(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return GetInstances().FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static McInstance? CurrentInstance
    {
        get
        {
            var saved = Config.Xe.Mods.SelectedInstance;
            return FindInstance(saved) ?? GetInstances().FirstOrDefault();
        }
        set => Config.Xe.Mods.SelectedInstance = value?.Name ?? "";
    }

    /// <summary>取得实例对应的 mods 目录（遵循上游的版本隔离规则）。</summary>
    public static string? GetModsFolder(McInstance? instance)
    {
        if (instance is null) return null;
        try
        {
            var suffix = instance.Info.HasLabyMod
                ? Path.Combine("labymod-neo", "fabric", instance.Info.VanillaName)
                : "";
            return Path.Combine(instance.PathIndie, suffix, "mods");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "解析模组目录失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    /// <summary>
    /// 判断当前目标是否允许由启动器管理模组。
    /// 服务端与网易版客户端都会返回 false，并给出「服务器 / 客户端不可管理模组」的明确原因。
    /// </summary>
    public static bool IsManageable(XeModTargetMode mode, McInstance? instance, out string reason)
    {
        switch (mode)
        {
            case XeModTargetMode.Server:
                reason = Lang.Text("Xe.Mods.Block.Server");
                return false;
            case XeModTargetMode.NeteaseClient:
                reason = Lang.Text("Xe.Mods.Block.Netease");
                return false;
        }

        if (instance is null)
        {
            reason = Lang.Text("Xe.Mods.Block.NoInstance");
            return false;
        }

        var mods = GetModsFolder(instance);
        if (string.IsNullOrEmpty(mods))
        {
            reason = Lang.Text("Xe.Mods.Block.NoModsFolder");
            return false;
        }

        // 目录里出现服务端标志性文件，说明用户其实选中了服务端根目录
        try
        {
            var root = instance.PathInstance;
            foreach (var marker in new[] { "server.jar", "server.properties", "eula.txt", "bukkit.yml", "spigot.yml" })
                if (File.Exists(Path.Combine(root, marker)))
                {
                    reason = Lang.Text("Xe.Mods.Block.DetectedServer", marker);
                    return false;
                }
        }
        catch { /* 目录不可读，按普通客户端处理 */ }

        reason = "";
        return true;
    }

    /// <summary>扫描 mods 目录并读取每个 jar 的基础元数据。</summary>
    public static List<XeModEntry> ListMods(string folder)
    {
        var result = new List<XeModEntry>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            var rawName = name.Replace(".disabled", "", StringComparison.OrdinalIgnoreCase)
                              .Replace(".old", "", StringComparison.OrdinalIgnoreCase);
            if (!ModExtensions.Contains(Path.GetExtension(rawName), StringComparer.OrdinalIgnoreCase)) continue;
            var disabled = !string.Equals(name, rawName, StringComparison.OrdinalIgnoreCase);
            string display = rawName;
            var serverOnly = false;
            var note = "";
            try { ReadMetadata(path, ref display, ref serverOnly, ref note); }
            catch (Exception ex) { note = ex.Message; }
            result.Add(new XeModEntry
            {
                FileName = name,
                RawFileName = rawName,
                FullPath = path,
                Enabled = !disabled,
                Size = new FileInfo(path).Length,
                DisplayName = display,
                ServerSideOnly = serverOnly,
                Note = serverOnly ? Lang.Text("Xe.Mods.Item.ServerSideOnly") : note
            });
        }
        result.Sort((a, b) => string.Compare(a.RawFileName, b.RawFileName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static void ReadMetadata(string path, ref string display, ref bool serverOnly, ref string note)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var entryName in new[] { "fabric.mod.json", "mcmod.info", "META-INF/mods.toml", "META-INF/neoforge.mods.toml", "quilt.mod.json" })
        {
            var entry = zip.GetEntry(entryName);
            if (entry is null) continue;
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();

            if (entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var json = ModBase.GetJson(text);
                    var node = json is JsonArray arr ? arr.FirstOrDefault() : json;
                    var name = node?["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) display = name!;
                    var env = node?["environment"]?.ToString();
                    if (string.Equals(env, "server", StringComparison.OrdinalIgnoreCase)) serverOnly = true;
                    if (node?["serverSideOnly"]?.ToString() == "true") serverOnly = true;
                }
                catch { /* 元数据不规范时使用文件名 */ }
            }
            else
            {
                var match = DisplayNameRegex.Match(text);
                if (match.Success) display = match.Groups[1].Value;
                if (ServerSideRegex.IsMatch(text)) serverOnly = true;
            }
            return;
        }
        note = Lang.Text("Xe.Mods.Item.NoMetadata");
    }

    /// <summary>启用 / 禁用单个模组（沿用上游 .disabled 后缀方案）。</summary>
    public static (bool Ok, string Message) SetEnabled(XeModEntry entry, bool enabled, XeModTargetMode mode, McInstance? instance)
    {
        if (!IsManageable(mode, instance, out var reason)) return (false, reason);
        if (entry.ServerSideOnly)
            return (false, Lang.Text("Xe.Mods.Block.ServerSideOnly"));
        if (entry.Enabled == enabled) return (true, Lang.Text("Xe.Mods.Action.NoChange"));

        try
        {
            var dir = Path.GetDirectoryName(entry.FullPath) ?? "";
            var raw = Path.Combine(dir, entry.RawFileName);
            var target = enabled ? raw : raw + ".disabled";
            if (File.Exists(target))
                return (false, Lang.Text("Xe.Mods.Action.Failed", $"目标文件已存在：{Path.GetFileName(target)}"));
            File.Move(entry.FullPath, target);
            ModBase.Log($"[XE] 模组 {(enabled ? "启用" : "禁用")}：{entry.RawFileName}");
            return (true, Lang.Text(enabled ? "Xe.Mods.Action.Enabled" : "Xe.Mods.Action.Disabled", entry.RawFileName));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "切换模组状态失败", ModBase.LogLevel.Normal);
            return (false, Lang.Text("Xe.Mods.Action.Failed", ex.Message));
        }
    }

    /// <summary>打开 mods 目录。</summary>
    public static void OpenFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            HintService.Hint(Lang.Text("Xe.Mods.Folder.Missing"), HintType.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { HintService.Hint(ex.Message, HintType.Error); }
    }

    /// <summary>
    /// 通过 Modrinth 搜索并下载一个模组到目标 mods 目录。
    /// 供「问问 AI」的 install_mod 指令调用；所有文件名都会做安全校验。
    /// </summary>
    public static async Task<(bool Ok, string Message)> InstallFromModrinthAsync(
        string keyword, string? mcVersion, string? loader, string modsFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return (false, Lang.Text("Xe.Ai.Action.Install.NoKeyword"));
        if (string.IsNullOrWhiteSpace(modsFolder))
            return (false, Lang.Text("Xe.Ai.Action.Install.NoFolder"));

        var client = XeHttp.General;
        var facets = new List<string> { "[\"project_type:mod\"]" };
        if (!string.IsNullOrWhiteSpace(loader)) facets.Add($"[\"categories:{loader.Trim().ToLowerInvariant()}\"]");
        if (!string.IsNullOrWhiteSpace(mcVersion)) facets.Add($"[\"versions:{mcVersion.Trim()}\"]");
        var facetParam = Uri.EscapeDataString("[" + string.Join(",", facets) + "]");
        var searchUrl = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(keyword.Trim())}&limit=5&facets={facetParam}";

        try
        {
            using var searchResp = await client.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (!searchResp.IsSuccessStatusCode)
                return (false, Lang.Text("Xe.Ai.Action.Install.SearchFailed", (int)searchResp.StatusCode));
            var searchJson = ModBase.GetJson(await searchResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var hit = searchJson?["hits"]?.AsArray().FirstOrDefault();
            if (hit is null) return (false, Lang.Text("Xe.Ai.Action.Install.NoResult", keyword));

            var projectId = hit["project_id"]?.ToString() ?? hit["slug"]?.ToString();
            var title = hit["title"]?.ToString() ?? keyword;
            if (string.IsNullOrWhiteSpace(projectId))
                return (false, Lang.Text("Xe.Ai.Action.Install.NoResult", keyword));

            var versionUrl = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(loader)) query.Add("loaders=" + Uri.EscapeDataString($"[\"{loader.Trim().ToLowerInvariant()}\"]"));
            if (!string.IsNullOrWhiteSpace(mcVersion)) query.Add("game_versions=" + Uri.EscapeDataString($"[\"{mcVersion.Trim()}\"]"));
            if (query.Count > 0) versionUrl += "?" + string.Join("&", query);

            using var versionResp = await client.GetAsync(versionUrl, ct).ConfigureAwait(false);
            if (!versionResp.IsSuccessStatusCode)
                return (false, Lang.Text("Xe.Ai.Action.Install.VersionFailed", (int)versionResp.StatusCode));
            var versions = ModBase.GetJson(await versionResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false))?.AsArray();
            JsonNode? file = null;
            string? versionNumber = null;
            if (versions is not null)
                foreach (var ver in versions)
                {
                    if (ver is null) continue;
                    versionNumber = ver["version_number"]?.ToString();
                    foreach (var f in ver["files"]?.AsArray() ?? new JsonArray())
                    {
                        if (f?["primary"]?.ToString() == "true") { file = f; break; }
                        file ??= f;
                    }
                    if (file is not null) break;
                }
            if (file is null) return (false, Lang.Text("Xe.Ai.Action.Install.NoFile", title));

            var url = file["url"]?.ToString();
            var fileName = Path.GetFileName(file["filename"]?.ToString() ?? "");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                return (false, Lang.Text("Xe.Ai.Action.Install.UnsafeFile"));

            Directory.CreateDirectory(modsFolder);
            var target = Path.Combine(modsFolder, fileName);
            if (File.Exists(target)) return (true, Lang.Text("Xe.Ai.Action.Install.AlreadyExists", fileName));

            using var fileResp = await client.GetAsync(url, ct).ConfigureAwait(false);
            fileResp.EnsureSuccessStatusCode();
            await using var stream = await fileResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(output, ct).ConfigureAwait(false);

            ModBase.Log($"[XE] 已安装模组：{title} -> {target}");
            return (true, Lang.Text("Xe.Ai.Action.Install.Success", title, versionNumber ?? "", fileName));
        }
        catch (OperationCanceledException)
        {
            return (false, Lang.Text("Xe.Ai.Action.Canceled"));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "安装模组失败", ModBase.LogLevel.Normal);
            return (false, Lang.Text("Xe.Ai.Action.Install.Failed", ex.Message));
        }
    }

    /// <summary>生成给 AI 的当前环境摘要，让模型能做出正确决策。</summary>
    public static string DescribeEnvironment()
    {
        var lines = new List<string>
        {
            Lang.Text("Xe.Ai.Context.Mode", CurrentMode switch
            {
                XeModTargetMode.Server => "server",
                XeModTargetMode.NeteaseClient => "netease-client",
                _ => "java-client"
            })
        };
        var instances = GetInstances();
        lines.Add(Lang.Text("Xe.Ai.Context.Instances",
            instances.Count == 0 ? Lang.Text("Xe.Ai.Context.None") : string.Join(", ", instances.Take(15).Select(i => i.Name))));
        var current = CurrentInstance;
        if (current is not null)
        {
            lines.Add(Lang.Text("Xe.Ai.Context.CurrentInstance", current.Name));
            try { lines.Add(Lang.Text("Xe.Ai.Context.McVersion", current.Info.VanillaName)); } catch { /* 元数据未就绪 */ }
            var folder = GetModsFolder(current);
            List<XeModEntry> mods = folder is null ? [] : ListMods(folder);
            lines.Add(Lang.Text("Xe.Ai.Context.ModCount", mods.Count));
            if (mods.Count > 0)
                lines.Add(Lang.Text("Xe.Ai.Context.ModList",
                    string.Join(", ", mods.Take(25).Select(m => m.RawFileName))));
        }
        return string.Join("\n", lines);
    }
}
