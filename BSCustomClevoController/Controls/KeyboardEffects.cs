using System;
using System.Drawing;
using System.Threading;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    internal static class KeyboardEffects
    {
        private const string AppRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";

        public static int Duration = 1000;

        public static KeyboardEffect Breath = new KeyboardEffect() { Name = "Breath", UniqId = 16, Id = 2, IteAnimMode = 0x02 };
        public static KeyboardEffect Cycle = new KeyboardEffect() { Name = "Cycle", UniqId = 51, Id = 3 };
        public static KeyboardEffect Wave = new KeyboardEffect() { Name = "Wave", UniqId = 176, Id = 4, IteAnimMode = 0x03, IteBehaviour = 0x01 };
        public static KeyboardEffect Dance = new KeyboardEffect() { Name = "Dance", UniqId = 128, Id = 5 };
        public static KeyboardEffect Tempo = new KeyboardEffect() { Name = "Tempo", UniqId = 144, Id = 6 };
        public static KeyboardEffect Flash = new KeyboardEffect() { Name = "Flash", UniqId = 160, Id = 7 };

        // ITE 8291 multi-directional wave (tuxedo-drivers ite_8291.c)
        public static KeyboardEffect WaveLeftRight = new KeyboardEffect() { Name = "Wave: Left to Right", UniqId = 176, Id = 4, IteAnimMode = 0x03, IteBehaviour = 0x01 };
        public static KeyboardEffect WaveRightLeft = new KeyboardEffect() { Name = "Wave: Right to Left", UniqId = 176, Id = 11, IteAnimMode = 0x03, IteBehaviour = 0x02 };
        public static KeyboardEffect WaveBottomTop = new KeyboardEffect() { Name = "Wave: Bottom to Top", UniqId = 176, Id = 12, IteAnimMode = 0x03, IteBehaviour = 0x03 };
        public static KeyboardEffect WaveTopBottom = new KeyboardEffect() { Name = "Wave: Top to Bottom", UniqId = 176, Id = 13, IteAnimMode = 0x03, IteBehaviour = 0x04 };

        // ITE 8291 interactive and hardware engine modes
        public static KeyboardEffect Reactive = new KeyboardEffect() { Name = "Reactive (Key Press)", UniqId = 0, Id = 14, IteAnimMode = 0x04, IteBehaviour = 0x00 };
        public static KeyboardEffect Ripple = new KeyboardEffect() { Name = "Ripple (Key Waves)", UniqId = 0, Id = 15, IteAnimMode = 0x06, IteBehaviour = 0x00 };
        public static KeyboardEffect Rainbow = new KeyboardEffect() { Name = "Rainbow Wave", UniqId = 51, Id = 16, IteAnimMode = 0x05, IteBehaviour = 0x00 };
        public static KeyboardEffect Marquee = new KeyboardEffect() { Name = "Marquee", UniqId = 128, Id = 17, IteAnimMode = 0x09, IteBehaviour = 0x00 };
        public static KeyboardEffect Raindrop = new KeyboardEffect() { Name = "Raindrop", UniqId = 112, Id = 18, IteAnimMode = 0x0A, IteBehaviour = 0x00 };
        public static KeyboardEffect Aurora = new KeyboardEffect() { Name = "Aurora", UniqId = 144, Id = 19, IteAnimMode = 0x0E, IteBehaviour = 0x00 };
        public static KeyboardEffect Spark = new KeyboardEffect() { Name = "Spark", UniqId = 160, Id = 20, IteAnimMode = 0x11, IteBehaviour = 0x00 };

        public static KeyboardEffect SoftwareBreath = new KeyboardEffect() { Name = "Breath", UniqId = 0, Id = 8, Func = BreathEffect, Milliseconds = 25 };
        public static KeyboardEffect SoftwareCycle = new KeyboardEffect() { Name = "Cycle", UniqId = 0, Id = 9, Func = CycleEffect, Milliseconds = 25 };
        public static KeyboardEffect SoftwareBreathColor = new KeyboardEffect() { Name = "Colorful Breath", UniqId = 0, Id = 10, Func = ColorfulBreathEffect, Milliseconds = 25 };

        public static KeyboardEffect[] Effects = new KeyboardEffect[]
        {
            Breath, Cycle, Wave, Dance, Tempo, Flash,
            WaveLeftRight, WaveRightLeft, WaveBottomTop, WaveTopBottom,
            Reactive, Ripple, Rainbow, Marquee, Raindrop, Aurora, Spark,
            SoftwareBreath, SoftwareCycle, SoftwareBreathColor
        };

        public static KeyboardEffect[] GetAvailableEffects(KeyboardHardwareCapabilities caps, bool allowSoftwareEffects = false)
        {
            if (caps == null) return Effects;

            var list = new System.Collections.Generic.List<KeyboardEffect>();

            if (caps.SupportsHardwareEffects)
            {
                list.Add(Breath);
                list.Add(Cycle);

                if (caps.SupportsMultiZone)
                {
                    list.Add(WaveLeftRight);
                    list.Add(WaveRightLeft);
                    list.Add(WaveBottomTop);
                    list.Add(WaveTopBottom);
                }

                list.Add(Dance);
                list.Add(Tempo);
                list.Add(Flash);

                if (caps.SupportsIteDirectHid)
                {
                    list.Add(Reactive);
                    list.Add(Ripple);
                    list.Add(Rainbow);
                    list.Add(Marquee);
                    list.Add(Raindrop);
                    list.Add(Aurora);
                    list.Add(Spark);
                }
            }

            if (allowSoftwareEffects && caps.SupportsBacklight)
            {
                if (!caps.SupportsHardwareEffects)
                {
                    list.Add(SoftwareBreath);
                }

                if (caps.SupportsRgb)
                {
                    if (!caps.SupportsHardwareEffects)
                    {
                        list.Add(SoftwareCycle);
                    }
                    list.Add(SoftwareBreathColor);
                }
            }

            return list.ToArray();
        }

        public static bool IsAnySoftwareEffectActive()
        {
            try
            {
                if (KeyboardEffect.IsAnySoftwareEffectRunning())
                {
                    return true;
                }

                object regActive = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectActive", null);
                object regSoft = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectIsSoftware", null);
                if (regActive is int a && a == 1 && regSoft is int s && s == 1)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardEffects.IsAnySoftwareEffectActive", ex);
            }
            return false;
        }

        public static bool IsRunningOnBattery => !NativeMethods.IsAcLineOnline(defaultIfUnknown: true);

        public static KeyboardEffect SavedBatterySoftwareEffect { get; set; }

        private static void RestoreStaticLightingAndSleepTimer()
        {
            KeyboardStatus status = KeyboardBackLight.GetStatus();
            if (status.LightState)
            {
                KeyboardBackLight.TurnOn();
                byte bri = status.BackLight.A > 0 ? status.BackLight.A : (byte)255;
                MaxBrightness = bri;
                KeyboardBackLight.ApplyCurrentStaticColorsRealtime();
                KeyboardBackLight.SetBrightnessRealtime(bri);
            }
            else
            {
                KeyboardBackLight.TurnOff();
            }

            int sleepSec = 30;
            object regUserSleep = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbUserSleepTimeout", null);
            if (regUserSleep is int us && us > 0)
            {
                sleepSec = us;
            }
            KeyboardBackLight.SetSleepTimer(sleepSec);
        }

        public static void DisableSoftwareAnimationForExit()
        {
            try
            {
                bool wasSoftwareRunning = KeyboardEffect.IsAnySoftwareEffectRunning();
                KeyboardEffect.StopAllSoftwareEffects();
                if (wasSoftwareRunning)
                {
                    RestoreStaticLightingAndSleepTimer();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardEffects.DisableSoftwareAnimationForExit", ex);
            }
        }

        public static void HandlePowerStatusChanged(bool isAcOnline)
        {
            try
            {
                if (!isAcOnline)
                {
                    if (IsAnySoftwareEffectActive())
                    {
                        KeyboardEffect runningEffect = KeyboardEffect.FindSoftwareEffect();
                        if (runningEffect == null)
                        {
                            object regId = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectId", null);
                            if (regId is int idVal)
                            {
                                runningEffect = KeyboardEffect.LookUpEffectById(idVal);
                            }
                        }

                        if (runningEffect != null && runningEffect.IsSoftware)
                        {
                            SavedBatterySoftwareEffect = runningEffect;
                        }

                        KeyboardEffect.StopAllSoftwareEffects();
                        RestoreStaticLightingAndSleepTimer();
                    }
                }
                else
                {
                    KeyboardEffect effectToResume = SavedBatterySoftwareEffect;
                    SavedBatterySoftwareEffect = null;

                    if (effectToResume == null)
                    {
                        object regActive = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectActive", null);
                        object regLight = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbLightState", null);
                        bool lightOn = !(regLight is int l && l == 0);

                        if (regActive is int a && a == 1 && lightOn)
                        {
                            object regId = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectId", null);
                            object regUniq = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectUniqId", null);
                            object regName = Microsoft.Win32.Registry.GetValue(AppRegistryKey, "KbEffectName", null);

                            if (regId is int idVal)
                            {
                                byte uniqVal = regUniq is int u ? (byte)u : (byte)0;
                                string nameVal = regName as string;
                                KeyboardEffect savedEffect = KeyboardEffect.LookUpEffect(idVal, uniqVal, nameVal);
                                if (savedEffect != null && savedEffect.IsSoftware)
                                {
                                    effectToResume = savedEffect;
                                }
                            }
                        }
                    }

                    if (effectToResume != null && effectToResume.IsSoftware)
                    {
                        KeyboardBackLight.TurnSleepTimerOff(updateRegistry: false);
                        UiDispatcher.Post(() =>
                        {
                            effectToResume.SetEffect();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("KeyboardEffects.HandlePowerStatusChanged", ex);
            }
        }

        private static readonly Color[] RainbowColors = new Color[]
        {
            Color.FromArgb(255, 0, 0),
            Color.FromArgb(255, 255, 0),
            Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 255, 255),
            Color.FromArgb(0, 0, 255),
            Color.FromArgb(255, 0, 255)
        };

        public static volatile byte MaxBrightness = 255;
        private static int _breathColorIndex = -1;
        private static long _transitionStartTicks = MonotonicClock.NowTicks();
        private static int _lastBrightness = -1;
        private static int _lastR = -1;
        private static int _lastG = -1;
        private static int _lastB = -1;
        private static int _liveEffectId = -1;
        private static int _liveColorIndex = 0;
        private static long _livePhaseBits = BitConverter.DoubleToInt64Bits(0.5);
        private static long _liveWrittenTicks = MonotonicClock.NowTicks();

        public static bool IsBreathingSoftwareEffectRunning()
        {
            KeyboardEffect running = KeyboardEffect.FindSoftwareEffect();
            return running != null && running.IsBreathingSoftwareEffect;
        }

        public static void ResetTime()
        {
            long now = MonotonicClock.NowTicks();
            Interlocked.Exchange(ref _transitionStartTicks, now);
            Volatile.Write(ref _breathColorIndex, -1);
            Volatile.Write(ref _lastBrightness, -1);
            Volatile.Write(ref _lastR, -1);
            Volatile.Write(ref _lastG, -1);
            Volatile.Write(ref _lastB, -1);
            Volatile.Write(ref _liveEffectId, -1);
            Volatile.Write(ref _liveColorIndex, 0);
            Interlocked.Exchange(ref _livePhaseBits, BitConverter.DoubleToInt64Bits(0.5));
            Interlocked.Exchange(ref _liveWrittenTicks, now);
        }

        public static bool TryComputeEffectColor(
            int effectId,
            int durationMs,
            double elapsedMs,
            byte staticR,
            byte staticG,
            byte staticB,
            byte maxBrightness,
            out byte r,
            out byte g,
            out byte b,
            out byte brightness)
        {
            r = staticR;
            g = staticG;
            b = staticB;
            brightness = maxBrightness;

            double safeElapsed = Math.Max(0.0, elapsedMs);
            if (effectId == 2 || effectId == 8) // Breath / Software Breath
            {
                double cycleDuration = Math.Max(200.0, durationMs * 2.0);
                double phase = (safeElapsed % cycleDuration) / cycleDuration;
                brightness = ScaleBreathBrightness(phase, maxBrightness);
                return true;
            }

            if (effectId == 10) // Colorful Breath
            {
                double cycleDuration = Math.Max(200.0, durationMs * 2.0);
                int colorIdx = (int)(safeElapsed / cycleDuration) % RainbowColors.Length;
                Color c = RainbowColors[colorIdx];
                r = c.R;
                g = c.G;
                b = c.B;
                double phase = (safeElapsed % cycleDuration) / cycleDuration;
                brightness = ScaleBreathBrightness(phase, maxBrightness);
                return true;
            }

            if (effectId == 3 || effectId == 9 || effectId == 4 || effectId == 11 || effectId == 12 || effectId == 13 || effectId == 16)
            {
                double stepDuration = Math.Max(100.0, durationMs);
                double totalCycle = stepDuration * RainbowColors.Length;
                double cycleElapsed = safeElapsed % totalCycle;
                int fromIndex = (int)(cycleElapsed / stepDuration) % RainbowColors.Length;
                int toIndex = (fromIndex + 1) % RainbowColors.Length;
                float t = (float)((cycleElapsed % stepDuration) / stepDuration);
                Color c = InterpolateColor(RainbowColors[fromIndex], RainbowColors[toIndex], t);
                r = c.R;
                g = c.G;
                b = c.B;
                brightness = maxBrightness;
                return true;
            }

            return false;
        }

        public static bool TryGetLiveSample(
            byte staticR,
            byte staticG,
            byte staticB,
            out int effectId,
            out byte r,
            out byte g,
            out byte b,
            out byte brightness,
            out double elapsedMs)
        {
            KeyboardEffect running = KeyboardEffect.FindSoftwareEffect();
            if (running != null)
            {
                effectId = running.Id;
                elapsedMs = MonotonicClock.ElapsedMs(Interlocked.Read(ref _transitionStartTicks));
                return TryComputeEffectColor(effectId, Volatile.Read(ref Duration), elapsedMs, staticR, staticG, staticB, MaxBrightness, out r, out g, out b, out brightness);
            }

            effectId = 0;
            r = staticR;
            g = staticG;
            b = staticB;
            brightness = MaxBrightness;
            elapsedMs = 0.0;
            return false;
        }

        public static bool TryGetLiveBreathState(int expectedEffectId, out int colorIndex, out double phase)
        {
            KeyboardEffect running = KeyboardEffect.FindSoftwareEffect();
            if (running != null && running.Id == expectedEffectId && (expectedEffectId == 8 || expectedEffectId == 10))
            {
                if (Volatile.Read(ref _liveEffectId) == expectedEffectId)
                {
                    double cycleDuration = Math.Max(200.0, Volatile.Read(ref Duration) * 2.0);
                    double committedPhase = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _livePhaseBits));
                    int committedColor = Volatile.Read(ref _liveColorIndex);
                    double sinceWriteMs = Math.Max(0.0, MonotonicClock.ElapsedMs(Interlocked.Read(ref _liveWrittenTicks)));

                    // Smoothly bridge between hardware WMI writes without crossing 0% ahead of physical LEDs.
                    double advanced = committedPhase + Math.Min(sinceWriteMs, 35.0) / cycleDuration;
                    phase = (committedPhase >= 0.5 && advanced >= 1.0) ? 0.996 : Math.Min(1.0, advanced);
                    colorIndex = expectedEffectId == 10 ? committedColor : 0;
                    return true;
                }

                colorIndex = 0;
                phase = 0.5;
                return true;
            }

            colorIndex = 0;
            phase = 0.5;
            return false;
        }

        private static byte ScaleBreathBrightness(double phase, byte maxBrightness)
        {
            if (maxBrightness == 0)
            {
                return 0;
            }

            // Gamma-compensated cosine curve aligns linear LED PWM falloff with perceived screen brightness.
            double ease = 0.5 - 0.5 * Math.Cos(phase * Math.PI * 2.0);
            double perceived = Math.Pow(Math.Max(0.0, Math.Min(1.0, ease)), 1.85);
            int scaled = (int)Math.Round(perceived * maxBrightness);
            return (byte)Math.Max(0, Math.Min((int)maxBrightness, scaled));
        }

        private static byte ComputeBreathBrightness(double elapsedMs, double cycleDurationMs, out double phase)
        {
            // Offset hardware timer by half cycle so breathing begins smoothly at peak brightness
            double shifted = elapsedMs + cycleDurationMs * 0.5;
            phase = (shifted % cycleDurationMs) / cycleDurationMs;
            return ScaleBreathBrightness(phase, MaxBrightness);
        }

        private static bool ShouldSendBrightnessWrite(byte targetBrightness)
        {
            int prev = _lastBrightness;
            if (prev < 0) return true;
            if (targetBrightness == prev) return false;
            if (targetBrightness == 0 || prev == 0) return true;
            int minDelta = targetBrightness <= 24 ? 1 : 2;
            return Math.Abs(targetBrightness - prev) >= minDelta;
        }

        private static void PublishLiveBreathState(int effectId, int colorIndex, double phase)
        {
            Volatile.Write(ref _liveColorIndex, colorIndex);
            Interlocked.Exchange(ref _livePhaseBits, BitConverter.DoubleToInt64Bits(phase));
            Interlocked.Exchange(ref _liveWrittenTicks, MonotonicClock.NowTicks());
            Volatile.Write(ref _liveEffectId, effectId);
        }

        public static void BreathEffect(object sender, EventArgs e)
        {
            double cycleDuration = Math.Max(200.0, Volatile.Read(ref Duration) * 2.0);
            double elapsed = MonotonicClock.ElapsedMs(Interlocked.Read(ref _transitionStartTicks));
            byte targetBrightness = ComputeBreathBrightness(elapsed, cycleDuration, out double phase);

            if (ShouldSendBrightnessWrite(targetBrightness))
            {
                _lastBrightness = targetBrightness;
                KeyboardBackLight.SetBrightnessRealtime(targetBrightness);
                PublishLiveBreathState(8, 0, phase);
            }
        }

        public static void ColorfulBreathEffect(object sender, EventArgs e)
        {
            double cycleDuration = Math.Max(200.0, Volatile.Read(ref Duration) * 2.0);
            double elapsed = MonotonicClock.ElapsedMs(Interlocked.Read(ref _transitionStartTicks));
            double shifted = elapsed + cycleDuration * 0.5;
            int colorIdx = (int)(shifted / cycleDuration) % RainbowColors.Length;
            byte targetBrightness = ComputeBreathBrightness(elapsed, cycleDuration, out double phase);

            int prevColorIdx = Volatile.Read(ref _breathColorIndex);
            bool colorChanged = colorIdx != prevColorIdx;
            if (colorChanged)
            {
                if (prevColorIdx >= 0 && _lastBrightness != 0)
                {
                    // Drop to 0% before changing color registers so the LED never flashes the next hue.
                    _lastBrightness = 0;
                    KeyboardBackLight.SetBrightnessRealtime(0);
                    targetBrightness = 0;
                }

                Volatile.Write(ref _breathColorIndex, colorIdx);
                Color baseColor = RainbowColors[colorIdx];
                KeyboardBackLight.SetColourRealtime(baseColor.R, baseColor.G, baseColor.B);
                PublishLiveBreathState(10, colorIdx, prevColorIdx >= 0 ? 0.0 : phase);
            }

            if (ShouldSendBrightnessWrite(targetBrightness) || (colorChanged && prevColorIdx < 0))
            {
                _lastBrightness = targetBrightness;
                KeyboardBackLight.SetBrightnessRealtime(targetBrightness);
                PublishLiveBreathState(10, colorIdx, phase);
            }
        }

        private static Color InterpolateColor(Color start, Color end, float t)
        {
            int r = (int)(start.R + (end.R - start.R) * t);
            int g = (int)(start.G + (end.G - start.G) * t);
            int b = (int)(start.B + (end.B - start.B) * t);
            return Color.FromArgb(255, Clamp(r), Clamp(g), Clamp(b));
        }

        public static void CycleEffect(object sender, EventArgs e)
        {
            double stepDuration = Math.Max(100.0, Volatile.Read(ref Duration));
            double totalCycle = stepDuration * RainbowColors.Length;
            double elapsed = MonotonicClock.ElapsedMs(Interlocked.Read(ref _transitionStartTicks)) % totalCycle;

            int fromIndex = (int)(elapsed / stepDuration) % RainbowColors.Length;
            int toIndex = (fromIndex + 1) % RainbowColors.Length;
            float t = (float)((elapsed % stepDuration) / stepDuration);

            Color interpolated = InterpolateColor(RainbowColors[fromIndex], RainbowColors[toIndex], t);
            bool colorChanged = interpolated.R != _lastR || interpolated.G != _lastG || interpolated.B != _lastB;
            if (colorChanged)
            {
                _lastR = interpolated.R;
                _lastG = interpolated.G;
                _lastB = interpolated.B;
                KeyboardBackLight.SetColourRealtime(interpolated.R, interpolated.G, interpolated.B);
            }

            byte targetMax = MaxBrightness;
            if (colorChanged || _lastBrightness != targetMax)
            {
                _lastBrightness = targetMax;
                KeyboardBackLight.SetBrightnessRealtime(targetMax);
            }
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));
    }
}
