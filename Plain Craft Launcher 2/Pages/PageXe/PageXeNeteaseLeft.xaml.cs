using System.IO;
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
        var first = candidates.FirstOrDefault();
        LabStatus.Text = first is null
            ? Lang.Text("Xe.Netease.Status.NotDetected")
            : Lang.Text("Xe.Netease.Status.Detected", new DirectoryInfo(first.Path).Name);

        ItemDir.Title = Lang.Text("Xe.Netease.McDir.Label");
        ItemDir.Info = first?.Path ?? "—";

        var versions = XeNeteaseService.GetVersions(first?.Path);
        ItemVersions.Title = Lang.Text("Xe.Netease.Left.Versions");
        ItemVersions.Info = versions.Count == 0
            ? Lang.Text("Xe.Netease.Versions.Empty")
            : versions.Count <= 3
                ? string.Join(", ", versions)
                : string.Join(", ", versions.Take(3)) + "…";

        ItemSource.Title = Lang.Text("Xe.Netease.Candidate.Source", first?.Source ?? "—");
        ItemSource.Info = Lang.Text("Xe.Netease.Detect.Done", candidates.Count);
    }
}
