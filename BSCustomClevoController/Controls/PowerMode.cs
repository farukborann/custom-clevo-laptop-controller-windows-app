using System;
using System.Threading.Tasks;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public class PowerMode
    {
        public static event Action<Mode> PowerProfileChanged;

        private static long _lastPowerCycleTicks;
        private static readonly object _powerCycleLock = new object();

        public enum Mode
        {
            Quiet = 0,
            Powersaving = 1,
            Performance = 2,
            Entertainment = 3,
            Unknown = 4,
        }

        public static Mode GetStatus()
        {
            try
            {
                object regVal = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "PowerMode", null);
                if (regVal is int intMode)
                {
                    return LookupMode((byte)intMode);
                }

                SetPowerMode(Mode.Entertainment);
                return Mode.Entertainment;
            }
            catch (Exception ex)
            {
                AppLogger.Log("PowerMode.GetStatus", ex);
                return Mode.Entertainment;
            }
        }

        private static Mode LookupMode(byte mode)
        {
            switch (mode)
            {
                case 0: return Mode.Quiet;
                case 1: return Mode.Powersaving;
                case 2: return Mode.Performance;
                case 3: return Mode.Entertainment;
                default: return Mode.Entertainment;
            }
        }

        public static void SetPowerMode(Mode mode)
        {
            try
            {
                byte value = (byte)mode;
                InsydeDriver.SetDCHU_Data(121, new byte[4] { value, 0, 0, 25 }, 4);
                InsydeDriver.WriteAppSettings(1, 1, 1, ref value);

                try
                {
                    Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\BSCustomClevoController", "PowerMode", (int)mode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("PowerMode.SetPowerMode.Registry", ex);
                }

                Task.Run(() =>
                {
                    try
                    {
                        SystemPreferences.SetDiscreteGpuState(mode != Mode.Powersaving);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("PowerMode.SetDiscreteGpuState", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log("PowerMode.SetPowerMode", ex);
            }
        }

        public static Mode CyclePowerMode()
        {
            lock (_powerCycleLock)
            {
                if (MonotonicClock.ElapsedMs(_lastPowerCycleTicks) < 400.0)
                {
                    return GetStatus();
                }
                _lastPowerCycleTicks = MonotonicClock.NowTicks();

                try
                {
                    var current = GetStatus();
                    Mode next;
                    switch (current)
                    {
                        case Mode.Quiet:
                            next = Mode.Powersaving;
                            break;
                        case Mode.Powersaving:
                            next = Mode.Entertainment;
                            break;
                        case Mode.Entertainment:
                            next = Mode.Performance;
                            break;
                        default:
                            next = Mode.Quiet;
                            break;
                    }
                    SetPowerMode(next);
                    OsdOverlay.ShowPowerMode(next);
                    PowerProfileChanged?.Invoke(next);

                    return next;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("PowerMode.CyclePowerMode", ex);
                    return Mode.Entertainment;
                }
            }
        }
    }
}
