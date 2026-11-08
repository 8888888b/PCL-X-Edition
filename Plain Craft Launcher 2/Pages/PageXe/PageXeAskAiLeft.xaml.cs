using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeAskAiLeft
{
    public PageXeAskAiLeft()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        BtnExample1.Click += (_, _) => ModMain.frmXeAskAiRight?.Submit(Lang.Text("Xe.Ai.Example1"));
        BtnExample2.Click += (_, _) => ModMain.frmXeAskAiRight?.Submit(Lang.Text("Xe.Ai.Example2"));
        BtnExample3.Click += (_, _) => ModMain.frmXeAskAiRight?.Submit(Lang.Text("Xe.Ai.Example3"));
        BtnExample4.Click += (_, _) => ModMain.frmXeAskAiRight?.Submit(Lang.Text("Xe.Ai.Example4"));
    }

    public void Refresh()
    {
        ItemStatus.Title = Lang.Text("Xe.Ai.Left.Status");
        ItemStatus.Info = XeAiService.IsConfigured
            ? Lang.Text("Xe.Ai.Status.Configured")
            : Lang.Text("Xe.Ai.Status.NotConfigured");
        ItemModel.Title = Lang.Text("Xe.Ai.Config.Model");
        ItemModel.Info = Config.Xe.Ai.Model;
        ItemTarget.Title = Lang.Text("Xe.Mods.Left.Target");
        ItemTarget.Info = XeModService.CurrentMode switch
        {
            XeModTargetMode.Server => Lang.Text("Xe.Mods.Mode.Server"),
            XeModTargetMode.NeteaseClient => Lang.Text("Xe.Mods.Mode.Netease"),
            _ => Lang.Text("Xe.Mods.Mode.Client")
        };
    }
}
