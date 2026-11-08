using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeNeteaseLeft
{
    public PageXeNeteaseLeft()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        BtnLaunch.Click += (_, _) => ModMain.frmXeNeteaseRight?.LaunchSelected();
        BtnRefresh.Click += (_, _) =>
        {
            ModMain.frmXeNeteaseRight?.Refresh();
            Refresh();
        };
        BtnOpenFolder.Click += (_, _) => XeNeteaseService.OpenGameDir();
    }

    public void Refresh()
    {
        var candidates = XeNeteaseService.Detect();
        var first = candidates.FirstOrDefault(c => c.Confirmed);

        // 手动填写的目录优先展示，但要明确标注是否被确认为中国版
        var configured = (Config.Xe.Netease.GameDir ?? "").Trim();
        var hasConfigured = !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured);
        var configuredIsChina = hasConfigured && XeNeteaseService.IsChinaEditionDir(configured);

        LabStatus.Text = configuredIsChina
            ? Lang.Text("Xe.Netease.Status.Detected", new DirectoryInfo(configured).Name)
            : hasConfigured
                ? Lang.Text("Xe.Netease.Status.Unconfirmed")
                : first is not null
                    ? Lang.Text("Xe.Netease.Status.Detected", new DirectoryInfo(first.Path).Name)
                    : Lang.Text("Xe.Netease.Status.NotDetected");

        var shownDir = hasConfigured ? configured : first?.Path;

        ItemDir.Title = Lang.Text("Xe.Netease.McDir.Label");
        ItemDir.Info = shownDir ?? "—";

        var versions = XeNeteaseService.GetVersions(shownDir);
        ItemVersions.Title = Lang.Text("Xe.Netease.Left.Versions");
        ItemVersions.Info = versions.Count == 0
            ? Lang.Text("Xe.Netease.Versions.Empty")
            : versions.Count <= 3
                ? string.Join(", ", versions)
                : string.Join(", ", versions.Take(3)) + "…";

        ItemSource.Title = Lang.Text("Xe.Netease.Candidate.Source",
            configuredIsChina ? Lang.Text("Xe.Netease.Source.Manual") : first?.Source ?? "—");
        ItemSource.Info = Lang.Text("Xe.Netease.Detect.Done", candidates.Count);
    }
}
