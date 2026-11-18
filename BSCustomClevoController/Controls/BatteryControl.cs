using System;
using System.Management;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public enum BatteryChargeMode
    {
        Maximum,      // Constantly charge to 100% (FlexiCharger disabled)
        Recommended,  // Charge under 70%, stop at 80%
        Custom        // Custom user-defined start and stop thresholds
    }

    public struct BatteryChargeStatus
    {
        public bool Enabled;
        public int StartThreshold;
        public int StopThreshold;
        public BatteryChargeMode Mode;
    }

    public struct BatteryTelemetry
    {
        public int VoltageMv;
        public int ChargeRateMw;
        public int DischargeRateMw;
        public bool IsCharging;
        public bool IsDischarging;
        public bool PowerOnline;
    }

    public static class BatteryControl
    {
        private static bool? _lastKnownSmartCharge;

        private static int GetDCHU_Data_Integer(int command, ref int data)
        {
            return InsydeDriver.GetDCHU_Data_Integer(command, ref data);
        }

        private static int SetDCHU_Data(int command, byte[] buffer, int length)
        {
            return InsydeDriver.SetDCHU_Data(command, buffer, length);
        }

        public static BatteryTelemetry GetTelemetry()
        {
            var telemetry = new BatteryTelemetry();

            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT Voltage, ChargeRate, DischargeRate, Charging, Discharging, PowerOnline FROM BatteryStatus"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Voltage"] != null) telemetry.VoltageMv = Convert.ToInt32(obj["Voltage"]);
                        if (obj["ChargeRate"] != null) telemetry.ChargeRateMw = Convert.ToInt32(obj["ChargeRate"]);
                        if (obj["DischargeRate"] != null) telemetry.DischargeRateMw = Convert.ToInt32(obj["DischargeRate"]);
                        if (obj["Charging"] != null) telemetry.IsCharging = Convert.ToBoolean(obj["Charging"]);
                        if (obj["Discharging"] != null) telemetry.IsDischarging = Convert.ToBoolean(obj["Discharging"]);
                        if (obj["PowerOnline"] != null) telemetry.PowerOnline = Convert.ToBoolean(obj["PowerOnline"]);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("BatteryControl.GetTelemetry.Wmi", ex);
            }

            if (telemetry.VoltageMv <= 0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT DesignVoltage FROM Win32_Battery"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            if (obj["DesignVoltage"] != null)
                            {
                                telemetry.VoltageMv = Convert.ToInt32(obj["DesignVoltage"]);
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("BatteryControl.GetTelemetry.Win32", ex);
                }
            }

            return telemetry;
        }

        public static BatteryChargeStatus GetStatus()
        {
            try
            {
                int data = 0;
                int ret = GetDCHU_Data_Integer(119, ref data);
                if (ret == 0 || data > 0)
                {
                    bool enabled = (data & 0xFF) != 0;
                    int start = (data >> 8) & 0xFF;
                    int stop = (data >> 16) & 0xFF;

                    if (start <= 0 || start > 99) start = 90;
                    if (stop <= 0 || stop > 100) stop = 100;

                    BatteryChargeMode mode;
                    if (!enabled)
                    {
                        mode = BatteryChargeMode.Maximum;
                    }
                    else if (start == 70 && stop == 80)
                    {
                        mode = BatteryChargeMode.Recommended;
                    }
                    else
                    {
                        mode = BatteryChargeMode.Custom;
                    }

                    _lastKnownSmartCharge = enabled;
                    return new BatteryChargeStatus
                    {
                        Enabled = enabled,
                        StartThreshold = start,
                        StopThreshold = stop,
                        Mode = mode
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("BatteryControl.GetStatus", ex);
            }

            return new BatteryChargeStatus
            {
                Enabled = false,
                StartThreshold = 90,
                StopThreshold = 100,
                Mode = BatteryChargeMode.Maximum
            };
        }

        public static bool SetChargeMode(BatteryChargeMode mode, int customStart = 90, int customStop = 100)
        {
            try
            {
                if (mode == BatteryChargeMode.Maximum)
                {
                    PersistBatterySettings(mode, customStart, customStop);
                    // Disable FlexiCharger: command 118, arg 0x05000000
                    SetDCHU_Data(118, new byte[4] { 0, 0, 0, 5 }, 4);
                    return true;
                }

                int start = mode == BatteryChargeMode.Recommended ? 70 : customStart;
                int stop = mode == BatteryChargeMode.Recommended ? 80 : customStop;

                if (start >= stop) return false;
                start = Math.Max(40, Math.Min(99, start));
                stop = Math.Max(50, Math.Min(100, stop));

                PersistBatterySettings(mode, start, stop);

                // Set thresholds: command 118, arg: 0x06000000 | (stop << 8) | start
                SetDCHU_Data(118, new byte[4] { (byte)start, (byte)stop, 0, 6 }, 4);

                // Enable FlexiCharger: command 118, arg 0x05000001
                SetDCHU_Data(118, new byte[4] { 1, 0, 0, 5 }, 4);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log("BatteryControl.SetChargeMode", ex);
                return false;
            }
        }

        private static void PersistBatterySettings(BatteryChargeMode mode, int start, int stop)
        {
            _lastKnownSmartCharge = mode != BatteryChargeMode.Maximum;
            try
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "BatteryMode", (int)mode, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "BatteryStart", start, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "BatteryStop", stop, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("BatteryControl.PersistBatterySettings", ex);
            }
        }

        public static bool IsSmartChargeEnabled()
        {
            if (_lastKnownSmartCharge.HasValue)
            {
                return _lastKnownSmartCharge.Value;
            }

            try
            {
                object val = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "BatteryMode", 0);
                return val is int intMode && intMode != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Maps battery percent and state to Segoe Fluent Icons glyphs (EBA0..EBAA idle, EBAB..EBB5 charging, EBB6..EBC0 smart charge heart).</summary>
        public static string ResolveBatteryIconGlyph(int percent, bool isCharging, bool smartCharge = false)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            int step = (int)Math.Round(clamped / 10.0, MidpointRounding.AwayFromZero);
            int baseCodePoint = isCharging
                ? 0xEBAB
                : (smartCharge ? 0xEBB6 : 0xEBA0);
            return char.ConvertFromUtf32(baseCodePoint + Math.Max(0, Math.Min(10, step)));
        }
    }
}
