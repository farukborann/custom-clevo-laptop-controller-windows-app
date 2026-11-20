using System;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public static class NvidiaPowerControl
    {
        // EC RAM offsets from tuxedo-drivers uniwill_interfaces.h and tuxedo_nb02_nvidia_power_ctrl.c
        private const int EcRegCtgpDbEnable = 0x43;
        private const int EcRegCtgpOffset = 0x44;
        private const int EcRegDbOffset = 0x46;

        private const byte BitGeneralEnable = 0x01;
        private const byte BitDbEnable = 0x02;
        private const byte BitCtgpEnable = 0x04;

        private static bool? _isNvidiaPresent;
        private static string _gpuModel;
        private static int _cachedTgpOffset = 0;
        private static bool _tgpLoaded = false;

        public static bool IsNvidiaPresent
        {
            get
            {
                if (!_isNvidiaPresent.HasValue)
                {
                    GpuBrand brand = HardwareDetector.DetectGpu(out _gpuModel);
                    _isNvidiaPresent = (brand == GpuBrand.Nvidia);
                }
                return _isNvidiaPresent.Value;
            }
        }

        public static string GpuModel
        {
            get
            {
                if (_gpuModel == null)
                {
                    _ = IsNvidiaPresent;
                }
                return _gpuModel ?? "NVIDIA GPU";
            }
        }

        public static void ResetDetection()
        {
            _isNvidiaPresent = null;
            _gpuModel = null;
            _tgpLoaded = false;
        }

        public static int GetTgpOffset()
        {
            if (_tgpLoaded) return _cachedTgpOffset;

            try
            {
                byte val = 0;
                InsydeDriver.ReadAppSettings(7, EcRegCtgpOffset, 1, ref val);
                if (val <= 25)
                {
                    _cachedTgpOffset = val;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("NvidiaPowerControl.GetTgpOffset", ex);
                _cachedTgpOffset = 0;
            }

            _tgpLoaded = true;
            return _cachedTgpOffset;
        }

        public static void SetTgpOffset(int offsetWatts)
        {
            if (offsetWatts < 0) offsetWatts = 0;
            if (offsetWatts > 25) offsetWatts = 25;

            _cachedTgpOffset = offsetWatts;
            _tgpLoaded = true;

            try
            {
                byte ctgpVal = (byte)offsetWatts;
                byte enableVal = (byte)(BitGeneralEnable | BitDbEnable | BitCtgpEnable);

                InsydeDriver.WriteAppSettings(7, EcRegCtgpDbEnable, 1, ref enableVal);
                InsydeDriver.WriteAppSettings(7, EcRegCtgpOffset, 1, ref ctgpVal);
                InsydeDriver.WriteAppSettings(7, EcRegDbOffset, 1, ref ctgpVal);
                InsydeDriver.SetDCHU_Data(121, new byte[4] { ctgpVal, 0, 0, 0x1A }, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("NvidiaPowerControl.SetTgpOffset", ex);
            }
        }
    }
}
