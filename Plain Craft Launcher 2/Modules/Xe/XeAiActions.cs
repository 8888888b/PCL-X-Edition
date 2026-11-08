using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// PCL XE：「问问 AI」返回的 JSON 指令执行器。
/// 只执行白名单内的动作；涉及网络下载、启动游戏、禁用模组等动作会在执行前
/// 用一次汇总确认弹窗征得用户同意（可在设置中关闭），之后全部自动完成。
/// 模型永远不能直接执行命令行、写任意路径或读取配置密钥。
/// </summary>
public static class XeAiActions
{
    private static readonly HashSet<string> Allowed =
    [
        "install_mod", "list_mods", "enable_mod", "disable_mod", "open_mods", "open_download",
        "switch_page", "launch_game", "launch_netease", "show_hint", "set_target_mode",
        "refresh_mods", "no_action"
    ];

    private static void Ui(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    /// <summary>执行一整批指令，返回逐条执行日志。</summary>
    public static async Task<List<string>> ExecuteAsync(
        IReadOnlyList<XeAiAction> actions, CancellationToken ct = default)
    {
        var logs = new List<string>();
        if (actions.Count == 0) return logs;

        if (Config.Xe.Ai.ConfirmDanger && !Confirm(actions, logs)) return logs;

        foreach (var action in actions)
        {
            if (ct.IsCancellationRequested)
            {
                logs.Add(Lang.Text("Xe.Ai.Action.Canceled"));
                break;
            }
            string message;
            try
            {
                message = await ExecuteOneAsync(action, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "执行 AI 指令失败", ModBase.LogLevel.Normal);
                message = Lang.Text("Xe.Ai.Action.Failed", action.Type, ex.Message);
            }
            var line = $"[{action.Type}] {message}";
            logs.Add(line);
            ModBase.Log("[XE] " + line);
        }
        return logs;
    }

    private static bool Confirm(IReadOnlyList<XeAiAction> actions, List<string> logs)
    {
        var summary = string.Join("\n", actions.Select((a, i) => $"  {i + 1}. {a.Type}  {Describe(a)}"));
        var result = 1;
        Ui(() => result = ModMain.MyMsgBox(
            Lang.Text("Xe.Ai.Confirm.Body", actions.Count) + "\n" + summary,
            Lang.Text("Xe.Ai.Confirm.Title"),
            Lang.Text("Xe.Ai.Confirm.Allow"),
            Lang.Text("Xe.Ai.Confirm.Deny")));
        if (result == 1) return true;
        logs.Add(Lang.Text("Xe.Ai.Confirm.Denied"));
        return false;
    }

    private static string Describe(XeAiAction action)
    {
        var parts = new List<string>();
        foreach (var key in new[] { "keyword", "file", "instance", "mc_version", "loader", "page", "mode", "text" })
        {
            var value = action.Str(key);
            if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{key}={value}");
        }
        return string.Join(", ", parts);
    }

    private static async Task<string> ExecuteOneAsync(XeAiAction action, CancellationToken ct)
    {
        if (!Allowed.Contains(action.Type))
            return Lang.Text("Xe.Ai.Action.Rejected", action.Type);

        switch (action.Type)
        {
            case "no_action":
                return Lang.Text("Xe.Ai.Action.Noop");

            case "show_hint":
            {
                var text = action.Str("text", "message") ?? Lang.Text("Xe.Ai.Action.Noop");
                Ui(() => HintService.Hint(text, HintType.Info));
                return text;
            }

            case "switch_page":
            {
                var page = action.Str("page") ?? "Launch";
                if (!TryGetPage(page, out var pageType))
                    return Lang.Text("Xe.Ai.Action.BadPage", page);
                Ui(() => ModMain.frmMain?.PageChange(pageType));
                return Lang.Text("Xe.Ai.Action.PageSwitched", page);
            }

            case "open_mods":
            {
                SelectInstance(action.Str("instance"));
                Ui(() =>
                {
                    ModMain.frmMain?.PageChange(FormMain.PageType.ModManager);
                    ModMain.frmXeModManagerRight?.Refresh();
                });
                return Lang.Text("Xe.Ai.Action.ModsOpened");
            }

            case "open_download":
            {
                var keyword = action.Str("keyword", "query") ?? "";
                Ui(() =>
                {
                    if (!string.IsNullOrWhiteSpace(keyword)) Clipboard.SetText(keyword);
                    ModMain.frmMain?.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadMod);
                });
                return Lang.Text("Xe.Ai.Action.DownloadOpened", keyword);
            }

            case "refresh_mods":
                Ui(() => ModMain.frmXeModManagerRight?.Refresh());
                return Lang.Text("Xe.Ai.Action.ModsRefreshed");

            case "set_target_mode":
            {
                var modeName = (action.Str("mode") ?? "client").ToLowerInvariant();
                var mode = modeName switch
                {
                    "server" => XeModTargetMode.Server,
                    "netease" or "netease-client" or "netease_client" => XeModTargetMode.NeteaseClient,
                    _ => XeModTargetMode.Client
                };
                XeModService.CurrentMode = mode;
                Ui(() =>
                {
                    ModMain.frmXeModManagerLeft?.Refresh();
                    ModMain.frmXeModManagerRight?.Refresh();
                });
                return Lang.Text("Xe.Ai.Action.ModeChanged", modeName);
            }

            case "launch_game":
            {
                var ok = false;
                Ui(() =>
                {
                    try { ok = ModLaunch.McLaunchStart(); }
                    catch (Exception ex) { HintService.Hint(ex.Message, HintType.Error); }
                });
                return ok ? Lang.Text("Xe.Ai.Action.Launched") : Lang.Text("Xe.Ai.Action.LaunchFailed");
            }

            case "launch_netease":
            {
                var launched = XeNeteaseService.Launch(out var message);
                Ui(() => HintService.Hint(message, launched ? HintType.Success : HintType.Warning));
                return message;
            }

            case "list_mods":
            {
                var instance = ResolveInstance(action.Str("instance"));
                var folder = XeModService.GetModsFolder(instance);
                if (folder is null) return Lang.Text("Xe.Ai.Action.Install.NoFolder");
                var mods = XeModService.ListMods(folder);
                return mods.Count == 0
                    ? Lang.Text("Xe.Ai.Action.ListEmpty")
                    : Lang.Text("Xe.Ai.Action.ListResult", mods.Count,
                        string.Join(", ", mods.Take(20).Select(m => m.RawFileName)));
            }

            case "install_mod":
                return await InstallModAsync(action, ct).ConfigureAwait(false);

            case "enable_mod":
            case "disable_mod":
                return ToggleMod(action, action.Type == "enable_mod");

            default:
                return Lang.Text("Xe.Ai.Action.Rejected", action.Type);
        }
    }

