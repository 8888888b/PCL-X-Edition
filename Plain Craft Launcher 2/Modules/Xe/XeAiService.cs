using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>一条由 AI 返回、等待程序执行的指令。</summary>
public sealed class XeAiAction
{
    public string Type { get; init; } = "";
    public JsonObject Raw { get; init; } = new();

    public string? Str(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Raw[key]?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    public bool Bool(string key, bool fallback = false)
    {
        var value = Raw[key]?.ToString();
        return value switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => fallback
        };
    }
}

/// <summary>AI 的一次回复（纯文本或结构化指令）。</summary>
public sealed class XeAiReply
{
    public string Text { get; init; } = "";
    public List<XeAiAction> Actions { get; init; } = [];
    public bool IsStructured { get; init; }
    public string Raw { get; init; } = "";
}

/// <summary>
/// PCL XE「问问 AI」：调用 OpenAI 兼容的 Chat Completions 接口，并要求模型返回固定 JSON，
/// 由 <see cref="XeAiActions"/> 白名单校验后自动执行。
/// </summary>
public static class XeAiService
{
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Config.Xe.Ai.ApiKey) &&
        !string.IsNullOrWhiteSpace(Config.Xe.Ai.BaseUrl) &&
        !string.IsNullOrWhiteSpace(Config.Xe.Ai.Model);

    public static string Endpoint
    {
        get
        {
            var baseUrl = Config.Xe.Ai.BaseUrl.Trim().TrimEnd('/');
            if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return baseUrl;
            return baseUrl + "/chat/completions";
        }
    }

    /// <summary>允许模型使用的动作清单，同时写在系统提示里，避免模型自由发挥。</summary>
    public const string SupportedActions =
        "install_mod(keyword, mc_version, loader, instance), " +
        "list_mods(instance), enable_mod(file, instance), disable_mod(file, instance), " +
        "open_mods(instance), open_download(keyword), " +
        "switch_page(page: Launch|Bedrock|Netease|Download|ModManager|Setup|Tools|AskAi), " +
        "launch_game, launch_netease, show_hint(text), set_target_mode(mode: client|server|netease), " +
        "refresh_mods, no_action";

    public static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Role"));
        sb.AppendLine();
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Format"));
        sb.AppendLine("{\"reply\":\"用中文简短说明你要做什么\",\"actions\":[{\"type\":\"install_mod\",\"keyword\":\"JEI\",\"mc_version\":\"1.20.1\",\"loader\":\"fabric\",\"instance\":\"\"}]}");
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Rules"));
        sb.AppendLine("- " + Lang.Text("Xe.Ai.Prompt.Rule1"));
        sb.AppendLine("- " + Lang.Text("Xe.Ai.Prompt.Rule2"));
        sb.AppendLine("- " + Lang.Text("Xe.Ai.Prompt.Rule3"));
        sb.AppendLine("- " + Lang.Text("Xe.Ai.Prompt.Rule4"));
        sb.AppendLine();
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Actions", SupportedActions));
        sb.AppendLine();
        if (Config.Xe.Ai.IncludeContext)
        {
            sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Context"));
            try { sb.AppendLine(XeModService.DescribeEnvironment()); }
            catch (Exception ex) { sb.AppendLine("(context unavailable: " + ex.Message + ")"); }
        }
        sb.AppendLine();
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Example1"));
        sb.AppendLine("{\"reply\":\"我来帮你装 JEI\",\"actions\":[{\"type\":\"install_mod\",\"keyword\":\"JEI\",\"mc_version\":\"1.20.1\",\"loader\":\"fabric\"},{\"type\":\"open_mods\"}]}");
        sb.AppendLine(Lang.Text("Xe.Ai.Prompt.Example2"));
        sb.AppendLine("{\"reply\":\"服务端和网易版客户端的模组不能由启动器管理，我先说明原因\",\"actions\":[{\"type\":\"show_hint\",\"text\":\"服务器 / 客户端不可管理模组\"},{\"type\":\"switch_page\",\"page\":\"ModManager\"}]}");
        return sb.ToString();
    }

    /// <summary>发送一轮对话，返回解析后的回复。</summary>
    public static async Task<XeAiReply> ChatAsync(IReadOnlyList<(string Role, string Content)> history, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new XeAiReply { Text = Lang.Text("Xe.Ai.Error.NotConfigured"), Raw = "" };

        var messages = new List<object> { new { role = "system", content = BuildSystemPrompt() } };
        foreach (var (role, content) in history.TakeLast(12))
            messages.Add(new { role, content });

        var body = new
        {
            model = Config.Xe.Ai.Model.Trim(),
            messages,
            temperature = 0.2,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.Xe.Ai.ApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await XeHttp.Ai.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var brief = raw.Length > 400 ? raw[..400] + "…" : raw;
            ModBase.Log($"[XE] AI 接口返回 {(int)response.StatusCode}: {brief}", ModBase.LogLevel.Normal);
            return new XeAiReply
            {
                Text = Lang.Text("Xe.Ai.Error.Http", (int)response.StatusCode, brief),
                Raw = raw,
                IsStructured = false
            };
        }

        string? contentText;
        try
        {
            var json = JsonNode.Parse(raw);
            contentText = json?["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(contentText))
                contentText = json?["choices"]?[0]?["text"]?.ToString();
        }
        catch (Exception ex)
        {
            return new XeAiReply { Text = Lang.Text("Xe.Ai.Error.BadResponse", ex.Message), Raw = raw };
        }

        return ParseReply(contentText ?? "");
    }

    /// <summary>宽容解析：支持 ```json 代码块、前后解释文字、裸数组、单条 action 对象。</summary>
    public static XeAiReply ParseReply(string content)
    {
        var text = (content ?? "").Trim();
        if (text.Length == 0) return new XeAiReply { Text = Lang.Text("Xe.Ai.Error.Empty"), Raw = content };

        // 去掉 markdown 围栏
        var fenced = Regex.Match(text, "```(?:json)?\\s*([\\s\\S]*?)```", RegexOptions.IgnoreCase);
        if (fenced.Success) text = fenced.Groups[1].Value.Trim();

        var candidate = ExtractJsonObject(text) ?? text;
        try
        {
            var node = JsonNode.Parse(candidate);
            if (node is JsonObject obj)
            {
                var actionsNode = obj["actions"] ?? obj["action_list"] ?? obj["commands"];
                var replyText = obj["reply"]?.ToString() ?? obj["message"]?.ToString() ?? obj["text"]?.ToString() ?? "";
                if (actionsNode is null && obj["type"] is not null)
                    actionsNode = new JsonArray { obj.DeepClone() };
                if (actionsNode is not null)
                {
                    var actions = new List<XeAiAction>();
                    if (actionsNode is JsonArray array)
                    {
                        foreach (var item in array)
                            if (item is JsonObject actionObj) AddAction(actions, actionObj);
                    }
                    else if (actionsNode is JsonObject single) AddAction(actions, single);
                    return new XeAiReply
                    {
                        Text = string.IsNullOrWhiteSpace(replyText) ? Lang.Text("Xe.Ai.Reply.Executing", actions.Count) : replyText,
                        Actions = actions,
                        IsStructured = true,
                        Raw = content
                    };
                }
                if (!string.IsNullOrWhiteSpace(replyText))
                    return new XeAiReply { Text = replyText, IsStructured = false, Raw = content };
            }
            else if (node is JsonArray bare)
            {
                var actions = new List<XeAiAction>();
                foreach (var item in bare)
                    if (item is JsonObject actionObj) AddAction(actions, actionObj);
                return new XeAiReply
                {
                    Text = Lang.Text("Xe.Ai.Reply.Executing", actions.Count),
                    Actions = actions,
                    IsStructured = true,
                    Raw = content
                };
            }
        }
        catch { /* 不是 JSON，按普通聊天回复展示 */ }

        return new XeAiReply { Text = text, Raw = content };
    }

    private static void AddAction(List<XeAiAction> list, JsonObject obj)
    {
        if (list.Count >= 10) return; // 单轮最多 10 条指令
        var type = (obj["type"] ?? obj["action"] ?? obj["name"])?.ToString();
        if (string.IsNullOrWhiteSpace(type)) return;
        list.Add(new XeAiAction { Type = type.Trim().ToLowerInvariant(), Raw = obj });
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }
}
