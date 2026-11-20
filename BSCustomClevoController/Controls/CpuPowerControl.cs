using System;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public static class CpuPowerControl
    {
        private static string _cpuModel;
        private static int _cachedCpuPowerOffset = 0;
        private static bool _powerLoaded = false;

        public static bool IsSupported => Capabilities.IsSupported("SupportCPUOC");

        public static string CpuModel
        {
            get
            {
                if (string.IsNullOrEmpty(_cpuModel))
                {
                    HardwareDetector.DetectCpu(out _cpuModel);
                }
                return string.IsNullOrWhiteSpace(_cpuModel) ? "Processor" : _cpuModel;
            }
        }

        public static void ResetDetection()
        {
            _cpuModel = null;
            _powerLoaded = false;
        }

        public static int GetPowerOffset()
        {
            if (_powerLoaded) return _cachedCpuPowerOffset;
            if (!IsSupported)
            {
                _cachedCpuPowerOffset = 0;
                _powerLoaded = true;
                return 0;
            }

            try
            {
                byte[] array = new byte[4];
                InsydeDriver.ReadAppSettings(6, 33, 4, ref array[0]);
                int val = BitConverter.ToInt32(array, 0);
                _cachedCpuPowerOffset = (val >= 0 && val <= 25) ? val : 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("CpuPowerControl.GetPowerOffset", ex);
                _cachedCpuPowerOffset = 0;
            }

            _powerLoaded = true;
            return _cachedCpuPowerOffset;
        }

        public static void SetPowerOffset(int offsetWatts)
        {
            if (!IsSupported) return;

            if (offsetWatts < 0) offsetWatts = 0;
            if (offsetWatts > 25) offsetWatts = 25;

            _cachedCpuPowerOffset = offsetWatts;
            _powerLoaded = true;

            try
            {
                // DCHU AppSettings Page 6: 32 = IsSaved flag, 33 = PL1 (4 bytes), 37 = PL2 (4 bytes)
                byte saveFlag = 1;
                InsydeDriver.WriteAppSettings(6, 32, 1, ref saveFlag);

                byte[] pl1Bytes = BitConverter.GetBytes(offsetWatts);
                InsydeDriver.WriteAppSettings(6, 33, 4, ref pl1Bytes[0]);

                byte[] pl2Bytes = BitConverter.GetBytes(offsetWatts);
                InsydeDriver.WriteAppSettings(6, 37, 4, ref pl2Bytes[0]);
            }
            catch (Exception ex)
            {
                AppLogger.Log("CpuPowerControl.SetPowerOffset", ex);
            }
        }
    }
}