    private static async Task<string> InstallModAsync(XeAiAction action, CancellationToken ct)
    {
        var keyword = action.Str("keyword", "name", "mod", "query");
        var instance = ResolveInstance(action.Str("instance"));
        var mode = XeModService.CurrentMode;
        if (!XeModService.IsManageable(mode, instance, out var reason))
            return reason; // 服务器 / 网易版客户端不可管理模组

        var folder = XeModService.GetModsFolder(instance);
        if (string.IsNullOrWhiteSpace(folder)) return Lang.Text("Xe.Ai.Action.Install.NoFolder");

        string mcVersion = action.Str("mc_version", "game_version", "version") ?? "";
        if (string.IsNullOrWhiteSpace(mcVersion))
        {
            try { mcVersion = instance?.Info.VanillaName ?? ""; } catch { /* 元数据未就绪 */ }
        }
        var loader = action.Str("loader", "mod_loader") ?? "";
        var result = await XeModService.InstallFromModrinthAsync(keyword ?? "", mcVersion, loader, folder, ct)
            .ConfigureAwait(false);
        Ui(() => ModMain.frmXeModManagerRight?.Refresh());
        return result.Message;
    }

    private static string ToggleMod(XeAiAction action, bool enable)
    {
        var instance = ResolveInstance(action.Str("instance"));
        var mode = XeModService.CurrentMode;
        if (!XeModService.IsManageable(mode, instance, out var reason)) return reason;
        var folder = XeModService.GetModsFolder(instance);
        if (string.IsNullOrWhiteSpace(folder)) return Lang.Text("Xe.Ai.Action.Install.NoFolder");
        var keyword = action.Str("file", "name", "keyword") ?? "";
        var match = XeModService.ListMods(folder).FirstOrDefault(m =>
            m.RawFileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            m.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (match is null) return Lang.Text("Xe.Ai.Action.ModNotFound", keyword);
        var result = XeModService.SetEnabled(match, enable, mode, instance);
        Ui(() => ModMain.frmXeModManagerRight?.Refresh());
        return result.Message;
    }

    private static void SelectInstance(string? name)
    {
        var instance = XeModService.FindInstance(name);
        if (instance is not null) XeModService.CurrentInstance = instance;
    }

    private static McInstance? ResolveInstance(string? name) =>
        XeModService.FindInstance(name) ?? XeModService.CurrentInstance;

    private static bool TryGetPage(string name, out FormMain.PageType page)
    {
        switch (name.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", ""))
        {
            case "launch" or "java": page = FormMain.PageType.Launch; return true;
            case "bedrock": page = FormMain.PageType.Bedrock; return true;
            case "netease" or "china": page = FormMain.PageType.Netease; return true;
            case "download": page = FormMain.PageType.Download; return true;
            case "modmanager" or "mods" or "mod": page = FormMain.PageType.ModManager; return true;
            case "setup" or "settings": page = FormMain.PageType.Setup; return true;
            case "tools": page = FormMain.PageType.Tools; return true;
            case "askai" or "ai": page = FormMain.PageType.AskAi; return true;
            default: page = FormMain.PageType.Launch; return false;
        }
    }
}
