using System;
using System.Collections.Generic;
using BSCustomClevoController.Utility;

namespace BSCustomClevoController.Controls
{
    public class KeyboardEffect
    {
        public byte UniqId { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public int Milliseconds { get; set; }
        public byte IteAnimMode { get; set; }
        public byte IteBehaviour { get; set; }
        public Action<object, EventArgs> Func { get; set; }

        public bool IsIteHardwareEffect => IteAnimMode > 0;

        private IEffectTimer _timer;
        public IEffectTimer Timer
        {
            get
            {
                if (_timer == null && Func != null)
                {
                    int intervalMs = Milliseconds > 0 ? Milliseconds : 16;
                    _timer = UiDispatcher.CreateEffectTimer(
                        TimeSpan.FromMilliseconds(intervalMs),
                        (s, e) => Func?.Invoke(s, e));
                }
                return _timer;
            }
        }

        public static KeyboardEffect LookUpEffect(byte mode)
        {
            if (mode == 8) return FindSoftwareEffect();
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.UniqId == mode) return effect;
            }
            return null;
        }

        public static KeyboardEffect LookUpEffect(int id, byte uniqId, string name = null)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (KeyboardEffect effect in KeyboardEffects.Effects)
                {
                    if (string.Equals(effect.Name, name, StringComparison.OrdinalIgnoreCase) && effect.Id == id)
                    {
                        return effect;
                    }
                }
            }
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.Id == id && effect.UniqId == uniqId) return effect;
            }
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.Id == id) return effect;
            }
            return null;
        }

        public static KeyboardEffect LookUpEffectById(int id)
        {
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.Id == id) return effect;
            }
            return null;
        }

        public static KeyboardEffect FindSoftwareEffect()
        {
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.UniqId == 0 && effect._timer != null && effect._timer.IsEnabled) return effect;
            }
            return null;
        }

        public static void StopAllSoftwareEffects()
        {
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.UniqId == 0 && effect._timer != null && effect._timer.IsEnabled)
                {
                    effect._timer.Stop();
                }
            }
        }

        public bool IsSoftware => UniqId == 0 && !IsIteHardwareEffect;
        public bool IsBreathingSoftwareEffect => Id == 8 || Id == 10;

        public static bool IsAnySoftwareEffectRunning()
        {
            foreach (KeyboardEffect effect in KeyboardEffects.Effects)
            {
                if (effect.UniqId == 0 && effect._timer != null && effect._timer.IsEnabled)
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly Dictionary<string, string> EffectKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Breath", "KbEffect_Breath" },
            { "Cycle", "KbEffect_Cycle" },
            { "Wave", "KbEffect_Wave" },
            { "Dance", "KbEffect_Dance" },
            { "Tempo", "KbEffect_Tempo" },
            { "Flash", "KbEffect_Flash" },
            { "Wave: Left to Right", "KbEffect_WaveLeftRight" },
            { "Wave: Right to Left", "KbEffect_WaveRightLeft" },
            { "Wave: Bottom to Top", "KbEffect_WaveBottomTop" },
            { "Wave: Top to Bottom", "KbEffect_WaveTopBottom" },
            { "Reactive (Key Press)", "KbEffect_Reactive" },
            { "Ripple (Key Waves)", "KbEffect_Ripple" },
            { "Rainbow Wave", "KbEffect_Rainbow" },
            { "Marquee", "KbEffect_Marquee" },
            { "Raindrop", "KbEffect_Raindrop" },
            { "Aurora", "KbEffect_Aurora" },
            { "Spark", "KbEffect_Spark" },
            { "Colorful Breath", "KbEffect_ColorfulBreath" },
        };

        /// <summary>Localized display name for UI bindings while keeping <see cref="Name"/> invariant for lookups and registry.</summary>
        public string LocalizedName => EffectKeyMap.TryGetValue(Name ?? string.Empty, out string key)
            ? LocalizationManager.T(key, Name)
            : Name;

        public string TypeLabel => LocalizationManager.T(IsSoftware ? "KbTypeSoftware" : "KbTypeHardware");
        public string DisplayName => $"{LocalizedName} {TypeLabel}";

        public override string ToString()
        {
            return DisplayName;
        }

        private static int _currentAnimationSpeed = 2; // 1 = Slow, 2 = Normal, 3 = Fast
        public static int CurrentAnimationSpeed => _currentAnimationSpeed;

        public static void SetCurrentAnimationSpeed(int speed) => _currentAnimationSpeed = speed;

        public static byte GetIteSpeedByte()
        {
            if (_currentAnimationSpeed == 1)
            {
                return 0x08;
            }
            return _currentAnimationSpeed == 3 ? (byte)0x02 : (byte)0x05;
        }

        public void SetEffect()
        {
            StopAllSoftwareEffects();
            if (IsIteHardwareEffect || UniqId != 0)
            {
                KeyboardBackLight.SetMode(this);
            }
            else
            {
                KeyboardEffects.ResetTime();
                Timer?.Start();
            }
        }
    }
}

