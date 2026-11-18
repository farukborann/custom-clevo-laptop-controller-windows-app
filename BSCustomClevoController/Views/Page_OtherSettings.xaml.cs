using BSCustomClevoController.Utility;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Views
{
    public partial class Page_OtherSettings : Page
    {
        private readonly DispatcherTimer _prefSyncTimer;
        private int _nativeMaxRefreshRate = 60;
        private long _syncResumeAtTicks;

        public Page_OtherSettings()
        {
            InitializeComponent();
            InitializePerformanceTuning();
            InitializePlatformTools();
            UpdateDisplayRefreshRateUI();
            SyncHardwarePreferences(true);

            _prefSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _prefSyncTimer.Tick += (s, e) =>
            {
                if (!MonotonicClock.IsBefore(_syncResumeAtTicks))
                {
                    SyncHardwarePreferences();
                }
            };
            _prefSyncTimer.Start();
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            InitializePlatformTools();
            UpdateDisplayRefreshRateUI();
            UpdateCpuPerformanceText(CpuPowerControl.IsSupported, (int)(CpuPowerSlider?.Value ?? 0));
            SyncHardwarePreferences(false);
        }

        private void InitializePlatformTools()
        {
            bool muxSupported = Capabilities.IsSupported("SupportMSHybrid_dGPUSwicth");
            GpuMuxSwitch.IsEnabled = muxSupported;
            GpuMuxCard.Opacity = muxSupported ? 1.0 : 0.55;
            if (muxSupported)
            {
                GpuMuxBadgeText.Text = LocalizationManager.T("ToolsGpuMuxReady");
                GpuMuxBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                GpuMuxBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                GpuMuxBadge.Background = new SolidColorBrush(Color.FromRgb(27, 62, 50));
            }
            else
            {
                GpuMuxBadgeText.Text = LocalizationManager.T("CommonNotSupported");
                GpuMuxBadgeText.Foreground = (Brush)FindResource("Amoled.TextMuted");
                GpuMuxBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                GpuMuxBadge.Background = new SolidColorBrush(Color.FromRgb(28, 28, 36));
            }

            ConfigureCapabilityCard(PrefWolSwitch, PrefWolCard, PrefWolBadge, Capabilities.IsSupported("WakeUpLanSupport"));
            ConfigureCapabilityCard(PrefEthLedSwitch, PrefEthLedCard, PrefEthLedBadge, Capabilities.IsSupported("SupportEthernetLED"));
            ConfigureCapabilityCard(PrefUsbResetBtn, PrefUsbResetCard, PrefUsbResetBadge, Capabilities.IsSupported("SupportUsbHubReset"));
        }

        private static void ConfigureCapabilityCard(Control control, FrameworkElement card, UIElement badge, bool supported)
        {
            if (control != null) control.IsEnabled = supported;
            if (card != null) card.Opacity = supported ? 1.0 : 0.55;
            if (badge != null) badge.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateDisplayRefreshRateUI()
        {
            try
            {
                int currentHz = DisplayControl.GetCurrentRefreshRate();
                List<int> availableRates = DisplayControl.GetAvailableRefreshRates();

                _nativeMaxRefreshRate = availableRates.Count > 0 ? availableRates[availableRates.Count - 1] : currentHz;
                RefreshRateCurrentText.Text = LocalizationManager.Format("ToolsRefreshCurrentFmt", currentHz);

                RefreshNativeBtn.Content = LocalizationManager.Format("ToolsRefreshNativeFmt", _nativeMaxRefreshRate);

                if (availableRates.Count <= 1 || _nativeMaxRefreshRate == 60)
                {
                    Refresh60Btn.IsEnabled = false;
                    RefreshNativeBtn.IsEnabled = false;
                    RefreshRateBadge.ToolTip = LocalizationManager.T("ToolsRefreshFixedTooltip");
                }
                else
                {
                    Refresh60Btn.IsEnabled = currentHz != 60;
                    RefreshNativeBtn.IsEnabled = currentHz != _nativeMaxRefreshRate;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_OtherSettings.UpdateDisplayRefreshRateUI", ex);
            }
        }

        private void InitializePerformanceTuning()
        {
            try
            {
                if (CpuNameText != null)
                {
                    CpuNameText.Text = CpuPowerControl.CpuModel;
                }

                bool cpuOcSupported = CpuPowerControl.IsSupported;
                if (CpuPowerSlider != null)
                {
                    CpuPowerSlider.IsEnabled = cpuOcSupported;
                }

                if (cpuOcSupported)
                {
                    if (CpuPerformanceCard != null)
                    {
                        CpuPerformanceCard.Opacity = 1.0;
                    }
                    if (CpuOcBadge != null)
                    {
                        CpuOcBadge.Background = new SolidColorBrush(Color.FromRgb(27, 62, 50));
                        CpuOcBadge.BorderBrush = (Brush)FindResource("Amoled.Success");
                    }
                    if (CpuOcBadgeText != null)
                    {
                        CpuOcBadgeText.Foreground = (Brush)FindResource("Amoled.Success");
                    }

                    int initialCpuPower = CpuPowerControl.GetPowerOffset();
                    if (CpuPowerSlider != null)
                    {
                        CpuPowerSlider.Value = initialCpuPower;
                    }
                    UpdateCpuPerformanceText(true, initialCpuPower);
                }
                else
                {
                    if (CpuPerformanceCard != null)
                    {
                        CpuPerformanceCard.Opacity = 0.65;
                    }
                    if (CpuOcBadge != null)
                    {
                        CpuOcBadge.Background = new SolidColorBrush(Color.FromArgb(40, 100, 100, 110));
                        CpuOcBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 140, 140, 150));
                    }
                    if (CpuOcBadgeText != null)
                    {
                        CpuOcBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 170));
                    }
                    UpdateCpuPerformanceText(false, 0);
                    if (CpuPowerSlider != null)
                    {
                        CpuPowerSlider.Value = 0;
                    }
                }

                if (NvidiaPowerControl.IsNvidiaPresent)
                {
                    NvidiaTgpCard.Visibility = Visibility.Visible;
                    NvidiaGpuNameText.Text = NvidiaPowerControl.GpuModel;
                    int initialTgp = NvidiaPowerControl.GetTgpOffset();
                    TgpOffsetSlider.Value = initialTgp;
                    UpdateTgpLabel(initialTgp);
                }
                else
                {
                    NvidiaTgpCard.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_OtherSettings.InitializePerformanceTuning", ex);
            }
        }

        private void SyncHardwarePreferences(bool queryEc = false)
        {
            try
            {
                if (queryEc)
                {
                    SystemPreferences.QueryKeyboardSettingsFromEC();
                }

                if (PrefWinKeySwitch != null)
                {
                    PrefWinKeySwitch.IsChecked = SystemPreferences.IsWinKeyLocked;
                }

                if (PrefSwapWinFnSwitch != null)
                {
                    if (!SystemPreferences.IsSwapWinFnSupported)
                    {
                        PrefSwapWinFnSwitch.IsEnabled = false;
                        PrefSwapWinFnSwitch.Opacity = 0.35;
                        PrefSwapWinFnSwitch.IsChecked = false;
                        PrefSwapWinFnSubtitle?.SetResourceReference(TextBlock.TextProperty, "Loc.CommonNotSupported");
                    }
                    else
                    {
                        PrefSwapWinFnSwitch.IsEnabled = true;
                        PrefSwapWinFnSwitch.Opacity = 1.0;
                        PrefSwapWinFnSwitch.IsChecked = SystemPreferences.IsSwapWinFn;
                        PrefSwapWinFnSubtitle?.SetResourceReference(TextBlock.TextProperty, "Loc.ToolsSwapWinFnDesc");
                    }
                }

                if (PrefFnLockSwitch != null)
                {
                    PrefFnLockSwitch.IsChecked = SystemPreferences.IsFnLock;
                }

                if (PrefTouchpadSwitch != null)
                {
                    PrefTouchpadSwitch.IsChecked = Touchpad.GetTouchpadState();
                }

                if (PrefCameraSwitch != null)
                {
                    PrefCameraSwitch.IsChecked = SystemPreferences.GetCameraState();
                }

                if (PrefMicSwitch != null)
                {
                    PrefMicSwitch.IsChecked = SystemPreferences.GetMicrophoneState();
                }

                if (PrefAirplaneSwitch != null)
                {
                    PrefAirplaneSwitch.IsChecked = SystemPreferences.GetAirplaneModeState();
                }

                if (PrefCapsSwitch != null)
                {
                    PrefCapsSwitch.IsChecked = SystemPreferences.GetCapsLockState();
                }

                if (PrefNumSwitch != null)
                {
                    PrefNumSwitch.IsChecked = SystemPreferences.GetNumLockState();
                }

                if (PrefScrSwitch != null)
                {
                    PrefScrSwitch.IsChecked = SystemPreferences.GetScrollLockState();
                }

                if (PerformanceTuningSection != null && NvidiaTgpCard != null && NvidiaTgpCard.Visibility == Visibility.Visible && NvidiaPowerControl.IsNvidiaPresent)
                {
                    bool isPowerSaver = (PowerMode.GetStatus() == PowerMode.Mode.Powersaving);
                    if (isPowerSaver)
                    {
                        TgpOffsetSlider.IsEnabled = false;
                        TgpOffsetText.Text = LocalizationManager.T("ToolsGpuPowerEco");
                    }
                    else
                    {
                        TgpOffsetSlider.IsEnabled = true;
                        int currentOffset = (int)TgpOffsetSlider.Value;
                        UpdateTgpLabel(currentOffset);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_OtherSettings.SyncHardwarePreferences", ex);
            }
        }

        private void Refresh60Btn_Click(object sender, RoutedEventArgs e)
        {
            DisplayControl.SetRefreshRate(60);
            UpdateDisplayRefreshRateUI();
        }

        private void RefreshNativeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_nativeMaxRefreshRate > 0)
            {
                DisplayControl.SetRefreshRate(_nativeMaxRefreshRate);
                UpdateDisplayRefreshRateUI();
            }
        }

        private void GpuMuxSwitch_Click(object sender, RoutedEventArgs e)
        {
            bool discreteOnly = GpuMuxSwitch.IsChecked == true;
            Capabilities.SetGpuMuxMode(discreteOnly);
            MessageBox.Show(
                LocalizationManager.T(discreteOnly ? "ToolsGpuMuxSwitchedDgpu" : "ToolsGpuMuxSwitchedHybrid"),
                LocalizationManager.T("ToolsGpuMuxTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void PrefWolBtn_Click(object sender, RoutedEventArgs e)
        {
            Capabilities.SetWakeOnLan(PrefWolSwitch.IsChecked == true);
        }

        private void PrefEthLedBtn_Click(object sender, RoutedEventArgs e)
        {
            Capabilities.SetEthernetLed(PrefEthLedSwitch.IsChecked == true);
        }

        private void PrefUsbResetBtn_Click(object sender, RoutedEventArgs e)
        {
            Capabilities.ResetUsbHub();
            MessageBox.Show(LocalizationManager.T("ToolsUsbResetDone"), LocalizationManager.T("ToolsUsbReset"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResetKeyboardPrefsBtn_Click(object sender, RoutedEventArgs e)
        {
            SystemPreferences.ResetKeyboardPreferences();
            SyncHardwarePreferences();
            MessageBox.Show(
                LocalizationManager.T("ToolsKeyboardResetDone"),
                LocalizationManager.T("ToolsKeyboardResetTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void PrefWinKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            _syncResumeAtTicks = MonotonicClock.DeadlineFromNowMs(2500);
            if (PrefWinKeySwitch != null)
            {
                SystemPreferences.SetWinKeyLock(PrefWinKeySwitch.IsChecked == true);
            }
            else
            {
                SystemPreferences.ToggleWinKeyLock();
            }
            SyncHardwarePreferences();
        }

        private void PrefSwapWinFnBtn_Click(object sender, RoutedEventArgs e)
        {
            _syncResumeAtTicks = MonotonicClock.DeadlineFromNowMs(2500);
            if (PrefSwapWinFnSwitch != null)
            {
                SystemPreferences.SetSwapWinFn(PrefSwapWinFnSwitch.IsChecked == true);
            }
            else
            {
                SystemPreferences.ToggleSwapWinFn();
            }
            SyncHardwarePreferences();
        }

        private void PrefFnLockBtn_Click(object sender, RoutedEventArgs e)
        {
            _syncResumeAtTicks = MonotonicClock.DeadlineFromNowMs(2500);
            if (PrefFnLockSwitch != null)
            {
                SystemPreferences.SetFnLock(PrefFnLockSwitch.IsChecked == true);
            }
            else
            {
                SystemPreferences.ToggleFnLock();
            }
            SyncHardwarePreferences();
        }

        private void PrefTouchpadBtn_Click(object sender, RoutedEventArgs e)
        {
            _syncResumeAtTicks = MonotonicClock.DeadlineFromNowMs(2500);
            bool targetState = (PrefTouchpadSwitch != null && PrefTouchpadSwitch.IsChecked.HasValue)
                ? PrefTouchpadSwitch.IsChecked.Value
                : !Touchpad.GetTouchpadState();
            Touchpad.SetTouchpadState(targetState);
            if (PrefTouchpadSwitch != null)
            {
                PrefTouchpadSwitch.IsChecked = targetState;
            }
        }

        private async void PrefMicBtn_Click(object sender, RoutedEventArgs e)
        {
            bool targetState = (PrefMicSwitch != null && PrefMicSwitch.IsChecked.HasValue)
                ? PrefMicSwitch.IsChecked.Value
                : !SystemPreferences.GetMicrophoneState();
            SystemPreferences.SetMicrophoneState(targetState);
            OsdOverlay.ShowMicrophone(targetState);
            if (PrefMicSwitch != null)
            {
                PrefMicSwitch.IsChecked = targetState;
            }
            await Task.Delay(200);
            SyncHardwarePreferences();
        }

        private async void PrefCameraBtn_Click(object sender, RoutedEventArgs e)
        {
            bool newState = SystemPreferences.ToggleCamera();
            if (PrefCameraSwitch != null)
            {
                PrefCameraSwitch.IsChecked = newState;
            }
            await Task.Delay(300);
            SyncHardwarePreferences();
        }

        private async void PrefAirplaneBtn_Click(object sender, RoutedEventArgs e)
        {
            bool newState = SystemPreferences.ToggleAirplaneMode();
            if (PrefAirplaneSwitch != null)
            {
                PrefAirplaneSwitch.IsChecked = newState;
            }
            await Task.Delay(300);
            SyncHardwarePreferences();
        }

        private void PrefCapsLockBtn_Click(object sender, RoutedEventArgs e)
        {
            SystemPreferences.ToggleCapsLock();
            SyncHardwarePreferences();
        }

        private void PrefNumLockBtn_Click(object sender, RoutedEventArgs e)
        {
            SystemPreferences.ToggleNumLock();
            SyncHardwarePreferences();
        }

        private void PrefScrLockBtn_Click(object sender, RoutedEventArgs e)
        {
            SystemPreferences.ToggleScrollLock();
            SyncHardwarePreferences();
        }

        private void CpuPowerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CpuPowerSlider == null || CpuOcBadgeText == null || !CpuPowerControl.IsSupported) return;

            int offset = (int)CpuPowerSlider.Value;
            UpdateCpuLabel(offset);
            CpuPowerControl.SetPowerOffset(offset);
        }

        private void UpdateCpuPerformanceText(bool cpuOcSupported, int offset)
        {
            if (cpuOcSupported)
            {
                if (CpuOcDescriptionText != null)
                {
                    CpuOcDescriptionText.Text = LocalizationManager.T("ToolsCpuPowerDesc");
                }
                if (CpuOcStatusSubText != null)
                {
                    CpuOcStatusSubText.Text = LocalizationManager.T("ToolsCpuPowerRange");
                }
                UpdateCpuLabel(offset);
            }
            else
            {
                if (CpuOcBadgeText != null)
                {
                    CpuOcBadgeText.Text = LocalizationManager.T("ToolsCpuOcLocked");
                }
                if (CpuOcDescriptionText != null)
                {
                    CpuOcDescriptionText.Text = LocalizationManager.T("ToolsCpuOcLockedDesc");
                }
                if (CpuOcStatusSubText != null)
                {
                    CpuOcStatusSubText.Text = LocalizationManager.T("ToolsCpuOcLockedRange");
                }
            }
        }

        private static string FormatPowerOffsetLabel(int offset)
        {
            if (offset == 0) return LocalizationManager.T("ToolsGpuStockBadge");
            if (offset >= 25) return LocalizationManager.Format("ToolsPowerBoostMaxFmt", offset);
            return "+" + offset + "W";
        }

        private void UpdateCpuLabel(int offset)
        {
            if (CpuOcBadgeText == null) return;
            CpuOcBadgeText.Text = FormatPowerOffsetLabel(offset);
        }

        private void TgpOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TgpOffsetSlider == null || TgpOffsetText == null) return;

            int offset = (int)TgpOffsetSlider.Value;
            UpdateTgpLabel(offset);
            NvidiaPowerControl.SetTgpOffset(offset);
        }

        private void UpdateTgpLabel(int offset)
        {
            if (TgpOffsetText == null) return;
            TgpOffsetText.Text = FormatPowerOffsetLabel(offset);
        }
    }
}
