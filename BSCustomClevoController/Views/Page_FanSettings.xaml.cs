using BSCustomClevoController.Utility;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BSCustomClevoController.Controls;


namespace BSCustomClevoController.Views
{
    public partial class Page_FanSettings : Page
    {
        private readonly SolidColorBrush _colorBlue = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        private readonly SolidColorBrush _colorYellow = new SolidColorBrush(Color.FromRgb(255, 178, 36));
        private readonly SolidColorBrush _colorRed = new SolidColorBrush(Color.FromRgb(255, 77, 77));
        private readonly SolidColorBrush _colorDefaultStatus = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private readonly SolidColorBrush _colorActiveBorder = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly SolidColorBrush _colorInactiveBorder = new SolidColorBrush(Color.FromRgb(60, 60, 70));
        private readonly SolidColorBrush _colorActiveBg = new SolidColorBrush(Color.FromRgb(36, 48, 64));
        private readonly SolidColorBrush _colorDefaultBg = new SolidColorBrush(Color.FromRgb(42, 42, 48));
        private readonly SolidColorBrush _gridLineBrush = new SolidColorBrush(Color.FromRgb(31, 36, 48));
        private readonly SolidColorBrush _pointCoreBrush = new SolidColorBrush(Color.FromRgb(11, 11, 14));
        private readonly SolidColorBrush _liveTempLineBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
        private SolidColorBrush _cpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(61, 184, 245));
        private SolidColorBrush _gpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(61, 214, 140));
        private readonly DispatcherTimer _rpmTimer;
        private readonly DispatcherTimer _curveStatusResetTimer;
        private const int CurvePointCount = 4;
        private readonly int[] _cpuCurveTemps = new int[] { 40, 60, 80, 100 };
        private readonly byte[] _cpuCurveSpeeds = new byte[] { 35, 50, 85, 100 };
        private readonly int[] _gpuCurveTemps = new int[] { 40, 60, 80, 100 };
        private readonly byte[] _gpuCurveSpeeds = new byte[] { 35, 50, 85, 100 };
        private int _draggingCpuIndex = -1;
        private int _draggingGpuIndex = -1;
        private int _hoverCpuIndex = -1;
        private int _hoverGpuIndex = -1;
        private int _lastCpuTemp;
        private int _lastGpuTemp;
        private Fan.Mode _currentMode = Fan.Mode.Auto;

        private const double CurvePadYTop = 18.0;
        private const double CurvePadYBottom = 10.0;

        private const string IntelLogoPath = "M20.42 7.345v9.18h1.651v-9.18zM0 7.475v1.737h1.737V7.474zm9.78.352v6.053c0 .513.044.945.13 1.292.087.34.235.618.44.828.203.21.475.359.803.451.334.093.754.136 1.255.136h.216v-1.533c-.24 0-.445-.012-.593-.037a.672.672 0 0 1-.39-.173.693.693 0 0 1-.173-.377 4.002 4.002 0 0 1-.037-.606v-2.182h1.193v-1.416h-1.193V7.827zm-3.505 2.312c-.396 0-.76.08-1.082.241-.327.161-.6.384-.822.668l-.087.117v-.902H2.658v6.256h1.639v-3.214c.018-.588.16-1.02.433-1.299.29-.297.642-.445 1.044-.445.476 0 .841.149 1.082.433.235.284.359.686.359 1.2v3.324h1.663V12.97c.006-.89-.229-1.595-.686-2.09-.458-.495-1.1-.742-1.917-.742zm10.065.006a3.252 3.252 0 0 0-2.306.946c-.29.29-.525.637-.692 1.033a3.145 3.145 0 0 0-.254 1.273c0 .452.08.878.241 1.274.161.395.39.742.674 1.032.284.29.637.526 1.045.693.408.173.86.26 1.342.26 1.397 0 2.262-.637 2.782-1.23l-1.187-.904c-.248.297-.841.699-1.583.699-.464 0-.847-.105-1.138-.321a1.588 1.588 0 0 1-.593-.872l-.019-.056h4.915v-.587c0-.451-.08-.872-.235-1.267a3.393 3.393 0 0 0-.661-1.033 3.013 3.013 0 0 0-1.02-.692 3.345 3.345 0 0 0-1.311-.248zm-16.297.118v6.256h1.651v-6.256zm16.278 1.286c1.132 0 1.664.797 1.664 1.255l-3.32.006c0-.458.525-1.255 1.656-1.261z";
        private const string AmdLogoPath = "M18.324 9.137l1.559 1.56h2.556v2.557L24 14.814V9.137zM2 9.52l-2 4.96h1.309l.37-.982H3.9l.408.982h1.338L3.432 9.52zm4.209 0v4.955h1.238v-3.092l1.338 1.562h.188l1.338-1.556v3.091h1.238V9.52H10.47l-1.592 1.845L7.287 9.52zm6.283 0v4.96h2.057c1.979 0 2.88-1.046 2.88-2.472 0-1.36-.937-2.488-2.747-2.488zm1.237.91h.792c1.17 0 1.63.711 1.63 1.57 0 .728-.372 1.572-1.616 1.572h-.806zm-10.985.273l.791 1.932H2.008zm17.137.307l-1.604 1.603v2.25h2.246l1.604-1.607h-2.246z";
        private const string NvidiaLogoPath = "M8.948 8.798v-1.43a6.7 6.7 0 0 1 .424-.018c3.922-.124 6.493 3.374 6.493 3.374s-2.774 3.851-5.75 3.851c-.398 0-.787-.062-1.158-.185v-4.346c1.528.185 1.837.857 2.747 2.385l2.04-1.714s-1.492-1.952-4-1.952a6.016 6.016 0 0 0-.796.035m0-4.735v2.138l.424-.027c5.45-.185 9.01 4.47 9.01 4.47s-4.08 4.964-8.33 4.964c-.37 0-.733-.035-1.095-.097v1.325c.3.035.61.062.91.062 3.957 0 6.82-2.023 9.593-4.408.459.371 2.34 1.263 2.73 1.652-2.633 2.208-8.772 3.984-12.253 3.984-.335 0-.653-.018-.971-.053v1.864H24V4.063zm0 10.326v1.131c-3.657-.654-4.673-4.46-4.673-4.46s1.758-1.944 4.673-2.262v1.237H8.94c-1.528-.186-2.73 1.245-2.73 1.245s.68 2.412 2.739 3.11M2.456 10.9s2.164-3.197 6.5-3.533V6.201C4.153 6.59 0 10.653 0 10.653s2.35 6.802 8.948 7.42v-1.237c-4.84-.6-6.492-5.936-6.492-5.936z";

        private static string DefaultCurveStatusText => LocalizationManager.T("FanCurveDefaultStatus");
        private static string AutoDesc => LocalizationManager.T("FanAutoDesc");
        private static string MaxDesc => LocalizationManager.T("FanMaxDesc");
        private static string MaxQDesc => LocalizationManager.T("FanMaxQDesc");
        private static string AntiDustDesc => LocalizationManager.T("FanAntiDustDesc");
        private static string CustomDesc => LocalizationManager.T("FanCustomDesc");

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            UiFocusHelper.ClearTextBoxFocusOnClick(Window.GetWindow(this) ?? Application.Current.MainWindow, e.OriginalSource);
        }

        public Page_FanSettings()
        {
            InitializeComponent();
            ApplyHardwareBranding();
            ApplyFeatureCapabilities();

            FanStatus fanStatus = Fan.GetStatus();

            _rpmTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _rpmTimer.Tick += rpmTimer_Tick;

            _curveStatusResetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _curveStatusResetTimer.Tick += (s, e) =>
            {
                _curveStatusResetTimer.Stop();
                CurveStatusMessage.Text = DefaultCurveStatusText;
                CurveStatusMessage.Foreground = _colorDefaultStatus;
            };

            Loaded += (s, e) =>
            {
                if (!_rpmTimer.IsEnabled) _rpmTimer.Start();
                rpmTimer_Tick(null, EventArgs.Empty);
            };
            Unloaded += (s, e) =>
            {
                if (_rpmTimer.IsEnabled) _rpmTimer.Stop();
                if (_curveStatusResetTimer.IsEnabled) _curveStatusResetTimer.Stop();
            };

            if (!_rpmTimer.IsEnabled)
                _rpmTimer.Start();

            OffsetSlider.Value = fanStatus.Offset;
            OffsetValueText.Text = string.Format("{0}%", (int)OffsetSlider.Value);

            SetCustomFanValues(fanStatus.Fan_CPU, fanStatus.Fan_GPU1);
            SetActiveModeUI(fanStatus.Mode);

            rpmTimer_Tick(null, EventArgs.Empty);
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            ApplyFeatureCapabilities();
            SetActiveModeUI(_currentMode);
            RenderAllCurves();
            if (!_curveStatusResetTimer.IsEnabled)
            {
                CurveStatusMessage.Text = DefaultCurveStatusText;
            }
        }

        private void ApplyFeatureCapabilities()
        {
            bool antiDustSupported = Fan.IsAntiDustSupported();
            this.AntiDustModeBtn.IsEnabled = antiDustSupported;
            if (!antiDustSupported)
            {
                this.AntiDustModeBtn.ToolTip = LocalizationManager.T("CommonNotSupported");
            }

            bool maxQSupported = Fan.IsMaxQSupported();
            this.MaxQModeBtn.IsEnabled = maxQSupported;
            if (!maxQSupported)
            {
                this.MaxQModeBtn.ToolTip = LocalizationManager.T("CommonNotSupported");
            }
        }

        internal void SetActiveModeUI(Fan.Mode mode, string customMessage = null)
        {
            if (mode == Fan.Mode.MaxQ && !Fan.IsMaxQSupported())
            {
                mode = Fan.Mode.Auto;
            }

            if (mode != Fan.Mode.Unknown)
            {
                _currentMode = mode;
            }

            SetButtonActive(AutoModeBtn, mode == Fan.Mode.Auto);
            SetButtonActive(MaxModeBtn, mode == Fan.Mode.Max);
            SetButtonActive(AntiDustModeBtn, false);
            SetButtonActive(MaxQModeBtn, mode == Fan.Mode.MaxQ);
            SetButtonActive(CustomModeBtn, mode == Fan.Mode.Custom);

            bool isCustom = mode == Fan.Mode.Custom;
            SetCustomFanUIElements(isCustom);

            if (!string.IsNullOrEmpty(customMessage))
            {
                FanModeDesText.Text = customMessage;
                return;
            }

            switch (mode)
            {
                case Fan.Mode.Auto:
                    FanModeDesText.Text = AutoDesc;
                    break;
                case Fan.Mode.Max:
                    FanModeDesText.Text = MaxDesc;
                    break;
                case Fan.Mode.MaxQ:
                    FanModeDesText.Text = MaxQDesc;
                    break;
                case Fan.Mode.Custom:
                    FanModeDesText.Text = CustomDesc;
                    break;
                default:
                    FanModeDesText.Text = LocalizationManager.T("FanStandardDesc");
                    break;
            }
        }

        private void SetButtonActive(Button btn, bool isActive)
        {
            btn.BorderBrush = isActive ? _colorActiveBorder : _colorInactiveBorder;
            btn.Background = isActive ? _colorActiveBg : _colorDefaultBg;
        }

        private void SetCustomFanUIElements(bool active)
        {
            ApplyCustomFan.IsEnabled = active;
            ResetCustomFan.IsEnabled = active;
            CpuCurveCanvas.IsEnabled = active;
            GpuCurveCanvas.IsEnabled = active;
            CpuCurveCanvas.Opacity = active ? 1.0 : 0.55;
            GpuCurveCanvas.Opacity = active ? 1.0 : 0.55;
        }

        private static void LoadFanInfoToArrays(FanInfo info, int[] temps, byte[] speeds)
        {
            int[] rawTemps =
            {
                info.T1 > 0 ? info.T1 : 40,
                info.T2 > 0 ? info.T2 : 60,
                info.T3 > 0 ? info.T3 : 80,
                info.T4 > 0 ? info.T4 : 100
            };

            byte[] rawSpeeds =
            {
                info.D1 > 0 ? info.D1 : (byte)35,
                info.D2 > 0 ? info.D2 : (byte)50,
                info.D3 > 0 ? info.D3 : (byte)85,
                info.D4 > 0 ? info.D4 : (byte)100
            };

            ClampCurvePoints(rawTemps, rawSpeeds, temps, speeds);
        }

        private static void ClampCurvePoints(int[] rawTemps, byte[] rawSpeeds, int[] temps, byte[] speeds)
        {
            for (int i = 0; i < CurvePointCount; i++)
            {
                int minAllowed = 30 + i;
                int maxAllowed = 100 - (CurvePointCount - 1 - i);
                int clampedTemp = Math.Max(minAllowed, Math.Min(maxAllowed, rawTemps[i]));
                if (i > 0 && clampedTemp <= temps[i - 1])
                {
                    clampedTemp = Math.Min(maxAllowed, temps[i - 1] + 1);
                }
                temps[i] = clampedTemp;

                byte clampedSpeed = Math.Min((byte)100, rawSpeeds[i]);
                if (i > 0 && clampedSpeed < speeds[i - 1])
                {
                    clampedSpeed = speeds[i - 1];
                }
                speeds[i] = clampedSpeed;
            }

            for (int i = CurvePointCount - 2; i >= 0; i--)
            {
                if (temps[i] >= temps[i + 1])
                {
                    temps[i] = Math.Max(30 + i, temps[i + 1] - 1);
                }
            }
        }

        private void SetCustomFanValues(FanInfo CPU, FanInfo GPU)
        {
            LoadFanInfoToArrays(CPU, _cpuCurveTemps, _cpuCurveSpeeds);
            LoadFanInfoToArrays(GPU, _gpuCurveTemps, _gpuCurveSpeeds);
            RenderAllCurves();
        }

        private void ApplyHardwareBranding()
        {
            string cpuModel;
            CpuBrand cpuBrand = HardwareDetector.DetectCpu(out cpuModel);

            string gpuModel;
            GpuBrand gpuBrand = HardwareDetector.DetectGpu(out gpuModel);

            if (cpuBrand == CpuBrand.Amd)
            {
                _cpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(255, 77, 77));
                CpuVendorLogo.Data = Geometry.Parse(AmdLogoPath);
                CpuTelemetryCard.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 25, 28));
            }
            else
            {
                _cpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(61, 184, 245));
                CpuVendorLogo.Data = Geometry.Parse(IntelLogoPath);
                CpuTelemetryCard.BorderBrush = new SolidColorBrush(Color.FromRgb(25, 38, 56));
            }

            CpuVendorLogo.Fill = _cpuPrimaryBrush;
            CpuFanBlades.Fill = _cpuPrimaryBrush;
            RpmCpuText.Foreground = _cpuPrimaryBrush;
            if (!string.IsNullOrEmpty(cpuModel) && cpuModel != "CPU")
            {
                CpuSubtitleText.Text = cpuModel;
            }

            if (gpuBrand == GpuBrand.Amd)
            {
                _gpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(255, 77, 77));
                GpuVendorLogo.Data = Geometry.Parse(AmdLogoPath);
                GpuTelemetryCard.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 25, 28));
            }
            else if (gpuBrand == GpuBrand.Intel)
            {
                _gpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(61, 184, 245));
                GpuVendorLogo.Data = Geometry.Parse(IntelLogoPath);
                GpuTelemetryCard.BorderBrush = new SolidColorBrush(Color.FromRgb(25, 38, 56));
            }
            else
            {
                _gpuPrimaryBrush = new SolidColorBrush(Color.FromRgb(61, 214, 140));
                GpuVendorLogo.Data = Geometry.Parse(NvidiaLogoPath);
                GpuTelemetryCard.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 44, 34));
            }

            GpuVendorLogo.Fill = _gpuPrimaryBrush;
            GpuFanBlades.Fill = _gpuPrimaryBrush;
            RpmGpuText.Foreground = _gpuPrimaryBrush;
            if (!string.IsNullOrEmpty(gpuModel) && gpuModel != "GPU")
            {
                GpuSubtitleText.Text = gpuModel;
            }

            if (Fan.HasThirdFan)
            {
                ColGpu2.Width = new GridLength(1, GridUnitType.Star);
                GpuTelemetryCard.Margin = new Thickness(8, 0, 8, 0);
                Gpu2TelemetryCard.Visibility = Visibility.Visible;
                Gpu2VendorLogo.Data = GpuVendorLogo.Data;
                Gpu2VendorLogo.Fill = _gpuPrimaryBrush;
                Gpu2FanBlades.Fill = _gpuPrimaryBrush;
                Gpu2TelemetryCard.BorderBrush = GpuTelemetryCard.BorderBrush;
                RpmGpu2Text.Foreground = _gpuPrimaryBrush;
                if (!string.IsNullOrEmpty(gpuModel) && gpuModel != "GPU")
                {
                    Gpu2SubtitleText.Text = gpuModel;
                }
            }
            else
            {
                ColGpu2.Width = new GridLength(0);
                GpuTelemetryCard.Margin = new Thickness(8, 0, 0, 0);
                Gpu2TelemetryCard.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateFanTelemetryDisplay(TextBlock label, int rpm, int percent, int temp, SolidColorBrush normalBrush)
        {
            label.Text = string.Format(LocalizationManager.CurrentCulture, "{0} {1}   {2}%   {3}°C",
                rpm > 0 ? rpm : 0,
                LocalizationManager.T("FanRpmUnit", "RPM"),
                percent > 0 ? percent : 0,
                temp > 0 ? temp : 0);
            if (temp > 85)
            {
                label.Foreground = _colorRed;
            }
            else
            {
                label.Foreground = temp > 70 ? _colorYellow : normalBrush;
            }
        }

        private void rpmTimer_Tick(object sender, EventArgs e)
        {
            FanInfo cpu = default;
            FanInfo gpu1 = default;
            FanInfo gpu2 = default;
            Fan.Read_FanSpeed(ref cpu, ref gpu1, ref gpu2);

            if (Fan.HasThirdFan)
            {
                UpdateFanTelemetryDisplay(RpmGpu2Text, gpu2.rpm, gpu2.percent, gpu2.remote, _gpuPrimaryBrush);
            }

            UpdateFanTelemetryDisplay(RpmCpuText, cpu.rpm, cpu.percent, cpu.remote, _cpuPrimaryBrush);
            UpdateFanTelemetryDisplay(RpmGpuText, gpu1.rpm, gpu1.percent, gpu1.remote, _gpuPrimaryBrush);

            _lastCpuTemp = cpu.remote;
            _lastGpuTemp = gpu1.remote;
            RenderAllCurves();
        }

        private void AutoModeBtn_Click(object sender, RoutedEventArgs e)
        {
            Fan.SetMode(Fan.Mode.Auto);
            SetActiveModeUI(Fan.Mode.Auto);
            OsdOverlay.ShowFan(Fan.Mode.Auto);
        }

        private void MaxModeBtn_Click(object sender, RoutedEventArgs e)
        {
            Fan.SetMode(Fan.Mode.Max);
            SetActiveModeUI(Fan.Mode.Max);
            OsdOverlay.ShowFan(Fan.Mode.Max);
        }

        private void AntiDustModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Fan.IsAntiDustSupported()) return;
            Fan.AntiDust();
            SetButtonActive(AntiDustModeBtn, true);
            SetActiveModeUI(Fan.Mode.Unknown, AntiDustDesc);
            OsdOverlay.ShowFanAntiDust();
        }

        private void MaxQModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Fan.IsMaxQSupported()) return;
            Fan.SetMode(Fan.Mode.MaxQ);
            SetActiveModeUI(Fan.Mode.MaxQ);
            OsdOverlay.ShowFan(Fan.Mode.MaxQ);
        }

        private void CustomModeBtn_Click(object sender, RoutedEventArgs e)
        {
            Fan.SetMode(Fan.Mode.Custom);
            SetActiveModeUI(Fan.Mode.Custom);
            OsdOverlay.ShowFan(Fan.Mode.Custom);
        }

        private void CommitOffsetSliderValue()
        {
            Fan.SetOffset(Convert.ToByte((int)this.OffsetSlider.Value));
            this.OffsetValueText.Text = string.Format("{0}%", (int)this.OffsetSlider.Value);
        }

        private void OffsetSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            CommitOffsetSliderValue();
        }

        private void OffsetSlider_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            OffsetValueText.Text = string.Format("{0}%", (int)OffsetSlider.Value);
        }

        private void OffsetSlider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CommitOffsetSliderValue();
        }

        private void RenderAllCurves()
        {
            int activeCpu = _draggingCpuIndex >= 0 ? _draggingCpuIndex : _hoverCpuIndex;
            int activeGpu = _draggingGpuIndex >= 0 ? _draggingGpuIndex : _hoverGpuIndex;
            RenderCurve(CpuCurveCanvas, _cpuCurveTemps, _cpuCurveSpeeds, _cpuPrimaryBrush, activeCpu, _lastCpuTemp, CpuCurvePointReadout);
            RenderCurve(GpuCurveCanvas, _gpuCurveTemps, _gpuCurveSpeeds, _gpuPrimaryBrush, activeGpu, _lastGpuTemp, GpuCurvePointReadout);
        }

        private static double TempToCanvasX(int temp, double colW)
        {
            return ((temp - 30) / 10.0 + 0.5) * colW;
        }

        private static double SpeedToCanvasY(byte speed, double plotH)
        {
            return CurvePadYTop + plotH * (1.0 - speed / 100.0);
        }

        private void RenderCurve(Canvas canvas, int[] temps, byte[] speeds, SolidColorBrush primaryBrush, int activeIdx, int liveTemp, TextBlock readout)
        {
            UpdateCurveReadout(readout, temps, speeds, activeIdx, liveTemp);

            if (canvas == null) return;
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 20 || h < 20) return;

            canvas.Children.Clear();

            double colW = w / 8.0;
            double plotH = h - CurvePadYTop - CurvePadYBottom;
            double bottomY = CurvePadYTop + plotH;

            RenderCurveGridAndLiveMarker(canvas, w, colW, bottomY, plotH, liveTemp);
            RenderCurveSeriesAndHandles(canvas, temps, speeds, primaryBrush, activeIdx);
        }

        private static void UpdateCurveReadout(TextBlock readout, int[] temps, byte[] speeds, int activeIdx, int liveTemp)
        {
            if (readout == null) return;
            if (activeIdx >= 0 && activeIdx < CurvePointCount)
            {
                readout.Text = LocalizationManager.Format("FanCurvePointFmt", activeIdx + 1, temps[activeIdx], speeds[activeIdx]);
            }
            else if (liveTemp > 0)
            {
                readout.Text = LocalizationManager.Format("FanCurveLiveFmt", liveTemp);
            }
            else
            {
                readout.Text = LocalizationManager.T("FanCurveRange");
            }
        }

        private void RenderCurveGridAndLiveMarker(Canvas canvas, double w, double colW, double bottomY, double plotH, int liveTemp)
        {
            for (int step = 0; step <= 4; step++)
            {
                double gy = CurvePadYTop + plotH * (step / 4.0);
                canvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = gy,
                    X2 = w,
                    Y2 = gy,
                    Stroke = _gridLineBrush,
                    StrokeThickness = 1
                });
            }

            for (int i = 0; i < 8; i++)
            {
                double gx = (i + 0.5) * colW;
                canvas.Children.Add(new Line
                {
                    X1 = gx,
                    Y1 = CurvePadYTop,
                    X2 = gx,
                    Y2 = bottomY,
                    Stroke = _gridLineBrush,
                    StrokeThickness = 1
                });
            }

            if (liveTemp >= 30 && liveTemp <= 100)
            {
                double liveX = ((liveTemp - 30) / 70.0) * (7.0 * colW) + 0.5 * colW;
                canvas.Children.Add(new Line
                {
                    X1 = liveX,
                    Y1 = CurvePadYTop,
                    X2 = liveX,
                    Y2 = bottomY,
                    Stroke = _liveTempLineBrush,
                    StrokeThickness = 1.2,
                    StrokeDashArray = new DoubleCollection { 3, 3 }
                });
            }
        }

        private void RenderCurveSeriesAndHandles(
            Canvas canvas,
            int[] temps,
            byte[] speeds,
            SolidColorBrush primaryBrush,
            int activeIdx)
        {
            double colW = canvas.ActualWidth / 8.0;
            double plotH = canvas.ActualHeight - CurvePadYTop - CurvePadYBottom;
            double bottomY = CurvePadYTop + plotH;

            double[] px = new double[CurvePointCount];
            double[] py = new double[CurvePointCount];
            for (int i = 0; i < CurvePointCount; i++)
            {
                px[i] = TempToCanvasX(temps[i], colW);
                py[i] = SpeedToCanvasY(speeds[i], plotH);
            }

            PointCollection linePoints = BuildCurveLinePoints(temps, colW, px, py);
            PointCollection fillPoints = BuildCurveFillPoints(linePoints, bottomY);

            Color baseColor = primaryBrush.Color;
            canvas.Children.Add(new Polygon
            {
                Points = fillPoints,
                Fill = new LinearGradientBrush(
                    Color.FromArgb(75, baseColor.R, baseColor.G, baseColor.B),
                    Color.FromArgb(4, baseColor.R, baseColor.G, baseColor.B),
                    90.0)
            });

            canvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = primaryBrush,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            });

            for (int i = 0; i < CurvePointCount; i++)
            {
                AddCurvePointHandle(canvas, primaryBrush, baseColor, px[i], py[i], speeds[i], i == activeIdx);
            }
        }

        private static PointCollection BuildCurveLinePoints(int[] temps, double colW, double[] px, double[] py)
        {
            PointCollection linePoints = new PointCollection(CurvePointCount + 2);
            if (temps[0] > 30)
            {
                linePoints.Add(new Point(0.5 * colW, py[0]));
            }
            for (int i = 0; i < CurvePointCount; i++)
            {
                linePoints.Add(new Point(px[i], py[i]));
            }
            if (temps[CurvePointCount - 1] < 100)
            {
                linePoints.Add(new Point(7.5 * colW, py[CurvePointCount - 1]));
            }
            return linePoints;
        }

        private static PointCollection BuildCurveFillPoints(PointCollection linePoints, double bottomY)
        {
            PointCollection fillPoints = new PointCollection(CurvePointCount + 4);
            fillPoints.Add(new Point(linePoints[0].X, bottomY));
            for (int i = 0; i < linePoints.Count; i++)
            {
                fillPoints.Add(linePoints[i]);
            }
            fillPoints.Add(new Point(linePoints[linePoints.Count - 1].X, bottomY));
            return fillPoints;
        }

        private void AddCurvePointHandle(
            Canvas canvas,
            SolidColorBrush primaryBrush,
            Color baseColor,
            double pointX,
            double pointY,
            byte speed,
            bool isActive)
        {
            if (isActive)
            {
                Ellipse glow = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(Color.FromArgb(55, baseColor.R, baseColor.G, baseColor.B))
                };
                Canvas.SetLeft(glow, pointX - 10.0);
                Canvas.SetTop(glow, pointY - 10.0);
                canvas.Children.Add(glow);
            }

            Ellipse handle = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = isActive ? (Brush)primaryBrush : _pointCoreBrush,
                Stroke = primaryBrush,
                StrokeThickness = 2.2
            };
            Canvas.SetLeft(handle, pointX - 5.5);
            Canvas.SetTop(handle, pointY - 5.5);
            canvas.Children.Add(handle);

            TextBlock pctText = new TextBlock
            {
                Text = speed + "%",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = isActive ? Brushes.White : (Brush)primaryBrush,
                Width = 32,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(pctText, pointX - 16.0);
            Canvas.SetTop(pctText, Math.Max(0.0, pointY - 19.0));
            canvas.Children.Add(pctText);
        }

        private static int HitTestNearestPointIndex(Canvas canvas, Point pos, int[] temps, byte[] speeds)
        {
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return 0;

            double colW = w / 8.0;
            double plotH = Math.Max(1.0, h - CurvePadYTop - CurvePadYBottom);

            int bestIdx = 0;
            double bestDistSq = double.MaxValue;
            for (int i = 0; i < CurvePointCount; i++)
            {
                double dx = TempToCanvasX(temps[i], colW) - pos.X;
                double dy = SpeedToCanvasY(speeds[i], plotH) - pos.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private void UpdateCurvePointFromMouse(Canvas canvas, int[] temps, byte[] speeds, int idx, Point pos)
        {
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double colW = w / 8.0;
            int targetTemp = (int)Math.Round(30.0 + ((pos.X - 0.5 * colW) / (7.0 * colW)) * 70.0);
            int minAllowed = 30 + idx;
            int maxAllowed = 100 - (CurvePointCount - 1 - idx);
            temps[idx] = Math.Max(minAllowed, Math.Min(maxAllowed, targetTemp));

            for (int i = idx - 1; i >= 0; i--)
            {
                if (temps[i] >= temps[i + 1])
                {
                    temps[i] = temps[i + 1] - 1;
                }
            }
            for (int i = idx + 1; i < CurvePointCount; i++)
            {
                if (temps[i] <= temps[i - 1])
                {
                    temps[i] = temps[i - 1] + 1;
                }
            }

            double plotH = Math.Max(1.0, h - CurvePadYTop - CurvePadYBottom);
            int pct = Math.Max(0, Math.Min(100, (int)Math.Round((1.0 - (pos.Y - CurvePadYTop) / plotH) * 100.0)));
            speeds[idx] = (byte)pct;

            for (int i = 0; i < idx; i++)
            {
                if (speeds[i] > speeds[idx])
                {
                    speeds[i] = speeds[idx];
                }
            }
            for (int i = idx + 1; i < CurvePointCount; i++)
            {
                if (speeds[i] < speeds[idx])
                {
                    speeds[i] = speeds[idx];
                }
            }

            RenderAllCurves();
        }

        private void HandleCurveMouseDown(Canvas canvas, int[] temps, byte[] speeds, ref int draggingIdx, ref int hoverIdx, MouseButtonEventArgs e)
        {
            if (!canvas.IsEnabled) return;
            Point pos = e.GetPosition(canvas);
            int idx = HitTestNearestPointIndex(canvas, pos, temps, speeds);
            draggingIdx = idx;
            hoverIdx = idx;
            canvas.CaptureMouse();
            UpdateCurvePointFromMouse(canvas, temps, speeds, idx, pos);
        }

        private void HandleCurveMouseMove(Canvas canvas, int[] temps, byte[] speeds, ref int draggingIdx, ref int hoverIdx, MouseEventArgs e)
        {
            if (!canvas.IsEnabled) return;
            Point pos = e.GetPosition(canvas);
            if (draggingIdx >= 0)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    draggingIdx = -1;
                    if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
                    RenderAllCurves();
                    return;
                }
                hoverIdx = draggingIdx;
                UpdateCurvePointFromMouse(canvas, temps, speeds, draggingIdx, pos);
            }
            else
            {
                int idx = HitTestNearestPointIndex(canvas, pos, temps, speeds);
                if (idx != hoverIdx)
                {
                    hoverIdx = idx;
                    RenderAllCurves();
                }
            }
        }

        private void HandleCurveMouseUp(Canvas canvas, ref int draggingIdx)
        {
            if (draggingIdx >= 0)
            {
                draggingIdx = -1;
                if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
                RenderAllCurves();
            }
        }

        private void HandleCurveMouseLeave(int draggingIdx, ref int hoverIdx)
        {
            if (draggingIdx < 0 && hoverIdx != -1)
            {
                hoverIdx = -1;
                RenderAllCurves();
            }
        }

        private void CpuCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderAllCurves();
        private void GpuCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderAllCurves();

        private void CpuCurveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => HandleCurveMouseDown(CpuCurveCanvas, _cpuCurveTemps, _cpuCurveSpeeds, ref _draggingCpuIndex, ref _hoverCpuIndex, e);

        private void CpuCurveCanvas_MouseMove(object sender, MouseEventArgs e)
            => HandleCurveMouseMove(CpuCurveCanvas, _cpuCurveTemps, _cpuCurveSpeeds, ref _draggingCpuIndex, ref _hoverCpuIndex, e);

        private void CpuCurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => HandleCurveMouseUp(CpuCurveCanvas, ref _draggingCpuIndex);

        private void CpuCurveCanvas_MouseLeave(object sender, MouseEventArgs e)
            => HandleCurveMouseLeave(_draggingCpuIndex, ref _hoverCpuIndex);

        private void GpuCurveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => HandleCurveMouseDown(GpuCurveCanvas, _gpuCurveTemps, _gpuCurveSpeeds, ref _draggingGpuIndex, ref _hoverGpuIndex, e);

        private void GpuCurveCanvas_MouseMove(object sender, MouseEventArgs e)
            => HandleCurveMouseMove(GpuCurveCanvas, _gpuCurveTemps, _gpuCurveSpeeds, ref _draggingGpuIndex, ref _hoverGpuIndex, e);

        private void GpuCurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => HandleCurveMouseUp(GpuCurveCanvas, ref _draggingGpuIndex);

        private void GpuCurveCanvas_MouseLeave(object sender, MouseEventArgs e)
            => HandleCurveMouseLeave(_draggingGpuIndex, ref _hoverGpuIndex);

        private void ResetCustomFan_Click(object sender, RoutedEventArgs e)
        {
            FanInfo defaults = Fan.GetDefaultFanInfo();
            SetCustomFanValues(defaults, defaults);
            ShowCurveStatus(LocalizationManager.T("FanCurveResetStatus"), _colorYellow, autoReset: false);
        }

        private void ApplyCustomFan_Click(object sender, RoutedEventArgs e)
        {
            FanInfo cpuInfo = new FanInfo(
                _cpuCurveTemps[0], _cpuCurveSpeeds[0],
                _cpuCurveTemps[1], _cpuCurveSpeeds[1],
                _cpuCurveTemps[2], _cpuCurveSpeeds[2])
            {
                T4 = (byte)_cpuCurveTemps[3],
                D4 = _cpuCurveSpeeds[3]
            };
            FanInfo gpuInfo = new FanInfo(
                _gpuCurveTemps[0], _gpuCurveSpeeds[0],
                _gpuCurveTemps[1], _gpuCurveSpeeds[1],
                _gpuCurveTemps[2], _gpuCurveSpeeds[2])
            {
                T4 = (byte)_gpuCurveTemps[3],
                D4 = _gpuCurveSpeeds[3]
            };

            Fan.SetCustomFanTable(cpuInfo, gpuInfo);
            Fan.SetMode(Fan.Mode.Custom);
            SetActiveModeUI(Fan.Mode.Custom);
            OsdOverlay.ShowFan(Fan.Mode.Custom);
            ShowCurveStatus(LocalizationManager.T("FanCurveAppliedStatus"), _colorBlue, autoReset: true);
        }

        private void ShowCurveStatus(string message, Brush brush, bool autoReset = true)
        {
            CurveStatusMessage.Text = message;
            CurveStatusMessage.Foreground = brush;

            _curveStatusResetTimer.Stop();
            if (autoReset)
            {
                _curveStatusResetTimer.Start();
            }
        }
    }
}
