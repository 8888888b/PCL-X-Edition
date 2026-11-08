using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeModManagerLeft
{
    private bool _loading;

    public PageXeModManagerLeft()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _loading = true;
        try
        {
            ComboMode.SelectedIndex = (int)XeModService.CurrentMode;

            var instances = XeModService.GetInstances();
            ComboInstance.Items.Clear();
            foreach (var instance in instances)
                ComboInstance.Items.Add(new MyComboBoxItem { Content = instance.Name });
            if (instances.Count == 0)
                ComboInstance.Items.Add(new MyComboBoxItem { Content = Lang.Text("Xe.Mods.Select.NoInstance") });

            var current = XeModService.CurrentInstance;
            var index = current is null ? -1 : instances.FindIndex(i => ReferenceEquals(i, current));
            ComboInstance.SelectedIndex = index >= 0 ? index : (instances.Count > 0 ? 0 : -1);
        }
        finally
        {
            _loading = false;
        }

        UpdateSummary();
        ModMain.frmXeModManagerRight?.Refresh();
    }

    private void UpdateSummary()
    {
        var mode = XeModService.CurrentMode;
        var instance = XeModService.CurrentInstance;
        var folder = XeModService.GetModsFolder(instance);
        List<XeModEntry> mods = folder is null ? [] : XeModService.ListMods(folder);
        var enabled = mods.Count(m => m.Enabled);

        ItemTotal.Title = Lang.Text("Xe.Mods.Right.Title");
        ItemTotal.Info = mods.Count.ToString();
        ItemEnabled.Title = Lang.Text("Xe.Mods.Item.Enabled");
        ItemEnabled.Info = Lang.Text("Xe.Mods.Count.Enabled", mods.Count, enabled);
        ItemTarget.Title = Lang.Text("Xe.Mods.Left.Target");
        ItemTarget.Info = mode switch
        {
            XeModTargetMode.Server => Lang.Text("Xe.Mods.Mode.Server"),
            XeModTargetMode.NeteaseClient => Lang.Text("Xe.Mods.Mode.Netease"),
            _ => Lang.Text("Xe.Mods.Mode.Client")
        };
    }

    private void ComboMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ComboMode.SelectedIndex < 0) return;
        XeModService.CurrentMode = (XeModTargetMode)ComboMode.SelectedIndex;
        UpdateSummary();
        ModMain.frmXeModManagerRight?.Refresh();
        ModMain.frmXeAskAiLeft?.Refresh();
    }

    private void ComboInstance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ComboInstance.SelectedIndex < 0) return;
        var name = (ComboInstance.SelectedItem as MyComboBoxItem)?.Content?.ToString();
        var instance = XeModService.FindInstance(name);
        if (instance is null) return;
        XeModService.CurrentInstance = instance;
        UpdateSummary();
        ModMain.frmXeModManagerRight?.Refresh();
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
        HintService.Hint(Lang.Text("Xe.Mods.Refresh.Done"), HintType.Success);
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e) =>
        XeModService.OpenFolder(XeModService.GetModsFolder(XeModService.CurrentInstance) ?? "");

    private void BtnGoDownload_Click(object sender, MouseButtonEventArgs e) =>
        ModMain.frmMain?.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadMod);
}
