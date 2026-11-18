using System;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Utility
{
    public static partial class SystemPreferences
    {
        public static event Action<bool> PowerLineChanged;
        public static event Action<Fan.Mode> FanProfileChanged;
        public static event Action ShowMainWindowRequested;

        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";
        private const string RuntimeSessionSubKey = @"Software\BSCustomClevoController\RuntimeSession";

        private static bool _initialized;
        private static bool _preferencesLoaded;
        private static bool _ecSettingsAppliedOnStartup;
        private static readonly object _initLock = new object();

        private static long _lastWinKeyChangeTicks;
        public static bool IsWinKeyLocked => _isWinKeyLocked;
        public static bool IsSwapWinFn => _isSwapWinFn;
        public static bool IsFnLock => _isFnLock;
        public static bool IsSwapWinFnSupported { get; private set; } = true;

        public static bool IsRecentWinKeyChange(double windowMs = 500.0)
        {
            return MonotonicClock.ElapsedMs(_lastWinKeyChangeTicks) < windowMs;
        }

        private static void EnsurePreferencesLoaded()
        {
            if (_preferencesLoaded) return;
            _preferencesLoaded = true;

            try
            {
                QueryKeyboardSettingsFromEC();

                object fnVal = Registry.GetValue(AppRegistryKey, "FnKeyLock", null);
                if (fnVal is int intFn) _isFnLock = intFn != 0;

                object swapVal = Registry.GetValue(AppRegistryKey, "SwapWinFn", null);
                if (swapVal is int intSwap) _isSwapWinFn = intSwap != 0;

                object winLockVal = Registry.GetValue(AppRegistryKey, "WinKeyLock", null);
                if (winLockVal is int intWinLock) _isWinKeyLocked = intWinLock != 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.EnsurePreferencesLoaded", ex);
            }
        }

        public static void Initialize()
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            EnsurePreferencesLoaded();

            if (AcpiDriverInstaller.IsDriverWorking())
            {
                SetAcpiEventListening(true);
            }
            Touchpad.Initialize();
            if (!_ecSettingsAppliedOnStartup)
            {
                ApplyKeyboardSettingsToEC();
                _ecSettingsAppliedOnStartup = true;
            }
            StartHsaEventListener();
            StartWmiEventWatcher();
            StartEventLogWatcher();
            StartHardwareStateWatcher();
            UpdateHookState();

            // Release any modifier keys left stuck by boot/startup tools or drivers
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP | NativeMethods.KEYEVENTF_EXTENDEDKEY, 0);
                NativeMethods.keybd_event(0x11, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                NativeMethods.keybd_event(0x10, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                NativeMethods.keybd_event(0x12, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.Initialize.ReleaseModifiers", ex);
            }
        }

        public static void Shutdown()
        {
            StopHsaEventListener();
            SetAcpiEventListening(false);
            StopWmiEventWatcher();
            StopEventLogWatcher();
            StopHardwareStateWatcher();
            RemoveKeyboardHook();
            _initialized = false;
        }

        public static void QueryKeyboardSettingsFromEC()
        {
            if (KeyboardEffect.IsAnySoftwareEffectRunning())
            {
                return;
            }
            try
            {
                byte[] input = new byte[256];
                byte[] output = new byte[256];
                input[0] = 24;
                int res = InsydeDriver.SetDCHU_DataEx(4, input, 256, ref output[0]);
                if (res > 0)
                {
                    Array.Copy(output, _lastEcKeyboardState, Math.Min(output.Length, _lastEcKeyboardState.Length));

                    if (output[0] == 1) _isFnLock = true;
                    else if (output[0] == 2) _isFnLock = false;

                    if (output[1] == 2) _isWinKeyLocked = true;
                    else if (output[1] == 1) _isWinKeyLocked = false;

                    if (output[2] != 0)
                    {
                        IsSwapWinFnSupported = true;
                        _isSwapWinFn = (output[2] == 1);
                    }
                    else
                    {
                        IsSwapWinFnSupported = false;
                        _isSwapWinFn = false;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.QueryKeyboardSettingsFromEC", ex);
            }
        }

        private static int ReadFnLockFromEC()
        {
            try
            {
                byte[] input = new byte[256];
                byte[] output = new byte[256];
                input[0] = 24;
                int res = InsydeDriver.SetDCHU_DataEx(4, input, 256, ref output[0]);
                if (res > 0 && (output[0] == 1 || output[0] == 2))
                {
                    Array.Copy(output, _lastEcKeyboardState, Math.Min(output.Length, _lastEcKeyboardState.Length));
                    return output[0];
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ReadFnLockFromEC", ex);
            }
            return 0;
        }

        public static void ApplyKeyboardSettingsToEC()
        {
            // Writing keyboard settings makes the EC echo a Fn-lock event; ignore it so we do not toggle back.
            _suppressEcKeyboardEventsUntilTicks = MonotonicClock.DeadlineFromNowMs(1500.0);
            try
            {
                byte[] input = new byte[256];
                byte[] output = new byte[256];
                input[0] = 25;
                input[1] = (byte)(_isFnLock ? 1 : 2);
                input[2] = (byte)(_isWinKeyLocked ? 2 : 1);
                byte swapState = _isSwapWinFn ? (byte)1 : (byte)2;
                input[3] = IsSwapWinFnSupported ? swapState : (byte)0;
                input[4] = _lastEcKeyboardState[3];
                input[5] = _lastEcKeyboardState[4];

                _lastEcKeyboardState[0] = input[1];
                _lastEcKeyboardState[1] = input[2];
                _lastEcKeyboardState[2] = input[3];

                InsydeDriver.SetDCHU_DataEx(4, input, 256, ref output[0]);

                try
                {
                    byte[] kbSettings = new byte[3] { input[1], input[2], input[3] };
                    InsydeDriver.WriteAppSettings(1, 36, 3, ref kbSettings[0]);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.ApplyKeyboardSettingsToEC.AppSettings", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ApplyKeyboardSettingsToEC", ex);
            }
        }

        public static void ResetKeyboardPreferences()
        {
            _isFnLock = false;
            _isWinKeyLocked = false;
            _isSwapWinFn = false;

            try
            {
                Registry.SetValue(AppRegistryKey, "FnKeyLock", 0, RegistryValueKind.DWord);
                Registry.SetValue(AppRegistryKey, "WinKeyLock", 0, RegistryValueKind.DWord);
                Registry.SetValue(AppRegistryKey, "SwapWinFn", 0, RegistryValueKind.DWord);
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\ControlCenter2.0", "FnLock", 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ResetKeyboardPreferences", ex);
            }

            ApplyKeyboardSettingsToEC();
            UpdateHookState();
        }

        public static bool SetWinKeyLock(bool enable)
        {
            _isWinKeyLocked = enable;
            try
            {
                Registry.SetValue(AppRegistryKey, "WinKeyLock", enable ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetWinKeyLock", ex);
            }

            ApplyKeyboardSettingsToEC();
            UpdateHookState();
            _lastWinKeyChangeTicks = MonotonicClock.NowTicks();
            SendHKeyMessage(262, (byte)(enable ? 1 : 0));
            OsdOverlay.ShowWinKeyLock(_isWinKeyLocked);
            NotifyHardwareStateChanged();
            return _isWinKeyLocked;
        }

        public static bool ToggleWinKeyLock()
        {
            bool res = SetWinKeyLock(!_isWinKeyLocked);
            NotifyHardwareStateChanged();
            return res;
        }

        public static bool SetSwapWinFn(bool enable)
        {
            _isSwapWinFn = enable;
            try
            {
                Registry.SetValue(AppRegistryKey, "SwapWinFn", _isSwapWinFn ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetSwapWinFn", ex);
            }

            ApplyKeyboardSettingsToEC();
            UpdateHookState();
            NotifyHardwareStateChanged();
            return _isSwapWinFn;
        }

        public static bool ToggleSwapWinFn()
        {
            bool res = SetSwapWinFn(!_isSwapWinFn);
            NotifyHardwareStateChanged();
            return res;
        }

        public static bool SetFnLock(bool enable)
        {
            _isFnLock = enable;
            try
            {
                Registry.SetValue(AppRegistryKey, "FnKeyLock", _isFnLock ? 1 : 0, RegistryValueKind.DWord);
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\ControlCenter2.0", "FnLock", _isFnLock ? 1 : 0);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetFnLock", ex);
            }

            ApplyKeyboardSettingsToEC();
            UpdateHookState();
            OsdOverlay.ShowFnLock(_isFnLock);
            NotifyHardwareStateChanged();
            return _isFnLock;
        }

        public static bool ToggleFnLock()
        {
            bool res = SetFnLock(!_isFnLock);
            NotifyHardwareStateChanged();
            return res;
        }

        private static void ToggleFnLockFromEC()
        {
            int ecState = ReadFnLockFromEC();
            if (ecState == 1 || ecState == 2)
            {
                bool ecOn = ecState == 1;
                bool target = (ecOn != _isFnLock) ? ecOn : !_isFnLock;
                SetFnLock(target);
                return;
            }
            SetFnLock(!_isFnLock);
        }

        public static bool GetCapsLockState() => (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;

        public static void ToggleCapsLock()
        {
            bool nextState = !GetCapsLockState();
            NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0x45, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0x45, NativeMethods.KEYEVENTF_KEYUP, 0);
            SendHKeyMessage(265, 0);
            OsdOverlay.ShowCaps(nextState);
            NotifyHardwareStateChanged();
        }

        public static bool GetNumLockState() => (NativeMethods.GetKeyState(NativeMethods.VK_NUMLOCK) & 1) != 0;

        public static void ToggleNumLock()
        {
            bool nextState = !GetNumLockState();
            NativeMethods.keybd_event(NativeMethods.VK_NUMLOCK, 0x45, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_NUMLOCK, 0x45, NativeMethods.KEYEVENTF_KEYUP, 0);
            SendHKeyMessage(267, 0);
            OsdOverlay.ShowNum(nextState);
            NotifyHardwareStateChanged();
        }

        public static bool GetScrollLockState() => (NativeMethods.GetKeyState(NativeMethods.VK_SCROLL) & 1) != 0;

        public static void ToggleScrollLock()
        {
            bool nextState = !GetScrollLockState();
            NativeMethods.keybd_event(NativeMethods.VK_SCROLL, 0x45, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_SCROLL, 0x45, NativeMethods.KEYEVENTF_KEYUP, 0);
            SendHKeyMessage(266, 0);
            OsdOverlay.ShowScroll(nextState);
            NotifyHardwareStateChanged();
        }

        public static void AdjustScreenBrightness(int delta)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                {
                    int current = 50;
                    bool found = false;
                    foreach (ManagementBaseObject obj in searcher.Get())
                    {
                        if (!found && obj["CurrentBrightness"] != null)
                        {
                            current = Convert.ToInt32(obj["CurrentBrightness"]);
                            found = true;
                        }
                    }

                    int target = Math.Max(0, Math.Min(100, current + delta));
                    using (var methodSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    {
                        bool invoked = false;
                        foreach (ManagementBaseObject obj in methodSearcher.Get())
                        {
                            if (!invoked && obj is ManagementObject methodObj)
                            {
                                methodObj.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)target });
                                invoked = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.AdjustScreenBrightness", ex);
            }
        }

        public static void CycleKeyboardBacklight()
        {
            try
            {
                int nextLevel = KeyboardBackLight.CycleBrightness();
                OsdOverlay.ShowBacklight(nextLevel);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.CycleKeyboardBacklight", ex);
            }
        }

        internal static void SendHKeyMessage(int cmd, byte param)
        {
            try
            {
                IntPtr hWnd = NativeMethods.FindWindow(null, "HkeyTray");
                if (hWnd != IntPtr.Zero)
                {
                    byte[] data = new byte[5] { (byte)(cmd & 255), param, 0, 0, 0 };
                    GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        NativeMethods.CopyDataStruct cds = new NativeMethods.CopyDataStruct
                        {
                            dwData = (IntPtr)((cmd >> 8) & 255),
                            cbData = data.Length,
                            lpData = handle.AddrOfPinnedObject()
                        };
                        NativeMethods.SendMessageData(hWnd, 74, 2, ref cds);
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SendHKeyMessage", ex);
            }
        }

        public static void ApplyFactoryDefaults()
        {
            try
            {
                FanInfo defaultCpu = Fan.GetDefaultFanInfo();
                FanInfo defaultGpu = Fan.GetDefaultFanInfo();
                Fan.SetCustomFanTable(defaultCpu, defaultGpu);
                Fan.SetOffset(0);
                Fan.SetMode(Fan.Mode.Auto);

                PowerMode.SetPowerMode(PowerMode.Mode.Entertainment);

                KeyboardEffect.StopAllSoftwareEffects();
                try
                {
                    Registry.SetValue(AppRegistryKey, "SoftwareEffects", 0, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.ApplyFactoryDefaults.Reg", ex);
                }

                KeyboardBackLight.TurnOn();
                KeyboardBackLight.SetBrightness(255);

                KeyboardHardwareCapabilities caps = KeyboardBackLight.GetCapabilities();
                if (caps.SupportsRgb)
                {
                    KeyboardBackLight.SetColour(0, 0, 255);
                }

                KeyboardBackLight.SetSleepTimer(30);

                if (caps.SupportsBootEffectOverride)
                {
                    KeyboardBackLight.OverrideBootEffect(false);
                }

                if (!Touchpad.GetTouchpadState())
                {
                    Touchpad.ToggleTouchpad();
                }

                ResetKeyboardPreferences();
                BatteryControl.SetChargeMode(BatteryChargeMode.Maximum);

                try
                {
                    Registry.SetValue(AppRegistryKey, "DefaultsApplied", 1, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.ApplyFactoryDefaults.DefaultsReg", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ApplyFactoryDefaults", ex);
            }
        }

        public static void ApplySavedSettings()
        {
            try
            {
                ApplySavedPowerAndFanSettings();
                ApplySavedKeyboardSettings();
                ApplySavedBatterySettings();
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ApplySavedSettings", ex);
            }
        }

        private static void ApplySavedPowerAndFanSettings()
        {
            PowerMode.Mode savedPower = PowerMode.GetStatus();
            PowerMode.SetPowerMode(savedPower);

            FanStatus fanStatus = Fan.GetStatus();
            Fan.SetOffset((byte)Math.Max(0, Math.Min(100, fanStatus.Offset)));
            if (fanStatus.Mode == Fan.Mode.Custom)
            {
                Fan.SetCustomFanTable(fanStatus.Fan_CPU, fanStatus.Fan_GPU1);
            }
            Fan.SetMode(fanStatus.Mode);
        }

        private static void ApplySavedKeyboardSettings()
        {
            KeyboardStatus kbStatus = KeyboardBackLight.GetStatus();
            KeyboardHardwareCapabilities caps = KeyboardBackLight.GetCapabilities();
            byte targetBrightness = kbStatus.BackLight.A > 0 ? kbStatus.BackLight.A : (byte)255;
            KeyboardEffects.SetMaxBrightness(targetBrightness);

            int animSpeed;
            KeyboardEffect savedEffect = ResolveSavedKeyboardEffect(out animSpeed);

            bool onBattery = KeyboardEffects.IsRunningOnBattery;
            bool shouldStartEffect = kbStatus.LightState && savedEffect != null && (!savedEffect.IsSoftware || !onBattery);
            if (shouldStartEffect)
            {
                KeyboardEffect.SetCurrentAnimationSpeed(animSpeed);
                KeyboardEffects.SetDuration(SpeedToDurationMs(animSpeed));
                KeyboardEffects.SetMaxBrightness(targetBrightness);
            }
            else if (kbStatus.LightState && savedEffect != null && savedEffect.IsSoftware && onBattery)
            {
                KeyboardEffects.SavedBatterySoftwareEffect = savedEffect;
            }

            bool alreadySynced = IsKeyboardHardwareSyncedInSession();
            KeyboardBackLight.ApplySavedLightingOnStartup(
                kbStatus,
                caps,
                savedEffect,
                shouldStartEffect,
                targetBrightness,
                skipHardwareWrite: alreadySynced);
            if (!alreadySynced)
            {
                MarkKeyboardHardwareSyncedInSession();
            }

            if (!_ecSettingsAppliedOnStartup)
            {
                ApplyKeyboardSettingsToEC();
                _ecSettingsAppliedOnStartup = true;
            }
        }

        private static int SpeedToDurationMs(int animSpeed)
        {
            if (animSpeed == 1)
            {
                return 5000;
            }
            if (animSpeed == 3)
            {
                return 1000;
            }
            return 2500;
        }

        private static KeyboardEffect ResolveSavedKeyboardEffect(out int animSpeed)
        {
            animSpeed = 2;
            object regEffectActive = Registry.GetValue(AppRegistryKey, "KbEffectActive", null);
            if (!(regEffectActive is int intEffActive) || intEffActive != 1)
            {
                return null;
            }

            KeyboardEffect savedEffect = null;
            object regEffectId = Registry.GetValue(AppRegistryKey, "KbEffectId", null);
            object regEffectUniq = Registry.GetValue(AppRegistryKey, "KbEffectUniqId", null);
            object regEffectName = Registry.GetValue(AppRegistryKey, "KbEffectName", null);
            if (regEffectId is int effId)
            {
                byte effUniq = regEffectUniq is int u ? (byte)u : (byte)0;
                string effName = regEffectName as string;
                savedEffect = KeyboardEffect.LookUpEffect(effId, effUniq, effName);
            }

            object regSpeed = Registry.GetValue(AppRegistryKey, "KbAnimationSpeed", null);
            if (regSpeed is int s && s >= 1 && s <= 3)
            {
                animSpeed = s;
            }
            return savedEffect;
        }

        private static void ApplySavedBatterySettings()
        {
            object regBatMode = Registry.GetValue(AppRegistryKey, "BatteryMode", null);
            if (regBatMode is int intBatMode)
            {
                int start = 90;
                int stop = 100;
                object regStart = Registry.GetValue(AppRegistryKey, "BatteryStart", 90);
                object regStop = Registry.GetValue(AppRegistryKey, "BatteryStop", 100);
                if (regStart is int s) start = s;
                if (regStop is int st) stop = st;
                BatteryControl.SetChargeMode((BatteryChargeMode)intBatMode, start, stop);
            }
            else
            {
                BatteryChargeStatus batStatus = BatteryControl.GetStatus();
                BatteryControl.SetChargeMode(batStatus.Mode, batStatus.StartThreshold, batStatus.StopThreshold);
            }
        }

        public static event Action HardwareStateChanged;

        internal static void NotifyHardwareStateChanged()
        {
            try
            {
                HardwareStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.NotifyHardwareStateChanged", ex);
            }
        }

        private static bool IsKeyboardHardwareSyncedInSession()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RuntimeSessionSubKey, writable: false))
                {
                    return key?.GetValue("KeyboardSynced") is int val && val == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void MarkKeyboardHardwareSyncedInSession()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    RuntimeSessionSubKey,
                    RegistryKeyPermissionCheck.ReadWriteSubTree,
                    RegistryOptions.Volatile))
                {
                    key?.SetValue("KeyboardSynced", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.MarkKeyboardHardwareSyncedInSession", ex);
            }
        }

        public static void InitializeHardwareOnStartup()
        {
            try
            {
                EnsurePreferencesLoaded();
                object appliedVal = Registry.GetValue(AppRegistryKey, "DefaultsApplied", null);
                bool defaultsApplied = appliedVal is int intVal && intVal == 1;

                if (!defaultsApplied)
                {
                    ApplyFactoryDefaults();
                    MarkKeyboardHardwareSyncedInSession();
                }
                else
                {
                    ApplySavedSettings();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.InitializeHardwareOnStartup", ex);
            }
        }
    }
}
