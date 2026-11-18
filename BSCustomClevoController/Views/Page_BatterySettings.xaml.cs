using BSCustomClevoController.Utility;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using BatteryChargeStatus = BSCustomClevoController.Controls.BatteryChargeStatus;
using BSCustomClevoController.Controls;


namespace BSCustomClevoController.Views
{
    public partial class PageBatterySettings : Page
    {
        private static readonly SolidColorBrush BrushCriticalRed = CreateBrush(239, 68, 68);
        private static readonly SolidColorBrush BrushWarningAmber = CreateBrush(245, 158, 11);
        private static readonly SolidColorBrush BrushChargingCyan = CreateBrush(27, 161, 226);
        private static readonly SolidColorBrush BrushHealthyGreen = CreateBrush(16, 185, 129);
        private static readonly SolidColorBrush BrushMutedSlate = CreateBrush(148, 163, 184);

        private readonly DispatcherTimer _liveBatteryTimer;
        private readonly DispatcherTimer _statusClearTimer;
        private bool _isInitializing = false;

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public PageBatterySettings()
        {
            InitializeComponent();
            LoadBatterySettings();
            SyncLiveBatteryStatus();
            LocalizationManager.LanguageChanged += () => SyncLiveBatteryStatus();

            _liveBatteryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _liveBatteryTimer.Tick += (s, e) => SyncLiveBatteryStatus();
            _liveBatteryTimer.Start();

            _statusClearTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _statusClearTimer.Tick += (s, e) =>
            {
                _statusClearTimer.Stop();
                BatteryStatusText.Text = "";
            };
        }

        private void LoadBatterySettings()
        {
            _isInitializing = true;
            try
            {
                BatteryChargeStatus status = BatteryControl.GetStatus();

                if (status.Mode == BatteryChargeMode.Recommended)
                {
                    RecommendedChargeRadio.IsChecked = true;
                }
                else if (status.Mode == BatteryChargeMode.Custom)
                {
                    CustomChargeRadio.IsChecked = true;
                }
                else
                {
                    MaxChargeRadio.IsChecked = true;
                }

                CustomStartBox.Text = status.StartThreshold.ToString();
                CustomStopBox.Text = status.StopThreshold.ToString();
                UpdateThresholdPanelState();
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_BatterySettings.LoadBatterySettings", ex);
                MaxChargeRadio.IsChecked = true;
                UpdateThresholdPanelState();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void SyncLiveBatteryStatus()
        {
            try
            {
                PowerStatus power = SystemInformation.PowerStatus;
                int percent = (int)Math.Round(power.BatteryLifePercent * 100);
                percent = Math.Max(0, Math.Min(100, percent));

                bool isCharging = (power.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0;
                bool isAc = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

                LiveBatteryPercentText.Text = percent + "%";
                LiveBatteryProgressBar.Value = percent;

                SolidColorBrush indicatorBrush = ResolveBatteryIndicatorBrush(percent, isCharging);
                LiveBatteryPercentText.Foreground = indicatorBrush;
                LiveBatteryProgressBar.Foreground = indicatorBrush;

                UpdatePowerSourceDisplay(isAc, isCharging, percent);
                UpdateChargeLimitDisplay();
                UpdateBatteryTelemetryDisplay(isAc, isCharging);
            }
            catch (Exception ex)
            {
                AppLogger.Log("PageBatterySettings.SyncLiveBatteryStatus", ex);
            }
        }

        private static SolidColorBrush ResolveBatteryIndicatorBrush(int percent, bool isCharging)
        {
            if (percent <= 20)
            {
                return BrushCriticalRed;
            }
            if (percent <= 45)
            {
                return BrushWarningAmber;
            }
            return isCharging ? BrushChargingCyan : BrushHealthyGreen;
        }

        private void UpdatePowerSourceDisplay(bool isAc, bool isCharging, int percent)
        {
            if (!isAc)
            {
                LivePowerSourceText.Text = LocalizationManager.T("BatSourceBattery");
                LivePowerSourceText.Foreground = (percent <= 20)
                    ? BrushCriticalRed
                    : (SolidColorBrush)FindResource("Amoled.TextPrimary");
                return;
            }

            if (isCharging)
            {
                LivePowerSourceText.Text = LocalizationManager.Format("BatSourceAcFmt", LocalizationManager.T("BatStateCharging"));
                LivePowerSourceText.Foreground = BrushChargingCyan;
            }
            else if (percent >= 99)
            {
                LivePowerSourceText.Text = LocalizationManager.Format("BatSourceAcFmt", LocalizationManager.T("BatStateFull"));
                LivePowerSourceText.Foreground = BrushHealthyGreen;
            }
            else
            {
                LivePowerSourceText.Text = LocalizationManager.Format("BatSourceAcFmt", LocalizationManager.T("BatStatePluggedIn"));
                LivePowerSourceText.Foreground = (SolidColorBrush)FindResource("Amoled.TextPrimary");
            }
        }

        private void UpdateChargeLimitDisplay()
        {
            BatteryChargeStatus status = BatteryControl.GetStatus();
            if (!status.Enabled || status.Mode == BatteryChargeMode.Maximum)
            {
                this.LiveLimitStatusText.Text = LocalizationManager.T("BatLimitDisabled");
                this.LiveLimitStatusText.Foreground = BrushMutedSlate;
            }
            else if (status.Mode == BatteryChargeMode.Recommended)
            {
                this.LiveLimitStatusText.Text = LocalizationManager.T("BatLimitRecommended");
                this.LiveLimitStatusText.Foreground = BrushChargingCyan;
            }
            else
            {
                this.LiveLimitStatusText.Text = LocalizationManager.Format("BatLimitCustomFmt", status.StartThreshold, status.StopThreshold);
                this.LiveLimitStatusText.Foreground = BrushChargingCyan;
            }
        }

        private void UpdateBatteryTelemetryDisplay(bool isAc, bool isCharging)
        {
            BatteryTelemetry telemetry = BatteryControl.GetTelemetry();

            if (telemetry.VoltageMv > 0)
            {
                double volts = telemetry.VoltageMv / 1000.0;
                LiveVoltageText.Text = volts.ToString("0.00") + " V";
            }
            else
            {
                LiveVoltageText.Text = "-- V";
            }

            if (telemetry.ChargeRateMw > 0)
            {
                double watts = telemetry.ChargeRateMw / 1000.0;
                LiveChargeRateText.Text = string.Format("+{0:0.0} W ({1})", watts, LocalizationManager.T("BatStateCharging"));
                LiveChargeRateText.Foreground = BrushChargingCyan;
            }
            else if (isCharging)
            {
                LiveChargeRateText.Text = LocalizationManager.T("BatStateCharging");
                LiveChargeRateText.Foreground = BrushChargingCyan;
            }
            else if (telemetry.DischargeRateMw > 0)
            {
                double watts = telemetry.DischargeRateMw / 1000.0;
                LiveChargeRateText.Text = string.Format("-{0:0.0} W ({1})", watts, LocalizationManager.T("BatStateDischarging"));
                LiveChargeRateText.Foreground = (SolidColorBrush)FindResource("Amoled.TextSecondary");
            }
            else
            {
                LiveChargeRateText.Text = isAc ? string.Format("0.0 W ({0})", LocalizationManager.T("BatStateIdle")) : "0.0 W";
                LiveChargeRateText.Foreground = (SolidColorBrush)FindResource("Amoled.TextMuted");
            }
        }

        private void ChargeRadio_Checked(object sender, RoutedEventArgs e)
        {
            UpdateThresholdPanelState();
            if (!_isInitializing)
            {
                ApplyCurrentSettings();
            }
        }

        private void CustomThresholdBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing && CustomChargeRadio != null && CustomChargeRadio.IsChecked.GetValueOrDefault())
            {
                ApplyCurrentSettings();
            }
        }

        private void CustomThresholdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (!_isInitializing && CustomChargeRadio != null && CustomChargeRadio.IsChecked.GetValueOrDefault())
                {
                    ApplyCurrentSettings();
                }
                (Window.GetWindow(sender as DependencyObject ?? this) ?? System.Windows.Application.Current.MainWindow)?.Focus();
                e.Handled = true;
            }
        }

