using System;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public struct FanStatus
    {
        public Fan.Mode Mode;
        public int Offset;
        public FanInfo Fan_CPU;
        public FanInfo Fan_GPU1;
        public FanInfo Fan_GPU2;
        public bool HasThirdFan;
    }

    public struct FanInfo
    {
        public byte T1;
        public byte T2;
        public byte T3;
        public byte T4;
        public byte T2_Default;
        public byte T3_Default;
        public byte D1;
        public byte D2;
        public byte D3;
        public byte D4;
        public byte D2_Default;
        public byte D3_Default;
        public int R12;
        public int R23;
        public int R34;
        public int rpm;
        public int remote;
        public int percent;

        public FanInfo(int T1, int D1, int T2, int D2, int T3, int D3, int T4 = 100, int D4 = 100)
        {
            this.T1 = (byte)T1;
            this.T2 = (byte)T2;
            this.T3 = (byte)T3;
            this.T4 = (byte)T4;
            this.D1 = (byte)D1;
            this.D2 = (byte)D2;
            this.D3 = (byte)D3;
            this.D4 = (byte)D4;

            T2_Default = 0;
            T3_Default = 0;
            D2_Default = 0;
            D3_Default = 0;
            R12 = 0;
            R23 = 0;
            R34 = 0;
            rpm = 0;
            remote = 0;
            percent = 0;
        }
    }

    public class Fan
    {
        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";

        public static event Action<Mode> FanProfileChanged;

        public static FanInfo GetDefaultFanInfo()
        {
            return new FanInfo(40, 35, 60, 50, 80, 85, 100, 100);
        }

        private static bool? _hasThirdFan;
        private static byte[] _fanSpeedCache;
        private static int _fanSpeedZeroStreak;
        private static long _lastFanToggleTicks;
        private static readonly object _fanToggleLock = new object();

        public enum Mode
        {
            Auto = 0,
            Max = 1,
            MaxQ = 5,
            Custom = 6,
            Unknown = 9
        }

        public static bool HasThirdFan
        {
            get
            {
                if (!_hasThirdFan.HasValue)
                {
                    _hasThirdFan = DetectThirdFan();
                }
                return _hasThirdFan.Value;
            }
        }

        private static bool DetectThirdFan()
        {
            try
            {
                byte[] numArray = Global.insyde.GetWMIPackage(12);
                int rawGpu2 = numArray[7] + (numArray[6] << 8);
                if (rawGpu2 > 0 || numArray[22] > 0 || numArray[24] > 0)
                {
                    return true;
                }

                byte[] wmi13 = Global.insyde.GetWMIPackage(13);
                if (wmi13[12] >= 3)
                {
                    return true;
                }

                int fan3Info = 0;
                int res = InsydeDriver.GetDCHU_Data_Integer(110, ref fan3Info);
                if (res == 0 && fan3Info > 0)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.DetectThirdFan", ex);
            }
            return false;
        }

        public static FanStatus GetStatus()
        {
            FanStatus status = new FanStatus
            {
                Mode = Mode.Auto,
                Offset = 0,
                Fan_CPU = GetDefaultFanInfo(),
                Fan_GPU1 = GetDefaultFanInfo(),
                Fan_GPU2 = GetDefaultFanInfo(),
                HasThirdFan = HasThirdFan
            };

            try
            {
                byte[] appData = Global.insyde.GetAPPData(4, 0, 256);
                if (appData != null && appData.Length > 7)
                {
                    status.Mode = LookupMode(appData[5]);
                    status.Offset = appData[7];
                    Read_FanInfo(appData, ref status.Fan_CPU, ref status.Fan_GPU1, ref status.Fan_GPU2);
                }

                Read_FanSpeed(ref status.Fan_CPU.rpm, ref status.Fan_CPU.percent, ref status.Fan_CPU.remote,
                              ref status.Fan_GPU1.rpm, ref status.Fan_GPU1.percent, ref status.Fan_GPU1.remote,
                              ref status.Fan_GPU2.rpm, ref status.Fan_GPU2.percent, ref status.Fan_GPU2.remote);

                if (status.Mode == Mode.Auto || status.Mode == Mode.Max)
                {
                    SetProgramModeOnly(status.Mode);
                }
                else
                {
                    object regVal = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "FanMode", null);
                    if (regVal is int intMode)
                    {
                        status.Mode = LookupMode((byte)intMode);
                    }
                    else
                    {
                        SetMode(Mode.Auto);
                        status.Mode = Mode.Auto;
                    }
                }

                object regOffset = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "FanOffset", null);
                if (regOffset is int intOffset)
                {
                    status.Offset = (byte)Math.Max(0, Math.Min(100, intOffset));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.GetStatus", ex);
                status.Mode = Mode.Auto;
            }

            return status;
        }

        public static bool Read_FanSpeed(ref int rpm_CPU, ref int percent_CPU, ref int remote_CPU,
                                         ref int rpm_GPU, ref int percent_GPU, ref int remote_GPU)
        {
            int dummyRpm = 0, dummyPercent = 0, dummyRemote = 0;
            return Read_FanSpeed(ref rpm_CPU, ref percent_CPU, ref remote_CPU,
                                 ref rpm_GPU, ref percent_GPU, ref remote_GPU,
                                 ref dummyRpm, ref dummyPercent, ref dummyRemote);
        }

        private static int ConvertTachToRpm(int rawTach)
        {
            return (rawTach > 0 && rawTach < 65535)
                ? (int)Math.Round(60.0 / (5.56521739130435E-05 * rawTach) * 2.0, 0)
                : 0;
        }

        private static int ConvertDutyToPercent(byte rawDuty)
        {
            int percent = (int)Math.Round(rawDuty / 255.0 * 100.0, 0);
            return (percent == 0 && rawDuty > 0) ? rawDuty : percent;
        }

        public static bool Read_FanSpeed(ref int rpm_CPU, ref int percent_CPU, ref int remote_CPU,
                                         ref int rpm_GPU, ref int percent_GPU, ref int remote_GPU,
                                         ref int rpm_GPU2, ref int percent_GPU2, ref int remote_GPU2)
        {
            try
            {
                byte[] array = Global.insyde.GetWMIPackage(12);

                // A transient all-zero tach/duty/temp block means a failed EC read, not a stopped fan.
                bool blockLooksEmpty = array[2] == 0 && array[3] == 0 && array[4] == 0 && array[5] == 0
                                       && array[16] == 0 && array[19] == 0 && array[18] == 0 && array[21] == 0;
                if (blockLooksEmpty && _fanSpeedZeroStreak < 5 && _fanSpeedCache != null)
                {
                    _fanSpeedZeroStreak++;
                    array = _fanSpeedCache;
                }
                else
                {
                    _fanSpeedZeroStreak = blockLooksEmpty ? _fanSpeedZeroStreak : 0;
                    _fanSpeedCache = (byte[])array.Clone();
                }

                rpm_CPU = ConvertTachToRpm(array[3] + (array[2] << 8));
                percent_CPU = ConvertDutyToPercent(array[16]);
                remote_CPU = array[18];

                rpm_GPU = ConvertTachToRpm(array[5] + (array[4] << 8));
                percent_GPU = ConvertDutyToPercent(array[19]);
                remote_GPU = array[21];

                rpm_GPU2 = ConvertTachToRpm(array[7] + (array[6] << 8));
                percent_GPU2 = ConvertDutyToPercent(array[22]);
                remote_GPU2 = array[24] > 0 ? array[24] : remote_GPU;

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.Read_FanSpeed", ex);
                return false;
            }
        }

        public static void AntiDust()
        {
            try
            {
                Global.insyde.SetWMI(121, 41, 1u);
                Global.insyde.SetAPPData(4, 80, 1, new byte[] { 1 });
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.AntiDust", ex);
            }
        }

        public static bool IsAntiDustSupported()
        {
            try
            {
                if (Global.support_bit != null && Global.support_bit.AntiDust_Fan)
                {
                    return true;
                }

                if (Capabilities.IsSupported("SupportAntiDust_Fan"))
                {
                    return true;
                }

                int wmi96 = 0;
                int res = InsydeDriver.GetDCHU_Data_Integer(96, ref wmi96);
                if (res == 0 && ((wmi96 >> 7) & 1) == 1)
                {
                    if (Global.support_bit != null)
                    {
                        Global.support_bit.AntiDust_Fan = true;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.IsAntiDustSupported", ex);
            }
            return false;
        }

        public static bool IsMaxQSupported() => Capabilities.IsMaxQSupported();

        public static void SetOffset(byte offset)
        {
            try
            {
                offset = Math.Min((byte)100, offset);
                int data = (int)Math.Round(255.0 * (offset / 100.0), 0);
                Global.insyde.SetWMI(121, 14, (uint)data);
                Global.insyde.SetAPPData(4, 7, 1, new byte[] { offset });
                try
                {
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "FanOffset", (int)offset, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Fan.SetOffset.Registry", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetOffset", ex);
            }
        }

        public static void SetMode(Mode mode)
        {
            try
            {
                _lastFanToggleTicks = MonotonicClock.NowTicks();
                SystemPreferences.UpdateHardwareFanModeCache(mode);
                Global.insyde.SetWMI(121, 1, (uint)mode);
                Global.insyde.SetAPPData(4, 5, 1, new byte[] { (byte)mode });
                try
                {
                    Microsoft.Win32.Registry.SetValue(AppRegistryKey, "FanMode", (int)mode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Fan.SetMode.Registry", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetMode", ex);
            }
        }

        public static void SetProgramModeOnly(Mode mode)
        {
            try
            {
                Global.insyde.SetAPPData(4, 5, 1, new byte[] { (byte)mode });
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetProgramModeOnly.AppData", ex);
            }
            try
            {
                Microsoft.Win32.Registry.SetValue(AppRegistryKey, "FanMode", (int)mode, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetProgramModeOnly.Registry", ex);
            }
            SystemPreferences.UpdateHardwareFanModeCache(mode);
        }

        public static Mode GetHardwareFanMode()
        {
            try
            {
                byte[] input = new byte[256];
                byte[] ram1 = new byte[256];
                input[0] = 3;
                if (InsydeDriver.SetDCHU_DataEx(4, input, 256, ref ram1[0]) > 0)
                {
                    byte oem4 = ram1[0xCC];
                    if (oem4 == 0xF3) return Mode.Max;
                    if (oem4 == 0xF2 || oem4 == 0x6B) return Mode.Auto;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.GetHardwareFanMode.Dchu", ex);
            }

            try
            {
                byte[] appData = Global.insyde.GetAPPData(4, 0, 256);
                if (appData != null && appData.Length > 5)
                {
                    return LookupMode(appData[5]);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.GetHardwareFanMode.AppData", ex);
            }
            return Mode.Unknown;
        }

        public static void SyncFanFromHardware(Mode targetMode, bool showOsd = true)
        {
            if (targetMode != Mode.Auto && targetMode != Mode.Max)
            {
                return;
            }

            lock (_fanToggleLock)
            {
                if (MonotonicClock.ElapsedMs(_lastFanToggleTicks) < 350.0)
                {
                    return;
                }
                _lastFanToggleTicks = MonotonicClock.NowTicks();

                try
                {
                    SetProgramModeOnly(targetMode);

                    if (showOsd)
                    {
                        OsdOverlay.ShowFan(targetMode);
                    }

                    FanProfileChanged?.Invoke(targetMode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Fan.SyncFanFromHardware", ex);
                }
            }
        }

        public static void CheckAndSyncFanFromHardware(bool forceOsd = false)
        {
            if (MonotonicClock.ElapsedMs(_lastFanToggleTicks) < 350.0)
            {
                return;
            }

            Mode hwMode = GetHardwareFanMode();
            if (hwMode == Mode.Auto || hwMode == Mode.Max)
            {
                object regVal = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "FanMode", null);
                Mode currentSwMode = (regVal is int intMode) ? LookupMode((byte)intMode) : Mode.Auto;

                if (forceOsd || hwMode != currentSwMode)
                {
                    SyncFanFromHardware(hwMode, showOsd: true);
                }
            }
        }

        public static void ToggleAutoMaxFan()
        {
            lock (_fanToggleLock)
            {
                if (MonotonicClock.ElapsedMs(_lastFanToggleTicks) < 350.0)
                {
                    return;
                }
                _lastFanToggleTicks = MonotonicClock.NowTicks();

                try
                {
                    Mode hwMode = GetHardwareFanMode();
                    object regVal = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "FanMode", null);
                    Mode currentSwMode = (regVal is int intMode) ? LookupMode((byte)intMode) : Mode.Auto;

                    Mode targetMode;
                    if ((hwMode == Mode.Auto || hwMode == Mode.Max) && hwMode != currentSwMode)
                    {
                        targetMode = hwMode;
                    }
                    else
                    {
                        targetMode = (currentSwMode == Mode.Max) ? Mode.Auto : Mode.Max;
                        SetMode(targetMode);
                    }

                    SetProgramModeOnly(targetMode);
                    SystemPreferences.UpdateHardwareFanModeCache(targetMode);
                    OsdOverlay.ShowFan(targetMode);
                    FanProfileChanged?.Invoke(targetMode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Fan.ToggleAutoMaxFan", ex);
                }
            }
        }

        private static Mode LookupMode(byte mode)
        {
            switch (mode)
            {
                case 0: return Mode.Auto;
                case 1: return Mode.Max;
                case 5: return Mode.MaxQ;
                case 6: return Mode.Custom;
                default: return Mode.Auto;
            }
        }

        internal static int ComputeSlope(byte dStart, byte dEnd, byte tStart, byte tEnd)
        {
            int raw = (int)Math.Round((dEnd - dStart) / (double)Math.Max(1, tEnd - tStart) * 2.55 * 16.0, 0);
            return Math.Max(0, Math.Min(65535, raw));
        }

        private static void WriteWmiFanSlopes(byte[] buffer, int baseOffset, FanInfo info)
        {
            int r12 = ComputeSlope(info.D1, info.D2, info.T1, info.T2);
            int r23 = ComputeSlope(info.D2, info.D3, info.T2, info.T3);
            int r34 = ComputeSlope(info.D3, info.D4, info.T3, info.T4);

            buffer[baseOffset] = (byte)(r12 >> 8);
            buffer[baseOffset + 1] = (byte)(r12 & 255);
            buffer[baseOffset + 2] = (byte)(r23 >> 8);
            buffer[baseOffset + 3] = (byte)(r23 & 255);
            buffer[baseOffset + 4] = (byte)(r34 >> 8);
            buffer[baseOffset + 5] = (byte)(r34 & 255);
        }

        private static void WriteAppFanChannel(byte[] buffer, int baseOffset, FanInfo info)
        {
            buffer[baseOffset] = info.D1;
            buffer[baseOffset + 1] = info.D2;
            buffer[baseOffset + 2] = info.D3;
            buffer[baseOffset + 3] = info.D4;
            buffer[baseOffset + 4] = info.D2;
            buffer[baseOffset + 5] = info.D3;
            buffer[baseOffset + 6] = info.T1;
            buffer[baseOffset + 7] = info.T2;
            buffer[baseOffset + 8] = info.T3;
            buffer[baseOffset + 9] = info.T4;
            buffer[baseOffset + 10] = info.T2;
            buffer[baseOffset + 11] = info.T3;

            int r12 = ComputeSlope(info.D1, info.D2, info.T1, info.T2);
            int r23 = ComputeSlope(info.D2, info.D3, info.T2, info.T3);
            int r34 = ComputeSlope(info.D3, info.D4, info.T3, info.T4);

            buffer[baseOffset + 12] = (byte)(r12 & 255);
            buffer[baseOffset + 13] = (byte)(r12 >> 8);
            buffer[baseOffset + 14] = (byte)(r23 & 255);
            buffer[baseOffset + 15] = (byte)(r23 >> 8);
            buffer[baseOffset + 16] = (byte)(r34 & 255);
            buffer[baseOffset + 17] = (byte)(r34 >> 8);
        }

        private static void ReadAppFanChannel(byte[] array, int baseOffset, ref FanInfo info)
        {
            if (array[baseOffset + 6] <= 0 || array[baseOffset + 7] <= 0)
            {
                return;
            }

            info.D1 = (array[baseOffset + 3] > 0) ? array[baseOffset] : (array[baseOffset] > 0 ? array[baseOffset] : (byte)35);
            info.D2 = array[baseOffset + 1] > 0 ? array[baseOffset + 1] : (byte)50;
            info.D3 = array[baseOffset + 2] > 0 ? array[baseOffset + 2] : (byte)85;
            info.D4 = array[baseOffset + 3] > 0 ? array[baseOffset + 3] : (byte)100;
            info.D2_Default = array[baseOffset + 4];
            info.D3_Default = array[baseOffset + 5];
            info.T1 = array[baseOffset + 6] > 0 ? array[baseOffset + 6] : (byte)40;
            info.T2 = array[baseOffset + 7] > 0 ? array[baseOffset + 7] : (byte)60;
            info.T3 = array[baseOffset + 8] > 0 ? array[baseOffset + 8] : (byte)80;
            info.T4 = array[baseOffset + 9] > 0 ? array[baseOffset + 9] : (byte)100;
            info.T2_Default = array[baseOffset + 10];
            info.T3_Default = array[baseOffset + 11];
            info.R12 = (array[baseOffset + 13] << 8) + array[baseOffset + 12];
            info.R23 = (array[baseOffset + 15] << 8) + array[baseOffset + 14];
            info.R34 = (array[baseOffset + 17] << 8) + array[baseOffset + 16];
        }

        public static void SetCustomFanTable(FanInfo CPU, FanInfo GPU)
        {
            try
            {
                byte[] buffer = new byte[256];
                buffer[2] = CPU.T2;
                buffer[3] = (byte)Math.Round(CPU.D2 / 100.0 * 255.0, 0);
                buffer[4] = CPU.T3;
                buffer[5] = (byte)Math.Round(CPU.D3 / 100.0 * 255.0, 0);
                buffer[6] = GPU.T2;
                buffer[7] = (byte)Math.Round(GPU.D2 / 100.0 * 255.0, 0);
                buffer[8] = GPU.T3;
                buffer[9] = (byte)Math.Round(GPU.D3 / 100.0 * 255.0, 0);
                buffer[10] = GPU.T2;
                buffer[11] = (byte)Math.Round(GPU.D2 / 100.0 * 255.0, 0);
                buffer[12] = GPU.T3;
                buffer[13] = (byte)Math.Round(GPU.D3 / 100.0 * 255.0, 0);

                WriteWmiFanSlopes(buffer, 14, CPU);
                WriteWmiFanSlopes(buffer, 20, GPU);
                WriteWmiFanSlopes(buffer, 26, GPU);

                Global.insyde.SetWMIPackage(14, buffer);
                SetMode(Mode.Custom);
                SetFanInfo(CPU, GPU);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetCustomFanTable", ex);
            }
        }

        private static void SetFanInfo(FanInfo CPU, FanInfo GPU)
        {
            try
            {
                byte[] currentAppData = Global.insyde.GetAPPData(4, 0, 256);
                byte offsetVal = currentAppData[7];

                byte[] buffer = new byte[256];
                buffer[0] = 3;
                buffer[4] = 8;
                buffer[5] = (byte)Mode.Custom;
                buffer[6] = 1;
                buffer[7] = offsetVal;

                WriteAppFanChannel(buffer, 16, CPU);
                WriteAppFanChannel(buffer, 34, GPU);
                WriteAppFanChannel(buffer, 52, GPU);

                Global.insyde.SetAPPData(4, 0, 256, buffer);
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.SetFanInfo", ex);
            }
        }

        private static void Read_FanInfo(byte[] array, ref FanInfo Fan_CPU, ref FanInfo Fan_GPU1, ref FanInfo Fan_GPU2)
        {
            try
            {
                if (array != null && array.Length >= 70)
                {
                    ReadAppFanChannel(array, 16, ref Fan_CPU);
                    ReadAppFanChannel(array, 34, ref Fan_GPU1);
                    ReadAppFanChannel(array, 52, ref Fan_GPU2);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Fan.Read_FanInfo", ex);
            }
        }
    }
}
