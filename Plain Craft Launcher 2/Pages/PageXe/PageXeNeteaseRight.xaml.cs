using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeNeteaseRight
{
    public PageXeNeteaseRight()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        PageEnter += Refresh; // 页面被缓存，每次重新进入都要重新扫描
        BtnRefresh.Click += (_, _) => Refresh();
        BtnLaunch.Click += (_, _) => LaunchSelected();
        BtnBrowseDir.Click += (_, _) => BrowseDir();
        BtnSave.Click += (_, _) =>
        {
            SaveConfig();
            Refresh();
            HintService.Hint(Lang.Text("Xe.Ai.Config.Saved"), HintType.Success);
        };
        BtnOpenFolder.Click += (_, _) => XeNeteaseService.OpenGameDir();
        BtnCopyReport.Click += (_, _) => CopyReport();
        BtnOpenLog.Click += (_, _) => XeNeteaseService.OpenLastLog();
        BtnOpenLogFolder.Click += (_, _) => XeNeteaseService.OpenLogFolder();
        XeNeteaseService.LaunchFinished += OnLaunchFinished;
    }

    /// <summary>重新扫描 .minecraft 并刷新当前页与左栏状态。</summary>
    public void Refresh()
    {
        LoadConfig();
        RebuildCandidates();
        RebuildVersions();
        RefreshConfirmHint();
        RefreshDiagnostics();
        ModMain.frmXeNeteaseLeft?.Refresh();
    }

    /// <summary>当前填写的目录不能确认是中国版时给出提示，避免把国际版当网易版启动。</summary>
    private void RefreshConfirmHint()
    {
        var dir = TextMcDir.Text.Trim();
        var isUnknown = !string.IsNullOrWhiteSpace(dir) && !XeNeteaseService.IsChinaEditionDir(dir);
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
    }

    private void RebuildCandidates()
    {
        PanCandidates.Children.Clear();
        var candidates = XeNeteaseService.Detect();
        if (candidates.Count == 0)
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

        foreach (var candidate in candidates.Take(5))
        {
            var info = candidate.Path + "  ·  " + Lang.Text("Xe.Netease.Candidate.Source", candidate.Source);
            if (!candidate.Confirmed)
                info += "  ·  " + Lang.Text("Xe.Netease.Candidate.Unconfirmed");

            var item = new MyListItem
            {
                Title = new DirectoryInfo(candidate.Path).Name,
                Info = info,
                SvgIcon = "lucide/folder-open",
                Height = 45
            };
            item.Click += (_, _) =>
            {
                TextMcDir.Text = candidate.Path;
                TextVersion.Text = "";
                SaveConfig();
                Refresh();
            };
            PanCandidates.Children.Add(item);
        }
    }

    private void RebuildVersions()
    {
        PanVersions.Children.Clear();
        var dir = string.IsNullOrWhiteSpace(TextMcDir.Text) ? null : TextMcDir.Text.Trim();
        var versions = XeNeteaseService.GetVersions(dir);
        if (versions.Count == 0)
        {
            PanVersions.Children.Add(new TextBlock
            {
                Text = Lang.Text("Xe.Netease.Versions.Empty"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var selected = string.IsNullOrWhiteSpace(TextVersion.Text)
            ? XeNeteaseService.GetPreferredVersion(dir)
            : TextVersion.Text.Trim();

        foreach (var version in versions)
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
                RebuildVersions();
            };
            PanVersions.Children.Add(button);
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
            Refresh();
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
        Refresh();
    }

    private void Config_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        RebuildVersions();
        RefreshConfirmHint();
    }
}
