using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public struct KeyboardStatus
    {
        public Color BackLight { get; set; }
        public KeyboardEffect Effect { get; set; }
        public bool LightState { get; set; }
        public bool BootEffect { get; set; }
        public int SleepSecond { get; set; }
        public bool SleepState { get; set; }
    }

    public class KeyboardHardwareCapabilities
    {
        public bool SupportsBacklight { get; set; }
        public bool SupportsRgb { get; set; }
        public bool SupportsHardwareEffects { get; set; }
        public bool SupportsMultiZone { get; set; }
        public int ZoneCount { get; set; }
        public bool SupportsBootEffectOverride { get; set; }
        public bool SupportsIteDirectHid { get; set; }
        public bool SupportsLightBar { get; set; }
        public int MaxBrightnessLevels { get; set; }
        private static readonly Dictionary<string, string> KeyboardTypeKeyMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Single-Color White (5-Level)", "KbTypeWhite5" },
                { "Single-Color White (2-Level)", "KbTypeWhite2" },
                { "Single-Line RGB (1-Zone)", "KbTypeSingleRgb" },
                { "Multi-Line RGB (ITE Per-Key)", "KbTypeMultiItePerKey" },
                { "Multi-Line RGB (Per-Key)", "KbTypeMultiPerKey" },
                { "Multi-Line RGB (3-Zone)", "KbTypeMulti3Zone" }
            };

        private string _keyboardTypeName;
        public string KeyboardTypeKey { get; set; }

        public string KeyboardTypeName
        {
            get => string.IsNullOrEmpty(KeyboardTypeKey)
                ? _keyboardTypeName
                : LocalizationManager.T(KeyboardTypeKey, _keyboardTypeName);
            set
            {
                _keyboardTypeName = value;
                KeyboardTypeKey = value != null && KeyboardTypeKeyMap.TryGetValue(value, out string key)
                    ? key
                    : null;
            }
        }
        public byte RawKbType { get; set; }

        public KeyboardHardwareCapabilities()
        {
            SupportsBacklight = true;
            SupportsRgb = true;
            SupportsHardwareEffects = true;
            SupportsMultiZone = false;
            ZoneCount = 1;
            SupportsBootEffectOverride = true;
            SupportsIteDirectHid = false;
            SupportsLightBar = false;
            MaxBrightnessLevels = 4;
            KeyboardTypeName = "Single-Line RGB (1-Zone)";
            RawKbType = 2;
        }
    }

    internal static class KeyboardBackLight
    {
        public static event Action BacklightChanged;

        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";
        private const string KbBrightnessValueName = "KbBrightness";
        private const string KbLightStateValueName = "KbLightState";
        private const string KbUserSleepTimeoutValueName = "KbUserSleepTimeout";
        private static readonly string[] ZoneControlCenterSubKeys = new string[] { "Left", "Center", "Right" };
        private static readonly byte[] BrightnessStepValues = new byte[] { 0, 64, 128, 192, 255 };

        public struct ColorPresetItem
        {
            public Color Color { get; set; }
            public string Name { get; set; }

            public ColorPresetItem(byte r, byte g, byte b, string name)
            {
                Color = Color.FromArgb(255, r, g, b);
                Name = name;
            }
        }

        public static readonly ColorPresetItem[] QuickColorPresets = new ColorPresetItem[]
        {
            new ColorPresetItem(255, 0, 0, "Red"),
            new ColorPresetItem(255, 128, 0, "Orange"),
            new ColorPresetItem(255, 255, 0, "Yellow"),
            new ColorPresetItem(0, 255, 0, "Green"),
            new ColorPresetItem(0, 255, 255, "Cyan"),
            new ColorPresetItem(0, 0, 255, "Blue"),
            new ColorPresetItem(255, 0, 255, "Purple"),
            new ColorPresetItem(255, 255, 255, "White")
        };

        private static byte _lastTargetBrightness = 255;
        private static int _currentColorPresetIndex = 0;
        private static long _lastColorCycleTicks;
        private static bool? _cachedMultiZone;
        private static readonly object _backlightAdjustLock = new object();

        public static KeyboardHardwareCapabilities GetCapabilities()
        {
            var caps = new KeyboardHardwareCapabilities();
            try
            {
                int cmd16 = 0;
                InsydeDriver.GetDCHU_Data_Integer(16, ref cmd16);

                int cmd96 = 0;
                InsydeDriver.GetDCHU_Data_Integer(96, ref cmd96);

                int cmd52 = 0;
                InsydeDriver.GetDCHU_Data_Integer(0x52, ref cmd52);

                int cmd7A = 0;
                InsydeDriver.GetDCHU_Data_Integer(0x7A, ref cmd7A);

                byte rawType = ResolveRawKeyboardType();
                caps.RawKbType = rawType;
                caps.SupportsIteDirectHid = IteHidController.IsDevicePresent;
                caps.SupportsLightBar = IteHidController.IsLightBarPresent;
                caps.SupportsBootEffectOverride = true;

                ApplyCapabilityProfile(caps, rawType, cmd16, cmd96, cmd52, cmd7A);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.GetCapabilities", ex);
                caps.SupportsBacklight = true;
                caps.SupportsRgb = true;
                caps.SupportsHardwareEffects = false;
                caps.SupportsMultiZone = false;
                caps.ZoneCount = 1;
                caps.SupportsBootEffectOverride = true;
                caps.SupportsIteDirectHid = false;
                caps.SupportsLightBar = IteHidController.IsLightBarPresent;
                caps.KeyboardTypeName = "Single-Line RGB (1-Zone)";
            }

            _cachedMultiZone = caps.SupportsMultiZone;
            return caps;
        }

        private static byte ResolveRawKeyboardType()
        {
            byte rawType = 0;
            InsydeDriver.ReadAppSettings(2, 4, 1, ref rawType);
            if (rawType == 0)
            {
                byte[] pkg13 = new byte[256];
                InsydeDriver.GetDCHU_Data_Buffer(13, ref pkg13[0]);
                byte wmiKbType = pkg13[15];
                if (wmiKbType == 3 || wmiKbType == 19 || wmiKbType == 35 || wmiKbType == 51 || wmiKbType == 243)
                {
                    rawType = 3;
                }
                else if (wmiKbType != 0)
                {
                    rawType = wmiKbType;
                }
            }

            if (rawType == 0)
            {
                object regKbType = Registry.GetValue(@"HKEY_CURRENT_USER\Software\ControlCenter2.0\LED", "KBType", null);
                if (regKbType != null)
                {
                    int parsedKbType = Convert.ToInt32(regKbType);
                    if (parsedKbType > 0 && parsedKbType <= 255)
                    {
                        rawType = (byte)parsedKbType;
                    }
                }
            }

            return rawType;
        }

        private static void ApplyCapabilityProfile(
            KeyboardHardwareCapabilities caps,
            byte rawType,
            int cmd16,
            int cmd96,
            int cmd52,
            int cmd7A)
        {
            bool isWhiteOnly = (cmd52 & 0x40000000) != 0;
            bool is3ZoneRgb = (cmd52 & 0x00400000) != 0;
            bool isWhite5Levels = (cmd7A & 0x4000) != 0;
            bool noSupportHw = (cmd96 & 1) == 1;
            bool hasPerKeyBit = (cmd16 & 8) == 8;
            bool hasRgbBit = (cmd16 & 16) == 16;

            bool isKnownRgbRaw = rawType == 0 || rawType == 2 || rawType == 3 || rawType == 6 || rawType == 22;
            if (isWhiteOnly || rawType == 1 || (!hasRgbBit && !hasPerKeyBit && !is3ZoneRgb && !isKnownRgbRaw))
            {
                caps.SupportsBacklight = true;
                caps.SupportsRgb = false;
                caps.SupportsHardwareEffects = false;
                caps.SupportsMultiZone = false;
                caps.ZoneCount = 1;
                caps.SupportsIteDirectHid = false;
                caps.MaxBrightnessLevels = isWhite5Levels ? 5 : 2;
                caps.KeyboardTypeName = isWhite5Levels ? "Single-Color White (5-Level)" : "Single-Color White (2-Level)";
                return;
            }

            if (rawType == 0 || rawType == 6 || rawType == 22)
            {
                caps.SupportsBacklight = true;
                caps.SupportsRgb = true;
                caps.SupportsMultiZone = false;
                caps.ZoneCount = 1;
                caps.SupportsHardwareEffects = false;
                caps.SupportsIteDirectHid = false;
                caps.KeyboardTypeName = "Single-Line RGB (1-Zone)";
                return;
            }

            if (rawType == 3 || hasPerKeyBit)
            {
                caps.SupportsBacklight = true;
                caps.SupportsRgb = true;
                caps.SupportsMultiZone = true;
                caps.ZoneCount = 3;
                caps.SupportsHardwareEffects = !noSupportHw || caps.SupportsIteDirectHid;
                caps.KeyboardTypeName = caps.SupportsIteDirectHid ? "Multi-Line RGB (ITE Per-Key)" : "Multi-Line RGB (Per-Key)";
                return;
            }

            caps.SupportsBacklight = true;
            caps.SupportsRgb = true;
            caps.SupportsMultiZone = true;
            caps.ZoneCount = 3;
            caps.SupportsHardwareEffects = !noSupportHw;
            caps.KeyboardTypeName = "Multi-Line RGB (3-Zone)";
        }

        public static KeyboardStatus GetStatus()
        {
            try
            {
                byte rawBrightness = 4;
                InsydeDriver.ReadAppSettings(2, 35, 1, ref rawBrightness);
                byte[] colour = new byte[3] { 0, 0, 255 };
                InsydeDriver.ReadAppSettings(2, 81, 3, ref colour[0]);
                byte mode = 0;
                InsydeDriver.ReadAppSettings(2, 32, 1, ref mode);
                byte status = 1;
                InsydeDriver.ReadAppSettings(2, 84, 1, ref status);
                byte booteffect = 0;
                InsydeDriver.ReadAppSettings(2, 7, 1, ref booteffect);
                byte[] sleep = new byte[3];
                InsydeDriver.ReadAppSettings(2, 37, 3, ref sleep[0]);
                byte sleepstatus = 0;
                InsydeDriver.ReadAppSettings(2, 36, 1, ref sleepstatus);

                byte displayBrightness = ResolveStatusBrightness(rawBrightness, status, out status);
                ResolveStatusColor(colour);
                int sleepsec = ResolveStatusSleep(sleep, ref sleepstatus);
                KeyboardEffect effect = ResolveStatusEffect(mode);

                _lastTargetBrightness = displayBrightness;
                return new KeyboardStatus()
                {
                    BackLight = Color.FromArgb(displayBrightness, colour[0], colour[1], colour[2]),
                    Effect = effect,
                    LightState = status != 0,
                    BootEffect = booteffect != 0,
                    SleepSecond = sleepsec,
                    SleepState = sleepstatus != 0
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.GetStatus", ex);
                return new KeyboardStatus()
                {
                    BackLight = Color.FromArgb(255, 0, 0, 255),
                    Effect = null,
                    LightState = true,
                    BootEffect = false,
                    SleepSecond = 30,
                    SleepState = true
                };
            }
        }

        private static byte ResolveStatusBrightness(byte rawBrightness, byte currentStatus, out byte resolvedStatus)
        {
            byte displayBrightness = rawBrightness <= 4
                ? (byte)Math.Min(255, (int)rawBrightness * 64)
                : rawBrightness;

            if (displayBrightness == 0 && currentStatus != 0)
            {
                displayBrightness = 255;
            }

            object regBri = Registry.GetValue(AppRegistryKey, KbBrightnessValueName, null);
            if (regBri is int intBri)
            {
                displayBrightness = (byte)Math.Max(0, Math.Min(255, intBri));
            }

            object regLight = Registry.GetValue(AppRegistryKey, KbLightStateValueName, null);
            if (regLight is int intLight)
            {
                resolvedStatus = (byte)(intLight != 0 ? 1 : 0);
            }
            else
            {
                resolvedStatus = 1;
                TurnOn();
                SetBrightness(255);
                displayBrightness = 255;
            }

            return displayBrightness;
        }

        private static void ResolveStatusColor(byte[] colour)
        {
            object regColor = Registry.GetValue(AppRegistryKey, "KbColor", null);
            if (regColor is int intCol)
            {
                colour[0] = (byte)((intCol >> 16) & 0xFF);
                colour[1] = (byte)((intCol >> 8) & 0xFF);
                colour[2] = (byte)(intCol & 0xFF);
                return;
            }

            colour[0] = 0;
            colour[1] = 0;
            colour[2] = 255;
            SetColour(0, 0, 255);
        }

        private static int ResolveStatusSleep(byte[] sleep, ref byte sleepstatus)
        {
            object regUserSleep = Registry.GetValue(AppRegistryKey, KbUserSleepTimeoutValueName, null);
            if (regUserSleep is int userSleep && userSleep > 0)
            {
                sleepstatus = 1;
                return userSleep;
            }

            object regSleep = Registry.GetValue(AppRegistryKey, "KbSleepTimer", null);
            if (regSleep is int intSleep && intSleep > 0)
            {
                sleepstatus = 1;
                try
                {
                    Registry.SetValue(AppRegistryKey, KbUserSleepTimeoutValueName, intSleep, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.GetStatus.SleepReg", ex);
                }
                return intSleep;
            }

            if (sleepstatus != 0)
            {
                return (sleep[0] * 3600) + (sleep[1] * 60) + sleep[2];
            }

            sleepstatus = 1;
            SetSleepTimer(30);
            return 30;
        }

        private static KeyboardEffect ResolveStatusEffect(byte mode)
        {
            KeyboardEffect effect = null;
            object regEffectActive = Registry.GetValue(AppRegistryKey, "KbEffectActive", null);
            if (regEffectActive is int intActive)
            {
                if (intActive != 0)
                {
                    object regId = Registry.GetValue(AppRegistryKey, "KbEffectId", null);
                    object regUniq = Registry.GetValue(AppRegistryKey, "KbEffectUniqId", null);
                    object regName = Registry.GetValue(AppRegistryKey, "KbEffectName", null);
                    if (regId is int idVal)
                    {
                        byte uniqVal = regUniq is int u ? (byte)u : (byte)0;
                        effect = KeyboardEffect.LookUpEffect(idVal, uniqVal, regName as string);
                    }
                }
            }
            else
            {
                effect = KeyboardEffect.LookUpEffect(mode);
            }

            if (effect != null && !effect.IsSoftware && !GetCapabilities().SupportsHardwareEffects)
            {
                return effect.Id == 3 ? KeyboardEffects.SoftwareCycle : KeyboardEffects.SoftwareBreath;
            }

            return effect;
        }

        public static byte GetCurrentTargetBrightness()
        {
            try
            {
                object regBri = Registry.GetValue(AppRegistryKey, KbBrightnessValueName, null);
                if (regBri is int intBri)
                {
                    byte clamped = (byte)Math.Max(1, Math.Min(255, intBri));
                    _lastTargetBrightness = clamped;
                    return clamped;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.GetCurrentTargetBrightness", ex);
            }

            if (KeyboardEffects.MaxBrightness > 0)
            {
                return KeyboardEffects.MaxBrightness;
            }

            return _lastTargetBrightness > 0 ? _lastTargetBrightness : (byte)255;
        }

        public static void SetBrightnessRealtime(byte value)
        {
            try
            {
                InsydeDriver.SetDCHU_Data(103, new byte[4] { value, 0, 0, 244 }, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetBrightnessRealtime", ex);
            }
        }

        public static void SetBrightness(byte value)
        {
            try
            {
                _lastTargetBrightness = value;
                KeyboardEffects.SetMaxBrightness(value);

                if (!KeyboardEffects.IsBreathingSoftwareEffectRunning())
                {
                    SetBrightnessRealtime(value);
                }

                InsydeDriver.WriteAppSettings(2, 35, 1, ref value);
                try
                {
                    Registry.SetValue(AppRegistryKey, KbBrightnessValueName, (int)value, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.SetBrightness.Reg", ex);
                }

                if (!KeyboardEffect.IsAnySoftwareEffectRunning() && IteHidController.IsDevicePresent)
                {
                    KeyboardStatus status = GetStatus();
                    if (status.LightState && status.Effect != null && status.Effect.IsIteHardwareEffect)
                    {
                        IteHidController.SetHardwareAnimation(
                            status.Effect.IteAnimMode,
                            KeyboardEffect.GetIteSpeedByte(),
                            IteHidController.ScaleBrightness(value),
                            status.Effect.IteBehaviour);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetBrightness", ex);
            }
        }

        public static void SetColourRealtime(byte R, byte G, byte B)
        {
            try
            {
                bool multiZone = _cachedMultiZone ?? GetCapabilities().SupportsMultiZone;
                if (!multiZone)
                {
                    // Single-line RGB boards only use subchannel 240; skipping 241..243 avoids 3 extra WMI stalls.
                    InsydeDriver.SetDCHU_Data(103, new byte[4] { G, R, B, 240 }, 4);
                    return;
                }

                for (byte sub = 240; sub <= 243; sub++)
                {
                    InsydeDriver.SetDCHU_Data(103, new byte[4] { G, R, B, sub }, 4);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetColourRealtime", ex);
            }
        }

        public static void SetZoneColourRealtime(int zone, byte R, byte G, byte B)
        {
            if (zone < 0 || zone > 3) return;
            try
            {
                InsydeDriver.SetDCHU_Data(103, new byte[4] { G, R, B, (byte)(240 + zone) }, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetZoneColourRealtime", ex);
            }
        }

        private static void SetAuxZoneLed(byte subCommand, bool enable, Color color)
        {
            try
            {
                byte[] payload = enable
                    ? new byte[4] { color.G, color.R, color.B, subCommand }
                    : new byte[4] { 0, 0, 0, subCommand };
                InsydeDriver.SetDCHU_Data(103, payload, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetAuxZoneLed", ex);
            }
        }

        public static void SetLogoLed(bool enable, Color color) => SetAuxZoneLed(246, enable, color);

        public static void SetTouchpadLed(bool enable, Color color) => SetAuxZoneLed(243, enable, color);

        public static bool IsPerZoneColorActive()
        {
            try
            {
                if (!GetCapabilities().SupportsMultiZone)
                {
                    return false;
                }
                object regMode = Registry.GetValue(AppRegistryKey, "KbZoneMode", 0);
                return regMode is int zm && zm == 1;
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.IsPerZoneColorActive", ex);
                return false;
            }
        }

        public static Color GetZoneColour(int zone)
        {
            int clamped = Math.Max(0, Math.Min(2, zone));
            try
            {
                object regCol = Registry.GetValue(AppRegistryKey, "KbColorZone" + clamped, null);
                if (regCol is int intCol)
                {
                    return Color.FromArgb(255, (intCol >> 16) & 0xFF, (intCol >> 8) & 0xFF, intCol & 0xFF);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.GetZoneColour", ex);
            }
            return GetStatus().BackLight;
        }

        public static void SetZoneColour(int zone, byte R, byte G, byte B)
        {
            if (zone < 0 || zone > 2) return;
            try
            {
                SetZoneColourRealtime(zone, R, G, B);

                int argb = unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;
                if (zone == 0)
                {
                    byte[] colors = new byte[3] { R, G, B };
                    InsydeDriver.WriteAppSettings(2, 81, 3, ref colors[0]);
                    try
                    {
                        Registry.SetValue(AppRegistryKey, "KbColor", argb, RegistryValueKind.DWord);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("KeyboardBackLight.SetZoneColour.PrimaryReg", ex);
                    }
                }

                byte mode = 8;
                InsydeDriver.WriteAppSettings(2, 32, 1, ref mode);

                try
                {
                    Registry.SetValue(AppRegistryKey, "KbZoneMode", 1, RegistryValueKind.DWord);
                    Registry.SetValue(AppRegistryKey, "KbColorZone" + zone, argb, RegistryValueKind.DWord);

                    string ccZoneKey = @"HKEY_CURRENT_USER\Software\ControlCenter2.0\LED\" + ZoneControlCenterSubKeys[zone];
                    Registry.SetValue(ccZoneKey, "R", (int)R, RegistryValueKind.DWord);
                    Registry.SetValue(ccZoneKey, "G", (int)G, RegistryValueKind.DWord);
                    Registry.SetValue(ccZoneKey, "B", (int)B, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.SetZoneColour.Reg", ex);
                }

                if (!KeyboardEffects.IsBreathingSoftwareEffectRunning())
                {
                    SetBrightnessRealtime(GetCurrentTargetBrightness());
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetZoneColour", ex);
            }
        }

        public static void ApplyCurrentStaticColorsRealtime()
        {
            try
            {
                if (IsPerZoneColorActive())
                {
                    for (int z = 0; z < 3; z++)
                    {
                        Color zc = GetZoneColour(z);
                        SetZoneColourRealtime(z, zc.R, zc.G, zc.B);
                    }
                }
                else
                {
                    KeyboardStatus st = GetStatus();
                    SetColourRealtime(st.BackLight.R, st.BackLight.G, st.BackLight.B);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.ApplyCurrentStaticColorsRealtime", ex);
            }
        }

        public static void SetColour(byte R, byte G, byte B)
        {
            try
            {
                SetColourRealtime(R, G, B);
                byte[] colors = new byte[3] { R, G, B };
                InsydeDriver.WriteAppSettings(2, 81, 3, ref colors[0]);
                byte mode = 8;
                InsydeDriver.WriteAppSettings(2, 32, 1, ref mode);
                try
                {
                    int argb = unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;
                    Registry.SetValue(AppRegistryKey, "KbColor", argb, RegistryValueKind.DWord);
                    Registry.SetValue(AppRegistryKey, "KbZoneMode", 0, RegistryValueKind.DWord);
                    for (int z = 0; z < 3; z++)
                    {
                        Registry.SetValue(AppRegistryKey, "KbColorZone" + z, argb, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.SetColour.Reg", ex);
                }

                if (!KeyboardEffects.IsBreathingSoftwareEffectRunning())
                {
                    SetBrightnessRealtime(GetCurrentTargetBrightness());
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetColour", ex);
            }
        }

        public static void SetColour(Color color) => SetColour(color.R, color.G, color.B);

        public static void SetMode(KeyboardEffect mode, byte? brightnessOverride = null)
        {
            if (mode == null) return;
            try
            {
                byte targetBrightness = brightnessOverride ?? GetCurrentTargetBrightness();
                _lastTargetBrightness = targetBrightness;
                KeyboardEffects.SetMaxBrightness(targetBrightness);

                if (mode.IsIteHardwareEffect && IteHidController.IsDevicePresent)
                {
                    byte iteSpeed = KeyboardEffect.GetIteSpeedByte();
                    byte iteBrightness = IteHidController.ScaleBrightness(targetBrightness);
                    IteHidController.SetHardwareAnimation(mode.IteAnimMode, iteSpeed, iteBrightness, mode.IteBehaviour);
                }

                if (mode.UniqId != 0)
                {
                    InsydeDriver.SetDCHU_Data(103, new byte[4] { 0, 0, 0, mode.UniqId }, 4);
                    byte id = (byte)mode.Id;
                    InsydeDriver.WriteAppSettings(2, 32, 1, ref id);
                    // Clevo EC resets or overrides PWM brightness on hardware mode switch; re-apply target brightness immediately after.
                    SetBrightnessRealtime(targetBrightness);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetMode", ex);
            }
        }

        public static void TurnOff()
        {
            try
            {
                if (IteHidController.IsDevicePresent)
                {
                    IteHidController.TurnOff();
                }

                SetBrightnessRealtime(0);
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 7, 0, 0, 224 }, 4);
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 0, 0, 0, 224 }, 4);
                byte val = 0;
                InsydeDriver.WriteAppSettings(2, 80, 1, ref val);
                InsydeDriver.WriteAppSettings(2, 84, 1, ref val);
                try
                {
                    Registry.SetValue(AppRegistryKey, KbLightStateValueName, 0, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.TurnOff.Reg", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.TurnOff", ex);
            }
        }

        public static void TurnOn(byte? initialBrightness = null)
        {
            try
            {
                byte zoneState = 0;
                InsydeDriver.ReadAppSettings(2, 80, 1, ref zoneState);
                if (zoneState == 0)
                {
                    InsydeDriver.SetDCHU_Data(103, new byte[4] { 7, 0xB0, 7, 224 }, 4);
                }
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 0, 0, 1, 224 }, 4);

                byte targetBri = initialBrightness ?? GetCurrentTargetBrightness();
                SetBrightnessRealtime(targetBri);

                byte val = 1;
                InsydeDriver.WriteAppSettings(2, 80, 1, ref val);
                InsydeDriver.WriteAppSettings(2, 84, 1, ref val);
                try
                {
                    Registry.SetValue(AppRegistryKey, KbLightStateValueName, 1, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.TurnOn.Reg", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.TurnOn", ex);
            }
        }

        /// <summary>
        /// Restores saved keyboard lighting during startup in a single minimal pass without redundant
        /// power-enable toggles or duplicate brightness writes that make the EC backlight flicker.
        /// </summary>
        public static void ApplySavedLightingOnStartup(
            KeyboardStatus kbStatus,
            KeyboardHardwareCapabilities caps,
            KeyboardEffect savedEffect,
            bool shouldStartEffect,
            byte targetBrightness,
            bool skipHardwareWrite)
        {
            if (skipHardwareWrite)
            {
                if (kbStatus.LightState && shouldStartEffect && savedEffect != null && savedEffect.IsSoftware)
                {
                    UiDispatcher.Post(() => savedEffect.SetEffect());
                }
                return;
            }

            if (!kbStatus.LightState)
            {
                TurnOff();
                SetSleepTimer(kbStatus.SleepSecond);
                return;
            }

            byte zoneState = 0;
            byte appLightState = 0;
            byte appMode = 0;
            InsydeDriver.ReadAppSettings(2, 80, 1, ref zoneState);
            InsydeDriver.ReadAppSettings(2, 84, 1, ref appLightState);
            InsydeDriver.ReadAppSettings(2, 32, 1, ref appMode);

            if (shouldStartEffect && savedEffect != null)
            {
                ApplySavedEffectOnStartup(kbStatus, caps, savedEffect, targetBrightness, zoneState, appLightState, appMode);
                SetSleepTimer(kbStatus.SleepSecond);
                return;
            }

            if (caps.SupportsRgb)
            {
                ApplySavedStaticColorsOnStartup(kbStatus);
            }

            EnsurePowerEnabledIfNeeded(zoneState, appLightState, needsStaticMode: caps.SupportsRgb && appMode != 8);
            SetBrightnessRealtime(targetBrightness);
            SyncPowerAndBrightnessMetadata(targetBrightness);
            SetSleepTimer(kbStatus.SleepSecond);
        }

        private static void ApplySavedEffectOnStartup(
            KeyboardStatus kbStatus,
            KeyboardHardwareCapabilities caps,
            KeyboardEffect savedEffect,
            byte targetBrightness,
            byte zoneState,
            byte appLightState,
            byte appMode)
        {
            if (savedEffect.IsSoftware)
            {
                if (caps.SupportsRgb)
                {
                    if (savedEffect.Id == 8)
                    {
                        SetColourRealtime(kbStatus.BackLight.R, kbStatus.BackLight.G, kbStatus.BackLight.B);
                    }
                    else if (savedEffect.Id == 9 || savedEffect.Id == 10)
                    {
                        SetColourRealtime(255, 0, 0);
                    }
                }

                EnsurePowerEnabledIfNeeded(zoneState, appLightState, needsStaticMode: caps.SupportsRgb && appMode != 8);
                SyncPowerAndBrightnessMetadata(targetBrightness);
                UiDispatcher.Post(() => savedEffect.SetEffect());
                return;
            }

            if (!savedEffect.IsIteHardwareEffect && zoneState == 0)
            {
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 7, 0xB0, 7, 224 }, 4);
            }

            // Skip TurnOn() static mode so hardware effects do not flash static color first.
            SetMode(savedEffect, targetBrightness);
            SyncPowerAndBrightnessMetadata(targetBrightness);
        }

        private static void ApplySavedStaticColorsOnStartup(KeyboardStatus kbStatus)
        {
            if (IsPerZoneColorActive())
            {
                ApplyCurrentStaticColorsRealtime();
                Color z0 = GetZoneColour(0);
                SyncStaticColorMetadata(z0.R, z0.G, z0.B);
            }
            else
            {
                SetColourRealtime(kbStatus.BackLight.R, kbStatus.BackLight.G, kbStatus.BackLight.B);
                SyncStaticColorMetadata(kbStatus.BackLight.R, kbStatus.BackLight.G, kbStatus.BackLight.B);
            }
        }

        private static void EnsurePowerEnabledIfNeeded(byte zoneState, byte appLightState, bool needsStaticMode)
        {
            if (zoneState == 0)
            {
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 7, 0xB0, 7, 224 }, 4);
            }
            if (zoneState == 0 || appLightState == 0 || needsStaticMode)
            {
                InsydeDriver.SetDCHU_Data(103, new byte[4] { 0, 0, 1, 224 }, 4);
            }
        }

        private static void SyncStaticColorMetadata(byte r, byte g, byte b)
        {
            byte[] colors = new byte[3] { r, g, b };
            InsydeDriver.WriteAppSettings(2, 81, 3, ref colors[0]);
            byte mode = 8;
            InsydeDriver.WriteAppSettings(2, 32, 1, ref mode);
        }

        private static void SyncPowerAndBrightnessMetadata(byte brightness)
        {
            _lastTargetBrightness = brightness;
            KeyboardEffects.SetMaxBrightness(brightness);

            byte bri = brightness;
            InsydeDriver.WriteAppSettings(2, 35, 1, ref bri);
            byte one = 1;
            InsydeDriver.WriteAppSettings(2, 80, 1, ref one);
            InsydeDriver.WriteAppSettings(2, 84, 1, ref one);
            try
            {
                Registry.SetValue(AppRegistryKey, KbBrightnessValueName, (int)brightness, RegistryValueKind.DWord);
                Registry.SetValue(AppRegistryKey, KbLightStateValueName, 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SyncPowerAndBrightnessMetadata.Reg", ex);
            }
        }

        public static void OverrideBootEffect(bool value)
        {
            try
            {
                byte num = (byte)(value ? 1 : 0);
                InsydeDriver.SetDCHU_Data(121, new byte[4] { num, 0, 0, 30 }, 4);
                InsydeDriver.WriteAppSettings(2, 7, 1, ref num);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.OverrideBootEffect", ex);
            }
        }

        public static uint BuildSleepTimerCommand(int seconds)
        {
            int clamped = Math.Max(0, Math.Min(65535, seconds));
            return 402653184u | ((uint)clamped << 8) | byte.MaxValue;
        }

        public static void SetSleepTimer(int value)
        {
            try
            {
                value = Math.Max(0, Math.Min(65535, value));
                byte active = 1;
                InsydeDriver.WriteAppSettings(2, 36, 1, ref active);
                byte[] times = new byte[3] { (byte)(value / 3600), (byte)(value / 60 % 60), (byte)(value % 60) };
                InsydeDriver.WriteAppSettings(2, 37, 3, ref times[0]);
                byte[] bytes = BitConverter.GetBytes(BuildSleepTimerCommand(value));
                InsydeDriver.SetDCHU_Data(121, bytes, bytes.Length);
                try
                {
                    Registry.SetValue(AppRegistryKey, "KbSleepTimer", value, RegistryValueKind.DWord);
                    Registry.SetValue(AppRegistryKey, KbUserSleepTimeoutValueName, value, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.SetSleepTimer.Reg", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetSleepTimer", ex);
            }
        }

        public static void TurnSleepTimerOff(bool updateRegistry = true)
        {
            try
            {
                InsydeDriver.SetDCHU_Data(121, new byte[4] { 0, 0, 0, 24 }, 4);
                byte val = 0;
                InsydeDriver.WriteAppSettings(2, 36, 1, ref val);
                if (updateRegistry)
                {
                    try
                    {
                        Registry.SetValue(AppRegistryKey, "KbSleepTimer", 0, RegistryValueKind.DWord);
                        Registry.SetValue(AppRegistryKey, KbUserSleepTimeoutValueName, 0, RegistryValueKind.DWord);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("KeyboardBackLight.TurnSleepTimerOff.Reg", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.TurnSleepTimerOff", ex);
            }
        }

        public static void SetLightBarColor(byte r, byte g, byte b, byte brightness = 100)
        {
            try
            {
                IteHidController.SetLightBarColor(r, g, b, brightness);
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetLightBarColor", ex);
            }
        }

        public static void SetLightBarOff()
        {
            try
            {
                IteHidController.SetLightBarOff();
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.SetLightBarOff", ex);
            }
        }

        public static int BrightnessToStep(byte brightness, bool isLightOn)
        {
            if (!isLightOn || brightness == 0) return 0;
            if (brightness <= 64) return 1;
            if (brightness <= 128) return 2;
            if (brightness <= 192) return 3;
            return 4;
        }

        public static int CycleBrightness()
        {
            lock (_backlightAdjustLock)
            {
                try
                {
                    KeyboardStatus status = GetStatus();
                    int currentStep = BrightnessToStep(status.BackLight.A, status.LightState);
                    int nextStep = (currentStep + 1) % 5;
                    if (nextStep == 0)
                    {
                        KeyboardEffect.StopAllSoftwareEffects();
                        TurnOff();
                    }
                    else
                    {
                        ApplyBrightnessStep(nextStep, status);
                    }
                    NotifyKeyboardUiChanged();
                    return nextStep;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.CycleBrightness", ex);
                    return 0;
                }
            }
        }

        public static int? IncreaseBrightness()
        {
            lock (_backlightAdjustLock)
            {
                try
                {
                    KeyboardStatus status = GetStatus();
                    int currentStep = BrightnessToStep(status.BackLight.A, status.LightState);
                    if (status.LightState && currentStep >= 4)
                    {
                        return null;
                    }
                    int nextStep = Math.Min(4, Math.Max(1, currentStep + 1));
                    ApplyBrightnessStep(nextStep, status);
                    NotifyKeyboardUiChanged();
                    return nextStep;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.IncreaseBrightness", ex);
                    return null;
                }
            }
        }

        public static int? DecreaseBrightness()
        {
            lock (_backlightAdjustLock)
            {
                try
                {
                    KeyboardStatus status = GetStatus();
                    int currentStep = BrightnessToStep(status.BackLight.A, status.LightState);
                    if (!status.LightState || currentStep <= 1)
                    {
                        return null;
                    }
                    int nextStep = Math.Max(1, currentStep - 1);
                    ApplyBrightnessStep(nextStep, status);
                    NotifyKeyboardUiChanged();
                    return nextStep;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.DecreaseBrightness", ex);
                    return null;
                }
            }
        }

        private static void ApplyBrightnessStep(int step, KeyboardStatus status)
        {
            int clamped = Math.Max(1, Math.Min(4, step));
            byte rawBrightness = BrightnessStepValues[clamped];
            bool wasOff = !status.LightState;
            bool isBreathing = status.Effect != null && status.Effect.IsBreathingSoftwareEffect;

            if (wasOff)
            {
                TurnOn(isBreathing ? (byte)0 : rawBrightness);
            }
            SetBrightness(rawBrightness);

            bool softwareRunning = KeyboardEffect.IsAnySoftwareEffectRunning();
            if (wasOff && status.Effect != null)
            {
                RestoreEffectAtBrightness(status.Effect, status.BackLight, rawBrightness);
            }
            else if (wasOff && status.Effect == null && !softwareRunning && GetCapabilities().SupportsRgb)
            {
                ApplyCurrentStaticColorsRealtime();
                SetBrightnessRealtime(rawBrightness);
            }
        }

        private static void RestoreEffectAtBrightness(KeyboardEffect effect, Color staticColor, byte rawBrightness)
        {
            if (effect == null) return;

            if (effect.IsSoftware)
            {
                if (KeyboardEffects.IsRunningOnBattery)
                {
                    if (GetCapabilities().SupportsRgb)
                    {
                        SetColourRealtime(staticColor.R, staticColor.G, staticColor.B);
                    }
                    SetBrightnessRealtime(rawBrightness);
                    return;
                }

                if (effect.Id == 8 && GetCapabilities().SupportsRgb)
                {
                    SetColourRealtime(staticColor.R, staticColor.G, staticColor.B);
                }

                SetBrightnessRealtime(effect.IsBreathingSoftwareEffect ? (byte)0 : rawBrightness);

                UiDispatcher.Post(() =>
                {
                    effect.SetEffect();
                });
            }
            else
            {
                SetMode(effect, rawBrightness);
            }
        }

        public static bool ToggleBacklight(bool? forceState = null)
        {
            lock (_backlightAdjustLock)
            {
                try
                {
                    KeyboardStatus status = GetStatus();
                    bool targetOn = forceState ?? !status.LightState;
                    if (!targetOn)
                    {
                        KeyboardEffect.StopAllSoftwareEffects();
                        TurnOff();
                    }
                    else
                    {
                        int currentStep = BrightnessToStep(status.BackLight.A, true);
                        if (currentStep < 1) currentStep = 4;
                        status.LightState = false;
                        ApplyBrightnessStep(currentStep, status);
                    }
                    NotifyKeyboardUiChanged();
                    return targetOn;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.ToggleBacklight", ex);
                    return true;
                }
            }
        }

        public static ColorPresetItem? CycleColorPreset()
        {
            lock (_backlightAdjustLock)
            {
                if (MonotonicClock.ElapsedMs(_lastColorCycleTicks) < 350.0)
                {
                    return null;
                }
                _lastColorCycleTicks = MonotonicClock.NowTicks();

                try
                {
                    if (!GetCapabilities().SupportsRgb)
                    {
                        return null;
                    }

                    KeyboardEffect.StopAllSoftwareEffects();

                    KeyboardStatus status = GetStatus();
                    int foundIndex = -1;
                    for (int i = 0; i < QuickColorPresets.Length; i++)
                    {
                        if (QuickColorPresets[i].Color.R == status.BackLight.R &&
                            QuickColorPresets[i].Color.G == status.BackLight.G &&
                            QuickColorPresets[i].Color.B == status.BackLight.B)
                        {
                            foundIndex = i;
                            break;
                        }
                    }

                    _currentColorPresetIndex = foundIndex >= 0
                        ? (foundIndex + 1) % QuickColorPresets.Length
                        : (_currentColorPresetIndex + 1) % QuickColorPresets.Length;

                    ColorPresetItem nextPreset = QuickColorPresets[_currentColorPresetIndex];
                    byte targetBrightness = status.BackLight.A > 0 ? status.BackLight.A : (byte)255;
                    TurnOn();
                    SetBrightness(targetBrightness);
                    SetColour(nextPreset.Color.R, nextPreset.Color.G, nextPreset.Color.B);
                    NotifyKeyboardUiChanged();
                    return nextPreset;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("KeyboardBackLight.CycleColorPreset", ex);
                    return null;
                }
            }
        }

        private static void NotifyKeyboardUiChanged()
        {
            try
            {
                BacklightChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardBackLight.NotifyKeyboardUiChanged", ex);
            }
        }
    }
}
