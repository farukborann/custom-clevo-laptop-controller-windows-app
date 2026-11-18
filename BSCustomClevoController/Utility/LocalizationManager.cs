using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace BSCustomClevoController.Utility
{
    public sealed class LanguageOption
    {
        public string Code { get; }
        public string DisplayName { get; }

        public LanguageOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }
    }

    public static class LocalizationManager
    {
        private const string DefaultLanguageCode = "en-us";

        public static event Action LanguageChanged;
        public static event Action<IReadOnlyDictionary<string, string>> StringsApplied;

        private static readonly (string Code, string DefaultName)[] BuiltInOrder =
        {
            (DefaultLanguageCode, "English"),
            ("id-id", "Bahasa Indonesia"),
            ("es", "Español"),
            ("de", "Deutsch"),
            ("it", "Italiano"),
            ("fr-fr", "Français (France)"),
            ("fr-ca", "Français (Canada)"),
            ("pt-br", "Português (Brasil)"),
            ("pt-pt", "Português (Portugal)"),
            ("ru", "Русский"),
            ("tr-tr", "Türkçe"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("zh-cn", "简体中文"),
            ("zh-tw", "繁體中文"),
            ("th-th", "ไทย"),
            ("vi-vn", "Tiếng Việt")
        };

        // Emergency fallback if Localization\*.ini files are missing or unreadable.
        private static readonly Dictionary<string, string> EmergencyFallback = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppTitle"] = "Control Center",
            ["NavBrand"] = "Control Center",
            ["NavPerformance"] = "Performance",
            ["NavFan"] = "Fan Control",
            ["NavKeyboard"] = "Keyboard Lighting",
            ["NavBattery"] = "Battery",
            ["NavTools"] = "System Tools",
            ["NavSettings"] = "Settings",
            ["NavAbout"] = "About",
            ["SettingsSupported"] = "Supported",
            ["SettingsNotSupported"] = "Not Supported",
            ["CommonNotSupported"] = "Not supported on this model",
            ["CommonResetDefaults"] = "Reset to Defaults",
            ["CommonNotice"] = "Notice"
        };

        private const long MaxIniFileBytes = 1024 * 1024;
        private const int MaxIniLineLength = 4096;

        private static readonly object SyncLock = new object();
        private static List<LanguageOption> _supportedLanguages = BuildDefaultLanguageList();
        private static Dictionary<string, string> _englishBase = new Dictionary<string, string>(EmergencyFallback, StringComparer.Ordinal);
        private static Dictionary<string, string> _activeStrings = new Dictionary<string, string>(EmergencyFallback, StringComparer.Ordinal);

        public static IReadOnlyDictionary<string, string> GetActiveStringsSnapshot()
        {
            lock (SyncLock)
            {
                return new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(_activeStrings, StringComparer.Ordinal));
            }
        }

        public static IReadOnlyList<LanguageOption> SupportedLanguages
        {
            get
            {
                lock (SyncLock)
                {
                    return _supportedLanguages;
                }
            }
        }

        public static string CurrentLanguage { get; private set; } = DefaultLanguageCode;

        public static string LocalizationFolder
        {
            get
            {
                string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                string assemblyLocation = typeof(LocalizationManager).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    string assemblyDir = Path.GetDirectoryName(assemblyLocation);
                    if (!string.IsNullOrEmpty(assemblyDir))
                    {
                        string assemblyCandidate = Path.Combine(assemblyDir, "Localization");
                        if (Directory.Exists(assemblyCandidate))
                        {
                            return assemblyCandidate;
                        }
                    }
                }

                return candidate;
            }
        }

        public static bool IsValidLanguageCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            string trimmed = code.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 32) return false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool isAlphaNum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                bool isSeparator = (c == '-' || c == '_') && i > 0 && i < trimmed.Length - 1;
                if (!isAlphaNum && !isSeparator)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TryResolveLanguagePackPath(string code, out string fullPath)
        {
            fullPath = null;
            if (!IsValidLanguageCode(code)) return false;

            try
            {
                string baseDir = Path.GetFullPath(LocalizationFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(baseDir, code.Trim().ToLowerInvariant() + ".ini"));
                string prefix = baseDir + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Initialize()
        {
            Initialize(null, saveToRegistry: true);
        }

        public static void Initialize(string overrideCode, bool saveToRegistry)
        {
            ReloadAvailableLanguages();
            string code = !string.IsNullOrWhiteSpace(overrideCode)
                ? overrideCode
                : ResolveSavedOrSystemLanguage();
            ApplyLanguage(code, saveToRegistry: saveToRegistry);
        }

        public static void SetLanguage(string code)
        {
            SetLanguage(code, saveToRegistry: true);
        }

        public static void SetLanguage(string code, bool saveToRegistry)
        {
            ReloadAvailableLanguages();
            ApplyLanguage(code, saveToRegistry: saveToRegistry);
        }

        public static string T(string key)
        {
            return T(key, key);
        }

        public static string T(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            lock (SyncLock)
            {
                if (_activeStrings.TryGetValue(key, out string localized))
                {
                    return localized;
                }

                if (_englishBase.TryGetValue(key, out string baseValue))
                {
                    return baseValue;
                }
            }

            return EmergencyFallback.TryGetValue(key, out string emergency) ? emergency : fallback;
        }

        private static string _cachedCultureCode;
        private static CultureInfo _cachedCulture;

        public static CultureInfo CurrentCulture
        {
            get
            {
                string code = CurrentLanguage;
                if (string.IsNullOrWhiteSpace(code))
                {
                    return CultureInfo.CurrentCulture;
                }

                if (_cachedCulture != null && string.Equals(_cachedCultureCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    return _cachedCulture;
                }

                CultureInfo resolved = CultureInfo.CurrentCulture;
                try
                {
                    resolved = CultureInfo.ReadOnly(CultureInfo.CreateSpecificCulture(code));
                }
                catch (CultureNotFoundException)
                {
                    // Unknown culture code; fall back to the resolved CurrentCulture.
                }
                catch (ArgumentException)
                {
                    // Invalid culture name syntax; fall back to the resolved CurrentCulture.
                }

                _cachedCultureCode = code;
                _cachedCulture = resolved;
                return resolved;
            }
        }

        public static string ToUpper(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return CurrentCulture.TextInfo.ToUpper(text);
        }

        public static string Format(string key, params object[] args)
        {
            string pattern = T(key);
            if (args == null || args.Length == 0) return pattern;

            try
            {
                return string.Format(CurrentCulture, pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }

        private static List<LanguageOption> BuildDefaultLanguageList()
        {
            var list = new List<LanguageOption>(BuiltInOrder.Length);
            foreach (var item in BuiltInOrder)
            {
                list.Add(new LanguageOption(item.Code, item.DefaultName));
            }
            return list;
        }

        private static void ReloadAvailableLanguages()
        {
            Dictionary<string, string> discovered = DiscoverLanguageDisplayNames(LocalizationFolder);

            var ordered = new List<LanguageOption>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var builtIn in BuiltInOrder)
            {
                string displayName = discovered.TryGetValue(builtIn.Code, out string fileDisplayName)
                    ? fileDisplayName
                    : builtIn.DefaultName;
                ordered.Add(new LanguageOption(builtIn.Code, displayName));
                added.Add(builtIn.Code);
            }

            List<LanguageOption> extras = discovered
                .Where(kvp => !added.Contains(kvp.Key))
                .Select(kvp => new LanguageOption(kvp.Key, kvp.Value))
                .ToList();
            extras.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            ordered.AddRange(extras);

            Dictionary<string, string> enBase = TryResolveLanguagePackPath(DefaultLanguageCode, out string enPath)
                ? LoadIniDictionary(enPath)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var mergedBase = new Dictionary<string, string>(EmergencyFallback, StringComparer.Ordinal);
            foreach (var kvp in enBase.Where(kvp => !string.Equals(kvp.Key, "_Name", StringComparison.Ordinal)))
            {
                mergedBase[kvp.Key] = kvp.Value;
            }

            lock (SyncLock)
            {
                _supportedLanguages = ordered;
                _englishBase = mergedBase;
            }
        }

        private static Dictionary<string, string> DiscoverLanguageDisplayNames(string folder)
        {
            var discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(folder))
                {
                    return discovered;
                }

                foreach (string filePath in Directory.GetFiles(folder, "*.ini"))
                {
                    string code = Path.GetFileNameWithoutExtension(filePath)?.Trim().ToLowerInvariant();
                    if (IsValidLanguageCode(code))
                    {
                        discovered[code] = ReadDisplayName(filePath) ?? code;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LocalizationManager.ReloadAvailableLanguages", ex);
            }
            return discovered;
        }

        private const string AppLanguageRegistryKey = @"HKEY_CURRENT_USER\Software\BSCustomClevoController";
        private const string AppLanguageRegistryValue = "Language";
        private const string LegacyLanguageRegistryKey = @"HKEY_CURRENT_USER\Software\ControlCenter3.0\Fan\";
        private const string LegacyLanguageRegistryValue = "language";

        private static string ResolveSavedOrSystemLanguage()
        {
            string raw = Convert.ToString(Registry.GetValue(AppLanguageRegistryKey, AppLanguageRegistryValue, ""), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = Convert.ToString(Registry.GetValue(LegacyLanguageRegistryKey, LegacyLanguageRegistryValue, ""), CultureInfo.InvariantCulture);
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name)
                    ? CultureInfo.CurrentCulture.Name
                    : CultureInfo.CurrentUICulture.Name;
            }
            return NormalizeCode(raw);
        }

        private static void SaveLanguageToRegistry(string normalizedCode)
        {
            Registry.SetValue(AppLanguageRegistryKey, AppLanguageRegistryValue, normalizedCode, RegistryValueKind.String);
            Registry.SetValue(LegacyLanguageRegistryKey, LegacyLanguageRegistryValue, normalizedCode, RegistryValueKind.String);
        }

        private static void ApplyLanguage(string code, bool saveToRegistry)
        {
            string normalized = NormalizeCode(code);
            CurrentLanguage = normalized;

            if (saveToRegistry)
            {
                try
                {
                    SaveLanguageToRegistry(normalized);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("LocalizationManager.ApplyLanguage", ex);
                }
            }

            Dictionary<string, string> mergedStrings;
            lock (SyncLock)
            {
                mergedStrings = new Dictionary<string, string>(_englishBase, StringComparer.Ordinal);
            }

            if (!string.Equals(normalized, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase) &&
                TryResolveLanguagePackPath(normalized, out string filePath))
            {
                Dictionary<string, string> overlay = LoadIniDictionary(filePath);
                foreach (var kvp in overlay.Where(kvp => !string.Equals(kvp.Key, "_Name", StringComparison.Ordinal)))
                {
                    mergedStrings[kvp.Key] = kvp.Value;
                }
            }

            lock (SyncLock)
            {
                _activeStrings = mergedStrings;
            }

            StringsApplied?.Invoke(new ReadOnlyDictionary<string, string>(mergedStrings));
            LanguageChanged?.Invoke();
        }

        private static string NormalizeCode(string code)
        {
            if (!IsValidLanguageCode(code)) return DefaultLanguageCode;
            string lower = code.Trim().ToLowerInvariant();

            lock (SyncLock)
            {
                LanguageOption match = _supportedLanguages.FirstOrDefault(
                    opt => string.Equals(opt.Code, lower, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match.Code;
                }
            }

            return DefaultLanguageCode;
        }

        private static string ReadDisplayName(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length > MaxIniFileBytes) return null;

                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > MaxIniLineLength) continue;
                        if (!TryParseLine(line, out string key, out string val)) continue;
                        if (string.Equals(key, "_Name", StringComparison.Ordinal))
                        {
                            string trimmed = val.Trim();
                            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LocalizationManager.ReadDisplayName", ex);
            }

            return null;
        }

        private static Dictionary<string, string> LoadIniDictionary(string filePath)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(filePath))
            {
                return result;
            }

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length > MaxIniFileBytes)
                {
                    return result;
                }

                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > MaxIniLineLength) continue;
                        if (TryParseLine(line, out string key, out string val))
                        {
                            result[key] = val;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LocalizationManager.LoadIniDictionary", ex);
            }

            return result;
        }

        private static bool TryParseLine(string rawLine, out string key, out string value)
        {
            key = null;
            value = null;
            if (string.IsNullOrWhiteSpace(rawLine)) return false;

            string trimmedStart = rawLine.TrimStart();
            if (trimmedStart.StartsWith("#", StringComparison.Ordinal) ||
                trimmedStart.StartsWith(";", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("[", StringComparison.Ordinal))
            {
                return false;
            }

            int eq = rawLine.IndexOf('=');
            if (eq <= 0) return false;

            key = rawLine.Substring(0, eq).Trim();
            if (string.IsNullOrEmpty(key)) return false;

            string rawVal = rawLine.Substring(eq + 1);
            value = UnescapeValue(rawVal);
            return true;
        }

        private static string UnescapeValue(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.IndexOf('\\') < 0)
            {
                return raw ?? string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            int i = 0;
            while (i < raw.Length)
            {
                char ch = raw[i];
                if (ch == '\\' && i + 1 < raw.Length && TryMapEscapeChar(raw[i + 1], out char unescaped))
                {
                    sb.Append(unescaped);
                    i += 2;
                    continue;
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        private static bool TryMapEscapeChar(char escaped, out char unescaped)
        {
            switch (escaped)
            {
                case 'n':
                    unescaped = '\n';
                    return true;
                case 'r':
                    unescaped = '\r';
                    return true;
                case 't':
                    unescaped = '\t';
                    return true;
                case '\\':
                    unescaped = '\\';
                    return true;
                default:
                    unescaped = '\0';
                    return false;
            }
        }
    }
}
