using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageXeModManagerRight
{
    public PageXeModManagerRight()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        PageEnter += () =>
        {
            ModMain.frmXeModManagerLeft?.Refresh();
            Refresh();
        };
    }

    /// <summary>可由「问问 AI」指令调用，重新扫描并渲染模组列表。</summary>
    public void Refresh()
    {
        var mode = XeModService.CurrentMode;
        var instance = XeModService.CurrentInstance;
        var manageable = XeModService.IsManageable(mode, instance, out var reason);
        var folder = XeModService.GetModsFolder(instance);

        HintBlocked.Visibility = manageable ? Visibility.Collapsed : Visibility.Visible;
        HintBlocked.Text = manageable ? "" : reason;
        BtnOpenFolder.IsEnabled = manageable && !string.IsNullOrEmpty(folder);

        PanMods.Children.Clear();
        LabEmpty.Visibility = Visibility.Collapsed;

        if (!manageable)
        {
            LabSummary.Text = Lang.Text("Xe.Mods.Right.WarnTitle") + " · " + reason;
            return;
        }

        var mods = XeModService.ListMods(folder!);
        LabSummary.Text = Lang.Text("Xe.Mods.Count.Enabled", mods.Count, mods.Count(m => m.Enabled));
        if (mods.Count == 0)
        {
            LabEmpty.Visibility = Visibility.Visible;
            return;
        }

        var gray = (Brush)FindResource("ColorBrushGray5");
        var accent = (Brush)FindResource("ColorBrush3");
        foreach (var entry in mods)
            PanMods.Children.Add(BuildRow(entry, gray, accent));
    }

    private Border BuildRow(XeModEntry entry, Brush gray, Brush accent)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("ColorBrushTransparentBackground"),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var state = entry.Enabled ? Lang.Text("Xe.Mods.Item.Enabled") : Lang.Text("Xe.Mods.Item.Disabled");
        var note = entry.ServerSideOnly ? " · " + Lang.Text("Xe.Mods.Item.ServerSideOnly") : "";
        info.Children.Add(new TextBlock
        {
            Text = $"{entry.RawFileName}  ·  {entry.SizeText}  ·  {state}{note}",
            FontSize = 11,
            Foreground = gray,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var canToggle = !entry.ServerSideOnly;
        var enableButton = new MyButton
        {
            Text = Lang.Text("Xe.Mods.Btn.Enable"),
            Height = 28,
            MinWidth = 72,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = canToggle && !entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        enableButton.Click += (_, _) => Toggle(entry, true);
        Grid.SetColumn(enableButton, 1);
        grid.Children.Add(enableButton);

        var disableButton = new MyButton
        {
            Text = Lang.Text("Xe.Mods.Btn.Disable"),
            Height = 28,
            MinWidth = 72,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = canToggle && entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        disableButton.Click += (_, _) => Toggle(entry, false);
        Grid.SetColumn(disableButton, 2);
        grid.Children.Add(disableButton);

        border.Child = grid;
        return border;
    }

    private void Toggle(XeModEntry entry, bool enable)
    {
        var result = XeModService.SetEnabled(entry, enable, XeModService.CurrentMode, XeModService.CurrentInstance);
        HintService.Hint(result.Message, result.Ok ? HintType.Success : HintType.Warning);
        Refresh();
        ModMain.frmXeModManagerLeft?.Refresh();
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
        ModMain.frmXeModManagerLeft?.Refresh();
        HintService.Hint(Lang.Text("Xe.Mods.Refresh.Done"), HintType.Success);
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e) =>
        XeModService.OpenFolder(XeModService.GetModsFolder(XeModService.CurrentInstance) ?? "");

    private void BtnGoDownload_Click(object sender, MouseButtonEventArgs e) =>
        ModMain.frmMain?.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadMod);
}
