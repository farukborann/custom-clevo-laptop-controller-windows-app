using System;
using System.Linq;
using Microsoft.Win32;

namespace BSCustomClevoController.Utility
{
    public enum CpuBrand
    {
        Intel,
        Amd,
        Unknown
    }

    public enum GpuBrand
    {
        Nvidia,
        Amd,
        Intel,
        Unknown
    }

    public static class HardwareDetector
    {
        public static CpuBrand DetectCpu(out string cpuModel)
        {
            cpuModel = "CPU";
            try
            {
                object val = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "");
                if (val != null && !string.IsNullOrWhiteSpace(val.ToString()))
                {
                    cpuModel = val.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("HardwareDetector.DetectCpu", ex);
            }

            if (cpuModel.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cpuModel.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CpuBrand.Amd;
            }
            if (cpuModel.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cpuModel.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CpuBrand.Intel;
            }

            return CpuBrand.Unknown;
        }

        public static GpuBrand DetectGpu(out string gpuModel)
        {
            gpuModel = "GPU";
            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (baseKey == null)
                        return GpuBrand.Unknown;

                    GpuBrand bestBrand = GpuBrand.Unknown;
                    string bestModel = null;

                    foreach (string sub in baseKey.GetSubKeyNames())
                    {
                        if (!sub.StartsWith("0"))
                            continue;

                        string desc = ReadSubKeyDescription(baseKey, sub);
                        if (desc == null)
                            continue;

                        GpuBrand brand = ClassifyGpuBrand(desc);
                        if (brand == GpuBrand.Nvidia)
                        {
                            gpuModel = desc;
                            return GpuBrand.Nvidia;
                        }

                        if (brand != GpuBrand.Unknown && bestBrand == GpuBrand.Unknown)
                        {
                            bestBrand = brand;
                            bestModel = desc;
                        }
                    }

                    if (bestBrand != GpuBrand.Unknown)
                    {
                        gpuModel = bestModel;
                        return bestBrand;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("HardwareDetector.DetectGpu", ex);
            }

            return GpuBrand.Unknown;
        }

        private static string ReadSubKeyDescription(RegistryKey baseKey, string sub)
        {
            using (RegistryKey subKey = baseKey.OpenSubKey(sub))
            {
                object descVal = subKey?.GetValue("DriverDesc") ?? subKey?.GetValue("ProviderName");
                return descVal?.ToString();
            }
        }

        private static GpuBrand ClassifyGpuBrand(string desc)
        {
            if (ContainsAnyIgnoreCase(desc, "NVIDIA", "GeForce", "RTX", "GTX", "Quadro"))
            {
                return GpuBrand.Nvidia;
            }
            if (ContainsAnyIgnoreCase(desc, "Radeon", "AMD"))
            {
                return GpuBrand.Amd;
            }
            if (desc.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GpuBrand.Intel;
            }
            return GpuBrand.Unknown;
        }

        private static bool ContainsAnyIgnoreCase(string source, params string[] tokens)
        {
            return tokens.Any(token => source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
