using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeAskAiRight
{
    private readonly List<(string Role, string Content)> _history = [];
    private bool _sending;
    private bool _loading;
    private TextBlock? _emptyChat;
    private TextBlock? _emptyLog;
    private CancellationTokenSource? _cts;

    public PageXeAskAiRight()
    {
        InitializeComponent();
        PanScroll = PanBack;
        Loaded += (_, _) =>
        {
            LoadConfig();
            ShowEmptyChat();
            ShowEmptyLog();
            RefreshStatus();
        };
        PageEnter += () =>
        {
            LoadConfig();
            RefreshStatus();
        };
    }

    #region 配置

    private void LoadConfig()
    {
        _loading = true;
        try
        {
            if (!TextBaseUrl.IsFocused) TextBaseUrl.Text = Config.Xe.Ai.BaseUrl;
            if (!TextApiKey.IsFocused) TextApiKey.Text = Config.Xe.Ai.ApiKey;
            if (!TextModel.IsFocused) TextModel.Text = Config.Xe.Ai.Model;
            CheckAutoExecute.Checked = Config.Xe.Ai.AutoExecute;
            CheckConfirm.Checked = Config.Xe.Ai.ConfirmDanger;
            CheckContext.Checked = Config.Xe.Ai.IncludeContext;
        }
        finally
        {
            _loading = false;
        }
        RefreshStatus();
    }

    private void SaveConfig()
    {
        Config.Xe.Ai.BaseUrl = TextBaseUrl.Text.Trim();
        Config.Xe.Ai.ApiKey = TextApiKey.Text.Trim();
        Config.Xe.Ai.Model = TextModel.Text.Trim();
        Config.Xe.Ai.AutoExecute = CheckAutoExecute.Checked == true;
        Config.Xe.Ai.ConfirmDanger = CheckConfirm.Checked == true;
        Config.Xe.Ai.IncludeContext = CheckContext.Checked == true;
    }

    private void RefreshStatus()
    {
        if (!IsLoaded) return;
        HintNeedConfig.Visibility = XeAiService.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
        BtnSend.IsEnabled = XeAiService.IsConfigured && !_sending;
        ModMain.frmXeAskAiLeft?.Refresh();
    }

    private void Config_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        RefreshStatus();
    }

    private void CheckBox_Change(object sender, bool user)
    {
        if (_loading) return;
        SaveConfig();
    }

    private void BtnSaveConfig_Click(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
        if (TextBaseUrl.Text.Trim().Length == 0 || TextApiKey.Text.Trim().Length == 0 || TextModel.Text.Trim().Length == 0)
        {
            HintService.Hint(Lang.Text("Xe.Ai.Config.Missing"), HintType.Warning);
            return;
        }
        RefreshStatus();
        HintService.Hint(Lang.Text("Xe.Ai.Config.Saved"), HintType.Success);
    }

    private async void BtnTest_Click(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
        if (!XeAiService.IsConfigured)
        {
            HintService.Hint(Lang.Text("Xe.Ai.Config.Missing"), HintType.Warning);
            return;
        }
        BtnTest.IsEnabled = false;
        try
        {
            var reply = await XeAiService.ChatAsync(
                [("user", "ping")], CancellationToken.None);
            HintService.Hint(Lang.Text("Xe.Ai.Config.TestOk", reply.Text), HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint(Lang.Text("Xe.Ai.Config.TestFailed", ex.Message), HintType.Error);
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }

    #endregion

    #region 对话

    public void Submit(string text)
    {
        TextInput.Text = text;
        Send();
    }

    private void BtnSend_Click(object sender, MouseButtonEventArgs e) => Send();

    private void TextInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Send();
    }

    private void BtnClear_Click(object sender, MouseButtonEventArgs e)
    {
        _cts?.Cancel();
        _history.Clear();
        PanChat.Children.Clear();
        PanLog.Children.Clear();
        _emptyChat = null;
        _emptyLog = null;
        ShowEmptyChat();
        ShowEmptyLog();
    }

    private async void Send()
    {
        var input = TextInput.Text.Trim();
        if (input.Length == 0 || _sending) return;
        if (!XeAiService.IsConfigured)
        {
            HintService.Hint(Lang.Text("Xe.Ai.Error.NotConfigured"), HintType.Warning);
            return;
        }

        SaveConfig();
        _sending = true;
        RefreshStatus();
        TextInput.Text = "";
        RemoveEmptyChat();
        AddMessage(Lang.Text("Xe.Ai.Chat.User"), input, false);
        _history.Add(("user", input));
        var thinking = AddMessage(Lang.Text("Xe.Ai.Chat.Assistant"), Lang.Text("Xe.Ai.Chat.Thinking"), true);

        _cts = new CancellationTokenSource();
        try
        {
            var reply = await XeAiService.ChatAsync(_history, _cts.Token);
            thinking.Text = reply.Text;
            _history.Add(("assistant", reply.Raw.Length > 0 ? reply.Raw : reply.Text));

            if (reply.IsStructured && reply.Actions.Count > 0)
            {
                AddMessage(Lang.Text("Xe.Ai.Chat.JsonLabel"), FormatJson(reply.Raw), true);
                if (Config.Xe.Ai.AutoExecute)
                {
                    var logs = await XeAiActions.ExecuteAsync(reply.Actions, _cts.Token);
                    foreach (var log in logs) AddLog(log);
                    AddMessage(Lang.Text("Xe.Ai.Chat.System"), string.Join("\n", logs), true);
                }
                else
                {
                    AddMessage(Lang.Text("Xe.Ai.Chat.System"), Lang.Text("Xe.Ai.Chat.AutoOff"), true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            thinking.Text = Lang.Text("Xe.Ai.Action.Canceled");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "问问 AI 请求失败", ModBase.LogLevel.Normal);
            thinking.Text = Lang.Text("Xe.Ai.Error.SendFailed", ex.Message);
        }
        finally
        {
            _sending = false;
            RefreshStatus();
            PanBack.ScrollToEnd();
        }
    }

    private static string FormatJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        try
        {
            var node = JsonNode.Parse(raw);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? raw;
        }
        catch { return raw; }
    }

    private TextBlock AddMessage(string role, string text, bool highlighted)
    {
        var border = new Border
        {
            Background = (Brush)FindResource(highlighted ? "ColorBrushTransparentBackground" : "ColorBrushBg0"),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = role,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3),
            Foreground = (Brush)FindResource("ColorBrushGray5")
        });
        var body = new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("ColorBrush3")
        };
        panel.Children.Add(body);
        border.Child = panel;
        PanChat.Children.Add(border);
        return body;
    }

    private void ShowEmptyChat()
    {
        if (_emptyChat is not null || PanChat.Children.Count > 0) return;
        _emptyChat = new TextBlock
        {
            Text = Lang.Text("Xe.Ai.Chat.Empty"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = (Brush)FindResource("ColorBrushGray5")
        };
        PanChat.Children.Add(_emptyChat);
    }

    private void RemoveEmptyChat()
    {
        if (_emptyChat is null) return;
        PanChat.Children.Remove(_emptyChat);
        _emptyChat = null;
    }

    private void AddLog(string line)
    {
        RemoveEmptyLog();
        PanLog.Children.Add(new TextBlock
        {
            Text = "• " + line,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = (Brush)FindResource("ColorBrushGray5")
        });
    }

    private void ShowEmptyLog()
    {
        if (_emptyLog is not null || PanLog.Children.Count > 0) return;
        _emptyLog = new TextBlock
        {
            Text = Lang.Text("Xe.Ai.Log.Empty"),
            FontSize = 12,
            Foreground = (Brush)FindResource("ColorBrushGray5")
        };
        PanLog.Children.Add(_emptyLog);
    }

    private void RemoveEmptyLog()
    {
        if (_emptyLog is null) return;
        PanLog.Children.Remove(_emptyLog);
        _emptyLog = null;
    }

    #endregion

    private void BtnCopyJson_Click(object sender, MouseButtonEventArgs e)
    {
        const string sample = """
                              {
                                "reply": "我来帮你装 JEI",
                                "actions": [
                                  { "type": "install_mod", "keyword": "JEI", "mc_version": "1.20.1", "loader": "fabric", "instance": "" },
                                  { "type": "open_mods" }
                                ]
                              }
                              """;
        Clipboard.SetText(sample);
        HintService.Hint(Lang.Text("Xe.Ai.Chat.Copied"), HintType.Success);
    }
}
