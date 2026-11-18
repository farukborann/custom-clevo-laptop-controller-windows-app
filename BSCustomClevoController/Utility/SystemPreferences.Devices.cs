using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using BSCustomClevoController.Controls;

namespace BSCustomClevoController.Utility
{
    public static partial class SystemPreferences
    {
        [DllImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint CoCreateInstance(ref Guid clsid, IntPtr pv, uint ctx, ref Guid iid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSystemRadioStateDelegate(IntPtr cg, out int radioState, out int settingSource, out int p3);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetSystemRadioStateDelegate(IntPtr ptr, int state);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr ptr);

        private static readonly Guid CLSID_RadioManager = new Guid("581333F6-28DB-41BE-BC7A-FF201F12F3F6");
        private static readonly Guid IID_RadioManager = new Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7");
        private static readonly Guid GUID_DEVCLASS_CAMERA = new Guid("ca3e7ab9-b4c3-4ae6-8251-579ef933890f");
        private static readonly Guid GUID_DEVCLASS_IMAGE = new Guid("6bdd1fc6-810f-11d0-bec7-08002be2092f");
        private static readonly Guid GUID_DEVCLASS_DISPLAY = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIF_PROPERTYCHANGE = 0x00000012;
        private const uint DICS_ENABLE = 0x00000001;
        private const uint DICS_DISABLE = 0x00000002;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;
        private const uint DN_HAS_PROBLEM = 0x00000400;
        private const uint CM_PROB_DISABLED = 0x00000016;
        private const string ConsentAllow = "Allow";
        private const string ConsentValueName = "Value";

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDevInfoData
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpClassInstallHeader
        {
            public int cbSize;
            public int InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpPropChangeParams
        {
            public SpClassInstallHeader ClassInstallHeader;
            public int StateChange;
            public int Scope;
            public int HwProfile;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            [MarshalAs(UnmanagedType.LPStr)] string Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SpDevInfoData DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiSetClassInstallParams(
            IntPtr DeviceInfoSet,
            ref SpDevInfoData DeviceInfoData,
            ref SpPropChangeParams ClassInstallParams,
            int ClassInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiCallClassInstaller(
            uint InstallFunction,
            IntPtr DeviceInfoSet,
            ref SpDevInfoData DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiGetDeviceInstanceId(
            IntPtr DeviceInfoSet,
            ref SpDevInfoData DeviceInfoData,
            StringBuilder DeviceInstanceId,
            int DeviceInstanceIdSize,
            out int RequiredSize);

        [DllImport("cfgmgr32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint CM_Get_DevNode_Status(
            out uint pulStatus,
            out uint pulProblemNumber,
            uint dnDevInst,
            uint ulFlags);

        private static readonly object _dgpuLock = new object();

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private sealed class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out int pcDevices);

            [PreserveSig]
            int Item(int nDevice, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int GetChannelCount(out int pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private const int VK_AIRPLANE = 0x84;
        private static long _lastMicToggleTicks;

        private static int CountClassDevices(Guid classGuid, uint flags)
        {
            int count = 0;
            IntPtr hDevInfo = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, flags);
            if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
            {
                return 0;
            }

            try
            {
                SpDevInfoData data = new SpDevInfoData { cbSize = Marshal.SizeOf(typeof(SpDevInfoData)) };
                uint idx = 0;
                while (SetupDiEnumDeviceInfo(hDevInfo, idx, ref data))
                {
                    count++;
                    idx++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return count;
        }

        public static bool GetCameraState()
        {
            if (HasDisabledCameraDevNode())
            {
                return false;
            }

            try
            {
                int installedCameras = CountClassDevices(GUID_DEVCLASS_CAMERA, 0);
                int presentCameras = CountClassDevices(GUID_DEVCLASS_CAMERA, DIGCF_PRESENT);
                if (installedCameras > 0 && presentCameras == 0)
                {
                    return false;
                }

                return !IsWebcamConsentDenied(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam")
                    && !IsWebcamConsentDenied(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam");
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetCameraState.Consent", ex);
            }

            return true;
        }

        private static bool HasDisabledCameraDevNode()
        {
            try
            {
                Guid[] guids = { GUID_DEVCLASS_CAMERA, GUID_DEVCLASS_IMAGE };
                return guids.Any(HasDisabledDeviceInClass);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetCameraState.DevNode", ex);
                return false;
            }
        }

        private static bool HasDisabledDeviceInClass(Guid classGuid)
        {
            Guid guidCopy = classGuid;
            IntPtr hDevInfo = SetupDiGetClassDevs(ref guidCopy, null, IntPtr.Zero, 0);
            if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
            {
                return false;
            }

            try
            {
                SpDevInfoData devInfoData = new SpDevInfoData { cbSize = Marshal.SizeOf(typeof(SpDevInfoData)) };
                uint index = 0;
                while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devInfoData))
                {
                    index++;
                    if (CM_Get_DevNode_Status(out uint status, out uint problem, devInfoData.DevInst, 0) == 0 &&
                        (problem == CM_PROB_DISABLED || (status & DN_HAS_PROBLEM) != 0))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return false;
        }

        private static bool IsWebcamConsentDenied(string registryPath)
        {
            object val = Registry.GetValue(registryPath, ConsentValueName, ConsentAllow);
            return val != null && string.Equals(val.ToString(), "Deny", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ToggleCamera()
        {
            bool target = !GetCameraState();
            _lastCameraState = target;
            _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2500.0);
            SetCameraHardwareState(target);
            OsdOverlay.ShowCamera(target);
            return target;
        }

        public static void SetDeviceEnabled(Guid classGuid, string[] instanceIdFilters, bool enable)
        {
            try
            {
                Guid guidCopy = classGuid;
                IntPtr hDevInfo = SetupDiGetClassDevs(ref guidCopy, null, IntPtr.Zero, 0);
                if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
                    return;

                try
                {
                    SpDevInfoData devInfoData = new SpDevInfoData { cbSize = Marshal.SizeOf(typeof(SpDevInfoData)) };
                    uint index = 0;

                    while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devInfoData))
                    {
                        index++;
                        string instanceId = ReadDeviceInstanceId(hDevInfo, ref devInfoData);
                        if (MatchesAnyInstanceFilter(instanceId, instanceIdFilters))
                        {
                            ApplyDeviceEnableChange(hDevInfo, ref devInfoData, instanceId, enable);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetDeviceEnabled", ex);
            }
        }

        private static string ReadDeviceInstanceId(IntPtr hDevInfo, ref SpDevInfoData devInfoData)
        {
            StringBuilder instanceIdBuf = new StringBuilder(512);
            return SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, instanceIdBuf, instanceIdBuf.Capacity, out _)
                ? instanceIdBuf.ToString()
                : string.Empty;
        }

        private static bool MatchesAnyInstanceFilter(string instanceId, string[] instanceIdFilters)
        {
            if (instanceIdFilters == null || instanceIdFilters.Length == 0)
            {
                return true;
            }
            if (string.IsNullOrEmpty(instanceId))
            {
                return false;
            }
            return Array.Exists(instanceIdFilters, filter => instanceId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void ApplyDeviceEnableChange(IntPtr hDevInfo, ref SpDevInfoData devInfoData, string instanceId, bool enable)
        {
            SpPropChangeParams pcp = new SpPropChangeParams
            {
                ClassInstallHeader = new SpClassInstallHeader
                {
                    cbSize = Marshal.SizeOf(typeof(SpClassInstallHeader)),
                    InstallFunction = (int)DIF_PROPERTYCHANGE
                },
                StateChange = enable ? (int)DICS_ENABLE : (int)DICS_DISABLE,
                Scope = (int)DICS_FLAG_GLOBAL,
                HwProfile = 0
            };

            int pcpSize = Marshal.SizeOf(typeof(SpPropChangeParams));
            if (!SetupDiSetClassInstallParams(hDevInfo, ref devInfoData, ref pcp, pcpSize))
            {
                pcp.Scope = (int)DICS_FLAG_CONFIGSPECIFIC;
                SetupDiSetClassInstallParams(hDevInfo, ref devInfoData, ref pcp, pcpSize);
            }

            SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, hDevInfo, ref devInfoData);

            if (!IsSafeDeviceInstanceId(instanceId))
            {
                return;
            }

            try
            {
                string cmd = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
                ProcessRunner.Run(
                    ProcessRunner.ResolveSystemTool("powershell.exe"),
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{cmd} -InstanceId '{instanceId}' -Confirm:$false -ErrorAction SilentlyContinue\"",
                    3000,
                    out _);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetDeviceEnabled.Ps", ex);
            }

            try
            {
                ProcessRunner.Run(
                    ProcessRunner.ResolveSystemTool("pnputil.exe"),
                    (enable ? "/enable-device \"" : "/disable-device \"") + instanceId + "\"",
                    2000,
                    out _);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetDeviceEnabled.PnpUtil", ex);
            }
        }

        private static bool IsSafeDeviceInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 200)
            {
                return false;
            }

            for (int i = 0; i < instanceId.Length; i++)
            {
                char c = instanceId[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '_' || c == '\\' || c == '&' || c == '#' ||
                          c == '.' || c == '-' || c == '{' || c == '}' ||
                          c == '+' || c == ':';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        public static void SetDiscreteGpuState(bool enable)
        {
            lock (_dgpuLock)
            {
                try
                {
                    string[] discreteGpuFilters = new string[] { "PCI\\VEN_10DE", "PCI\\VEN_1002" };

                    if (enable)
                    {
                        Thread.Sleep(300);
                        RunScanDevices();
                        SetDeviceEnabled(GUID_DEVCLASS_DISPLAY, discreteGpuFilters, true);
                        RunScanDevices();

                        NvidiaPowerControl.ResetDetection();
                        if (NvidiaPowerControl.IsNvidiaPresent)
                        {
                            NvidiaPowerControl.SetTgpOffset(NvidiaPowerControl.GetTgpOffset());
                        }
                    }
                    else
                    {
                        SetDeviceEnabled(GUID_DEVCLASS_DISPLAY, discreteGpuFilters, false);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("SystemPreferences.SetDiscreteGpuState", ex);
                }
            }
        }

        private static void RunScanDevices()
        {
            try
            {
                ProcessRunner.Run("pnputil.exe", "/scan-devices", 3000, out _);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.RunScanDevices", ex);
            }
        }

        public static void SetCameraHardwareState(bool enable)
        {
            try
            {
                Guid[] guids = new Guid[] { GUID_DEVCLASS_CAMERA, GUID_DEVCLASS_IMAGE };
                foreach (Guid classGuid in guids)
                {
                    SetDeviceEnabled(classGuid, null, enable);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetCameraHardwareState.Dev", ex);
            }

            string regValue = enable ? ConsentAllow : "Deny";
            try
            {
                Registry.SetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                    ConsentValueName,
                    regValue,
                    RegistryValueKind.String);
            }
            catch
            {
                // HKLM write requires elevation when not elevated
            }

            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", ConsentValueName, regValue, RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged", ConsentValueName, regValue, RegistryValueKind.String);

                using (RegistryKey consentKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", true))
                {
                    if (consentKey != null)
                    {
                        foreach (string subName in consentKey.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey subKey = consentKey.OpenSubKey(subName, true))
                                {
                                    subKey?.SetValue(ConsentValueName, regValue, RegistryValueKind.String);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log("SystemPreferences.SetCameraHardwareState.ConsentSub", ex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetCameraHardwareState.Consent", ex);
            }

            _lastCameraState = enable;
            _suppressCameraOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2500.0);

            try
            {
                byte[] camBytes = BitConverter.GetBytes(enable ? 1 : 0);
                InsydeDriver.WriteAppSettings(1, 6, 1, ref camBytes[0]);
                InsydeDriver.SetDCHU_Data(34, camBytes, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetCameraHardwareState.Ec", ex);
            }

            SendHKeyMessage(257, (byte)(enable ? 1 : 0));
            NotifyHardwareStateChanged();
        }

        private static void ReleaseComObjectSafe(object comObj)
        {
            if (comObj != null && Marshal.IsComObject(comObj))
            {
                Marshal.ReleaseComObject(comObj);
            }
        }

        public static bool GetMicrophoneState()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (object)new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
                if (enumerator != null &&
                    enumerator.EnumAudioEndpoints(1, 1, out collection) == 0 &&
                    collection != null)
                {
                    collection.GetCount(out int count);
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice device = null;
                        object epObj = null;
                        try
                        {
                            if (collection.Item(i, out device) == 0 && device != null &&
                                device.Activate(ref iid, 23, IntPtr.Zero, out epObj) == 0 &&
                                epObj is IAudioEndpointVolume endpointVolume &&
                                endpointVolume.GetMute(out bool isMuted) == 0)
                            {
                                return !isMuted;
                            }
                        }
                        finally
                        {
                            ReleaseComObjectSafe(epObj);
                            ReleaseComObjectSafe(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetMicrophoneState.Com", ex);
            }
            finally
            {
                ReleaseComObjectSafe(collection);
                ReleaseComObjectSafe(enumerator);
            }

            try
            {
                string val = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
                    ConsentValueName,
                    null) as string;
                if (!string.IsNullOrEmpty(val))
                {
                    return !string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetMicrophoneState.Reg", ex);
            }

            try
            {
                byte val = 0;
                if (InsydeDriver.ReadAppSettings(1, 41, 1, ref val) != 0)
                {
                    return val == 0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetMicrophoneState.Ec", ex);
            }

            return true;
        }

        public static bool ToggleMicrophone()
        {
            if (MonotonicClock.ElapsedMs(_lastMicToggleTicks) < 250.0)
            {
                return _lastMicState ?? GetMicrophoneState();
            }
            _lastMicToggleTicks = MonotonicClock.NowTicks();
            bool target = !GetMicrophoneState();
            SetMicrophoneState(target);
            OsdOverlay.ShowMicrophone(target);
            return target;
        }

        public static void SetMicrophoneState(bool enable)
        {
            _lastMicState = enable;
            _suppressMicOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2500.0);

            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (object)new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
                if (enumerator != null &&
                    enumerator.EnumAudioEndpoints(1, 1, out collection) == 0 &&
                    collection != null)
                {
                    collection.GetCount(out int count);
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    Guid ctx = Guid.Empty;
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice device = null;
                        object epObj = null;
                        try
                        {
                            if (collection.Item(i, out device) == 0 && device != null &&
                                device.Activate(ref iid, 23, IntPtr.Zero, out epObj) == 0 &&
                                epObj is IAudioEndpointVolume endpointVolume)
                            {
                                endpointVolume.SetMute(!enable, ref ctx);
                            }
                        }
                        finally
                        {
                            ReleaseComObjectSafe(epObj);
                            ReleaseComObjectSafe(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetMicrophoneState.Com", ex);
            }
            finally
            {
                ReleaseComObjectSafe(collection);
                ReleaseComObjectSafe(enumerator);
            }

            string regValue = enable ? ConsentAllow : "Deny";
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", ConsentValueName, regValue, RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged", ConsentValueName, regValue, RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetMicrophoneState.Hkcu", ex);
            }

            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", ConsentValueName, regValue, RegistryValueKind.String);
            }
            catch
            {
                // HKLM write requires elevation when not elevated
            }

            try
            {
                byte mutedByte = (byte)(enable ? 0 : 1);
                InsydeDriver.WriteAppSettings(1, 41, 1, ref mutedByte);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.SetMicrophoneState.Ec", ex);
            }

            NotifyHardwareStateChanged();
        }

        private static T RunInSta<T>(Func<T> action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                return action();
            }

            T result = default;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            })
            {
                IsBackground = true,
                Name = "RadioManagerSta"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw error;
            }
            return result;
        }

        private static bool TryOpenRadioManagerVtable(out IntPtr irm, out IntPtr[] methods, out ReleaseDelegate release)
        {
            irm = IntPtr.Zero;
            methods = null;
            release = null;

            Guid clsid = CLSID_RadioManager;
            Guid iid = IID_RadioManager;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out irm) != 0 || irm == IntPtr.Zero)
            {
                irm = IntPtr.Zero;
                return false;
            }

            IntPtr vtbl = Marshal.ReadIntPtr(irm);
            if (vtbl == IntPtr.Zero)
            {
                return false;
            }

            methods = new IntPtr[8];
            Marshal.Copy(vtbl, methods, 0, 8);
            if (methods[2] == IntPtr.Zero || methods[5] == IntPtr.Zero)
            {
                return false;
            }

            release = (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(methods[2], typeof(ReleaseDelegate));
            return true;
        }

        public static bool GetAirplaneModeState()
        {
            try
            {
                return RunInSta(() =>
                {
                    IntPtr irm = IntPtr.Zero;
                    ReleaseDelegate release = null;
                    try
                    {
                        if (TryOpenRadioManagerVtable(out irm, out IntPtr[] methods, out release))
                        {
                            var getState = (GetSystemRadioStateDelegate)Marshal.GetDelegateForFunctionPointer(methods[5], typeof(GetSystemRadioStateDelegate));
                            if (getState(irm, out int currentState, out _, out _) == 0)
                            {
                                return currentState == 0;
                            }
                        }
                    }
                    finally
                    {
                        if (irm != IntPtr.Zero && release != null)
                        {
                            release(irm);
                        }
                    }
                    return false;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.GetAirplaneModeState", ex);
            }
            return false;
        }

        public static bool ToggleAirplaneMode()
        {
            SendHKeyMessage(264, 0);

            bool newActiveState = false;
            bool toggledViaCom = false;
            try
            {
                (toggledViaCom, newActiveState) = RunInSta(() =>
                {
                    IntPtr irm = IntPtr.Zero;
                    ReleaseDelegate release = null;
                    try
                    {
                        if (TryOpenRadioManagerVtable(out irm, out IntPtr[] methods, out release) && methods[6] != IntPtr.Zero)
                        {
                            var getState = (GetSystemRadioStateDelegate)Marshal.GetDelegateForFunctionPointer(methods[5], typeof(GetSystemRadioStateDelegate));
                            var setState = (SetSystemRadioStateDelegate)Marshal.GetDelegateForFunctionPointer(methods[6], typeof(SetSystemRadioStateDelegate));
                            if (getState(irm, out int currentState, out _, out _) == 0)
                            {
                                int targetState = (currentState == 0) ? 1 : 0;
                                setState(irm, targetState);
                                return (true, targetState == 0);
                            }
                        }
                    }
                    finally
                    {
                        if (irm != IntPtr.Zero && release != null)
                        {
                            release(irm);
                        }
                    }
                    return (false, false);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log("SystemPreferences.ToggleAirplaneMode", ex);
            }

            if (!toggledViaCom)
            {
                NativeMethods.keybd_event(VK_AIRPLANE, 0x47, 0, 0);
                NativeMethods.keybd_event(VK_AIRPLANE, 0x47, NativeMethods.KEYEVENTF_KEYUP, 0);
            }

            _lastAirplaneState = newActiveState;
            _suppressAirplaneOsdUntilTicks = MonotonicClock.DeadlineFromNowMs(2000.0);
            OsdOverlay.ShowAirplane(newActiveState);
            NotifyHardwareStateChanged();
            return newActiveState;
        }
    }
}

