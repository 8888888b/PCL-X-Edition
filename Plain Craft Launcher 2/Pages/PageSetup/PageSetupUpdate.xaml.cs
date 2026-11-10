using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.Utils;
using PCL.Core.App.Localization;
using PCL.Core.Utils.OS;

namespace PCL;

public partial class PageSetupUpdate
{
    public VersionDataModel updateInfo;

    private bool _xeBusy;

    public PageSetupUpdate()
    {
        InitializeComponent();
        BtnXeCheck.Click += (_, _) => _ = CheckXeAsync();
        BtnXeInstall.Click += (_, _) => _ = InstallXeAsync();
        BtnXeSaveSource.Click += (_, _) => SaveXeSource();
        CheckXeAutoCheck.Change += (_, _) => Config.Xe.Update.AutoCheck = CheckXeAutoCheck.Checked == true;
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        ModAnimation.AniBeginControl();
        TextMirrorCDK.Password = Config.Update.MirrorChyanKey;

        ComboSystemUpdateChannel.SelectedIndex = (int)Config.Update.UpdateChannel;
        ComboSystemUpdateMode.SelectedIndex = (int)Config.Update.UpdateMode;

        TextCurrentVersion.Text = "PCL XE " + VersionNameFormat(ModBase.versionBaseName);
        ModAnimation.AniEndControl();

        // 上游 PCL Symbio Edition 的更新源与 XE 的版本号体系互不相干，安装上游包会覆盖 XE，
        // 因此整块上游更新 UI 都隐藏，只保留 PCL XE 自己的更新卡片。
        CardUpstreamOption.Visibility = Visibility.Collapsed;
        CardUpdate.Visibility = Visibility.Collapsed;
        CardCheck.Visibility = Visibility.Collapsed;
        CardOtherOption.Visibility = Visibility.Collapsed;

        TextXeVersion.Text = "PCL XE " + VersionNameFormat(ModBase.versionBaseName);
        TextXeSource.Text = Config.Xe.Update.ManifestUrl;
        CheckXeAutoCheck.SetChecked(Config.Xe.Update.AutoCheck, false);
        SetXeStatus(XeUpdateService.IsConfigured
            ? Lang.Text("Setup.Update.Xe.Idle")
            : Lang.Text("Setup.Update.Xe.NoSource"), false);

        // 启动时已自动检查过就直接展示结果，避免每次进页面都发请求
        if (XeUpdateService.Latest is { } latest)
            ShowXeAvailable(latest);
        else if (XeUpdateService.IsConfigured)
            _ = CheckXeAsync(true);
    }

    #region PCL XE 自动更新

    private void SetXeStatus(string text, bool progress)
    {
        TextXeStatus.Text = text;
        PanXeProgress.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        if (!progress) BarXeProgress.Width = 0;
    }

    private void SaveXeSource()
    {
        Config.Xe.Update.ManifestUrl = TextXeSource.Text.Trim();
        XeUpdateService.Reset();
        BtnXeInstall.Visibility = Visibility.Collapsed;
        TextXeNotes.Visibility = Visibility.Collapsed;
        SetXeStatus(XeUpdateService.IsConfigured
            ? Lang.Text("Setup.Update.Xe.Idle")
            : Lang.Text("Setup.Update.Xe.NoSource"), false);
        HintService.Hint(Lang.Text("Setup.Update.Xe.SourceSaved"), HintType.Success);
    }

    private async Task CheckXeAsync(bool silent = false)
    {
        if (_xeBusy) return;
        if (!XeUpdateService.IsConfigured)
        {
            SetXeStatus(Lang.Text("Setup.Update.Xe.NoSource"), false);
            return;
        }

        _xeBusy = true;
        BtnXeCheck.IsEnabled = false;
        BtnXeInstall.Visibility = Visibility.Collapsed;
        TextXeNotes.Visibility = Visibility.Collapsed;
        SetXeStatus(Lang.Text("Setup.Update.Xe.Checking"), true);
        try
        {
            var info = await XeUpdateService.CheckAsync();
            if (info is null)
            {
                SetXeStatus(Lang.Text("Setup.Update.Xe.NoSource"), false);
                return;
            }

            if (XeUpdateService.IsNewer(info))
                ShowXeAvailable(info);
            else
                SetXeStatus(Lang.Text("Setup.Update.Xe.Latest", ModBase.versionBaseName), false);
        }
        catch (Exception ex)
        {
            var message = Lang.Text("Setup.Update.Xe.Error", ex.Message);
            SetXeStatus(message, false);
            if (!silent) HintService.Hint(message, HintType.Warning);
            ModBase.Log(ex, "[XE] 检查更新失败", ModBase.LogLevel.Debug);
        }
        finally
        {
            _xeBusy = false;
            BtnXeCheck.IsEnabled = true;
        }
    }

