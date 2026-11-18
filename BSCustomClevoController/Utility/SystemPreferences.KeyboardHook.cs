using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Utility
{
    public static partial class SystemPreferences
    {
        [DllImport("PowrProf.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool SetSuspendState(
            [MarshalAs(UnmanagedType.U1)] bool hibernate,
            [MarshalAs(UnmanagedType.U1)] bool forceCritical,
            [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

        private const int VK_VOLUME_MUTE = 0xAD;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP = 0xAF;

        private const uint WM_SYSCOMMAND = 0x0112;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private static readonly IntPtr SC_MONITORPOWER = new IntPtr(0xF170);
        private static readonly IntPtr MONITOR_OFF = new IntPtr(2);

        private static readonly NativeMethods.LowLevelKeyboardProc _hookCallback = HookProcedure;
        private static readonly HashSet<uint> _handledFnKeys = new HashSet<uint>();
        private static readonly object _hookLock = new object();
        private static IntPtr _hookId = IntPtr.Zero;
        private static int _hookThreadId;
        private static bool _isWinKeyLocked = false;
        private static bool _isSwapWinFn = false;
        private static bool _isFnLock = false;
        private static long _lastWinKeyOsdTicks;

        public static void UpdateHookState()
        {
            InstallKeyboardHook();
        }

        private static void InstallKeyboardHook()
        {
            // WH_KEYBOARD_LL callbacks are only dispatched to a thread with an active Win32 message loop.
            UiDispatcher.Post(InstallKeyboardHookCore);
        }

        private static void InstallKeyboardHookCore()
        {
            lock (_hookLock)
            {
                if (_hookId != IntPtr.Zero) return;
                try
                {
                    using (Process curProcess = Process.GetCurrentProcess())
                    using (ProcessModule curModule = curProcess.MainModule)
                    {
                        IntPtr hMod = curModule != null ? NativeMethods.GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                        _hookId = NativeMethods.SetWindowsHookEx(
                            NativeMethods.WH_KEYBOARD_LL,
                            _hookCallback,
                            hMod,
                            0);
                        if (_hookId != IntPtr.Zero)
                        {
                            _hookThreadId = Environment.CurrentManagedThreadId;
                            _handledFnKeys.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.InstallKeyboardHook", ex);
                }
            }
        }

        private static void RemoveKeyboardHook()
        {
            int ownerThreadId;
            lock (_hookLock)
            {
                if (_hookId == IntPtr.Zero) return;
                ownerThreadId = _hookThreadId;
            }

            if (ownerThreadId == 0 || Environment.CurrentManagedThreadId == ownerThreadId || UiDispatcher.PostHandler == null)
            {
                RemoveKeyboardHookCore();
            }
            else
            {
                UiDispatcher.Post(RemoveKeyboardHookCore);
            }
        }

        private static void RemoveKeyboardHookCore()
        {
            lock (_hookLock)
            {
                if (_hookId != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.UnhookWindowsHookEx(_hookId);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("SystemPreferences.RemoveKeyboardHook", ex);
                    }
                    _hookId = IntPtr.Zero;
                    _hookThreadId = 0;
                    _handledFnKeys.Clear();
                }
            }
        }

        private static IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

                // Ignore synthetic keys to prevent hook recursion and self-blocking
                const int LLKHF_INJECTED = 0x10;
                if ((hookStruct.flags & LLKHF_INJECTED) != 0)
                {
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                int msg = (int)wParam;
                bool isKeyDown = (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN);
                bool isKeyUp = (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP);
                uint vk = hookStruct.vkCode;

                if (_isWinKeyLocked && (vk == NativeMethods.VK_LWIN || vk == NativeMethods.VK_RWIN))
                {
                    if (isKeyDown && OsdOverlay.IsEnabled && MonotonicClock.ElapsedMs(_lastWinKeyOsdTicks) >= 500.0)
                    {
                        _lastWinKeyOsdTicks = MonotonicClock.NowTicks();
                        OsdOverlay.ShowWinKeyLock(true);
                    }
                    return (IntPtr)1;
                }

                if (vk == NativeMethods.VK_F24)
                {
                    _lastHardwareTouchpadKeyTicks = MonotonicClock.NowTicks();
                }

                if (OsdOverlay.IsEnabled && isKeyUp)
                {
                    if (vk == NativeMethods.VK_CAPITAL)
                    {
                        OsdOverlay.ShowCaps(GetCapsLockState());
                    }
                    else if (vk == NativeMethods.VK_NUMLOCK)
                    {
                        OsdOverlay.ShowNum(GetNumLockState());
                    }
                    else if (vk == NativeMethods.VK_SCROLL)
                    {
                        OsdOverlay.ShowScroll(GetScrollLockState());
                    }
                    else if (vk == VK_AIRPLANE)
                    {
                        Task.Delay(120).ContinueWith(_ =>
                        {
                            bool airState = GetAirplaneModeState();
                            _lastAirplaneState = airState;
                            _suppressAirplaneOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                            OsdOverlay.ShowAirplane(airState);
                        });
                    }
                    else if (vk == NativeMethods.VK_F24)
                    {
                        Task.Delay(120).ContinueWith(_ =>
                        {
                            bool state = Touchpad.SyncFromPrecisionTouchpad();
                            OsdOverlay.ShowTouchpad(state);
                        });
                    }
                }

                if (_isFnLock && _lastEcKeyboardState[0] == 0 && IsFnKey(vk))
                {
                    // Allow OS shortcuts (Alt+F4, Ctrl+F5, Shift+F10, Win+F1) through normally
                    bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                    bool shiftDown = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
                    bool altDown = (hookStruct.flags & 0x20) != 0 || (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
                    bool winDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
                                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
                    if (ctrlDown || altDown || shiftDown || winDown)
                    {
                        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (isKeyDown)
                    {
                        if (HandleFnAction(vk))
                        {
                            _handledFnKeys.Add(vk);
                            return (IntPtr)1;
                        }
                    }
                    else if (isKeyUp && _handledFnKeys.Remove(vk))
                    {
                        return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void QueueHookAction(string name, Action action)
        {
            Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.KeyboardHook." + name, ex);
                }
            });
        }

        private static bool IsFnKey(uint vk) => vk >= 0x70 && vk <= 0x7B;

        private static bool HandleFnAction(uint vk)
        {
            switch (vk)
            {
                case 0x70:
                    QueueHookAction("Touchpad", () => OsdOverlay.ShowTouchpad(Touchpad.ToggleTouchpad()));
                    return true;
                case 0x71:
                    NativeMethods.PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
                    return true;
                case 0x72:
                    NativeMethods.keybd_event(VK_VOLUME_MUTE, 0, 0, 0);
                    NativeMethods.keybd_event(VK_VOLUME_MUTE, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    return true;
                case 0x73:
                    QueueHookAction("CycleBacklight", CycleKeyboardBacklight);
                    return true;
                case 0x74:
                    NativeMethods.keybd_event(VK_VOLUME_DOWN, 0, 0, 0);
                    NativeMethods.keybd_event(VK_VOLUME_DOWN, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    return true;
                case 0x75:
                    NativeMethods.keybd_event(VK_VOLUME_UP, 0, 0, 0);
                    NativeMethods.keybd_event(VK_VOLUME_UP, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    return true;
                case 0x76:
                    NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, 0);
                    NativeMethods.keybd_event(0x50, 0, 0, 0);
                    NativeMethods.keybd_event(0x50, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    return true;
                case 0x77:
                    QueueHookAction("BrightnessDown", () => AdjustScreenBrightness(-10));
                    return true;
                case 0x78:
                    QueueHookAction("BrightnessUp", () => AdjustScreenBrightness(10));
                    return true;
                case 0x79:
                    QueueHookAction("ToggleCamera", () => ToggleCamera());
                    return true;
                case 0x7A:
                    QueueHookAction("ToggleAirplane", () => ToggleAirplaneMode());
                    return true;
                case 0x7B:
                    QueueHookAction("Suspend", () => SetSuspendState(false, false, false));
                    return true;
            }
            return false;
        }
    }
}

