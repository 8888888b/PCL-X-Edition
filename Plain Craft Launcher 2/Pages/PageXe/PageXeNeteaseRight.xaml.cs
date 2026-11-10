using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeNeteaseRight
{
    private string _lastCandidatesSignature = "";
    private string _lastVersionsSignature = "";

    private bool _refreshing;
    private DateTime _enterAt = DateTime.MinValue;

    public PageXeNeteaseRight()
    {
        InitializeComponent();
        PanScroll = PanBack;
        // 页面实例会被缓存，每次重新进入都要刷新；PageEnter 已经覆盖首次进入
        PageEnter += () =>
        {
            _enterAt = DateTime.UtcNow;
            Refresh();
        };
        BtnRefresh.Click += (_, _) => Refresh(true);
        BtnLaunch.Click += (_, _) => LaunchSelected();
        BtnBrowseDir.Click += (_, _) => BrowseDir();
        BtnSave.Click += (_, _) =>
        {
            SaveConfig();
            Refresh(true);
            HintService.Hint(Lang.Text("Xe.Ai.Config.Saved"), HintType.Success);
        };
        BtnOpenFolder.Click += (_, _) => XeNeteaseService.OpenGameDir();
        BtnCopyReport.Click += (_, _) => CopyReport();
        BtnOpenLog.Click += (_, _) => XeNeteaseService.OpenLastLog();
        BtnOpenLogFolder.Click += (_, _) => XeNeteaseService.OpenLogFolder();
        XeNeteaseService.LaunchFinished += OnLaunchFinished;
    }

    /// <summary>
    ///     刷新页面。扫描与注册表读取全部在后台线程完成，UI 只做一次赋值，
    ///     因此不会阻塞页面切换 / 逐个进入动画。
    /// </summary>
    /// <param name="force">true 时忽略缓存重新扫盘（用户手动点「重新检测」时使用）。</param>
    public void Refresh(bool force = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Refresh(force));
            return;
        }

        LoadConfig();

        // 1. 有缓存就先填一遍，页面立刻有内容，不会出现「空白再跳出来」
        var dir = TextMcDir.Text.Trim();
        var cached = XeNeteaseService.PeekCache(dir);
        if (cached is not null)
        {
            Apply(cached);
            if (!force) return;
        }

        // 2. 后台重扫。刚进入页面时先让入场动画跑完，避免重建列表打断动画
        if (_refreshing) return;
        _refreshing = true;
        var delay = DateTime.UtcNow - _enterAt < TimeSpan.FromMilliseconds(420) ? 300 : 0;
        _ = Task.Run(() =>
        {
            try
            {
                if (delay > 0) Thread.Sleep(delay);
                var snapshot = XeNeteaseService.Capture(dir, force);
                Dispatcher.Invoke(() => Apply(snapshot));
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[XE] 刷新网易版页面失败", ModBase.LogLevel.Debug);
            }
            finally
            {
                _refreshing = false;
            }
        });
    }

    /// <summary>把后台算好的数据一次性刷到界面上。</summary>
    private void Apply(XeNeteaseService.Snapshot snapshot)
    {
        ApplyCandidates(snapshot.Candidates);
        ApplyVersions(snapshot);
        ApplyConfirmHint(snapshot);
        RefreshDiagnostics();
        ModMain.frmXeNeteaseLeft?.Apply(snapshot);
    }

    /// <summary>当前填写的目录不能确认是中国版时给出提示，避免把国际版当网易版启动。</summary>
    private void ApplyConfirmHint(XeNeteaseService.Snapshot snapshot)
    {
        var isUnknown = snapshot.ConfiguredDir is not null && !snapshot.ConfiguredIsChina;
        HintNotChina.Visibility = isUnknown ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadConfig()
    {
        if (!TextMcDir.IsFocused) TextMcDir.Text = Config.Xe.Netease.GameDir;
        if (!TextVersion.IsFocused) TextVersion.Text = Config.Xe.Netease.SelectedVersion;
    }

    private void SaveConfig()
    {
        Config.Xe.Netease.GameDir = TextMcDir.Text.Trim();
        Config.Xe.Netease.SelectedVersion = TextVersion.Text.Trim();
        XeNeteaseService.InvalidateDetect();
    }

    private void ApplyCandidates(List<XeNeteaseService.Candidate> candidates)
    {
        var shown = candidates.Take(5).ToList();
        // 内容没变就不重建，避免每次进页面都整块刷新
        var signature = string.Join("|", shown.Select(c => $"{c.Path}#{c.Confirmed}#{c.Source}"));
        if (signature == _lastCandidatesSignature) return;
        _lastCandidatesSignature = signature;

        PanCandidates.Children.Clear();
        if (shown.Count == 0)
        {
            PanCandidates.Children.Add(new TextBlock
            {
                Text = Lang.Text("Xe.Netease.Launch.NotFound"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = (Brush)FindResource("ColorBrushGray5")
            });
            return;
        }

        foreach (var candidate in shown)
        {
            var info = candidate.Path + "  ·  " + Lang.Text("Xe.Netease.Candidate.Source", candidate.Source);
            if (!candidate.Confirmed)
                info += "  ·  " + Lang.Text("Xe.Netease.Candidate.Unconfirmed");

            var item = new MyListItem
            {
                Title = SafeName(candidate.Path),
                Info = info,
                SvgIcon = "lucide/folder-open",
                Height = 45
            };
            item.Click += (_, _) =>
            {
                TextMcDir.Text = candidate.Path;
                TextVersion.Text = "";
                SaveConfig();
                Refresh(true);
            };
            PanCandidates.Children.Add(item);
        }
    }

    private void ApplyVersions(XeNeteaseService.Snapshot snapshot)
    {
        var selected = string.IsNullOrWhiteSpace(TextVersion.Text)
            ? snapshot.PreferredVersion
            : TextVersion.Text.Trim();
        var signature = snapshot.ActiveDir + "||" + string.Join("|", snapshot.Versions) + "||" + selected;
        if (signature == _lastVersionsSignature) return;
        _lastVersionsSignature = signature;

        PanVersions.Children.Clear();
        if (snapshot.Versions.Count == 0)
        {
            PanVersions.Children.Add(new TextBlock
            {
                Text = Lang.Text("Xe.Netease.Versions.Empty"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var version in snapshot.Versions)
        {
            var isSelected = string.Equals(version, selected, StringComparison.OrdinalIgnoreCase);
            var button = new MyButton
            {
                Text = version,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColorType = isSelected ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal
            };
            button.Click += (_, _) =>
            {
                TextVersion.Text = version;
                SaveConfig();
                // 只切换高亮，不做任何磁盘访问
                foreach (var child in PanVersions.Children)
                    if (child is MyButton target)
                        target.ColorType = string.Equals(target.Text, version, StringComparison.OrdinalIgnoreCase)
                            ? MyButton.ColorState.Highlight
                            : MyButton.ColorState.Normal;
            };
            PanVersions.Children.Add(button);
        }
    }

    private static string SafeName(string path)
    {
        try
        {
            return new DirectoryInfo(path).Name;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>启动当前选中（或最近使用）的版本。</summary>
    public void LaunchSelected()
    {
        SaveConfig();
        var version = string.IsNullOrWhiteSpace(TextVersion.Text) ? null : TextVersion.Text.Trim();
        var ok = XeNeteaseService.Launch(version, out var message);
        if (ok)
        {
            HintService.Hint(message, HintType.Success);
            Refresh(true);
            return;
        }

        HintService.Hint(message, HintType.Warning);
        Refresh();
        // 启动前的失败（目录、版本、账号、实例）直接把完整报告弹出来，便于用户复制或查看日志
        var diag = XeNeteaseDiagnostics.Current;
        if (diag is { IsFailed: true }) XeNeteaseService.ShowFailureReport(diag);
    }

    #region 启动诊断

    private void OnLaunchFinished(XeNeteaseDiagnostics diag)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => RefreshDiagnostics());
            return;
        }

        RefreshDiagnostics();
    }

    /// <summary>展示最近一次启动的结果、失败原因与日志位置。</summary>
    private void RefreshDiagnostics()
    {
        var diag = XeNeteaseDiagnostics.Current;
        if (diag is null)
        {
            LabDiagStatus.Text = Lang.Text("Xe.Netease.Diag.NotTried");
            LabDiagDetail.Visibility = Visibility.Collapsed;
            PanDiagnostics.Visibility = Visibility.Collapsed;
            LabDiagLog.Text = Lang.Text("Xe.Netease.Diag.LogFolder", XeNeteaseDiagnostics.LogDirectory);
            return;
        }

        LabDiagStatus.Text = diag.IsFailed
            ? Lang.Text("Xe.Netease.Diag.Failed", diag.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))
            : Lang.Text("Xe.Netease.Diag.Success", diag.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));

        if (diag.IsFailed)
        {
            LabDiagDetail.Text = Lang.Text("Xe.Netease.Diag.Detail", diag.Stage ?? "—", diag.FailureMessage ?? "—");
            LabDiagDetail.Visibility = Visibility.Visible;
        }
        else
        {
            LabDiagDetail.Text = diag.Version is null
                ? ""
                : Lang.Text("Xe.Netease.Diag.Version", diag.Version);
            LabDiagDetail.Visibility = string.IsNullOrWhiteSpace(LabDiagDetail.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        LabDiagLog.Text = diag.LogPath is null
            ? Lang.Text("Xe.Netease.Diag.LogUnavailable")
            : Lang.Text("Xe.Netease.Diag.LogFile", diag.LogPath);
        // 成功时也保留「打开日志」，便于排查启动后立刻退出的情况；「复制错误报告」只在失败时出现
        PanDiagnostics.Visibility = Visibility.Visible;
        BtnCopyReport.Visibility = diag.IsFailed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyReport()
    {
        var diag = XeNeteaseDiagnostics.Current;
        if (diag is null) return;
        ModBase.ClipboardSet(diag.BuildReport());
    }

    #endregion

    private void BrowseDir()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Lang.Text("Xe.Netease.BrowseDir.Title"),
            Multiselect = false
        };
        var current = TextMcDir.Text.Trim();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog() != true) return;
        TextMcDir.Text = dialog.FolderName;
        TextVersion.Text = "";
        SaveConfig();
        Refresh(true);
    }

    private void Config_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        Refresh(true);
    }
}