    private void ShowXeAvailable(XeUpdateService.UpdateInfo info)
    {
        SetXeStatus(Lang.Text("Setup.Update.Xe.Available", info.Version, ModBase.versionBaseName), false);
        BtnXeInstall.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(info.Notes))
        {
            TextXeNotes.Visibility = Visibility.Collapsed;
            return;
        }

        var notes = info.Notes.Trim();
        TextXeNotes.Text = notes.Length > 800 ? notes[..800] + "…" : notes;
        TextXeNotes.Visibility = Visibility.Visible;
    }

    private async Task InstallXeAsync()
    {
        var info = XeUpdateService.Latest;
        if (info is null || _xeBusy) return;

        _xeBusy = true;
        BtnXeCheck.IsEnabled = false;
        BtnXeInstall.IsEnabled = false;
        var progress = new Progress<double>(value =>
        {
            BarXeProgress.Width = Math.Max(0d, PanXeProgress.ActualWidth * value);
            TextXeStatus.Text = Lang.Text("Setup.Update.Xe.Downloading", Lang.Number(value * 100d, "P0"));
        });
        SetXeStatus(Lang.Text("Setup.Update.Xe.Downloading", "0%"), true);
        try
        {
            var file = await XeUpdateService.DownloadAsync(info, progress);
            SetXeStatus(Lang.Text("Setup.Update.Xe.Verifying"), true);
            SetXeStatus(Lang.Text("Setup.Update.Xe.Ready"), false);
            XeUpdateService.InstallAndRestart(file);
        }
        catch (Exception ex)
        {
            var message = Lang.Text("Setup.Update.Xe.DownloadFailed", ex.Message);
            SetXeStatus(message, false);
            HintService.Hint(message, HintType.Warning);
            ModBase.Log(ex, "[XE] 下载更新失败", ModBase.LogLevel.Debug);
            BtnXeInstall.IsEnabled = true;
            BtnXeCheck.IsEnabled = true;
            _xeBusy = false;
        }
    }

    #endregion

    private async Task<UpdateStatus> IsLatestAsync()
    {
        try
        {
            // 强制刷新版本源缓存，否则「再次检查」只会复用手动检查前缓存的旧结果
            await Task.Run(() => UpdateManager.remoteServer.RefreshCache());

            if (await UpdateManager.remoteServer.IsLatestAsync(
                    UpdateManager.IsCurrentVersionBeta ? UpdateChannel.beta : UpdateChannel.stable,
                    SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64,
                    SemVer.Parse(ModBase.versionBaseName),
                    ModBase.versionCode))
            {
                ModBase.Log("[Update] 已是最新版本");
                return UpdateStatus.Latest;
            }

            ModBase.Log("[Update] 有可用的新版本");
            return UpdateStatus.Available;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Update.Error.NetworkFailed"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Setup.Update.Error.NetworkFailed"));
            return UpdateStatus.Error;
        }
    }

    /// <summary>供启动页等入口调用：改为检查 PCL XE 自己的更新源。</summary>
    public void CheckUpdate()
    {
        _ = CheckXeAsync();
    }

    private async void BtnUpdate_Timer()
    {
        // 不能用 Thread.Sleep 在 UI 线程里等，否则整个窗口会卡死
        while (UpdateManager.updateLoader is not null && UpdateManager.updateLoader.State == ModBase.LoadState.Loading)
        {
            BtnUpdate.Text = Lang.Number(UpdateManager.updateLoader.Progress, "P2");
            await Task.Delay(200);
        }

        if (UpdateManager.isUpdateWaitingRestart)
        {
            BtnUpdate.Text = Lang.Text("Setup.Update.RestartInstall");
        }
        else
        {
            BtnUpdate.Text = Lang.Text("Setup.Update.Install");
        }

        BtnUpdate.IsEnabled = true;
    }

    private void BtnUpdate_Click(object sender, MouseButtonEventArgs e)
    {
        if (UpdateManager.isUpdateWaitingRestart)
        {
            UpdateManager.UpdateRestart(true);
            return;
        }

        // 开始更新流程
        UpdateManager.UpdateStart(UpdateEnums.UpdateType.UpdateNow);
    }

    private void BtnChangelogDetail_Click(object sender, EventArgs e)
    {
        if (updateInfo is null)
            ModMain.MyMsgBox(Lang.Text("Setup.Update.Changelog.Unavailable"), Lang.Text("Setup.Update.Changelog.Title"));
        else
            ModMain.MyMsgBoxMarkdown(updateInfo.Changelog, Lang.Text("Setup.Update.Changelog.Title"));
    }

    private void ComboSystemUpdateMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ModAnimation.AniExitsAnyControl())
            Config.Update.UpdateMode = (LauncherAutoUpdateBehavior)ComboSystemUpdateMode.SelectedIndex;
    }

    private void ComboSystemUpdateBranch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;

        var isCancelled = false;
        switch (ComboSystemUpdateChannel.SelectedIndex)
        {
            case 0:
            {
                break;
            }
            case 1:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Update.Channel.Beta.Warning.Message"),
                        Lang.Text("Setup.Update.Channel.Common.Warning.Title"),
                        Lang.Text("Setup.Update.Channel.Common.Warning.Confirm"),
                        Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
                    isCancelled = true;
                else
                    CheckUpdate();
                break;
            }
            case 2:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Update.Channel.Dev.Warning.Message"),
                        Lang.Text("Setup.Update.Channel.Common.Warning.Title"),
                        Lang.Text("Setup.Update.Channel.Common.Warning.Confirm"),
                        Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
                {
                    isCancelled = true;
                    break;
                }

                var confirmText = Lang.Text("Setup.Update.Channel.Dev.FinalConfirm.ExpectedInput");
                var ret = ModMain.MyMsgBoxInput(
                    Lang.Text("Setup.Update.Channel.Dev.FinalConfirm.Title"),
                    Lang.Text("Setup.Update.Channel.Dev.FinalConfirm.Message", confirmText),
                    button1: Lang.Text("Setup.Update.Channel.Dev.FinalConfirm.Submit"),
                    button2: Lang.Text("Common.Action.Cancel"), isWarn: true);
    
                if (ret == confirmText)
                {
                    CheckUpdate();
                }
                else
                {
                    HintService.Hint(Lang.Text("Setup.Update.Channel.Dev.FinalConfirm.WrongInput"));
                    isCancelled = true;
                }
                break;
            }
        }

        if (isCancelled)
        {
            ModAnimation.AniBeginControl();
            ComboSystemUpdateChannel.SelectedItem = e.RemovedItems[0];
            ModAnimation.AniEndControl();
        }
        else
        {
            Config.Update.UpdateChannel = (Core.App.UpdateChannel)ComboSystemUpdateChannel.SelectedIndex;
        }
    }

    private void TextMirrorCDK_PasswordChanged(object sender, EventArgs e)
    {
        Config.Update.MirrorChyanKey = TextMirrorCDK.Password;
    }

    private void BtnGetMirrorCDK_Click(object sender, MouseButtonEventArgs e)
    {
        ModBase.OpenWebsite("https://mirrorchyan.com/");
    }

    private void BtnChangelog_Click(object sender, MouseButtonEventArgs e)
    {
        ModBase.OpenWebsite("https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases");
    }

    public string VersionNameFormat(string str)
    {
        str = str.Replace("v", "");
        if (!str.Contains("-"))
            return str;
        var add = str.AfterLast("-");
        str = str.BeforeLast("-");
        return $"{str} {add.Replace(".", " ").Replace("beta", "Beta").Replace("rc", "RC")}";
    }

    private void BtnCheckAgain_OnClick(object sender, MouseButtonEventArgs e)
    {
        CheckUpdate();
    }

    private enum UpdateStatus
    {
        Checking = 0,
        Available = 1,
        Error = 2,
        Latest = 3
    }
}
