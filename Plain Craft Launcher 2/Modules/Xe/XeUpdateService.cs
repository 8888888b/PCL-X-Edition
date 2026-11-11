using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// PCL XE 自动更新。
///
/// 支持两种更新源：
/// 1. 自建 JSON 清单：{ "version": "2.14.02", "code": 21402, "url": "…/PCLXE.exe", "sha256": "…", "notes": "…" }
/// 2. GitHub Releases API：https://api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest
///
/// 下载完成后交给启动器内置的更新器（UpdateManager.UpdateRestart）替换 exe 并重启，
/// 因此不需要额外的更新程序，也不会出现“自己覆盖自己”的文件占用问题。
/// </summary>
public static class XeUpdateService
{
    public sealed record UpdateInfo(
        string Version,
        int Code,
        string Url,
        string? Sha256,
        long Size,
        string? Notes,
        string? PageUrl);

    /// <summary>最近一次检查到的新版本；没有新版本时为 null。</summary>
    public static UpdateInfo? Latest { get; private set; }

    /// <summary>清空检查结果（更新源变更后调用）。</summary>
    public static void Reset() => Latest = null;

    public static string CurrentVersion => ModBase.versionBaseName;
    public static int CurrentCode => ModBase.versionCode;

    /// <summary>官方更新源（GitHub Releases API），也是更新源留空时的默认值。</summary>
    public static string DefaultManifestUrl => Config.XeDefaultManifestUrl;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Config.Xe.Update.ManifestUrl);

    /// <summary>检查更新。未配置更新源时返回 null。</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var url = (Config.Xe.Update.ManifestUrl ?? "").Trim();
        if (url.Length == 0) return null;

        using var response = await XeHttp.General.GetAsync(url, ct).ConfigureAwait(false);
        // 仓库还没发布过 Release 时 GitHub 返回 404，这不是错误，只是没有新版本
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Latest = null;
            Config.Xe.Update.LastCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return null;
        }

        // 未登录调用 GitHub API 有 60 次/小时的限制，超出时给出可读提示
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Lang.Text("Setup.Update.Xe.RateLimited"));

        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var info = Parse(text, url) ?? throw new InvalidOperationException(
            Lang.Text("Setup.Update.Xe.ParseFailed"));
        Latest = IsNewer(info) ? info : null;
        Config.Xe.Update.LastCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return info;
    }

    /// <summary>判断清单里的版本是否高于当前版本。</summary>
    public static bool IsNewer(UpdateInfo info)
    {
        if (info.Code > 0 && CurrentCode > 0) return info.Code > CurrentCode;
        return CompareVersion(info.Version, CurrentVersion) > 0;
    }

    /// <summary>下载更新文件到临时目录，校验 SHA-256 后返回本地路径。</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PCLXE", "update");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "PCLXE-update.exe");
        if (File.Exists(file)) File.Delete(file);

        using var response = await XeHttp.General
            .GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? info.Size;

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                read += count;
                if (total > 0) progress?.Report(Math.Min(1d, (double)read / total));
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            var actual = ModBase.GetFileSHA256(file);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 忽略删除失败
                }

                throw new InvalidOperationException(Lang.Text("Setup.Update.Xe.HashMismatch"));
            }
        }

        progress?.Report(1d);
        return file;
    }

    /// <summary>把下载好的 exe 放进更新目录，交给内置更新器替换并重启。</summary>
    public static void InstallAndRestart(string downloadedFile)
    {
        var target = Path.Combine(ModBase.exePath, "PCL", "Plain Craft Launcher XE.exe");
        var folder = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        File.Copy(downloadedFile, target, true);
        ModBase.Log($"[XE] 准备安装更新：{target}");
        UpdateManager.UpdateRestart(true);
    }

    // ---- 解析 ----

    private static UpdateInfo? Parse(string text, string sourceUrl)
    {
        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject;
        }
        catch
        {
            return null;
        }

        if (obj is null) return null;

        // GitHub Releases API
        if (obj["tag_name"] is JsonNode tagNode)
        {
            var version = CleanVersion((string?)tagNode ?? "");
            var asset = (obj["assets"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(a =>
                ((string?)a["name"] ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            var download = (string?)asset?["browser_download_url"];
            if (version.Length == 0 || string.IsNullOrWhiteSpace(download)) return null;

            var sha = (string?)asset?["digest"];
            if (!string.IsNullOrWhiteSpace(sha))
            {
                var index = sha.IndexOf(':');
                if (index >= 0) sha = sha[(index + 1)..];
            }

            return new UpdateInfo(version, 0, download, sha, (long?)asset?["size"] ?? 0,
                (string?)obj["body"], (string?)obj["html_url"]);
        }

        // 自建清单
        var listVersion = CleanVersion((string?)obj["version"] ?? (string?)obj["tag"] ?? "");
        var listUrl = (string?)obj["url"] ?? (string?)obj["download"];
        if (listVersion.Length == 0 || string.IsNullOrWhiteSpace(listUrl)) return null;
        if (!listUrl.Contains("://"))
        {
            try
            {
                listUrl = new Uri(new Uri(sourceUrl), listUrl).ToString();
            }
            catch
            {
                return null;
            }
        }

        return new UpdateInfo(listVersion, (int?)obj["code"] ?? 0, listUrl, (string?)obj["sha256"],
            (long?)obj["size"] ?? 0, (string?)obj["notes"] ?? (string?)obj["changelog"],
            (string?)obj["page"]);
    }

    private static string CleanVersion(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return text.Trim();
    }

    private static int CompareVersion(string left, string right)
    {
        var a = NumberParts(left);
        var b = NumberParts(right);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }

    private static List<int> NumberParts(string version)
    {
        var result = new List<int>();
        foreach (var part in (version ?? "").Split('.', '-', '+', ' '))
        {
            var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0) break;
            result.Add(int.TryParse(digits, out var value) ? value : 0);
        }

        return result;
    }
}