        private void UpdateThresholdPanelState()
        {
            if (this.CustomThresholdPanel == null) return;
            bool isCustom = this.CustomChargeRadio != null && this.CustomChargeRadio.IsChecked.GetValueOrDefault();
            this.CustomStartBox.IsEnabled = isCustom;
            this.CustomStopBox.IsEnabled = isCustom;
            this.CustomThresholdPanel.Opacity = isCustom ? 1.0 : 0.45;
        }

        private void ApplyCurrentSettings()
        {
            try
            {
                _statusClearTimer.Stop();

                BatteryChargeMode selectedMode;
                int start = 90;
                int stop = 100;

                if (MaxChargeRadio.IsChecked.GetValueOrDefault())
                {
                    selectedMode = BatteryChargeMode.Maximum;
                }
                else if (RecommendedChargeRadio.IsChecked.GetValueOrDefault())
                {
                    selectedMode = BatteryChargeMode.Recommended;
                }
                else
                {
                    selectedMode = BatteryChargeMode.Custom;

                    if (!int.TryParse(CustomStartBox.Text.Trim(), out int parsedStart) || parsedStart < 40 || parsedStart > 99)
                    {
                        ShowStatusMessage(LocalizationManager.T("BatWarnStartRange"), isError: true);
                        return;
                    }

                    if (!int.TryParse(CustomStopBox.Text.Trim(), out int parsedStop) || parsedStop < 50 || parsedStop > 100)
                    {
                        ShowStatusMessage(LocalizationManager.T("BatWarnStopRange"), isError: true);
                        return;
                    }

                    if (parsedStart >= parsedStop)
                    {
                        ShowStatusMessage(LocalizationManager.T("BatWarnStartBelowStop"), isError: true);
                        return;
                    }

                    start = parsedStart;
                    stop = parsedStop;
                }

                bool success = BatteryControl.SetChargeMode(selectedMode, start, stop);
                if (success)
                {
                    ShowStatusMessage(LocalizationManager.T("BatApplied"), isError: false);
                    SyncLiveBatteryStatus();
                    _statusClearTimer.Start();
                }
                else
                {
                    ShowStatusMessage(LocalizationManager.T("BatApplyFailed"), isError: true);
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage(LocalizationManager.Format("CommonErrorFmt", ex.Message), isError: true);
            }
        }

        private void ShowStatusMessage(string message, bool isError)
        {
            this.BatteryStatusText.Text = message;
            this.BatteryStatusText.Foreground = isError ? BrushCriticalRed : BrushHealthyGreen;
        }
    }
}
