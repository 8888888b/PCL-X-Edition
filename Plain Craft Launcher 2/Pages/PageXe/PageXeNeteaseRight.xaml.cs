using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }

    /// <summary>重新扫描客户端并刷新当前页与左栏状态。</summary>
    public void Refresh()
    {
        LoadConfig();
        RebuildCandidates();
        RebuildVersions();
        ModMain.frmXeNeteaseLeft?.Refresh();
    }

    private void LoadConfig()
    {
        if (!TextLauncherPath.IsFocused) TextLauncherPath.Text = Config.Xe.Netease.LauncherPath;
        if (!TextLaunchArgs.IsFocused) TextLaunchArgs.Text = Config.Xe.Netease.LaunchArgs;
        if (!TextGameDir.IsFocused) TextGameDir.Text = Config.Xe.Netease.GameDir;
    }

    private void SaveConfig()
    {
        Config.Xe.Netease.LauncherPath = TextLauncherPath.Text.Trim();
        Config.Xe.Netease.LaunchArgs = TextLaunchArgs.Text.Trim();
        Config.Xe.Netease.GameDir = TextGameDir.Text.Trim();
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
                Foreground = (System.Windows.Media.Brush)FindResource("ColorBrushGray5")
            });
            return;
        }
        foreach (var candidate in candidates.Take(8))
            PanCandidates.Children.Add(new MyListItem
            {
                Title = candidate.Name,
                Info = $"{candidate.Path}  ·  {Lang.Text("Xe.Netease.Candidate.Source", candidate.Source)}",
                SvgIcon = "lucide/play",
                Height = 45,
                IsHitTestVisible = false
            });
    }

    private void RebuildVersions()
    {
        PanVersions.Children.Clear();
        var versions = XeNeteaseService.GetVersionNames();
        if (versions.Count == 0)
        {
            PanVersions.Children.Add(new TextBlock
            {
                Text = Lang.Text("Xe.Netease.Versions.Empty"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        foreach (var version in versions)
            PanVersions.Children.Add(new MyListItem
            {
                Title = version,
                SvgIcon = "lucide/folder-open",
                Height = 38,
                IsHitTestVisible = false
            });
    }

    public void LaunchClient()
    {
        SaveConfig();
        var ok = XeNeteaseService.Launch(out var message);
        HintService.Hint(message, ok ? HintType.Success : HintType.Warning);
        Refresh();
    }

    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e) => LaunchClient();

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
        Refresh();
        HintService.Hint(Lang.Text("Xe.Netease.Detect.Done", XeNeteaseService.Detect().Count));
    }

    private void BtnBrowse_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Lang.Text("Xe.Netease.Browse.Title"),
            Filter = Lang.Text("Xe.Netease.Browse.Filter"),
            CheckFileExists = true
        };
        var current = TextLauncherPath.Text.Trim();
        if (!string.IsNullOrEmpty(current) && Directory.Exists(Path.GetDirectoryName(current)))
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog() != true) return;
        TextLauncherPath.Text = dialog.FileName;
        SaveConfig();
        Refresh();
    }

    private void BtnSave_Click(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
        Refresh();
        HintService.Hint(Lang.Text("Xe.Ai.Config.Saved"), HintType.Success);
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
        XeNeteaseService.OpenGameDir();
    }

    private void Config_LostFocus(object sender, RoutedEventArgs e) => SaveConfig();
}
