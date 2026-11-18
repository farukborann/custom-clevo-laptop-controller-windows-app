using BSCustomClevoController.Utility;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Views
{
    public partial class Page_KeyboardSettings : Page
    {
        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";
        private const string SoftwareEffectsValueName = "SoftwareEffects";
        private const int DefaultSleepTimeoutSeconds = 30;

        private readonly DispatcherTimer _colorThrottleTimer;
        private readonly DispatcherTimer _saveSettingsTimer;
        private Color? _pendingColor;
        private int _pendingZoneIndex = -1;
        private byte _pendingBrightness = 255;
        private bool _isInitializing;
        private bool _isUpdatingEffects = false;
        private bool _isUpdatingColorUi = false;
        private bool _isDraggingSv = false;
        private bool _isDraggingHue = false;
        private bool _hasLoadedOnce = false;
        private bool? _lastAcStateInUI = null;
        private Color _selectedColor = Color.FromRgb(0, 120, 215);
        private double _currentHue = 206.0;
        private double _currentSat = 1.0;
        private double _currentVal = 0.84;
        private int _selectedZoneIndex = -1;
        private readonly KeyboardHardwareCapabilities _caps;

        public Page_KeyboardSettings()
        {
            _isInitializing = true;
            InitializeComponent();

            _colorThrottleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(35)
            };
            _colorThrottleTimer.Tick += ColorThrottleTimer_Tick;

            _saveSettingsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _saveSettingsTimer.Tick += SaveSettingsTimer_Tick;

            KeyboardStatus status = KeyboardBackLight.GetStatus();
            _caps = KeyboardBackLight.GetCapabilities();

            InitializeCapabilityControls(status);
            InitializeEffectsControls(status);
            InitializeLightingAndSleepControls(status);

            _isInitializing = false;

            if (ModeStatus.IsChecked.GetValueOrDefault())
            {
                if (status.Effect == null || status.Effect.IsSoftware)
                {
                    SetSleepTimerPaused(true);
                }

                if (EffectsListBox.SelectedItem is KeyboardEffect activeEff && activeEff.IsSoftware && !KeyboardEffect.IsAnySoftwareEffectRunning() && !KeyboardEffects.IsRunningOnBattery)
                {
                    ApplyCurrentEffect();
                }
            }
            UpdateColorControlsState();
            UpdateBootEffectControlState();
            AttachLoadedHandlers();
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_caps != null)
            {
                if (KeyboardColorModeBadgeText != null)
                {
                    KeyboardColorModeBadgeText.Text = _caps.KeyboardTypeName;
                }

                if (ActiveZoneLabelText != null)
                {
                    if (!_caps.SupportsRgb)
                    {
                        ActiveZoneLabelText.Text = LocalizationManager.T("KbColorModeMono");
                    }
                    else if (_caps.SupportsMultiZone)
                    {
                        UpdateZoneButtonsUI();
                    }
                    else
                    {
                        ActiveZoneLabelText.Text = LocalizationManager.T("KbColorModeSingleLine");
                    }
                }
            }

            EffectsListBox?.Items.Refresh();
            UpdateBootEffectControlState();
            UpdateColorControlsState();
        }

        private static byte StepToBrightnessValue(int step)
        {
            switch (step)
            {
                case 1: return 64;
                case 2: return 128;
                case 3: return 192;
                default: return 255;
            }
        }

        private static byte ScaleBrightnessToPercent(byte brightness)
        {
            return (byte)Math.Round(brightness / 255.0 * 100.0);
        }

        private static int SpeedToDurationMs(int speed)
        {
            if (speed == 1) return 5000;
            if (speed == 3) return 1000;
            return 2500;
        }

        private System.Drawing.Color GetSelectedDrawingColor()
        {
            return System.Drawing.Color.FromArgb(_selectedColor.R, _selectedColor.G, _selectedColor.B);
        }

        private static void ConfigureCapabilityRow(CheckBox checkBox, FrameworkElement row, UIElement badge, bool supported)
        {
            checkBox.IsEnabled = supported;
            row.Opacity = supported ? 1.0 : 0.55;
            badge.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        }

        private void InitializeCapabilityControls(KeyboardStatus status)
        {
            if (KeyboardColorModeBadgeText != null)
            {
                KeyboardColorModeBadgeText.Text = _caps.KeyboardTypeName;
            }

            if (!_caps.SupportsRgb)
            {
                ApplyMonochromeColorCapabilities();
            }
            else
            {
                ApplyRgbColorCapabilities();
            }

            bool lightBarSupported = _caps.SupportsLightBar || IteHidController.IsLightBarPresent || Capabilities.IsSupported("SupportLightbar");
            ConfigureCapabilityRow(LightBarCheckBox, LightBarRow, LightBarBadge, lightBarSupported);
            if (lightBarSupported)
            {
                LightBarCheckBox.IsChecked = status.LightState;
            }

            ConfigureCapabilityRow(LogoCheckBox, LogoRow, LogoBadge, Capabilities.IsSupported("SupportLogoColor"));
            ConfigureCapabilityRow(TouchpadLedCheckBox, TouchpadLedRow, TouchpadLedBadge, Capabilities.IsSupported("SupportTpColor"));

            bool bootOverrideSupported = _caps.SupportsBootEffectOverride;
            ConfigureCapabilityRow(BootEffectCheckBox, BootEffectRow, BootEffectBadge, bootOverrideSupported);
            if (bootOverrideSupported)
            {
                BootEffectCheckBox.IsChecked = status.BootEffect;
            }
        }

        private void ApplyMonochromeColorCapabilities()
        {
            QuickColorPresetsCard.IsEnabled = false;
            ColorCanvasCard.IsEnabled = false;
            QuickColorPresetsCard.Opacity = 0.5;
            ColorCanvasCard.Opacity = 0.5;
            MonochromeColorBadge.Visibility = Visibility.Visible;
            ZoneSelectorCard.Visibility = Visibility.Collapsed;
            _selectedZoneIndex = -1;
            if (KeyboardColorModeBadge != null)
            {
                KeyboardColorModeBadge.Background = new SolidColorBrush(Color.FromRgb(28, 28, 36));
                KeyboardColorModeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                if (KeyboardColorModeBadgeText != null)
                {
                    KeyboardColorModeBadgeText.Foreground = (Brush)FindResource("Amoled.TextMuted");
                }
            }
            if (ActiveZoneLabelText != null)
            {
                ActiveZoneLabelText.Text = LocalizationManager.T("KbColorModeMono");
            }
        }

        private void ApplyRgbColorCapabilities()
        {
            QuickColorPresetsCard.IsEnabled = true;
            ColorCanvasCard.IsEnabled = true;
            MonochromeColorBadge.Visibility = Visibility.Collapsed;

            if (_caps.SupportsMultiZone)
            {
                ZoneSelectorCard.Visibility = Visibility.Visible;
                _selectedZoneIndex = KeyboardBackLight.IsPerZoneColorActive() ? 0 : -1;
                RefreshZoneIndicatorDots();
                UpdateZoneButtonsUI();
                return;
            }

            ZoneSelectorCard.Visibility = Visibility.Collapsed;
            _selectedZoneIndex = -1;
            if (ActiveZoneLabelText != null)
            {
                ActiveZoneLabelText.Text = LocalizationManager.T("KbColorModeSingleLine");
            }
        }

        private void InitializeEffectsControls(KeyboardStatus status)
        {
            object regSpeed = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbAnimationSpeed", null);
            int savedSpeed = (regSpeed is int sp && sp >= 1 && sp <= 3) ? sp : 2;
            KeyboardEffect.SetCurrentAnimationSpeed(savedSpeed);
            UpdateSpeedButtonsUI(savedSpeed);

            bool isBattery = KeyboardEffects.IsRunningOnBattery;
            object regSoftware = Microsoft.Win32.Registry.GetValue(AppRegistryKey, SoftwareEffectsValueName, null);
            bool allowSoftware = regSoftware is int intSoft && intSoft == 1;
            if (status.Effect != null && status.Effect.IsSoftware && !isBattery)
            {
                allowSoftware = true;
                try
                {
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, SoftwareEffectsValueName, 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Page_KeyboardSettings.InitSoftwareEffects", ex);
                }
            }

            SoftwareEffectsSwitch.IsChecked = allowSoftware && !isBattery;
            SoftwareEffectsSwitch.IsEnabled = !isBattery;
            if (SoftwareBatteryBadge != null)
            {
                SoftwareBatteryBadge.Visibility = isBattery ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateAvailableEffects();
        }

        private void InitializeLightingAndSleepControls(KeyboardStatus status)
        {
            LedStatusCheckBox.IsChecked = status.LightState;

            int initialStep = KeyboardBackLight.BrightnessToStep(status.BackLight.A, true);
            if (initialStep < 1) initialStep = 4;
            _pendingBrightness = StepToBrightnessValue(initialStep);
            KeyboardEffects.SetMaxBrightness(_pendingBrightness);
            KeyboardBrightnessSlider.Value = initialStep;
            BrightnessPercentText.Text = $"{initialStep * 25}%";

            Color initialColor = (_caps.SupportsMultiZone && _selectedZoneIndex >= 0)
                ? ToMediaColor(KeyboardBackLight.GetZoneColour(_selectedZoneIndex))
                : Color.FromRgb(status.BackLight.R, status.BackLight.G, status.BackLight.B);
            SetSelectedColor(initialColor, updateHsv: true, triggerHardware: false);

            if (status.Effect != null && EffectsListBox.ItemsSource is KeyboardEffect[] available)
            {
                bool matched = false;
                for (int i = 0; i < available.Length; i++)
                {
                    if (available[i].UniqId == status.Effect.UniqId && available[i].Id == status.Effect.Id)
                    {
                        EffectsListBox.SelectedIndex = i;
                        matched = true;
                        break;
                    }
                }
                ModeStatus.IsChecked = matched;
            }

            BootEffectCheckBox.IsChecked = status.BootEffect;

            int effectiveSleep = ResolveInitialSleepSeconds(status.SleepSecond);
            KeyboarSleepTimerHours.Text = Convert.ToString(effectiveSleep / 3600);
            KeyboarSleepTimerMunites.Text = Convert.ToString((effectiveSleep % 3600) / 60);
            KeyboarSleepTimerSeconds.Text = Convert.ToString(effectiveSleep % 60);

            KeyboarSleepTimerHours.TextChanged += KeyboarSleepTimer_TextChanged;
            KeyboarSleepTimerMunites.TextChanged += KeyboarSleepTimer_TextChanged;
            KeyboarSleepTimerSeconds.TextChanged += KeyboarSleepTimer_TextChanged;

            KeyboarSleepTimerHours.KeyDown += KeyboarSleepTimer_KeyDown;
            KeyboarSleepTimerMunites.KeyDown += KeyboarSleepTimer_KeyDown;
            KeyboarSleepTimerSeconds.KeyDown += KeyboarSleepTimer_KeyDown;
            KeyboarSleepTimerHours.LostFocus += KeyboarSleepTimer_LostFocus;
            KeyboarSleepTimerMunites.LostFocus += KeyboarSleepTimer_LostFocus;
            KeyboarSleepTimerSeconds.LostFocus += KeyboarSleepTimer_LostFocus;
        }

        private static int ResolveInitialSleepSeconds(int statusSleepSeconds)
        {
            if (statusSleepSeconds > 0)
            {
                return statusSleepSeconds;
            }

            object regUserSleep = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbUserSleepTimeout", null);
            if (regUserSleep is int us && us > 0)
            {
                return us;
            }

            object regSleep = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbSleepTimer", null);
            if (regSleep is int s && s > 0)
            {
                return s;
            }

            return DefaultSleepTimeoutSeconds;
        }

        private void AttachLoadedHandlers()
        {
            Loaded += (s, e) =>
            {
                PositionPickerThumbs();

                bool isAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                if (!_hasLoadedOnce)
                {
                    _hasLoadedOnce = true;
                    _lastAcStateInUI = isAc;
                    SyncPowerBadgeUI(isAc);
                }
                else if (!_lastAcStateInUI.HasValue || _lastAcStateInUI.Value != isAc)
                {
                    _lastAcStateInUI = isAc;
                    UpdateBatterySoftwareAnimationState(isAc);
                }
            };
        }

        private void ColorThrottleTimer_Tick(object sender, EventArgs e)
        {
            _colorThrottleTimer.Stop();
            if (_pendingColor.HasValue)
            {
                Color c = _pendingColor.Value;
                ApplyColorRealtime(_pendingZoneIndex, c);
                KeyboardBackLight.SetBrightnessRealtime(_pendingBrightness);

                if (LightBarCheckBox != null && LightBarCheckBox.IsChecked.GetValueOrDefault())
                {
                    KeyboardBackLight.SetLightBarColor(c.R, c.G, c.B, ScaleBrightnessToPercent(_pendingBrightness));
                }

                _saveSettingsTimer.Stop();
                _saveSettingsTimer.Start();
            }
        }

        private void SaveSettingsTimer_Tick(object sender, EventArgs e)
        {
            _saveSettingsTimer.Stop();
            if (_pendingColor.HasValue)
            {
                Color c = _pendingColor.Value;
                int zone = _pendingZoneIndex;
                _pendingColor = null;
                PersistColor(zone, c);
            }
            KeyboardBackLight.SetBrightness(_pendingBrightness);
        }

        private void ApplyColorRealtime(int zoneIndex, Color c)
        {
            if (_caps.SupportsMultiZone && zoneIndex >= 0 && zoneIndex <= 2)
            {
                KeyboardBackLight.SetZoneColourRealtime(zoneIndex, c.R, c.G, c.B);
            }
            else
            {
                KeyboardBackLight.SetColourRealtime(c.R, c.G, c.B);
            }
        }

        private void PersistColor(int zoneIndex, Color c)
        {
            if (_caps.SupportsMultiZone && zoneIndex >= 0 && zoneIndex <= 2)
            {
                KeyboardBackLight.SetZoneColour(zoneIndex, c.R, c.G, c.B);
            }
            else
            {
                KeyboardBackLight.SetColour(c.R, c.G, c.B);
            }
        }

        private void ApplySelectedColorToHardware()
        {
            if (!_caps.SupportsRgb) return;

            if (_caps.SupportsMultiZone && KeyboardBackLight.IsPerZoneColorActive())
            {
                for (int z = 0; z < 3; z++)
                {
                    System.Drawing.Color zc = KeyboardBackLight.GetZoneColour(z);
                    KeyboardBackLight.SetZoneColour(z, zc.R, zc.G, zc.B);
                }
            }
            else
            {
                PersistColor(_selectedZoneIndex, _selectedColor);
            }
        }

        private void OnUserColorChanged(Color chosen)
        {
            _pendingColor = Color.FromRgb(chosen.R, chosen.G, chosen.B);
            _pendingZoneIndex = _caps.SupportsMultiZone ? _selectedZoneIndex : -1;

            if (ModeStatus.IsChecked.GetValueOrDefault())
            {
                ModeStatus.IsChecked = false;
            }

            LedStatusCheckBox.IsChecked = true;

            if (!_colorThrottleTimer.IsEnabled)
            {
                _colorThrottleTimer.Start();
            }
        }

        private void KeyboardBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessPercentText == null) return;

            int step = Math.Max(1, Math.Min(4, (int)Math.Round(e.NewValue)));
            byte val = StepToBrightnessValue(step);
            _pendingBrightness = val;
            KeyboardEffects.SetMaxBrightness(val);
            BrightnessPercentText.Text = $"{step * 25}%";

            if (_isInitializing) return;

            bool isModeActive = ModeStatus.IsChecked.GetValueOrDefault();
            KeyboardEffect effect = EffectsListBox.SelectedItem as KeyboardEffect;
            bool isSoftwareBreathRunning = isModeActive && effect != null && effect.IsBreathingSoftwareEffect;

            if (!isSoftwareBreathRunning)
            {
                KeyboardBackLight.SetBrightnessRealtime(val);
            }

            if (isModeActive && effect != null && effect.IsIteHardwareEffect && IteHidController.IsDevicePresent)
            {
                IteHidController.SetHardwareAnimation(
                    effect.IteAnimMode,
                    KeyboardEffect.GetIteSpeedByte(),
                    IteHidController.ScaleBrightness(val),
                    effect.IteBehaviour);
            }

            if (LightBarCheckBox != null && LightBarCheckBox.IsChecked.GetValueOrDefault())
            {
                KeyboardBackLight.SetLightBarColor(_selectedColor.R, _selectedColor.G, _selectedColor.B, ScaleBrightnessToPercent(val));
            }

            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                try
                {
                    Color col = (Color)ColorConverter.ConvertFromString(hex);

                    if (ModeStatus.IsChecked.GetValueOrDefault())
                    {
                        ModeStatus.IsChecked = false;
                    }

                    LedStatusCheckBox.IsChecked = true;

                    _pendingColor = null;
                    int targetZone = _caps.SupportsMultiZone ? _selectedZoneIndex : -1;
                    SetSelectedColor(col, updateHsv: true, triggerHardware: false);

                    PersistColor(targetZone, col);
                    KeyboardBackLight.SetBrightness(_pendingBrightness);

                    if (LightBarCheckBox != null && LightBarCheckBox.IsChecked.GetValueOrDefault())
                    {
                        KeyboardBackLight.SetLightBarColor(col.R, col.G, col.B, ScaleBrightnessToPercent(_pendingBrightness));
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Page_KeyboardSettings.PresetColor", ex);
                }
            }
        }

        private void UpdateAvailableEffects()
        {
            _isUpdatingEffects = true;
            try
            {
                bool isBattery = KeyboardEffects.IsRunningOnBattery;
                bool allowSoftware = SoftwareEffectsSwitch != null && SoftwareEffectsSwitch.IsChecked.GetValueOrDefault() && !isBattery;
                KeyboardEffect[] availableEffects = KeyboardEffects.GetAvailableEffects(_caps, allowSoftware);
                EffectsListBox.ItemsSource = availableEffects;

                bool hasEffects = availableEffects != null && availableEffects.Length > 0;
                if (NoEffectsMessage != null)
                {
                    NoEffectsMessage.SetResourceReference(TextBlock.TextProperty, isBattery ? "Loc.KbNoEffectsBattery" : "Loc.KbNoEffectsTurnOn");
                    NoEffectsMessage.Visibility = hasEffects ? Visibility.Collapsed : Visibility.Visible;
                }

                if (AnimationSpeedRow != null)
                {
                    AnimationSpeedRow.IsEnabled = hasEffects;
                    AnimationSpeedRow.Opacity = hasEffects ? 1.0 : 0.55;
                }

                if (hasEffects && EffectsListBox.SelectedIndex < 0)
                {
                    EffectsListBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _isUpdatingEffects = false;
            }
        }

        private void SoftwareEffectsSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (KeyboardEffects.IsRunningOnBattery)
            {
                SoftwareEffectsSwitch.IsChecked = false;
                UpdateBatterySoftwareAnimationState(false);
                return;
            }

            bool allowSoftware = SoftwareEffectsSwitch.IsChecked.GetValueOrDefault();

            try
            {
                Microsoft.Win32.Registry.SetValue(AppRegistryKey, SoftwareEffectsValueName, allowSoftware ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_KeyboardSettings.SoftwareEffectsSwitch", ex);
            }

            if (!allowSoftware && EffectsListBox.SelectedItem is KeyboardEffect active && active.IsSoftware)
            {
                ModeStatus.IsChecked = false;
                SaveActiveEffect(null, false);
            }

            UpdateAvailableEffects();

            if (allowSoftware)
            {
                if (!_caps.SupportsHardwareEffects && !ModeStatus.IsChecked.GetValueOrDefault())
                {
                    ModeStatus.IsChecked = true;
                }
                else if (ModeStatus.IsChecked.GetValueOrDefault() && EffectsListBox.SelectedItem is KeyboardEffect selectedEffect)
                {
                    ApplyCurrentEffect();
                    SaveActiveEffect(selectedEffect, true);
                }
            }

            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void EffectsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingEffects) return;

            if (EffectsListBox.SelectedItem is KeyboardEffect effect)
            {
                if (effect.IsSoftware && KeyboardEffects.IsRunningOnBattery)
                {
                    KeyboardEffect.StopAllSoftwareEffects();
                    SetSleepTimerPaused(false);
                    UpdateColorControlsState();
                    UpdateBootEffectControlState();
                    return;
                }

                if (!ModeStatus.IsChecked.GetValueOrDefault())
                {
                    ModeStatus.IsChecked = true;
                }
                else
                {
                    ApplyCurrentEffect();
                }
                SaveActiveEffect(effect, ModeStatus.IsChecked.GetValueOrDefault());
            }

            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void ModeStatus_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingEffects) return;

            LedStatusCheckBox.IsChecked = true;

            if (EffectsListBox.SelectedItem is KeyboardEffect effect && effect.IsSoftware && KeyboardEffects.IsRunningOnBattery)
            {
                UpdateColorControlsState();
                UpdateBootEffectControlState();
                return;
            }

            if (!_caps.SupportsHardwareEffects && !SoftwareEffectsSwitch.IsChecked.GetValueOrDefault() && !KeyboardEffects.IsRunningOnBattery)
            {
                SoftwareEffectsSwitch.IsChecked = true;
                try
                {
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, SoftwareEffectsValueName, 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Page_KeyboardSettings.ModeStatus_Checked", ex);
                }
                UpdateAvailableEffects();
            }

            if (EffectsListBox.SelectedIndex < 0)
            {
                EffectsListBox.SelectedIndex = 0;
            }
            else
            {
                ApplyCurrentEffect();
            }

            if (EffectsListBox.SelectedItem is KeyboardEffect selectedEffect)
            {
                SaveActiveEffect(selectedEffect, true);
            }
            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void ModeStatus_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingEffects) return;

            KeyboardEffect.StopAllSoftwareEffects();
            var status = KeyboardBackLight.GetStatus();
            if (status.Effect?.Timer != null)
            {
                status.Effect.Timer.Stop();
            }

            SetSleepTimerPaused(false);
            if (!KeyboardEffects.IsRunningOnBattery)
            {
                SaveActiveEffect(null, false);
            }

            ApplySelectedColorToHardware();
            KeyboardBackLight.SetBrightnessRealtime(_pendingBrightness);

            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void LedStatusCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            KeyboardBackLight.TurnOn();
            KeyboardBackLight.SetBrightness(_pendingBrightness);

            if (ModeStatus.IsChecked.GetValueOrDefault())
            {
                ApplyCurrentEffect();
            }
            else if (_caps.SupportsRgb)
            {
                ApplySelectedColorToHardware();
                KeyboardBackLight.SetBrightnessRealtime(_pendingBrightness);
            }

            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void LedStatusCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            KeyboardEffect.StopAllSoftwareEffects();
            SetSleepTimerPaused(false);
            KeyboardBackLight.TurnOff();

            UpdateColorControlsState();
            UpdateBootEffectControlState();
        }

        private void ApplyCurrentEffect()
        {
            if (!(EffectsListBox.SelectedItem is KeyboardEffect effect))
            {
                return;
            }

            if (effect.IsSoftware && KeyboardEffects.IsRunningOnBattery)
            {
                return;
            }

            int speed = KeyboardEffect.CurrentAnimationSpeed;
            KeyboardEffects.SetDuration(SpeedToDurationMs(speed));

            KeyboardEffects.SetMaxBrightness(_pendingBrightness);

            if (effect.Id == 8 && _caps.SupportsRgb)
            {
                if (_caps.SupportsMultiZone && KeyboardBackLight.IsPerZoneColorActive())
                {
                    KeyboardBackLight.ApplyCurrentStaticColorsRealtime();
                }
                else
                {
                    KeyboardBackLight.SetColourRealtime(_selectedColor.R, _selectedColor.G, _selectedColor.B);
                }
            }

            bool isSoftwareBreath = effect.IsBreathingSoftwareEffect;
            KeyboardBackLight.TurnOn(isSoftwareBreath ? (byte)0 : _pendingBrightness);

            SetSleepTimerPaused(true);
            effect.SetEffect();
            UpdateColorControlsState();
        }

        private void LightBarCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            byte lbBri = ScaleBrightnessToPercent(_pendingBrightness);
            if (_caps.SupportsRgb)
            {
                KeyboardBackLight.SetLightBarColor(_selectedColor.R, _selectedColor.G, _selectedColor.B, lbBri);
            }
            else
            {
                KeyboardBackLight.SetLightBarColor(59, 130, 246, lbBri);
            }
        }

        private void LightBarCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.SetLightBarOff();
        }

        private void BootEffectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.OverrideBootEffect(true);
        }

        private void BootEffectCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.OverrideBootEffect(false);
        }

        private void SpeedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int speed))
            {
                ApplyAnimationSpeed(speed);
            }
        }

        private void ApplyAnimationSpeed(int speed)
        {
            KeyboardEffect.SetCurrentAnimationSpeed(speed);
            UpdateSpeedButtonsUI(speed);

            try
            {
                Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbAnimationSpeed", speed, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_KeyboardSettings.ApplyAnimationSpeed", ex);
            }

            Capabilities.SetKeyboardAnimationSpeed(speed);

            if (EffectsListBox?.SelectedItem is KeyboardEffect effect)
            {
                if (effect.IsIteHardwareEffect && IteHidController.IsDevicePresent)
                {
                    byte iteSpeed = KeyboardEffect.GetIteSpeedByte();
                    IteHidController.SetHardwareAnimation(effect.IteAnimMode, iteSpeed, IteHidController.ScaleBrightness(_pendingBrightness), effect.IteBehaviour);
                }
                else if (effect.IsSoftware)
                {
                    KeyboardEffects.SetDuration(SpeedToDurationMs(speed));
                }
            }
        }

        private void UpdateSpeedButtonsUI(int activeSpeed)
        {
            if (SpeedSlowBtn == null || SpeedNormalBtn == null || SpeedFastBtn == null) return;
            SpeedSlowBtn.Style = activeSpeed == 1 ? (Style)FindResource("AmoledAccentButtonStyle") : (Style)FindResource("AmoledButtonStyle");
            SpeedNormalBtn.Style = activeSpeed == 2 ? (Style)FindResource("AmoledAccentButtonStyle") : (Style)FindResource("AmoledButtonStyle");
            SpeedFastBtn.Style = activeSpeed == 3 ? (Style)FindResource("AmoledAccentButtonStyle") : (Style)FindResource("AmoledButtonStyle");
        }

        private void UpdateBootEffectControlState()
        {
            bool bootOverrideSupported = _caps != null && _caps.SupportsBootEffectOverride;
            if (!bootOverrideSupported)
            {
                if (BootEffectCheckBox != null)
                {
                    BootEffectCheckBox.IsEnabled = false;
                    BootEffectCheckBox.ToolTip = LocalizationManager.T("CommonNotSupported");
                }
                if (BootEffectRow != null) BootEffectRow.Opacity = 0.55;
                if (BootEffectBadge != null) BootEffectBadge.Visibility = Visibility.Visible;
                return;
            }

            if (BootEffectBadge != null) BootEffectBadge.Visibility = Visibility.Collapsed;

            if (BootEffectCheckBox != null)
            {
                BootEffectCheckBox.IsEnabled = true;
                BootEffectCheckBox.ToolTip = null;
            }
            if (BootEffectRow != null) BootEffectRow.Opacity = 1.0;
        }

        private static void SaveActiveEffect(KeyboardEffect effect, bool active)
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbEffectActive", active ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                if (effect != null)
                {
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbEffectId", effect.Id, Microsoft.Win32.RegistryValueKind.DWord);
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbEffectUniqId", (int)effect.UniqId, Microsoft.Win32.RegistryValueKind.DWord);
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbEffectName", effect.Name ?? string.Empty);
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "KbEffectIsSoftware", effect.IsSoftware ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_KeyboardSettings.SaveActiveEffect", ex);
            }
        }

        private void LogoCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.SetLogoLed(true, GetSelectedDrawingColor());
        }

        private void LogoCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.SetLogoLed(false, GetSelectedDrawingColor());
        }

        private void TouchpadLedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.SetTouchpadLed(true, GetSelectedDrawingColor());
        }

        private void TouchpadLedCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            KeyboardBackLight.SetTouchpadLed(false, GetSelectedDrawingColor());
        }

        private int GetConfiguredSleepSeconds()
        {
            int h = int.TryParse(this.KeyboarSleepTimerHours?.Text, out int parsedH) ? parsedH : 0;
            int m = int.TryParse(this.KeyboarSleepTimerMunites?.Text, out int parsedM) ? parsedM : 0;
            int s = int.TryParse(this.KeyboarSleepTimerSeconds?.Text, out int parsedS) ? parsedS : 0;
            return (h * 3600) + (m * 60) + s;
        }

        private void ApplyConfiguredSleepTimer()
        {
            int totalSeconds = GetConfiguredSleepSeconds();
            if (totalSeconds > 0)
            {
                KeyboardBackLight.SetSleepTimer(totalSeconds);
            }
            else
            {
                KeyboardBackLight.TurnSleepTimerOff(updateRegistry: true);
            }
        }

        private void KeyboarSleepTimer_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || ModeStatus.IsChecked.GetValueOrDefault()) return;

            if (sender is TextBox tb && int.TryParse(tb.Text, out int val) && val > 59)
            {
                tb.Text = "59";
            }

            ApplyConfiguredSleepTimer();
        }

        private void SetSleepTimerPaused(bool paused)
        {
            if (KeyboarSleepTimerHours == null || KeyboarSleepTimerMunites == null || KeyboarSleepTimerSeconds == null)
            {
                return;
            }

            KeyboarSleepTimerHours.IsEnabled = !paused;
            KeyboarSleepTimerMunites.IsEnabled = !paused;
            KeyboarSleepTimerSeconds.IsEnabled = !paused;

            if (paused)
            {
                KeyboardBackLight.TurnSleepTimerOff(updateRegistry: false);
            }
            else
            {
                ApplyConfiguredSleepTimer();
            }
        }

        private void SyncPowerBadgeUI(bool isAc)
        {
            if (this.SoftwareBatteryBadge != null)
            {
                this.SoftwareBatteryBadge.Visibility = isAc ? Visibility.Collapsed : Visibility.Visible;
            }
            if (this.SoftwareEffectsSwitch != null)
            {
                this.SoftwareEffectsSwitch.IsEnabled = isAc;
                if (!isAc)
                {
                    this.SoftwareEffectsSwitch.IsChecked = false;
                }
            }
        }

        public void UpdateBatterySoftwareAnimationState(bool isAcOnline)
        {
            Dispatcher.Invoke(() =>
            {
                _isUpdatingEffects = true;
                try
                {
                    _lastAcStateInUI = isAcOnline;

                    if (!isAcOnline)
                    {
                        ApplyBatteryPowerAnimationState();
                    }
                    else
                    {
                        ApplyAcPowerAnimationState();
                    }

                    UpdateColorControlsState();
                    UpdateBootEffectControlState();
                }
                finally
                {
                    _isUpdatingEffects = false;
                }
            });
        }

        private void ApplyBatteryPowerAnimationState()
        {
            if (SoftwareBatteryBadge != null) SoftwareBatteryBadge.Visibility = Visibility.Visible;
            SoftwareAnimationSubtitle?.SetResourceReference(TextBlock.TextProperty, "Loc.KbSoftwareAnimationsBatterySubtitle");
            if (SoftwareEffectsSwitch != null)
            {
                SoftwareEffectsSwitch.IsEnabled = false;
                SoftwareEffectsSwitch.IsChecked = false;
            }

            if (EffectsListBox?.SelectedItem is KeyboardEffect effect && effect.IsSoftware)
            {
                KeyboardEffect.StopAllSoftwareEffects();
                KeyboardBackLight.SetBrightnessRealtime(_pendingBrightness);
                ApplySelectedColorToHardware();
                SetSleepTimerPaused(false);
            }

            UpdateAvailableEffects();
        }

        private void ApplyAcPowerAnimationState()
        {
            if (SoftwareBatteryBadge != null) SoftwareBatteryBadge.Visibility = Visibility.Collapsed;
            SoftwareAnimationSubtitle?.SetResourceReference(TextBlock.TextProperty, "Loc.KbSoftwareAnimationsSubtitle");
            if (SoftwareEffectsSwitch != null)
            {
                SoftwareEffectsSwitch.IsEnabled = true;
                object reg = Microsoft.Win32.Registry.GetValue(AppRegistryKey, SoftwareEffectsValueName, 1);
                SoftwareEffectsSwitch.IsChecked = reg == null || Convert.ToInt32(reg) != 0;
            }

            UpdateAvailableEffects();
            RestoreSelectedEffectForAc();
        }

        private void RestoreSelectedEffectForAc()
        {
            object regId = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectId", null);
            if (regId is int idVal && this.EffectsListBox.ItemsSource is KeyboardEffect[] available)
            {
                for (int i = 0; i < available.Length; i++)
                {
                    if (available[i].Id == idVal)
                    {
                        this.EffectsListBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            object regActive = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectActive", null);
            if (regActive is int a && a == 1)
            {
                this.ModeStatus.IsChecked = true;
            }
        }

        private void UpdateColorControlsState()
        {
            if (_caps != null && !_caps.SupportsRgb)
            {
                SetColorControlsEnabled(false, LocalizationManager.T("KbColorDisabledMono"));
                return;
            }

            if (LedStatusCheckBox == null || !LedStatusCheckBox.IsChecked.GetValueOrDefault())
            {
                SetColorControlsEnabled(false, LocalizationManager.T("KbColorDisabledOff"));
                return;
            }

            bool isModeActive = ModeStatus != null && ModeStatus.IsChecked.GetValueOrDefault();
            KeyboardEffect effect = EffectsListBox?.SelectedItem as KeyboardEffect;

            bool isSelfColoringEffect = isModeActive && effect != null &&
                (effect.Id == 9 ||
                 effect.Id == 10 ||
                 effect.Id == 3 ||
                 effect.Id == 1 ||
                 (!string.IsNullOrEmpty(effect.Name) &&
                  (effect.Name.IndexOf("Cycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   effect.Name.IndexOf("Colorful", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   effect.Name.IndexOf("Rainbow", StringComparison.OrdinalIgnoreCase) >= 0)));

            if (isSelfColoringEffect)
            {
                SetColorControlsEnabled(false, LocalizationManager.Format("KbColorDisabledEffectFmt", effect.LocalizedName));
            }
            else
            {
                SetColorControlsEnabled(true, null);
            }
        }

        private void SetColorControlsEnabled(bool isEnabled, string tooltipReason)
        {
            if (this.QuickColorPresetsCard != null)
            {
                this.QuickColorPresetsCard.IsEnabled = isEnabled;
                this.QuickColorPresetsCard.Opacity = isEnabled ? 1.0 : 0.45;
                this.QuickColorPresetsCard.ToolTip = tooltipReason;
            }

            if (this.ColorCanvasCard != null)
            {
                this.ColorCanvasCard.IsEnabled = isEnabled;
                this.ColorCanvasCard.Opacity = isEnabled ? 1.0 : 0.45;
                this.ColorCanvasCard.ToolTip = tooltipReason;
            }
        }

        private void KeyboarSleepTimer_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                (Window.GetWindow(sender as DependencyObject ?? this) ?? Application.Current.MainWindow)?.Focus();
                e.Handled = true;
            }
        }

        private void KeyboarSleepTimer_LostFocus(object sender, RoutedEventArgs e)
        {
            KeyboarSleepTimer_TextChanged(sender, null);
        }

        internal void SyncFromHardwareHotkey()
        {
            bool prevInit = _isInitializing;
            _isInitializing = true;
            try
            {
                KeyboardStatus status = KeyboardBackLight.GetStatus();
                LedStatusCheckBox.IsChecked = status.LightState;
                if (status.LightState && status.BackLight.A > 0)
                {
                    int step = KeyboardBackLight.BrightnessToStep(status.BackLight.A, status.LightState);
                    if (step < 1) step = 1;
                    _pendingBrightness = StepToBrightnessValue(step);
                    KeyboardEffects.SetMaxBrightness(_pendingBrightness);
                    KeyboardBrightnessSlider.Value = step;
                    BrightnessPercentText.Text = $"{step * 25}%";
                }
                if (_caps.SupportsRgb)
                {
                    int targetZone = _selectedZoneIndex >= 0 ? _selectedZoneIndex : 0;
                    Color syncedColor = (_caps.SupportsMultiZone && KeyboardBackLight.IsPerZoneColorActive())
                        ? ToMediaColor(KeyboardBackLight.GetZoneColour(targetZone))
                        : Color.FromRgb(status.BackLight.R, status.BackLight.G, status.BackLight.B);
                    SetSelectedColor(syncedColor, updateHsv: true, triggerHardware: false);
                    RefreshZoneIndicatorDots();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Page_KeyboardSettings.SyncFromHardwareHotkey", ex);
            }
            finally
            {
                _isInitializing = prevInit;
            }
        }

        private static Color ToMediaColor(System.Drawing.Color c) => Color.FromRgb(c.R, c.G, c.B);

        private void ZoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_caps.SupportsRgb || !_caps.SupportsMultiZone) return;

            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int zone))
            {
                _selectedZoneIndex = zone;
                UpdateZoneButtonsUI();

                Color targetColor;
                if (zone >= 0)
                {
                    targetColor = ToMediaColor(KeyboardBackLight.GetZoneColour(zone));
                }
                else
                {
                    KeyboardStatus st = KeyboardBackLight.GetStatus();
                    targetColor = Color.FromRgb(st.BackLight.R, st.BackLight.G, st.BackLight.B);
                }

                SetSelectedColor(targetColor, updateHsv: true, triggerHardware: false);
            }
        }

        private void UpdateZoneButtonsUI()
        {
            if (ZoneAllBtn == null || ZoneLeftBtn == null || ZoneCenterBtn == null || ZoneRightBtn == null)
            {
                return;
            }

            Style inactive = (Style)FindResource("OpenSenseSegmentedPillButtonStyle");
            Style active = (Style)FindResource("OpenSenseSegmentedPillActiveStyle");

            ZoneAllBtn.Style = _selectedZoneIndex == -1 ? active : inactive;
            ZoneLeftBtn.Style = _selectedZoneIndex == 0 ? active : inactive;
            ZoneCenterBtn.Style = _selectedZoneIndex == 1 ? active : inactive;
            ZoneRightBtn.Style = _selectedZoneIndex == 2 ? active : inactive;

            if (ActiveZoneLabelText != null)
            {
                switch (_selectedZoneIndex)
                {
                    case 0: ActiveZoneLabelText.Text = LocalizationManager.T("KbZone1Left"); break;
                    case 1: ActiveZoneLabelText.Text = LocalizationManager.T("KbZone2Center"); break;
                    case 2: ActiveZoneLabelText.Text = LocalizationManager.T("KbZone3Right"); break;
                    default: ActiveZoneLabelText.Text = LocalizationManager.T("KbZoneAllSync"); break;
                }
            }
        }

        private void RefreshZoneIndicatorDots()
        {
            if (!_caps.SupportsMultiZone) return;

            if (ZoneLeftDot != null)
            {
                ZoneLeftDot.Fill = new SolidColorBrush(ToMediaColor(KeyboardBackLight.GetZoneColour(0)));
            }
            if (ZoneCenterDot != null)
            {
                ZoneCenterDot.Fill = new SolidColorBrush(ToMediaColor(KeyboardBackLight.GetZoneColour(1)));
            }
            if (ZoneRightDot != null)
            {
                ZoneRightDot.Fill = new SolidColorBrush(ToMediaColor(KeyboardBackLight.GetZoneColour(2)));
            }
        }

        private void SetSelectedColor(Color c, bool updateHsv, bool triggerHardware, TextBox skipTextFieldUpdate = null)
        {
            _selectedColor = Color.FromRgb(c.R, c.G, c.B);
            if (updateHsv)
            {
                RgbToHsv(_selectedColor.R, _selectedColor.G, _selectedColor.B, out _currentHue, out _currentSat, out _currentVal);
            }

            UpdatePickerVisuals(skipTextFieldUpdate);

            if (triggerHardware && !_isInitializing)
            {
                OnUserColorChanged(_selectedColor);
            }
        }

        private void UpdatePickerVisuals(TextBox skipTextFieldUpdate = null)
        {
            _isUpdatingColorUi = true;
            try
            {
                Color pureHue = HsvToRgb(_currentHue, 1.0, 1.0);
                if (SvHueBackground != null)
                {
                    SvHueBackground.Background = new SolidColorBrush(pureHue);
                }

                SolidColorBrush selectedBrush = new SolidColorBrush(_selectedColor);
                if (SvPickerThumbFill != null)
                {
                    SvPickerThumbFill.Background = selectedBrush;
                }
                if (SelectedColorSwatch != null)
                {
                    SelectedColorSwatch.Background = selectedBrush;
                }

                UpdateColorInputTextFields(skipTextFieldUpdate);
                UpdateZoneIndicatorDots(selectedBrush);
                PositionPickerThumbs();
            }
            finally
            {
                _isUpdatingColorUi = false;
            }
        }

        private void UpdateColorInputTextFields(TextBox skipTextFieldUpdate)
        {
            if (HexColorInput != null && skipTextFieldUpdate != HexColorInput)
            {
                HexColorInput.Text = $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
            }
            if (RColorInput != null && skipTextFieldUpdate != RColorInput)
            {
                RColorInput.Text = _selectedColor.R.ToString(CultureInfo.InvariantCulture);
            }
            if (GColorInput != null && skipTextFieldUpdate != GColorInput)
            {
                GColorInput.Text = _selectedColor.G.ToString(CultureInfo.InvariantCulture);
            }
            if (BColorInput != null && skipTextFieldUpdate != BColorInput)
            {
                BColorInput.Text = _selectedColor.B.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void UpdateZoneIndicatorDots(SolidColorBrush selectedBrush)
        {
            if (!_caps.SupportsMultiZone) return;

            if (_selectedZoneIndex == -1)
            {
                if (ZoneLeftDot != null) ZoneLeftDot.Fill = selectedBrush;
                if (ZoneCenterDot != null) ZoneCenterDot.Fill = selectedBrush;
                if (ZoneRightDot != null) ZoneRightDot.Fill = selectedBrush;
                return;
            }

            if (_selectedZoneIndex == 0 && ZoneLeftDot != null)
            {
                ZoneLeftDot.Fill = selectedBrush;
            }
            else if (_selectedZoneIndex == 1 && ZoneCenterDot != null)
            {
                ZoneCenterDot.Fill = selectedBrush;
            }
            else if (_selectedZoneIndex == 2 && ZoneRightDot != null)
            {
                ZoneRightDot.Fill = selectedBrush;
            }
        }

        private void PositionPickerThumbs()
        {
            if (SvPickerHost != null && SvPickerThumb != null && SvPickerHost.ActualWidth > 0 && SvPickerHost.ActualHeight > 0)
            {
                double x = Clamp01(_currentSat) * SvPickerHost.ActualWidth - (SvPickerThumb.Width / 2.0);
                double y = (1.0 - Clamp01(_currentVal)) * SvPickerHost.ActualHeight - (SvPickerThumb.Height / 2.0);
                Canvas.SetLeft(SvPickerThumb, x);
                Canvas.SetTop(SvPickerThumb, y);
            }

            if (HueSliderHost != null && HueSliderThumb != null && HueSliderHost.ActualWidth > 0)
            {
                double maxLeft = Math.Max(0.0, HueSliderHost.ActualWidth - HueSliderThumb.Width);
                double hueRatio = Math.Max(0.0, Math.Min(1.0, _currentHue / 360.0));
                double left = Math.Max(0.0, Math.Min(maxLeft, hueRatio * HueSliderHost.ActualWidth - (HueSliderThumb.Width / 2.0)));
                Canvas.SetLeft(HueSliderThumb, left);
            }
        }

        private void SvPickerHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_caps.SupportsRgb) return;
            _isDraggingSv = true;
            SvPickerHost.CaptureMouse();
            UpdateSvFromPoint(e.GetPosition(SvPickerHost));
            e.Handled = true;
        }

        private void SvPickerHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSv && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateSvFromPoint(e.GetPosition(SvPickerHost));
            }
        }

        private void SvPickerHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSv)
            {
                _isDraggingSv = false;
                SvPickerHost.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void SvPickerHost_SizeChanged(object sender, SizeChangedEventArgs e) => PositionPickerThumbs();

        private void UpdateSvFromPoint(Point pt)
        {
            if (SvPickerHost == null || SvPickerHost.ActualWidth <= 0 || SvPickerHost.ActualHeight <= 0) return;

            _currentSat = Clamp01(pt.X / SvPickerHost.ActualWidth);
            _currentVal = Clamp01(1.0 - (pt.Y / SvPickerHost.ActualHeight));

            Color chosen = HsvToRgb(_currentHue, _currentSat, _currentVal);
            SetSelectedColor(chosen, updateHsv: false, triggerHardware: true);
        }

        private void HueSliderHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_caps.SupportsRgb) return;
            _isDraggingHue = true;
            HueSliderHost.CaptureMouse();
            UpdateHueFromPoint(e.GetPosition(HueSliderHost));
            e.Handled = true;
        }

        private void HueSliderHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingHue && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateHueFromPoint(e.GetPosition(HueSliderHost));
            }
        }

        private void HueSliderHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingHue)
            {
                _isDraggingHue = false;
                HueSliderHost.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void HueSliderHost_SizeChanged(object sender, SizeChangedEventArgs e) => PositionPickerThumbs();

        private void UpdateHueFromPoint(Point pt)
        {
            if (HueSliderHost == null || HueSliderHost.ActualWidth <= 0) return;

            double ratio = Clamp01(pt.X / HueSliderHost.ActualWidth);
            _currentHue = ratio * 359.99;

            Color chosen = HsvToRgb(_currentHue, _currentSat, _currentVal);
            SetSelectedColor(chosen, updateHsv: false, triggerHardware: true);
        }

        private void ColorInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                (Window.GetWindow(sender as DependencyObject ?? this) ?? Application.Current.MainWindow)?.Focus();
                e.Handled = true;
            }
        }

        private void HexColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingColorUi || !_caps.SupportsRgb) return;

            string raw = (HexColorInput.Text ?? string.Empty).Trim().TrimStart('#');
            if (raw.Length == 6 &&
                byte.TryParse(raw.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(raw.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(raw.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                SetSelectedColor(Color.FromRgb(r, g, b), updateHsv: true, triggerHardware: true, skipTextFieldUpdate: HexColorInput);
            }
        }

        private void ColorInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingColorUi) return;
            UpdatePickerVisuals();
        }

        private void RgbColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingColorUi || !_caps.SupportsRgb) return;

            if (int.TryParse(RColorInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) &&
                int.TryParse(GColorInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) &&
                int.TryParse(BColorInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) &&
                r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
            {
                SetSelectedColor(Color.FromRgb((byte)r, (byte)g, (byte)b), updateHsv: true, triggerHardware: true, skipTextFieldUpdate: sender as TextBox);
            }
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            return value > 1.0 ? 1.0 : value;
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            v = max;
            s = max <= 0.0 ? 0.0 : delta / max;

            if (delta <= 0.00001)
            {
                h = 0.0;
                return;
            }

            if (Math.Abs(max - rd) < 0.00001)
            {
                h = 60.0 * (((gd - bd) / delta) % 6.0);
            }
            else if (Math.Abs(max - gd) < 0.00001)
            {
                h = 60.0 * (((bd - rd) / delta) + 2.0);
            }
            else
            {
                h = 60.0 * (((rd - gd) / delta) + 4.0);
            }

            if (h < 0.0) h += 360.0;
        }

        private static Color HsvToRgb(double hue, double sat, double val)
        {
            double h = ((hue % 360.0) + 360.0) % 360.0;
            double s = Clamp01(sat);
            double v = Clamp01(val);

            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;

            double rp, gp, bp;
            if (h < 60.0) { rp = c; gp = x; bp = 0.0; }
            else if (h < 120.0) { rp = x; gp = c; bp = 0.0; }
            else if (h < 180.0) { rp = 0.0; gp = c; bp = x; }
            else if (h < 240.0) { rp = 0.0; gp = x; bp = c; }
            else if (h < 300.0) { rp = x; gp = 0.0; bp = c; }
            else { rp = c; gp = 0.0; bp = x; }

            byte r = (byte)Math.Round((rp + m) * 255.0);
            byte g = (byte)Math.Round((gp + m) * 255.0);
            byte b = (byte)Math.Round((bp + m) * 255.0);
            return Color.FromRgb(r, g, b);
        }
    }
}
