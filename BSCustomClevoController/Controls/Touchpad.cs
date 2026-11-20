using System;
using Microsoft.Win32;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public static class Touchpad
    {
        private const string PrecisionTouchpadStatusKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";

        private static long _suppressHardwareEventsUntilTicks;
        private static long _lastTouchpadToggleTicks;
        private static long _pendingDesiredUntilTicks;
        private static bool _pendingDesiredState;
        private static bool _hasPendingDesired;
        private static readonly object _touchpadToggleLock = new object();

        public static event Action<bool> TouchpadStateChanged;

        public static bool IsHardwareEventSuppressed => MonotonicClock.IsBefore(_suppressHardwareEventsUntilTicks);

        public static void Initialize()
        {
            try
            {
                object savedVal = Registry.GetValue(AppRegistryKey, "TouchpadEnabled", null);
                if (savedVal != null)
                {
                    bool desiredState = Convert.ToInt32(savedVal) != 0;
                    if (GetTouchpadState() != desiredState)
                    {
                        SetTouchpadState(desiredState, showOsd: false);
                        return;
                    }
                }

                bool state = GetTouchpadState();
                SyncTouchpadToEC(state);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.Initialize", ex);
            }
        }

        public static void SyncTouchpadToEC(bool enable)
        {
            try
            {
                byte[] state = new byte[4] { (byte)(enable ? 1 : 0), 0, 0, 0 };
                InsydeDriver.WriteAppSettings(1, 10, 1, ref state[0]);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.SyncTouchpadToEC", ex);
            }
        }

        private static bool? GetWindowsPrecisionTouchpadState()
        {
            try
            {
                object ptpVal = Registry.GetValue(PrecisionTouchpadStatusKey, "Enabled", null);
                if (ptpVal != null)
                {
                    return Convert.ToInt32(ptpVal) != 0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.GetWindowsPrecisionTouchpadState", ex);
            }
            return null;
        }

        public static bool GetTouchpadState()
        {
            try
            {
                bool? rawState = null;
                bool? ptpState = GetWindowsPrecisionTouchpadState();
                if (ptpState.HasValue)
                {
                    rawState = ptpState.Value;
                }
                else
                {
                    int ecState = 0;
                    int ret = InsydeDriver.GetDCHU_Data_Integer(9, ref ecState);
                    if (ret >= 0)
                    {
                        rawState = ecState == 1;
                    }
                }

                if (_hasPendingDesired)
                {
                    if ((rawState.HasValue && rawState.Value == _pendingDesiredState) ||
                        !MonotonicClock.IsBefore(_pendingDesiredUntilTicks))
                    {
                        _hasPendingDesired = false;
                    }
                    else
                    {
                        return _pendingDesiredState;
                    }
                }

                if (rawState.HasValue)
                {
                    return rawState.Value;
                }

                object appVal = Registry.GetValue(AppRegistryKey, "TouchpadEnabled", null);
                if (appVal != null)
                {
                    return Convert.ToInt32(appVal) != 0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.GetTouchpadState", ex);
            }
            return true;
        }

        internal static void NotifyExternalState(bool enable)
        {
            if (_hasPendingDesired && MonotonicClock.IsBefore(_pendingDesiredUntilTicks))
            {
                return;
            }

            SyncDchuAndRegistry(enable);
            TouchpadStateChanged?.Invoke(enable);
        }

        private static void DispatchPrecisionTouchpadShortcut()
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY, 0);
                NativeMethods.keybd_event(NativeMethods.VK_F24, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_F24, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP | NativeMethods.KEYEVENTF_EXTENDEDKEY, 0);
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.DispatchPrecisionTouchpadShortcut", ex);
            }
        }

        public static bool SyncFromPrecisionTouchpad()
        {
            bool state = GetTouchpadState();
            SyncTouchpadToEC(state);
            PersistTouchpadState(state);
            return state;
        }

        private static void PersistTouchpadState(bool enable)
        {
            try
            {
                Registry.SetValue(AppRegistryKey, "TouchpadEnabled", enable ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.PersistTouchpadState", ex);
            }
        }

        private static void SyncDchuAndRegistry(bool enable)
        {
            SyncTouchpadToEC(enable);

            try
            {
                byte[] buffer = BitConverter.GetBytes(enable ? 1u : 0u);
                InsydeDriver.SetDCHU_Data(42, buffer, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Touchpad.SyncDchuAndRegistry", ex);
            }

            PersistTouchpadState(enable);
        }

        public static bool SetTouchpadState(bool enable, bool showOsd = true)
        {
            lock (_touchpadToggleLock)
            {
                _suppressHardwareEventsUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                _lastTouchpadToggleTicks = MonotonicClock.NowTicks();

                bool current = GetTouchpadState();
                if (current != enable)
                {
                    _pendingDesiredState = enable;
                    _pendingDesiredUntilTicks = MonotonicClock.DeadlineFromNowMs(900.0);
                    _hasPendingDesired = true;
                    DispatchPrecisionTouchpadShortcut();
                }

                SyncDchuAndRegistry(enable);

                if (showOsd)
                {
                    OsdOverlay.ShowTouchpad(enable);
                }
                TouchpadStateChanged?.Invoke(enable);
                SystemPreferences.NotifyHardwareStateChanged();
                return enable;
            }
        }

        public static void HandleHardwareStateChange(bool enable)
        {
            lock (_touchpadToggleLock)
            {
                if (IsHardwareEventSuppressed)
                {
                    return;
                }

                _suppressHardwareEventsUntilTicks = MonotonicClock.DeadlineFromNowMs(1500.0);
                _lastTouchpadToggleTicks = MonotonicClock.NowTicks();

                bool? ptpState = GetWindowsPrecisionTouchpadState();
                if (ptpState.HasValue && ptpState.Value != enable)
                {
                    _pendingDesiredState = enable;
                    _pendingDesiredUntilTicks = MonotonicClock.DeadlineFromNowMs(900.0);
                    _hasPendingDesired = true;
                    DispatchPrecisionTouchpadShortcut();
                }

                SyncDchuAndRegistry(enable);
                OsdOverlay.ShowTouchpad(enable);
                TouchpadStateChanged?.Invoke(enable);
                SystemPreferences.NotifyHardwareStateChanged();
            }
        }

        public static bool ToggleTouchpad()
        {
            lock (_touchpadToggleLock)
            {
                if (MonotonicClock.ElapsedMs(_lastTouchpadToggleTicks) < 600.0)
                {
                    return GetTouchpadState();
                }
                _lastTouchpadToggleTicks = MonotonicClock.NowTicks();
                bool current = GetTouchpadState();
                return SetTouchpadState(!current);
            }
        }
    }
}
