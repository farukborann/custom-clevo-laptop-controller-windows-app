using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BSCustomClevoController.Utility;


namespace BSCustomClevoController.Controls
{
    /// <summary>
    /// Direct Windows USB HID communication controller for ITE 8291 / 8297 keyboard backlight chips.
    /// Implements hardware animation packets and multi-directional wave from tuxedo-drivers.
    /// </summary>
    public static class IteHidController
    {
        private static readonly int[] SupportedVendorIds = new int[]
        {
            0x048D, // ITE
            0x373B  // Clevo MCU
        };

        private static readonly ushort[] SupportedProductIds = new ushort[]
        {
            0x10C9, // Clevo MCU Keyboard
            0x1107, // Clevo MCU Keyboard alternate
            0xCE00, // ITE 8291 default Clevo/Uniwill
            0x6004, // ITE 8291 variant 4
            0x600A, // ITE 8291 variant A
            0x600B, // ITE 8291 variant B
            0x8291, // ITE 8291 alternate PID
            0x8297, // ITE 8297 alternate PID
            0x8910, // ITE 8291 Light Bar controller
            0x6010, // ITE Light Bar variant 6010
            0x7000, // ITE Light Bar variant 7000
            0x7001  // ITE Light Bar variant 7001
        };

        private static readonly ushort[] LightBarProductIds = new ushort[]
        {
            0x8910,
            0x6010,
            0x7000,
            0x7001
        };

        private static bool _devicePresenceChecked = false;
        private static bool _devicePresent = false;
        private static bool _lightBarPresent = false;
        private static ushort _detectedPid = 0;
        private static string _cachedDevicePath = null;
        private static string _cachedLightBarPath = null;
        private static readonly object _lock = new object();

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid interfaceClassGuid;
            public int flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("hid.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] lpReportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, ref uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Returns true if an ITE USB HID keyboard controller is detected on this machine.
        /// </summary>
        public static bool IsDevicePresent
        {
            get
            {
                lock (_lock)
                {
                    if (!_devicePresenceChecked)
                    {
                        RefreshDevice();
                    }
                    return _devicePresent;
                }
            }
        }

        /// <summary>
        /// Returns true if an ITE USB HID Light Bar controller is detected on this machine.
        /// </summary>
        public static bool IsLightBarPresent
        {
            get
            {
                lock (_lock)
                {
                    if (!_devicePresenceChecked)
                    {
                        RefreshDevice();
                    }
                    return _lightBarPresent;
                }
            }
        }

        public static void RefreshDevice()
        {
            lock (_lock)
            {
                _cachedDevicePath = FindIteDevicePath(out _cachedLightBarPath, out _detectedPid);
                _devicePresent = !string.IsNullOrEmpty(_cachedDevicePath);
                _lightBarPresent = !string.IsNullOrEmpty(_cachedLightBarPath);
                _devicePresenceChecked = true;
            }
        }

        private static string FindIteDevicePath(out string lightBarPath, out ushort detectedPid)
        {
            lightBarPath = null;
            detectedPid = 0;
            string keyboardPath = null;

            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfo == INVALID_HANDLE_VALUE) return null;

            try
            {
                SpDeviceInterfaceData interfaceData = new SpDeviceInterfaceData();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);

                uint index = 0;
                while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
                {
                    string devicePath = TryReadDeviceInterfacePath(devInfo, ref interfaceData);
                    if (IsCandidateVendorPath(devicePath))
                    {
                        ClassifyIteHidDevice(devicePath, ref keyboardPath, ref lightBarPath, ref detectedPid);
                    }
                }
            }
            catch (Exception ex)
            {
                BSCustomClevoController.Utility.AppLogger.Log("IteHidController.FindDevicePath", ex);
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }

            return keyboardPath ?? lightBarPath;
        }

        private static string TryReadDeviceInterfacePath(IntPtr devInfo, ref SpDeviceInterfaceData interfaceData)
        {
            uint requiredSize = 0;
            SetupDiGetDeviceInterfaceDetail(devInfo, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
            uint minSize = (uint)(IntPtr.Size == 8 ? 8 : 6);
            if (requiredSize < minSize || requiredSize > 65536)
            {
                return null;
            }

            IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                {
                    return null;
                }

                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                return Marshal.PtrToStringAuto(pDevicePath);
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }

        private static bool IsCandidateVendorPath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                return false;
            }

