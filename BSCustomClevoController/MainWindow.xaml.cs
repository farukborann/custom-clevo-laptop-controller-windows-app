using MahApps.Metro.Controls;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Navigation;
using BSCustomClevoController.Controls;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController
{
    public partial class MainWindow : MetroWindow
    {
        private const string KeyboardSettingsTag = "KeyboardSettings";

        private int _clevoMsg;
        private int _clevoWmiMsg;
        private bool _startMinimized;
        private long _lastTouchpadToggleTicks;
        private readonly Frame _frame;
        private NotifyIcon _notifyIcon;
        private readonly System.Collections.Generic.Dictionary<string, Page> _pageCache =
            new System.Collections.Generic.Dictionary<string, Page>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();
            SubscribeControlEvents();

            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BSCustomClevoController.Assets.main_icon.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon(stream),
                Text = LocalizationManager.T("AppTitle", "Control Center"),
                Visible = true
            };

            Utility.SystemPreferences.InitializeHardwareOnStartup();

            _lastPowerMode = Controls.PowerMode.GetStatus();
            UpdateTrayTooltip(GetPowerModeDisplayName(_lastPowerMode));

            Utility.SystemPreferences.Initialize();

            Utility.StartupManager.EnsureConfiguredCorrectly();

            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();

            var perfProfileMenu = new ToolStripMenuItem(LocalizationManager.T("TrayPerfProfile"));
            var quietItem = new ToolStripMenuItem(LocalizationManager.T("PerfQuiet"), null, (s, e) => ApplyPowerProfile(Controls.PowerMode.Mode.Quiet));
            var powerSaveItem = new ToolStripMenuItem(LocalizationManager.T("PerfPowerSaver"), null, (s, e) => ApplyPowerProfile(Controls.PowerMode.Mode.Powersaving));
            var entertainmentItem = new ToolStripMenuItem(LocalizationManager.T("PerfEntertainment"), null, (s, e) => ApplyPowerProfile(Controls.PowerMode.Mode.Entertainment));
            var performanceItem = new ToolStripMenuItem(LocalizationManager.T("PerfPerformance"), null, (s, e) => ApplyPowerProfile(Controls.PowerMode.Mode.Performance));
            perfProfileMenu.DropDownItems.Add(quietItem);
            perfProfileMenu.DropDownItems.Add(powerSaveItem);
            perfProfileMenu.DropDownItems.Add(entertainmentItem);
            perfProfileMenu.DropDownItems.Add(performanceItem);
            _notifyIcon.ContextMenuStrip.Items.Add(perfProfileMenu);

            var fanProfileMenu = new ToolStripMenuItem(LocalizationManager.T("TrayFanProfile"));
            var autoFanItem = new ToolStripMenuItem(LocalizationManager.T("FanAuto"), null, (s, e) => ApplyFanProfile(Controls.Fan.Mode.Auto));
            var maxFanItem = new ToolStripMenuItem(LocalizationManager.T("FanMax"), null, (s, e) => ApplyFanProfile(Controls.Fan.Mode.Max));
            var customFanItem = new ToolStripMenuItem(LocalizationManager.T("FanCustom"), null, (s, e) => ApplyFanProfile(Controls.Fan.Mode.Custom));
            fanProfileMenu.DropDownItems.Add(autoFanItem);
            fanProfileMenu.DropDownItems.Add(maxFanItem);
            fanProfileMenu.DropDownItems.Add(customFanItem);
            _notifyIcon.ContextMenuStrip.Items.Add(fanProfileMenu);

            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            var kbTrayItem = new ToolStripMenuItem(LocalizationManager.T("NavKeyboard"), null, notifyIconContext_KeyboardMenuClick);
            var batTrayItem = new ToolStripMenuItem(LocalizationManager.T("NavBattery"), null, notifyIconContext_BatteryMenuClick);
            var toolsTrayItem = new ToolStripMenuItem(LocalizationManager.T("NavTools"), null, notifyIconContext_OtherMenuClick);
            var settingsTrayItem = new ToolStripMenuItem(LocalizationManager.T("NavSettings"), null, notifyIconContext_SettingsMenuClick);
            var aboutTrayItem = new ToolStripMenuItem(LocalizationManager.T("NavAbout"), null, notifyIconContext_AboutMenuClick);
            var exitTrayItem = new ToolStripMenuItem(LocalizationManager.T("TrayExit"), null, notifyIconContext_ExitClick);

            _notifyIcon.ContextMenuStrip.Items.Add(kbTrayItem);
            _notifyIcon.ContextMenuStrip.Items.Add(batTrayItem);
            _notifyIcon.ContextMenuStrip.Items.Add(toolsTrayItem);
            _notifyIcon.ContextMenuStrip.Items.Add(settingsTrayItem);
            _notifyIcon.ContextMenuStrip.Items.Add(aboutTrayItem);
            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add(exitTrayItem);

            LocalizationManager.LanguageChanged += () =>
            {
                perfProfileMenu.Text = LocalizationManager.T("TrayPerfProfile");
                quietItem.Text = LocalizationManager.T("PerfQuiet");
                powerSaveItem.Text = LocalizationManager.T("PerfPowerSaver");
                entertainmentItem.Text = LocalizationManager.T("PerfEntertainment");
                performanceItem.Text = LocalizationManager.T("PerfPerformance");
                fanProfileMenu.Text = LocalizationManager.T("TrayFanProfile");
                autoFanItem.Text = LocalizationManager.T("FanAuto");
                maxFanItem.Text = LocalizationManager.T("FanMax");
                customFanItem.Text = LocalizationManager.T("FanCustom");
                kbTrayItem.Text = LocalizationManager.T("NavKeyboard");
                batTrayItem.Text = LocalizationManager.T("NavBattery");
                toolsTrayItem.Text = LocalizationManager.T("NavTools");
                settingsTrayItem.Text = LocalizationManager.T("NavSettings");
                aboutTrayItem.Text = LocalizationManager.T("NavAbout");
                exitTrayItem.Text = LocalizationManager.T("TrayExit");
                UpdateTrayTooltip(GetPowerModeDisplayName(_lastPowerMode));
            };

            _notifyIcon.ContextMenuStrip.Opening += (s, e) =>
            {
                try
                {
                    var currentPower = Controls.PowerMode.GetStatus();
                    quietItem.Checked = (currentPower == Controls.PowerMode.Mode.Quiet);
                    powerSaveItem.Checked = (currentPower == Controls.PowerMode.Mode.Powersaving);
                    entertainmentItem.Checked = (currentPower == Controls.PowerMode.Mode.Entertainment);
                    performanceItem.Checked = (currentPower == Controls.PowerMode.Mode.Performance);

                    var currentFan = Controls.Fan.GetStatus();
                    autoFanItem.Checked = (currentFan.Mode == Controls.Fan.Mode.Auto);
                    maxFanItem.Checked = (currentFan.Mode == Controls.Fan.Mode.Max);
                    customFanItem.Checked = (currentFan.Mode == Controls.Fan.Mode.Custom);
                }
                catch (Exception ex)
                {
                    Utility.AppLogger.Log("MainWindow.ContextMenuOpening", ex);
                }
            };

            _notifyIcon.MouseClick += delegate (object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left)
                {
                    BringToForeground();
                }
            };

            _notifyIcon.DoubleClick += delegate (object sender, EventArgs args)
            {
                BringToForeground();
            };

            _frame = new Frame { NavigationUIVisibility = NavigationUIVisibility.Hidden };
            _frame.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            _frame.VerticalAlignment = VerticalAlignment.Stretch;
            _frame.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;

            HamburgerMenuControl.Content = _frame;
            _frame.Navigate(GetOrCreatePage("PerformanceSettings"));

            string[] cmdArgs = Environment.GetCommandLineArgs();
            for (int i = 1; i < cmdArgs.Length; i++)
            {
                if (string.Equals(cmdArgs[i], "/minimized", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cmdArgs[i], "-minimized", StringComparison.OrdinalIgnoreCase))
                {
                    _startMinimized = true;
                    WindowState = WindowState.Minimized;
                    Hide();
                    break;
                }
            }
        }

        public Page GetOrCreatePage(string pageTag)
        {
            if (!_pageCache.TryGetValue(pageTag, out Page page))
            {
                switch (pageTag)
                {
                    case "PerformanceSettings":
                        page = new Views.Page_PerformanceSettings();
                        break;
                    case "FanSettings":
                        page = new Views.Page_FanSettings();
                        break;
                    case KeyboardSettingsTag:
                        page = new Views.Page_KeyboardSettings();
                        break;
                    case "BatterySettings":
                        page = new Views.PageBatterySettings();
                        break;
                    case "OtherSettings":
                        page = new Views.Page_OtherSettings();
                        break;
                    case "Settings":
                        page = new Views.PageSettings();
                        break;
                    case "About":
                        page = new Views.PageAbout();
                        break;
                    default:
                        page = new Views.Page_PerformanceSettings();
                        break;
                }
                _pageCache[pageTag] = page;
            }
            return page;
        }

        public void ReloadCachedPages()
        {
            foreach (var key in _pageCache.Keys
                .Where(k => !string.Equals(k, "OtherSettings", StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                _pageCache.Remove(key);
            }
        }

        private Controls.PowerMode.Mode _lastPowerMode;

        private static string GetPowerModeDisplayName(Controls.PowerMode.Mode mode)
        {
            switch (mode)
            {
                case Controls.PowerMode.Mode.Quiet:
                    return LocalizationManager.T("PerfQuiet", "Quiet");
                case Controls.PowerMode.Mode.Powersaving:
                    return LocalizationManager.T("PerfPowerSaver", "Power Saver");
                case Controls.PowerMode.Mode.Performance:
                    return LocalizationManager.T("PerfPerformance", "Performance");
                case Controls.PowerMode.Mode.Entertainment:
                default:
                    return LocalizationManager.T("PerfEntertainment", "Entertainment");
            }
        }

        public void UpdateTrayTooltip(string extraInfo = null)
        {
            if (_notifyIcon == null) return;
            try
            {
                string appTitle = LocalizationManager.T("AppTitle", "Control Center");
                string text = string.IsNullOrWhiteSpace(extraInfo)
                    ? appTitle
                    : string.Format(LocalizationManager.CurrentCulture, "{0} ({1})", appTitle, extraInfo);

                if (text.Length > 63)
                {
                    text = text.Substring(0, 60) + "...";
                }
                _notifyIcon.Text = text;
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.UpdateTrayTooltip", ex);
            }
        }

        private void NavigateToPage(int index, string pageTag)
        {
            _startMinimized = false;
            Show();
            WindowState = WindowState.Normal;
            HamburgerMenuControl.SelectedIndex = index;
            _frame.Navigate(GetOrCreatePage(pageTag));
            while (_frame.NavigationService.CanGoBack)
            {
                _frame.NavigationService.RemoveBackEntry();
            }

            var collection = HamburgerMenuControl.ItemsSource as System.Collections.IList;
            if (collection != null && index >= 0 && index < collection.Count)
            {
                UpdateSidebarIcons(collection[index] as HamburgerMenuIconItem);
            }
        }

        private void UpdateSidebarIcons(HamburgerMenuIconItem activeItem)
        {
            if (activeItem == null) return;
            var collection = this.HamburgerMenuControl.ItemsSource as System.Collections.IEnumerable;
            if (collection == null) return;

            foreach (HamburgerMenuIconItem item in collection)
            {
                if (item == activeItem)
                {
                    item.Icon = "/Assets/Main_Window/" + item.Tag + "_Selected.png";
                }
                else
                {
                    item.Icon = "/Assets/Main_Window/" + item.Tag + ".png";
                }
            }
        }

        private void notifyIconContext_KeyboardMenuClick(object sender, EventArgs e)
        {
            NavigateToPage(2, KeyboardSettingsTag);
        }

        private void notifyIconContext_BatteryMenuClick(object sender, EventArgs e)
        {
            NavigateToPage(3, "BatterySettings");
        }

        private void notifyIconContext_OtherMenuClick(object sender, EventArgs e)
        {
            NavigateToPage(4, "OtherSettings");
        }

        private void notifyIconContext_SettingsMenuClick(object sender, EventArgs e)
        {
            NavigateToPage(5, "Settings");
        }

        private void notifyIconContext_AboutMenuClick(object sender, EventArgs e)
        {
            NavigateToPage(6, "About");
        }

        private void SubscribeControlEvents()
        {
            Controls.PowerMode.PowerProfileChanged += OnPowerProfileChanged;
            Controls.Fan.FanProfileChanged += OnFanProfileChanged;
            Controls.KeyboardBackLight.BacklightChanged += OnBacklightChanged;
            Utility.SystemPreferences.PowerLineChanged += OnPowerLineChanged;
            Utility.SystemPreferences.FanProfileChanged += OnFanProfileChanged;
            Utility.SystemPreferences.ShowMainWindowRequested += OnShowMainWindowRequested;
        }

        private void UnsubscribeControlEvents()
        {
            Controls.PowerMode.PowerProfileChanged -= OnPowerProfileChanged;
            Controls.Fan.FanProfileChanged -= OnFanProfileChanged;
            Controls.KeyboardBackLight.BacklightChanged -= OnBacklightChanged;
            Utility.SystemPreferences.PowerLineChanged -= OnPowerLineChanged;
            Utility.SystemPreferences.FanProfileChanged -= OnFanProfileChanged;
            Utility.SystemPreferences.ShowMainWindowRequested -= OnShowMainWindowRequested;
        }

        private void OnPowerProfileChanged(Controls.PowerMode.Mode mode) => SyncPowerProfileUI(mode);
        private void OnFanProfileChanged(Controls.Fan.Mode mode) => SyncFanProfileUI(mode);
        private void OnBacklightChanged() => Dispatcher.BeginInvoke(new Action(SyncKeyboardSettingsUI));
        private void OnPowerLineChanged(bool isAcOnline) => Dispatcher.BeginInvoke(new Action(() => SyncPowerLineUI(isAcOnline)));
        private void OnShowMainWindowRequested() => Dispatcher.BeginInvoke(new Action(BringToForeground));

        private void SyncPowerLineUI(bool isAcOnline)
        {
            try
            {
                if (_pageCache.TryGetValue(KeyboardSettingsTag, out Page page) && page is Views.Page_KeyboardSettings kbPage)
                {
                    kbPage.UpdateBatterySoftwareAnimationState(isAcOnline);
                }
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.SyncPowerLineUI", ex);
            }
        }

        private void SyncKeyboardSettingsUI()
        {
            try
            {
                if (_pageCache.TryGetValue(KeyboardSettingsTag, out Page page) && page is Views.Page_KeyboardSettings kbPage)
                {
                    kbPage.SyncFromHardwareHotkey();
                }
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.SyncKeyboardSettingsUI", ex);
            }
        }

        public void ExitApplication()
        {
            App.CleanupOnExit();
            DisposeNotifyIcon();
            System.Windows.Application.Current.Shutdown();
        }

        public void BringToForeground()
        {
            _startMinimized = false;
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                Utility.NativeMethods.ShowWindow(helper.Handle, Utility.NativeMethods.SW_RESTORE);
                Utility.NativeMethods.SetForegroundWindow(helper.Handle);
            }
        }

        private void notifyIconContext_ExitClick(object sender, EventArgs e)
        {
            ExitApplication();
        }

        protected override void OnClosed(EventArgs e)
        {
            UnsubscribeControlEvents();
            App.CleanupOnExit();
            DisposeNotifyIcon();
            base.OnClosed(e);
        }

        private void DisposeNotifyIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                Hide();

            base.OnStateChanged(e);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_startMinimized)
            {
                _startMinimized = false;
                WindowState = WindowState.Minimized;
                Hide();
            }
        }

        private void HamburgerMenuControl_ItemClick(object sender, ItemClickEventArgs args)
        {
            var clicked = (HamburgerMenuIconItem)args.ClickedItem;
            if (clicked?.Tag != null)
            {
                _frame.Navigate(GetOrCreatePage(clicked.Tag.ToString()));
                while (_frame.NavigationService.CanGoBack)
                {
                    _frame.NavigationService.RemoveBackEntry();
                }
                UpdateSidebarIcons(clicked);
            }
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            Views.UiFocusHelper.ClearTextBoxFocusOnClick(this, e.OriginalSource);
        }

        private void ApplyPowerProfile(Controls.PowerMode.Mode mode)
        {
            try
            {
                Controls.PowerMode.SetPowerMode(mode);
                Utility.OsdOverlay.ShowPowerMode(mode);
                SyncPowerProfileUI(mode);
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.ApplyPowerProfile", ex);
            }
        }

        internal void SyncPowerProfileUI(Controls.PowerMode.Mode mode)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _lastPowerMode = mode;
                    UpdateTrayTooltip(GetPowerModeDisplayName(mode));
                    if (_pageCache.TryGetValue("PerformanceSettings", out Page p) && p is Views.Page_PerformanceSettings ps)
                    {
                        ps.ApplyModeUI(mode);
                    }
                }));
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.SyncPowerProfileUI", ex);
            }
        }

        private void ApplyFanProfile(Controls.Fan.Mode mode)
        {
            try
            {
                Controls.Fan.SetMode(mode);
                Utility.OsdOverlay.ShowFan(mode);
                SyncFanProfileUI(mode);
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.ApplyFanProfile", ex);
            }
        }

        internal void SyncFanProfileUI(Controls.Fan.Mode mode)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_pageCache.TryGetValue("FanSettings", out Page p) && p is Views.Page_FanSettings fs)
                    {
                        fs.SetActiveModeUI(mode);
                    }
                }));
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.SyncFanProfileUI", ex);
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                Rect work = SystemParameters.WorkArea;
                if (work.Width > 0 && work.Height > 0)
                {
                    double clampedWidth = Math.Min(Width, work.Width * 0.88);
                    double clampedHeight = Math.Min(Height, work.Height * 0.88);
                    if (clampedWidth < Width || clampedHeight < Height)
                    {
                        Width = clampedWidth;
                        Height = clampedHeight;
                        Left = work.Left + (work.Width - Width) / 2.0;
                        Top = work.Top + (work.Height - Height) / 2.0;
                    }
                }

                _clevoMsg = Utility.NativeMethods.RegisterWindowMessage("ClevoMsg_ControlCenter");
                _clevoWmiMsg = Utility.NativeMethods.RegisterWindowMessage("ClevoMsg_ControlCenter_WMI");

                if (_clevoMsg > 0)
                {
                    Utility.NativeMethods.ChangeWindowMessageFilter(unchecked((uint)_clevoMsg), Utility.NativeMethods.MSGFLT_ADD);
                }
                if (_clevoWmiMsg > 0)
                {
                    Utility.NativeMethods.ChangeWindowMessageFilter(unchecked((uint)_clevoWmiMsg), Utility.NativeMethods.MSGFLT_ADD);
                }

                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null)
                {
                    source.AddHook(WndProc);
                }
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.OnSourceInitialized", ex);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0011 || msg == 0x0016) // WM_QUERYENDSESSION or WM_ENDSESSION
            {
                App.CleanupOnExit();
                return (IntPtr)1;
            }

            if (msg == 0x0218) // WM_POWERBROADCAST
            {
                bool ac = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                Controls.KeyboardEffects.HandlePowerStatusChanged(ac);
                SyncPowerLineUI(ac);
            }

            if (msg == _clevoMsg || msg == _clevoWmiMsg)
            {
                HandleClevoWindowMessage(wParam.ToInt32(), ref handled);
            }
            return IntPtr.Zero;
        }

        private void HandleClevoWindowMessage(int cmd, ref bool handled)
        {
            switch (cmd)
            {
                case 266: // Fan (Cooler Boost / Max Fan)
                    Controls.Fan.ToggleAutoMaxFan();
                    handled = true;
                    break;
                case 271: // FN_3
                case 258: // SituationMode
                    Controls.PowerMode.CyclePowerMode();
                    handled = true;
                    break;
                case 259: // Touchpad
                    HandleTouchpadHotkey();
                    handled = true;
                    break;
                case 260: // Camera / CCD
                    Utility.OsdOverlay.ShowCamera(Utility.SystemPreferences.GetCameraState());
                    handled = true;
                    break;
                case 261: // Airplane
                    Utility.OsdOverlay.ShowAirplane(Utility.SystemPreferences.GetAirplaneModeState());
                    handled = true;
                    break;
                case 263: // Caps Lock
                    Utility.OsdOverlay.ShowCaps(Utility.SystemPreferences.GetCapsLockState());
                    handled = true;
                    break;
                case 264: // Scroll Lock
                    Utility.OsdOverlay.ShowScroll(Utility.SystemPreferences.GetScrollLockState());
                    handled = true;
                    break;
                case 265: // Num Lock
                    Utility.OsdOverlay.ShowNum(Utility.SystemPreferences.GetNumLockState());
                    handled = true;
                    break;
                case 262: // WinKey / FlexiKey hotkey
                    if (!Utility.SystemPreferences.IsRecentWinKeyChange(500))
                    {
                        Utility.SystemPreferences.ToggleWinKeyLock();
                    }
                    handled = true;
                    break;
                case 267: // Generic hardware change notification (backlight, fan, power)
                    Controls.Fan.CheckAndSyncFanFromHardware();
                    handled = true;
                    break;
                case 515: // KbBrightness
                case 516: // WhiteKbBrightness
                case 518: // PerKbBrightness
                    ShowBacklightLevelOsd();
                    handled = true;
                    break;
            }
        }

        private void HandleTouchpadHotkey()
        {
            if (!Controls.Touchpad.IsHardwareEventSuppressed &&
                Utility.MonotonicClock.ElapsedMs(_lastTouchpadToggleTicks) >= 800)
            {
                _lastTouchpadToggleTicks = Utility.MonotonicClock.NowTicks();
                Controls.Touchpad.ToggleTouchpad();
            }
        }

        private static void ShowBacklightLevelOsd()
        {
            try
            {
                var kbStatus = Controls.KeyboardBackLight.GetStatus();
                int lvl = (kbStatus.LightState && kbStatus.BackLight.A > 0) ? (int)Math.Round(kbStatus.BackLight.A / 255.0 * 4.0) : 0;
                Utility.OsdOverlay.ShowBacklight(lvl);
            }
            catch (Exception ex)
            {
                Utility.AppLogger.Log("MainWindow.WndProc.BacklightOsd", ex);
            }
        }
    }
}
