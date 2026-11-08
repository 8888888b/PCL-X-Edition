using System.IO;
using System.Windows;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeNeteaseLeft
{
    public PageXeNeteaseLeft()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        BtnLaunch.Click += (_, _) => ModMain.frmXeNeteaseRight?.LaunchClient();
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
            : Lang.Text("Xe.Netease.Status.Detected", Path.GetFileName(first.Path));

        ItemLauncher.Title = Lang.Text("Xe.Netease.Path.Label");
        ItemLauncher.Info = first?.Path ?? "—";
        ItemSource.Title = Lang.Text("Xe.Netease.Candidate.Source", first?.Source ?? "—");
        ItemSource.Info = Lang.Text("Xe.Netease.Detect.Done", candidates.Count);

        var versions = XeNeteaseService.GetVersionNames();
        ItemVersions.Title = Lang.Text("Xe.Netease.Right.VersionsTitle");
        ItemVersions.Info = versions.Count == 0
            ? Lang.Text("Xe.Netease.Versions.Empty")
            : string.Join(", ", versions.Take(4)) + (versions.Count > 4 ? "…" : "");
    }
}