            return devicePath.IndexOf("vid_048d", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   devicePath.IndexOf("vid_373b", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ClassifyIteHidDevice(
            string devicePath,
            ref string keyboardPath,
            ref string lightBarPath,
            ref ushort detectedPid)
        {
            IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE)
            {
                return;
            }

            try
            {
                HiddAttributes attrs = new HiddAttributes();
                attrs.Size = Marshal.SizeOf(attrs);
                if (!HidD_GetAttributes(handle, ref attrs) || Array.IndexOf(SupportedVendorIds, (int)attrs.VendorID) < 0)
                {
                    return;
                }

                if (Array.IndexOf(LightBarProductIds, attrs.ProductID) >= 0)
                {
                    lightBarPath = devicePath;
                    detectedPid = attrs.ProductID;
                }

                if (keyboardPath == null && Array.IndexOf(SupportedProductIds, attrs.ProductID) >= 0)
                {
                    keyboardPath = devicePath;
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool SendCustomFeatureReport(string devicePath, byte[] reportBuffer)
        {
            if (string.IsNullOrEmpty(devicePath)) return false;

            IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) return false;

            try
            {
                return HidD_SetFeature(handle, reportBuffer, reportBuffer.Length);
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool SendFeatureReport(byte[] reportBuffer)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_cachedDevicePath))
                {
                    RefreshDevice();
                    if (string.IsNullOrEmpty(_cachedDevicePath)) return false;
                }

                return SendCustomFeatureReport(_cachedDevicePath, reportBuffer);
            }
        }

        /// <summary>
        /// Sets light bar RGB color and brightness (from tuxedo-drivers ite_8291_lb.c).
        /// Falls back to Clevo DCHU zone 4 control when direct USB HID is absent.
        /// </summary>
        public static bool SetLightBarColor(byte r, byte g, byte b, byte brightness = 100)
        {
            if (brightness > 100) brightness = 100;

            string lightBarPath = null;
            ushort pid = 0;
            lock (_lock)
            {
                if (_lightBarPresent && !string.IsNullOrEmpty(_cachedLightBarPath))
                {
                    lightBarPath = _cachedLightBarPath;
                    pid = _detectedPid;
                }
            }

            if (lightBarPath != null)
            {
                byte[] colorPacket;
                byte[] ctrlPacket;

                switch (pid)
                {
                    case 0x7000:
                        colorPacket = new byte[] { 0x14, 0x01, 0x01, r, g, b, 0x00, 0x00 };
                        ctrlPacket = new byte[] { 0x08, 0x21, 0x01, 0x01, brightness, 0x01, 0x00, 0x00 };
                        break;
                    case 0x7001:
                        colorPacket = new byte[] { 0x14, 0x00, 0x01, r, g, b, 0x00, 0x00 };
                        ctrlPacket = new byte[] { 0x08, 0x22, 0x01, 0x01, brightness, 0x01, 0x00, 0x00 };
                        break;
                    default:
                        colorPacket = new byte[] { 0x14, 0x00, 0x01, r, g, b, 0x00, 0x00 };
                        ctrlPacket = new byte[] { 0x08, 0x02, 0x01, 0x01, brightness, 0x08, 0x00, 0x00 };
                        break;
                }

                lock (_lock)
                {
                    SendCustomFeatureReport(lightBarPath, colorPacket);
                    return SendCustomFeatureReport(lightBarPath, ctrlPacket);
                }
            }

            KeyboardBackLight.SetZoneColourRealtime(3, r, g, b);
            return true;
        }

        /// <summary>
        /// Powers off the chassis light bar.
        /// </summary>
        public static bool SetLightBarOff()
        {
            string lightBarPath = null;
            lock (_lock)
            {
                if (_lightBarPresent && !string.IsNullOrEmpty(_cachedLightBarPath))
                {
                    lightBarPath = _cachedLightBarPath;
                }
            }

            if (lightBarPath != null)
            {
                SetLightBarColor(0, 0, 0, 0);
                byte[] offPacket = new byte[] { 0x08, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                lock (_lock)
                {
                    return SendCustomFeatureReport(lightBarPath, offPacket);
                }
            }

            KeyboardBackLight.SetZoneColourRealtime(3, 0, 0, 0);
            return true;
        }

        /// <summary>
        /// Converts a 0-255 brightness level to the 0x00-0x32 hardware scale used by ITE 8291.
        /// </summary>
        public static byte ScaleBrightness(byte rawBrightness)
        {
            if (rawBrightness == 0) return 0;
            int scaled = (int)Math.Round(rawBrightness / 255.0 * 0x32);
            return (byte)Math.Max(1, Math.Min(0x32, scaled));
        }

        /// <summary>
        /// Sends a direct ITE 8291 hardware animation control packet (tuxedo-drivers ite_8291).
        /// Control: 08 [power state] [anim mode] [speed] [brightness] 08 [behaviour] 00
        /// </summary>
        public static bool SetHardwareAnimation(byte animMode, byte speed = 0x05, byte brightness = 0x32, byte behaviour = 0x00)
        {
            byte[] packet = new byte[] { 0x08, 0x02, animMode, speed, brightness, 0x08, behaviour, 0x00 };
            return SendFeatureReport(packet);
        }

        public static bool TurnOff()
        {
            byte[] packet = new byte[] { 0x08, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            return SendFeatureReport(packet);
        }
    }
}
