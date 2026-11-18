using Microsoft.Win32;
using System;
using BSCustomClevoController.Controls;


namespace BSCustomClevoController.Utility
{
    public static class OsdOverlay
    {
        private static bool _isEnabled = LoadInitialEnabledState();
        private static readonly object _lock = new object();
        private static string _lastOsdKey;
        private static long _lastOsdTicks;

        /// <summary>UI-layer callback invoked with (imageName, localizedText) when an OSD should appear.</summary>
        public static Action<string, string> DisplayHandler { get; set; }

        private static bool LoadInitialEnabledState()
        {
            try
            {
                object val = Registry.GetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "ShowOSD", 1);
                return !(val is int intVal) || intVal != 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("OsdOverlay.LoadInitialEnabledState", ex);
                return true;
            }
        }

        public static bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                try
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "ShowOSD", value ? 1 : 0, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("OsdOverlay.IsEnabled", ex);
                }
            }
        }

        private static void ShowInternal(string imageName, string text)
        {
            if (!_isEnabled) return;

            string key = imageName + "|" + text;
            lock (_lock)
            {
                if (string.Equals(key, _lastOsdKey, StringComparison.Ordinal) &&
                    MonotonicClock.ElapsedMs(_lastOsdTicks) < 1000.0)
                {
                    return;
                }
                _lastOsdKey = key;
                _lastOsdTicks = MonotonicClock.NowTicks();
            }

            DisplayHandler?.Invoke(imageName, text);
        }

        private static void ShowToggle(bool on, string onImg, string offImg, string onKey, string onFallback, string offKey, string offFallback)
        {
            ShowInternal(
                on ? onImg : offImg,
                LocalizationManager.T(on ? onKey : offKey, on ? onFallback : offFallback));
        }

        public static void ShowCaps(bool on)
        {
            ShowToggle(on, "osd_caps_on_cc40_1.png", "osd_caps_off_cc40_1.png", "OsdCapsOn", "CAPS LOCK ON", "OsdCapsOff", "CAPS LOCK OFF");
        }

        public static void ShowNum(bool on)
        {
            ShowToggle(on, "osd_num_on_cc40_1.png", "osd_num_off_cc40_1.png", "OsdNumOn", "NUM LOCK ON", "OsdNumOff", "NUM LOCK OFF");
        }

        public static void ShowScroll(bool on)
        {
            ShowToggle(on, "osd_scr_on_cc40_1.png", "osd_scr_off_cc40_1.png", "OsdScrollOn", "SCROLL LOCK ON", "OsdScrollOff", "SCROLL LOCK OFF");
        }

        public static void ShowWinKeyLock(bool locked)
        {
            ShowToggle(locked, "osd_winkey_lock_cc40_1.png", "osd_winkey_unlock_cc40_1.png", "OsdWinLockOn", "WIN KEY LOCKED", "OsdWinLockOff", "WIN KEY UNLOCKED");
        }

        public static void ShowTouchpad(bool enabled)
        {
            ShowToggle(enabled, "osd_touchpad_on_cc40_1.png", "osd_touchpad_off_cc40_1.png", "OsdTouchpadOn", "TOUCHPAD ON", "OsdTouchpadOff", "TOUCHPAD OFF");
        }

        public static void ShowCamera(bool enabled)
        {
            ShowToggle(enabled, "osd_camera_on_cc40_1.png", "osd_camera_off_cc40_1.png", "OsdCameraOn", "CAMERA ON", "OsdCameraOff", "CAMERA OFF");
        }

        public static void ShowMicrophone(bool enabled)
        {
            ShowToggle(enabled, "Thirdwave_MicUnmute_cc40_1.png", "Thirdwave_MicMute_cc40_1.png", "OsdMicOn", "MICROPHONE ON", "OsdMicOff", "MICROPHONE OFF");
        }

        public static void ShowCameraMic(bool enabled)
        {
            ShowToggle(enabled, "osd_cameraMic_on_cc40_1.png", "osd_cameraMic_off_cc40_1.png", "OsdCameraMicOn", "CAMERA & MIC ON", "OsdCameraMicOff", "CAMERA & MIC OFF");
        }

        public static void ShowAirplane(bool enabled)
        {
            // Windows displays its built-in Airplane Mode toast.
        }

        public static void ShowFnLock(bool locked)
        {
            ShowToggle(locked, "osd_Fn_lock_enable.png", "osd_Fn_lock_disable.png", "OsdFnLockOn", "FN LOCK ON", "OsdFnLockOff", "FN LOCK OFF");
        }

        public static void ShowBacklight(int level)
        {
            if (level <= 0)
            {
                ShowBacklightToggle(false);
            }
            else
            {
                int imgIdx = Math.Min(4, Math.Max(1, level));
                string pattern = LocalizationManager.T("OsdBacklightLevelFmt", "KEYBOARD\nBACKLIGHT {0}%");
                ShowInternal(
                    string.Format("osd_backlight_{0}_cc40_1.png", imgIdx),
                    string.Format(LocalizationManager.CurrentCulture, pattern, imgIdx * 25));
            }
        }

        public static void ShowBacklightToggle(bool isOn)
        {
            ShowToggle(isOn, "osd_backlight_on_cc40_1.png", "osd_backlight_off_cc40_1.png", "OsdBacklightOn", "KEYBOARD\nBACKLIGHT ON", "OsdBacklightOff", "KEYBOARD\nBACKLIGHT OFF");
        }

        public static void ShowBacklightColor(string colorName)
        {
            string localizedColor = string.Empty;
            if (!string.IsNullOrWhiteSpace(colorName))
            {
                string trimmed = colorName.Trim();
                string normalized = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1).ToLowerInvariant();
                localizedColor = LocalizationManager.ToUpper(
                    LocalizationManager.T("KbColor" + normalized, trimmed));
            }

            string pattern = LocalizationManager.T("OsdBacklightColorFmt", "KEYBOARD\nCOLOR {0}");
            ShowInternal(
                "osd_backlight_on_cc40_1.png",
                string.Format(LocalizationManager.CurrentCulture, pattern, localizedColor));
        }

        public static void ShowFan(bool isMax)
        {
            ShowFan(isMax ? Fan.Mode.Max : Fan.Mode.Auto);
        }

        internal static void ShowFan(Fan.Mode mode)
        {
            string img;
            string key;
            string fallback;
            switch (mode)
            {
                case Fan.Mode.Max:
                    img = "osd_fan_speed_max_cc40_1.png";
                    key = "OsdFanMax";
                    fallback = "MAX FAN";
                    break;
                case Fan.Mode.MaxQ:
                    img = "osd_fan_speed_MaxQ_cc40_1.png";
                    key = "OsdFanQuiet";
                    fallback = "QUIET FAN";
                    break;
                case Fan.Mode.Custom:
                    img = "osd_fan_speed_Custom_cc40_1.png";
                    key = "OsdFanCustom";
                    fallback = "CUSTOM FAN";
                    break;
                default:
                    img = "osd_fan_speed_auto_cc40_1.png";
                    key = "OsdFanAuto";
                    fallback = "AUTO FAN";
                    break;
            }
            ShowInternal(img, LocalizationManager.T(key, fallback));
        }

        public static void ShowFanAntiDust()
        {
            ShowInternal("osd_fan_speed_AntiDustRuning_cc40_1.png", LocalizationManager.T("OsdFanAntiDust", "ANTI-DUST FAN"));
        }

        public static void ShowPowerMode(PowerMode.Mode mode)
        {
            string img;
            string key;
            string fallback;
            switch (mode)
            {
                case PowerMode.Mode.Quiet:
                    img = "osd_quiet2_cc40_1.png";
                    key = "OsdPowerQuiet";
                    fallback = "QUIET";
                    break;
                case PowerMode.Mode.Powersaving:
                    img = "osd_powerSaving2_cc40_1.png";
                    key = "OsdPowerSaver";
                    fallback = "POWER SAVER";
                    break;
                case PowerMode.Mode.Performance:
                    img = "osd_peformance2_cc40_1.png";
                    key = "OsdPowerPerformance";
                    fallback = "PERFORMANCE";
                    break;
                default:
                    img = "osd_entetainment2_cc40_1.png";
                    key = "OsdPowerEntertainment";
                    fallback = "ENTERTAINMENT";
                    break;
            }
            ShowInternal(img, LocalizationManager.T(key, fallback));
        }
    }
}
