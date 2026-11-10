using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeNeteaseLeft
{
    public PageXeNeteaseLeft()
    {
        InitializeComponent();
        // 直接指定逐个进入动画的作用域：XAML 里的 ElementName 绑定在控件构造阶段可能还是 null，
        // 一旦取到 null 就会退化成「整块缩放」，左栏看起来就是“闪一下”而不是逐项滑入
        AnimatedControl = PanSide;
        // 左栏内容全部由右页在后台扫描后推送（Apply），这里不主动扫盘，避免切换页面时卡顿
        BtnLaunch.Click += (_, _) => ModMain.frmXeNeteaseRight?.LaunchSelected();
        BtnRefresh.Click += (_, _) => ModMain.frmXeNeteaseRight?.Refresh(true);
        BtnOpenFolder.Click += (_, _) => XeNeteaseService.OpenGameDir();
    }

    /// <summary>兼容旧调用：转交给右页刷新（后台线程执行）。</summary>
    public void Refresh()
    {
        ModMain.frmXeNeteaseRight?.Refresh();
    }

    /// <summary>把后台算好的快照显示到左栏。</summary>
    public void Apply(XeNeteaseService.Snapshot snapshot)
    {
        var first = snapshot.Candidates.FirstOrDefault(c => c.Confirmed);

        // 手动填写的目录优先展示，但要明确标注是否被确认为中国版
        LabStatus.Text = snapshot.ConfiguredIsChina
            ? Lang.Text("Xe.Netease.Status.Detected", SafeName(snapshot.ConfiguredDir))
            : snapshot.ConfiguredExists
                ? Lang.Text("Xe.Netease.Status.Unconfirmed")
                : first is not null
                    ? Lang.Text("Xe.Netease.Status.Detected", SafeName(first.Path))
                    : Lang.Text("Xe.Netease.Status.NotDetected");

        ItemDir.Title = Lang.Text("Xe.Netease.McDir.Label");
        ItemDir.Info = snapshot.ActiveDir ?? "—";

        ItemVersions.Title = Lang.Text("Xe.Netease.Left.Versions");
        ItemVersions.Info = snapshot.Versions.Count == 0
            ? Lang.Text("Xe.Netease.Versions.Empty")
            : snapshot.Versions.Count <= 3
                ? string.Join(", ", snapshot.Versions)
                : string.Join(", ", snapshot.Versions.Take(3)) + "…";

        ItemSource.Title = Lang.Text("Xe.Netease.Candidate.Source",
            snapshot.ConfiguredIsChina ? Lang.Text("Xe.Netease.Source.Manual") : first?.Source ?? "—");
        ItemSource.Info = Lang.Text("Xe.Netease.Detect.Done", snapshot.Candidates.Count);
    }

    private static string SafeName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "—";
        try
        {
            return new DirectoryInfo(path).Name;
        }
        catch
        {
            return path;
        }
    }
}
