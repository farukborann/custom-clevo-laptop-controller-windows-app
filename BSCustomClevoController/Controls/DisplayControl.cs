using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace BSCustomClevoController.Controls
{
    public static class DisplayControl
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DM_DISPLAYFREQUENCY = 0x400000;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int ChangeDisplaySettings(ref DevMode devMode, int flags);

        public static int GetCurrentRefreshRate()
        {
            DevMode dm = new DevMode();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayFrequency;
            }
            return 60;
        }

        public static List<int> GetAvailableRefreshRates()
        {
            var rates = new SortedSet<int>();
            DevMode current = new DevMode();
            current.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current))
            {
                int currentWidth = current.dmPelsWidth;
                int currentHeight = current.dmPelsHeight;

                DevMode mode = new DevMode();
                mode.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
                int i = 0;
                while (EnumDisplaySettings(null, i, ref mode))
                {
                    if (mode.dmPelsWidth == currentWidth && mode.dmPelsHeight == currentHeight && mode.dmDisplayFrequency > 0)
                    {
                        rates.Add(mode.dmDisplayFrequency);
                    }
                    i++;
                }
            }

            if (rates.Count == 0)
            {
                rates.Add(60);
            }

            return rates.ToList();
        }

        public static bool SetRefreshRate(int hz)
        {
            DevMode dm = new DevMode();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                dm.dmDisplayFrequency = hz;
                dm.dmFields = DM_DISPLAYFREQUENCY;
                int result = ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
                return result == DISP_CHANGE_SUCCESSFUL;
            }
            return false;
        }
    }
}
