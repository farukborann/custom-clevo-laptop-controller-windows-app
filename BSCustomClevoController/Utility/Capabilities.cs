using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace BSCustomClevoController.Utility
{
    public class CapabilityItem
    {
        private string _name;

        public string Name
        {
            get => !string.IsNullOrEmpty(RawKey) ? LocalizationManager.T("Cap_" + RawKey, _name) : _name;
            set => _name = value;
        }

        public bool IsSupported { get; set; }
        public string RawKey { get; set; }

        public string StatusText => IsSupported
            ? LocalizationManager.T("SettingsSupported")
            : LocalizationManager.T("SettingsNotSupported");

        public override string ToString()
        {
            return $"{Name}: {StatusText}";
        }
    }

    public class Capabilities
    {
        public static int GetDCHU_Data_Integer(int command, ref int data)
        {
            return InsydeDriver.GetDCHU_Data_Integer(command, ref data);
        }

        public static int SetDCHU_Data(int command, byte[] buffer, int length)
        {
            return InsydeDriver.SetDCHU_Data(command, buffer, length);
        }

        private static readonly object CacheLock = new object();
        private static Dictionary<string, bool> _cachedMap;

        public static bool IsSupported(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey))
            {
                return false;
            }

            lock (CacheLock)
            {
                if (_cachedMap == null)
                {
                    var list = Check();
                    var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in list.Where(item => !string.IsNullOrEmpty(item.RawKey)))
                    {
                        map[item.RawKey] = item.IsSupported;
                    }
                    _cachedMap = map;
                }

                return _cachedMap.TryGetValue(rawKey, out bool val) && val;
            }
        }

        public static void InvalidateCache()
        {
            lock (CacheLock)
            {
                _cachedMap = null;
            }
        }

        public static bool SetWMI(int command, uint data)
        {
            try
            {
                byte[] buffer = BitConverter.GetBytes(data);
                int res = SetDCHU_Data(command, buffer, 4);
                return res >= 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("Capabilities.SetWMI", ex);
                return false;
            }
        }

        public static bool SetKeyboardAnimationSpeed(int speed)
        {
            // Clevo DCHU CMD 527 sets keyboard animation speed: 1 (Slow), 2 (Normal), 3 (Fast)
            int val = Math.Max(1, Math.Min(3, speed));
            return SetWMI(527, (uint)val);
        }

        private static bool SetWmiFlag(int command, bool enable, uint enableMask)
        {
            return SetWMI(command, enable ? enableMask : 0u);
        }

        public static bool SetEnergyStar15C(bool enable) => SetWmiFlag(82, enable, 2097152u);

        public static bool SetDynamicTuning(bool enable) => SetWmiFlag(96, enable, 512u);

        public static bool SetWakeOnLan(bool enable) => SetWmiFlag(60, enable, 32u);

        public static bool SetEthernetLed(bool enable) => SetWmiFlag(122, enable, 2097152u);

        public static bool ResetUsbHub() => SetWMI(122, 256u);

        // CMD 122 bit 20: 1048576 (0 = MSHybrid, 1 = Discrete Only)
        public static bool SetGpuMuxMode(bool discreteOnly) => SetWmiFlag(122, discreteOnly, 1048576u);

        public static bool IsMaxQSupported()
        {
            try
            {
                if (Global.support_bit != null && Global.support_bit.MaxQ)
                    return true;

                if (Convert.ToBoolean(Registry.GetValue("HKEY_CURRENT_USER\\Software\\ControlCenter2.0\\EC\\", "SupportMaxQ_FAN", false)))
                    return true;

                byte[] appData = Global.insyde.GetAPPData(4, 0, 256);
                if (appData != null && appData.Length > 5 && (appData[4] == 5 || appData[5] == 5))
                {
                    if (Global.support_bit != null)
                        Global.support_bit.MaxQ = true;
                    return true;
                }

                byte[] wmi13 = Global.insyde.GetWMIPackage(13);
                if (wmi13 != null && wmi13.Length > 14 && wmi13[14] == 5)
                {
                    if (Global.support_bit != null)
                        Global.support_bit.MaxQ = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Capabilities.IsMaxQSupported", ex);
            }
            return false;
        }

        public static List<CapabilityItem> Check()
        {
            try
            {
                Features features = new Features();
                features.FanExists = true;
                features.SupportFanOffset = true;

                int cmd16 = GetWMI(16);
                int cmd60 = GetWMI(60);
                int cmd82 = GetWMI(82);
                int cmd96 = GetWMI(96);
                int cmd122 = GetWMI(122);

                DoWMIFlag(82, cmd82, ref features);
                DoWMIFlag(122, cmd122, ref features);
                DoWMIFlag(96, cmd96, ref features);
                DoWMIFlag(60, cmd60, ref features);
                DoWMIFlag(16, cmd16, ref features);

                return GetFeatures(features, cmd16, cmd96);
            }
            catch (Exception ex)
            {
                return new List<CapabilityItem>
                {
                    new CapabilityItem
                    {
                        Name = LocalizationManager.Format("CapQueryUnavailableFmt", ex.Message),
                        IsSupported = false
                    }
                };
            }
        }

        private static List<CapabilityItem> GetFeatures(Features features, int cmd16, int cmd96)
        {
            var list = new List<CapabilityItem>();

            list.Add(new CapabilityItem { Name = "Power Profiles", IsSupported = features.SupportPowerMode, RawKey = "SupportPowerMode" });
            list.Add(new CapabilityItem { Name = "CPU Overclocking", IsSupported = features.SupportCPUOC, RawKey = "SupportCPUOC" });
            list.Add(new CapabilityItem { Name = "GPU Overclocking", IsSupported = features.SupportGPUOC || features.SupportGPUOC_WMI == 1, RawKey = "SupportGPUOC" });
            list.Add(new CapabilityItem { Name = "GPU Overclocking in All Modes", IsSupported = features.SupportAllModeGPUOC, RawKey = "SupportAllModeGPUOC" });
            list.Add(new CapabilityItem { Name = "Energy Saving Mode", IsSupported = features.SupportEnergySave, RawKey = "SupportEnergySave" });
            list.Add(new CapabilityItem { Name = "Battery Utility", IsSupported = features.SupportBatteryUtility, RawKey = "SupportBatteryUtility" });
            list.Add(new CapabilityItem { Name = "Battery Charge Control", IsSupported = features.BatteryChargeControl == 1, RawKey = "BatteryChargeControl" });
            list.Add(new CapabilityItem { Name = "Energy Star", IsSupported = features.EnergyStar15C == 1, RawKey = "EnergyStar15C" });
            list.Add(new CapabilityItem { Name = "Hardware P-States", IsSupported = features.SupportHW_P_state, RawKey = "SupportHW_P_state" });
            list.Add(new CapabilityItem { Name = "Dynamic Tuning", IsSupported = ((cmd96 >> 12) & 1) == 1 || features.SupportDisablePCIE_ASPM, RawKey = "DynamicTuning" });

            bool perKey = (cmd16 & 8) == 8;
            bool rgb = (cmd16 & 16) == 16;
            list.Add(new CapabilityItem { Name = "RGB Keyboard", IsSupported = rgb, RawKey = "RGBKeyboard" });
            list.Add(new CapabilityItem { Name = "Per-Key Keyboard", IsSupported = perKey, RawKey = "PerKeyKeyboard" });
            list.Add(new CapabilityItem { Name = "Keyboard Sleep Timer", IsSupported = features.SupportKBLEDsleep_WMI == 1, RawKey = "SupportKBLEDsleep_WMI" });
            list.Add(new CapabilityItem { Name = "FlexiKey", IsSupported = features.SupportFlexikey, RawKey = "SupportFlexikey" });
            list.Add(new CapabilityItem { Name = "FlexiAccess", IsSupported = features.SupportFlexiAccess, RawKey = "SupportFlexiAccess" });
            list.Add(new CapabilityItem { Name = "Light Bar", IsSupported = features.SupportLightbar, RawKey = "SupportLightbar" });
            list.Add(new CapabilityItem { Name = "Logo Lighting", IsSupported = features.SupportLogoColor, RawKey = "SupportLogoColor" });
            list.Add(new CapabilityItem { Name = "Touchpad Lighting", IsSupported = features.SupportTpColor, RawKey = "SupportTpColor" });

            list.Add(new CapabilityItem { Name = "Fan Control", IsSupported = features.FanSpeedSupport || features.SupportFanSpeedSetting, RawKey = "FanSpeedSupport" });
            list.Add(new CapabilityItem { Name = "Custom Fan Curves", IsSupported = features.CustomFanSupport, RawKey = "CustomFanSupport" });
            list.Add(new CapabilityItem { Name = "Fan Offset", IsSupported = features.SupportFanOffset, RawKey = "SupportFanOffset" });
            list.Add(new CapabilityItem { Name = "Dust Cleaning", IsSupported = features.SupportAntiDust_Fan, RawKey = "SupportAntiDust_Fan" });
            list.Add(new CapabilityItem { Name = "Fanless Mode", IsSupported = features.SupportFanLess, RawKey = "SupportFanLess" });
            list.Add(new CapabilityItem { Name = "Max-Q Mode", IsSupported = IsMaxQSupported(), RawKey = "SupportMaxQ" });

            list.Add(new CapabilityItem { Name = "Wake on LAN", IsSupported = features.WakeUpLanSupport, RawKey = "WakeUpLanSupport" });
            list.Add(new CapabilityItem { Name = "XMP Profiles", IsSupported = features.SupportXMP, RawKey = "SupportXMP" });
            list.Add(new CapabilityItem { Name = "Hybrid Graphics Switch", IsSupported = features.SupportMSHybrid_dGPUSwicth, RawKey = "SupportMSHybrid_dGPUSwicth" });
            list.Add(new CapabilityItem { Name = "Ethernet LED", IsSupported = features.SupportEthernetLED, RawKey = "SupportEthernetLED" });
            list.Add(new CapabilityItem { Name = "3D Headphones", IsSupported = features.Support3dHeadphone || features.SupportWmi3dHp, RawKey = "Support3dHeadphone" });
            list.Add(new CapabilityItem { Name = "ESS Audio", IsSupported = features.SupportEss, RawKey = "SupportEss" });
            list.Add(new CapabilityItem { Name = "Boot CPU Voltage Tuning", IsSupported = features.SupportBootChangeCPUVoltage, RawKey = "SupportBootChangeCPUVoltage" });
            list.Add(new CapabilityItem { Name = "USB Hub Reset", IsSupported = features.SupportUsbHubReset, RawKey = "SupportUsbHubReset" });

            list.Sort((a, b) =>
            {
                if (a.IsSupported != b.IsSupported)
                {
                    return a.IsSupported ? -1 : 1;
                }
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        static int GetWMI(int command)
        {
            int data = 0;
            GetDCHU_Data_Integer(command, ref data);
            return data;
        }

        struct Features
        {
            public int EnergyStar15C;
            public int BatteryChargeControl;
            public bool FanSpeedSupport;
            public bool SupportGamingGroup;
            public bool Support3dHeadphone;
            public bool SupportTpColor;
            public bool CustomFanSupport;
            public bool WakeUpLanSupport;
            public bool SupportCheckNotAirplaneOsd;
            public bool SupportUsbHubReset;
            public bool SupportBootChangeCPUVoltage;
            public bool SupportLightbar;
            public bool SupportFanLess;
            public bool FanExists;
            public bool SupportTpSmbios;
            public bool SupportLogoColor;
            public bool SupportWmi3dHp;
            public bool SupportMSHybrid_dGPUSwicth;
            public bool SupportEthernetLED;
            public bool SupportEss;
            public bool SupportXMP;
            public int SupportGPUOC_WMI;
            public int SupportGPUcustom_WMI;
            public int SupportKBLEDsleep_WMI;
            public bool SupportHW_P_state;
            public bool NoSupportHW_KB_Type;
            public bool CheckUnigine;
            public bool SupportNewMode;
            public int WakeUpLanState;
            public bool SupportAllModeGPUOC;
            public bool SupportAntiDust_Fan;
            public bool SupportDisablePCIE_ASPM;
            public bool SupportPowerMode;
            public bool SupportFlexikey;
            public bool SupportFlexiAccess;
            public bool SupportGPUOC;
            public bool SupportCPUOC;
            public bool SupportFanSpeedSetting;
            public bool SupportEnergySave;
            public bool SupportBatteryUtility;
            public bool SupportFanOffset;
        }

        static void DoWMIFlag(int command, int WMI_Data, ref Features features)
        {
            if (command <= 61)
            {
                if (command <= 16)
                {
                    if (command == 6 || command == 9 || command != 16)
                        return;
                    features.SupportPowerMode = (WMI_Data & 1) == 1;
                    features.SupportFlexikey = (WMI_Data & 2) == 2;
                    features.SupportFlexiAccess = (WMI_Data & 4) == 4;
                    features.SupportGPUOC = (WMI_Data & 32) == 32;
                    features.SupportCPUOC = (WMI_Data & 64) == 64;
                    features.SupportFanSpeedSetting = (WMI_Data & 128) == 128;
                    features.SupportEnergySave = (WMI_Data & 256) == 256;
                    features.SupportBatteryUtility = (WMI_Data & 512) == 512;
                }
                else
                {
                    if (command == 17)
                        return;
                    if (command == 60)
                    {
                        features.WakeUpLanState = (WMI_Data & 32) != 32 ? 0 : 1;
                    }
                }
            }
            else if (command <= 82)
            {
                if (command == 82)
                {
                    if ((WMI_Data & 2097152) == 2097152)
                        features.EnergyStar15C = 1;
                    if ((WMI_Data & 134217728) == 134217728)
                        features.BatteryChargeControl = 1;
                }
            }
            else if (command != 96)
            {
                if (command == 119 || command != 122)
                    return;
                features.FanSpeedSupport = (WMI_Data & 1) == 1;
                features.SupportGamingGroup = (WMI_Data & 2) == 2;
                features.Support3dHeadphone = (WMI_Data & 4) == 4;
                features.SupportTpColor = (WMI_Data & 8) == 8;
                features.CustomFanSupport = (WMI_Data & 16) == 16;
                features.WakeUpLanSupport = (WMI_Data & 32) == 32;
                if ((WMI_Data & 64) == 64)
                    features.SupportCheckNotAirplaneOsd = true;
                features.SupportUsbHubReset = (WMI_Data & 256) == 256;
                if ((WMI_Data & 512) == 512)
                    features.SupportBootChangeCPUVoltage = true;
                features.SupportTpColor |= (WMI_Data & 2048) == 2048;
                features.SupportLightbar = (WMI_Data & 4096) == 4096;
                features.SupportFanLess = (WMI_Data & 32768) == 32768;
                if ((WMI_Data & 65536) == 65536)
                    features.FanExists = false;
                if ((WMI_Data & 131072) == 131072)
                    features.SupportTpSmbios = true;
                if ((WMI_Data & 262144) == 262144)
                    features.SupportLogoColor = true;
                if ((WMI_Data & 524288) == 524288)
                    features.SupportWmi3dHp = true;
                if ((WMI_Data & 1048576) == 1048576)
                    features.SupportMSHybrid_dGPUSwicth = true;
                if ((WMI_Data & 2097152) == 2097152)
                    features.SupportEthernetLED = true;
                if ((WMI_Data & 4194304) == 4194304)
                    features.SupportEss = true;
                features.SupportXMP = (WMI_Data & 16777216) == 16777216;
                features.SupportGPUOC_WMI = (WMI_Data >> 26 & 1) == 1 ? 1 : 0;
                features.SupportGPUcustom_WMI = (WMI_Data >> 27 & 1) == 1 ? 1 : 0;
                features.SupportKBLEDsleep_WMI = (WMI_Data >> 28 & 1) == 1 ? 1 : 0;
                features.SupportHW_P_state = (WMI_Data & 1073741824) == 1073741824;
            }
            else
            {
                features.NoSupportHW_KB_Type = (WMI_Data & 1) == 1;
                features.CheckUnigine = (WMI_Data & 2) == 2;
                features.SupportNewMode = (WMI_Data >> 2 & 1) == 1;
                if ((WMI_Data & 32) == 32)
                    features.SupportAllModeGPUOC = true;
                features.SupportAntiDust_Fan = (WMI_Data >> 7 & 1) == 1;
                features.SupportDisablePCIE_ASPM = (WMI_Data >> 9 & 1) == 1;
                features.SupportFanOffset = (WMI_Data >> 10 & 1) == 0;
            }
        }
    }
}