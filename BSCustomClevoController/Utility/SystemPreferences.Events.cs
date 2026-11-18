using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Utility
{
    public static partial class SystemPreferences
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ACPI_HSA_WAIT_EVENT
        {
            public IntPtr HsaEvent;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateFileW(
            string filename,
            uint access,
            uint sharing,
            IntPtr securityAttributes,
            uint mode,
            uint options,
            IntPtr template);

        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeviceIoControlWaitEvent(
            IntPtr hDevice,
            uint dwIoControlCode,
            ref ACPI_HSA_WAIT_EVENT inBuffer,
            int inBufferSize,
            byte[] outBuffer,
            uint outBufferSize,
            ref uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeviceIoControlGetNotify(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr inBuffer,
            int inBufferSize,
            ref long outBuffer,
            uint outBufferSize,
            ref uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static long _suppressAirplaneOsdUntilTicks;
        private static long _suppressCameraOsdUntilTicks;
        private static long _suppressMicOsdUntilTicks;
        private static long _suppressEcKeyboardEventsUntilTicks;
        private static bool? _lastTouchpadState;
        private static bool? _lastCapsState;
        private static bool? _lastNumState;
        private static bool? _lastScrollState;
        private static bool? _lastAirplaneState;
        private static bool? _lastCameraState;
        private static bool? _lastMicState;
        private static bool? _lastAcState;
        private static Fan.Mode? _lastHardwareFanMode;
        private static byte[] _lastEcKeyboardState = new byte[16];

        private static readonly object _ecPollLock = new object();
        private static byte _lastOem4 = 0;

        private static int _lastAcpiEventCode = -1;
        private static long _lastAcpiEventTicks;
        private static long _lastHardwareTouchpadKeyTicks;
        private static readonly object _acpiEventLock = new object();

        private static Thread _hsaListenerThread;
        private static volatile bool _hsaRunning = false;
        private static EventLog _outLogWatcher;
        private static ManagementEventWatcher _clevoEventWatcher;
        private static Timer _hardwareStatePollTimer;

        internal static void UpdateHardwareFanModeCache(Fan.Mode mode)
        {
            _lastHardwareFanMode = mode;
        }

        public static void SetAcpiEventListening(bool enable)
        {
            try
            {
                byte[] zero4 = new byte[4];
                InsydeDriver.WriteAppSettings(0, 8, 4, ref zero4[0]);

                byte[] status = new byte[1] { (byte)(enable ? 1 : 0) };
                InsydeDriver.WriteAppSettings(1, 12, 1, ref status[0]);

                if (enable)
                {
                    int dchu70 = 0;
                    InsydeDriver.GetDCHU_Data_Integer(70, ref dchu70);

                    InsydeDriver.SetDCHU_Data(121, new byte[4] { 1, 0, 0, 17 }, 4);
                    InsydeDriver.SetDCHU_Data(121, new byte[4] { 1, 0, 0, 26 }, 4);
                    InsydeDriver.SetDCHU_Data(121, new byte[4] { 1, 0, 0, 34 }, 4);

                    ClearEcHotkeyByte();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetAcpiEventListening", ex);
            }
        }

        private static void ClearEcHotkeyByte()
        {
            try
            {
                InsydeDriver.SetDCHU_Data(107, new byte[4] { 0xCC, 0, 0, 0 }, 4);
                InsydeDriver.SetDCHU_Data(117, new byte[4] { 0, 0, 0, 0 }, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ClearEcHotkeyByte", ex);
            }
        }

        private static void PollEcHotkeysAndFanState()
        {
            lock (_ecPollLock)
            {
                try
                {
                    byte[] input = new byte[256];
                    byte[] ram1 = new byte[256];
                    input[0] = 3;
                    if (InsydeDriver.SetDCHU_DataEx(4, input, 256, ref ram1[0]) <= 0)
                    {
                        return;
                    }

                    byte oem4 = ram1[0xCC];
                    if (oem4 == 0)
                    {
                        _lastOem4 = 0;
                        return;
                    }

                    int mappedCode;
                    switch (oem4)
                    {
                        case 0x80: mappedCode = 131; break;
                        case 0x81:
                        case 0x20: mappedCode = 129; break;
                        case 0x82:
                        case 0x21: mappedCode = 130; break;
                        case 0x8A: mappedCode = 138; break;
                        case 0x9E: mappedCode = 158; break;
                        case 0x9F:
                        case 0x3F: mappedCode = 159; break;
                        case 0xF2:
                        case 0x6B: mappedCode = 112; break;
                        case 0xF3: mappedCode = 143; break;
                        case 0xFA: mappedCode = 38; break;
                        case 0x59: mappedCode = 89; break;
                        default: return;
                    }

                    ClearEcHotkeyByte();
                    if (oem4 == _lastOem4)
                    {
                        return;
                    }
                    _lastOem4 = oem4;
                    HandleAcpiEvent(mappedCode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.PollEcHotkeysAndFanState", ex);
                }
            }
        }

        public static void StartWmiEventWatcher()
        {
            try
            {
                if (_clevoEventWatcher != null) return;
                ManagementScope scope = new ManagementScope(@"root\WMI");
                scope.Connect();
                WqlEventQuery query = new WqlEventQuery("SELECT * FROM CLEVO_EVENT");
                _clevoEventWatcher = new ManagementEventWatcher(scope, query);
                _clevoEventWatcher.EventArrived += (s, e) =>
                {
                    try
                    {
                        using (ManagementObject clevoObj = new ManagementObject("root\\WMI", "CLEVO_GET.InstanceName='ACPI\\PNP0C14\\0_0'", null))
                        using (ManagementBaseObject outParams = clevoObj.InvokeMethod("GetEvent", null, null))
                        {
                            if (outParams != null && outParams["Data"] != null)
                            {
                                string text = outParams["Data"].ToString();
                                if (int.TryParse(text, out int eventCode))
                                {
                                    HandleAcpiEvent(eventCode);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("SystemPreferences.WmiEventArrived", ex);
                    }
                };
                _clevoEventWatcher.Start();
            }
            catch (Exception ex)
            {
                if (_clevoEventWatcher != null)
                {
                    try
                    {
                        _clevoEventWatcher.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        AppLogger.Log("SystemPreferences.StartWmiEventWatcher.Dispose", disposeEx);
                    }
                    _clevoEventWatcher = null;
                }
                AppLogger.Log("SystemPreferences.StartWmiEventWatcher", ex);
            }
        }

        public static void StopWmiEventWatcher()
        {
            if (_clevoEventWatcher != null)
            {
                try
                {
                    _clevoEventWatcher.Stop();
                    _clevoEventWatcher.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.StopWmiEventWatcher", ex);
                }
                _clevoEventWatcher = null;
            }
        }

        private static readonly object _hsaLock = new object();

        public static void StartHsaEventListener()
        {
            lock (_hsaLock)
            {
                if (_hsaRunning || (_hsaListenerThread != null && _hsaListenerThread.IsAlive)) return;
                _hsaRunning = true;
                _hsaListenerThread = new Thread(HsaListenerWorker)
                {
                    IsBackground = true,
                    Name = "ClevoHsaEventListener"
                };
                _hsaListenerThread.Start();
            }
        }

        public static void StopHsaEventListener()
        {
            Thread worker;
            lock (_hsaLock)
            {
                _hsaRunning = false;
                worker = _hsaListenerThread;
            }

            if (worker != null && worker.IsAlive && worker.Join(2000))
            {
                lock (_hsaLock)
                {
                    if (_hsaListenerThread == worker)
                    {
                        _hsaListenerThread = null;
                    }
                }
            }
        }

        private static void HsaListenerWorker()
        {
            const string AcpiBridgeDevicePath = @"\\?\ACPI#CLV0001#1#{86994c74-ad43-4812-b7e7-0c420b5c5fd7}";
            const uint GENERIC_READ = 0x80000000;
            const uint GENERIC_WRITE = 0x40000000;
            const uint FILE_SHARE_READ = 0x00000001;
            const uint FILE_SHARE_WRITE = 0x00000002;
            const uint OPEN_EXISTING = 3;
            const uint IOCTL_ACPIBIOS_WAIT_EVENT = 3286020u;
            const uint IOCTL_ACPIBIOS_GET_NOTIFY_VALUE = 3286024u;

            while (_hsaRunning)
            {
                IntPtr hDevice = CreateFileW(AcpiBridgeDevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
                {
                    PollEcHotkeysAndFanState();
                    Thread.Sleep(100);
                    continue;
                }

                IntPtr hsaEvent = CreateEvent(IntPtr.Zero, true, false, null);
                if (hsaEvent == IntPtr.Zero)
                {
                    CloseHandle(hDevice);
                    Thread.Sleep(100);
                    continue;
                }
                try
                {
                    SetAcpiEventListening(true);
                    ACPI_HSA_WAIT_EVENT waitStruct = new ACPI_HSA_WAIT_EVENT { HsaEvent = hsaEvent };
                    byte[] waitOutBuf = new byte[256];
                    uint returned = 0;
                    ResetEvent(hsaEvent);
                    DeviceIoControlWaitEvent(hDevice, IOCTL_ACPIBIOS_WAIT_EVENT, ref waitStruct, Marshal.SizeOf(typeof(ACPI_HSA_WAIT_EVENT)), waitOutBuf, 256u, ref returned, IntPtr.Zero);

                    while (_hsaRunning)
                    {
                        uint waitResult = WaitForSingleObject(hsaEvent, 80);
                        if (waitResult == 0)
                        {
                            ResetEvent(hsaEvent);
                            long notifyValue = 0;
                            if (DeviceIoControlGetNotify(hDevice, IOCTL_ACPIBIOS_GET_NOTIFY_VALUE, IntPtr.Zero, 0, ref notifyValue, 8, ref returned, IntPtr.Zero))
                            {
                                if (notifyValue != 0)
                                {
                                    HandleAcpiEvent((int)notifyValue);
                                    ClearEcHotkeyByte();
                                    DeviceIoControlWaitEvent(hDevice, IOCTL_ACPIBIOS_WAIT_EVENT, ref waitStruct, Marshal.SizeOf(typeof(ACPI_HSA_WAIT_EVENT)), waitOutBuf, 256u, ref returned, IntPtr.Zero);
                                }
                            }
                        }
                        else
                        {
                            PollEcHotkeysAndFanState();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.HsaListenerWorker", ex);
                }
                finally
                {
                    CloseHandle(hsaEvent);
                    CloseHandle(hDevice);
                }
            }
        }

        public static void StartEventLogWatcher()
        {
            try
            {
                if (_outLogWatcher != null) return;
                if (!EventLog.Exists("OutLog")) return;

                _outLogWatcher = new EventLog("OutLog") { Source = "PowerBIOSServer_Out" };
                _outLogWatcher.EntryWritten += (s, e) =>
                {
                    try
                    {
                        string msg = e.Entry.Message;
                        if (!string.IsNullOrEmpty(msg) && msg.StartsWith("Arg001^"))
                        {
                            string[] parts = msg.Split('^');
                            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int eventCode))
                            {
                                HandleAcpiEvent(eventCode);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("SystemPreferences.OutLogEntryWritten", ex);
                    }
                };
                _outLogWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.StartEventLogWatcher", ex);
            }
        }

        public static void StopEventLogWatcher()
        {
            if (_outLogWatcher != null)
            {
                try
                {
                    _outLogWatcher.EnableRaisingEvents = false;
                    _outLogWatcher.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.StopEventLogWatcher", ex);
                }
                _outLogWatcher = null;
            }
        }

        private static int _pollInFlight;
        private static volatile bool _pollStopping;

        private static void StartHardwareStateWatcher()
        {
            if (_hardwareStatePollTimer != null) return;
            _pollStopping = false;
            _lastAirplaneState = GetAirplaneModeState();
            _lastCameraState = GetCameraState();
            _lastMicState = GetMicrophoneState();
            _lastAcState = NativeMethods.IsAcLineOnline(defaultIfUnknown: false);
            _lastHardwareFanMode = Fan.GetHardwareFanMode();
            _hardwareStatePollTimer = new Timer(PollHardwareStates, null, 500, 400);

            try
            {
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.StartHardwareStateWatcher", ex);
            }
        }

        private static void StopHardwareStateWatcher()
        {
            _pollStopping = true;
            try
            {
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.StopHardwareStateWatcher", ex);
            }

            if (_hardwareStatePollTimer != null)
            {
                try
                {
                    using (var waitDone = new ManualResetEvent(false))
                    {
                        if (_hardwareStatePollTimer.Dispose(waitDone))
                        {
                            waitDone.WaitOne(2000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.StopHardwareStateWatcher.Timer", ex);
                }
                _hardwareStatePollTimer = null;
            }
        }

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                SetAcpiEventListening(true);
            }
            if (e.Mode == PowerModes.StatusChange || e.Mode == PowerModes.Resume)
            {
                bool ac = NativeMethods.IsAcLineOnline(defaultIfUnknown: false);
                _lastAcState = ac;
                KeyboardEffects.HandlePowerStatusChanged(ac);
                PowerLineChanged?.Invoke(ac);
            }
        }

        private static void PollHardwareStates(object state)
        {
            if (_pollStopping || Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            {
                return;
            }
            try
            {
                if (_pollStopping)
                {
                    return;
                }
                bool hardwareChanged = false;

                bool currentAirplane = GetAirplaneModeState();
                if (!_lastAirplaneState.HasValue)
                {
                    _lastAirplaneState = currentAirplane;
                }
                else if (_lastAirplaneState.Value != currentAirplane)
                {
                    _lastAirplaneState = currentAirplane;
                    hardwareChanged = true;
                    if (!MonotonicClock.IsBefore(_suppressAirplaneOsdUntilTicks))
                    {
                        OsdOverlay.ShowAirplane(currentAirplane);
                    }
                }

                bool currentMic = GetMicrophoneState();
                if (!_lastMicState.HasValue)
                {
                    _lastMicState = currentMic;
                }
                else if (_lastMicState.Value != currentMic)
                {
                    _lastMicState = currentMic;
                    hardwareChanged = true;
                    if (!MonotonicClock.IsBefore(_suppressMicOsdUntilTicks))
                    {
                        OsdOverlay.ShowMicrophone(currentMic);
                    }
                }

                bool currentCamera = GetCameraState();
                if (!_lastCameraState.HasValue)
                {
                    _lastCameraState = currentCamera;
                }
                else if (_lastCameraState.Value != currentCamera)
                {
                    _lastCameraState = currentCamera;
                    hardwareChanged = true;
                    if (!MonotonicClock.IsBefore(_suppressCameraOsdUntilTicks))
                    {
                        OsdOverlay.ShowCamera(currentCamera);
                    }
                }

                bool currentTouchpad = Touchpad.GetTouchpadState();
                if (!_lastTouchpadState.HasValue)
                {
                    _lastTouchpadState = currentTouchpad;
                }
                else if (_lastTouchpadState.Value != currentTouchpad)
                {
                    _lastTouchpadState = currentTouchpad;
                    Touchpad.NotifyExternalState(currentTouchpad);
                    hardwareChanged = true;
                }

                bool currentCaps = GetCapsLockState();
                if (!_lastCapsState.HasValue)
                {
                    _lastCapsState = currentCaps;
                }
                else if (_lastCapsState.Value != currentCaps)
                {
                    _lastCapsState = currentCaps;
                    hardwareChanged = true;
                }

                bool currentNum = GetNumLockState();
                if (!_lastNumState.HasValue)
                {
                    _lastNumState = currentNum;
                }
                else if (_lastNumState.Value != currentNum)
                {
                    _lastNumState = currentNum;
                    hardwareChanged = true;
                }

                bool currentScroll = GetScrollLockState();
                if (!_lastScrollState.HasValue)
                {
                    _lastScrollState = currentScroll;
                }
                else if (_lastScrollState.Value != currentScroll)
                {
                    _lastScrollState = currentScroll;
                    hardwareChanged = true;
                }

                bool currentAc = NativeMethods.IsAcLineOnline(defaultIfUnknown: false);
                if (!_lastAcState.HasValue)
                {
                    _lastAcState = currentAc;
                }
                else if (_lastAcState.Value != currentAc)
                {
                    _lastAcState = currentAc;
                    KeyboardEffects.HandlePowerStatusChanged(currentAc);
                    PowerLineChanged?.Invoke(currentAc);
                    hardwareChanged = true;
                }

                if (hardwareChanged)
                {
                    NotifyHardwareStateChanged();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.PollHardwareStates", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _pollInFlight, 0);
            }
        }

        private static void HandleAcpiEvent(int rawEventCode)
        {
            if (rawEventCode <= 0)
            {
                return;
            }
            bool isKnownMultiByte = rawEventCode == 259 || rawEventCode == 303 || rawEventCode == 304 ||
                                    rawEventCode == 24600 || rawEventCode == 24700 || rawEventCode == 24900;
            if (rawEventCode > 0xFF && !isKnownMultiByte)
            {
                return;
            }
            int eventCode = rawEventCode;

            lock (_acpiEventLock)
            {
                if (eventCode == _lastAcpiEventCode && MonotonicClock.ElapsedMs(_lastAcpiEventTicks) < 250.0)
                {
                    return;
                }
                _lastAcpiEventCode = eventCode;
                _lastAcpiEventTicks = MonotonicClock.NowTicks();
            }

            switch (eventCode)
            {
                case 37:
                    if (!MonotonicClock.IsBefore(_suppressEcKeyboardEventsUntilTicks))
                    {
                        ToggleFnLockFromEC();
                    }
                    break;
                case 38:
                    ToggleMicrophone();
                    break;
                case 58:
                    Fan.ToggleAutoMaxFan();
                    break;
                case 112:
                    Fan.SyncFanFromHardware(Fan.Mode.Auto, showOsd: true);
                    break;
                case 143:
                    Fan.SyncFanFromHardware(Fan.Mode.Max, showOsd: true);
                    break;
                case 64:
                    OsdOverlay.ShowFanAntiDust();
                    break;
                case 106:
                    Fan.SetMode(Fan.Mode.MaxQ);
                    OsdOverlay.ShowFan(Fan.Mode.MaxQ);
                    FanProfileChanged?.Invoke(Fan.Mode.MaxQ);
                    break;
                case 107:
                    Fan.SetMode(Fan.Mode.Custom);
                    OsdOverlay.ShowFan(Fan.Mode.Custom);
                    FanProfileChanged?.Invoke(Fan.Mode.Custom);
                    break;
                case 89:
                    PowerMode.CyclePowerMode();
                    break;
                case 84:
                    NativeMethods.PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
                    break;
                case 252:
                    Touchpad.HandleHardwareStateChange(false);
                    break;
                case 253:
                    Touchpad.HandleHardwareStateChange(true);
                    break;
                case 259:
                    Task.Delay(60).ContinueWith(_ =>
                    {
                        if (MonotonicClock.ElapsedMs(_lastHardwareTouchpadKeyTicks) >= 800.0 && !Touchpad.IsHardwareEventSuppressed)
                        {
                            _lastHardwareTouchpadKeyTicks = MonotonicClock.NowTicks();
                            Touchpad.ToggleTouchpad();
                        }
                    });
                    break;
                case 138:
                    CycleKeyboardBacklight();
                    break;
                case 32:
                case 129:
                    {
                        int? step = KeyboardBackLight.DecreaseBrightness();
                        if (step.HasValue)
                        {
                            OsdOverlay.ShowBacklight(step.Value);
                        }
                    }
                    break;
                case 33:
                case 130:
                    {
                        int? step = KeyboardBackLight.IncreaseBrightness();
                        if (step.HasValue)
                        {
                            OsdOverlay.ShowBacklight(step.Value);
                        }
                    }
                    break;
                case 63:
                case 158:
                case 159:
                    OsdOverlay.ShowBacklightToggle(KeyboardBackLight.ToggleBacklight());
                    break;
                case 131:
                    {
                        var preset = KeyboardBackLight.CycleColorPreset();
                        if (preset.HasValue)
                        {
                            OsdOverlay.ShowBacklightColor(preset.Value.Name);
                        }
                    }
                    break;
                case 132:
                    ToggleCamera();
                    break;
                case 246:
                case 24600:
                    _lastCameraState = false;
                    _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                    SetCameraHardwareState(false);
                    OsdOverlay.ShowCamera(false);
                    break;
                case 248:
                    _lastCameraState = false;
                    _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                    SetCameraHardwareState(false);
                    SetMicrophoneState(false);
                    OsdOverlay.ShowCameraMic(false);
                    break;
                case 247:
                case 24700:
                case 24900:
                    _lastCameraState = true;
                    _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                    SetCameraHardwareState(true);
                    OsdOverlay.ShowCamera(true);
                    break;
                case 249:
                    _lastCameraState = true;
                    _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
                    SetCameraHardwareState(true);
                    SetMicrophoneState(true);
                    OsdOverlay.ShowCameraMic(true);
                    break;
                case 303:
                case 304:
                    ShowMainWindowRequested?.Invoke();
                    break;
            }
        }
    }
}

