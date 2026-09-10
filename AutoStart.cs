using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Registers/unregisters launching this app at sign-in via the per-user Run key.
    /// The on/off preference is remembered separately from the registry key's mere
    /// presence (in a small settings file) so turning it off via the tray menu sticks -
    /// otherwise a later run would just see the key missing and "helpfully" recreate it.
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AppleMusicDiscordPresence";

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppleMusicDiscordPresence", "settings.json");

        // AppContext.BaseDirectory (not Environment.ProcessPath) so this is correct even
        // when the current process is `dotnet.exe` hosting us during development - the
        // apphost this build produces always sits next to the managed dll.
        private static string ExePath => Path.Combine(AppContext.BaseDirectory, "AppleMusicDiscordPresence.exe");

        public static bool IsEnabled
        {
            get => LoadPreference() ?? true;
            set
            {
                SavePreference(value);
                Apply(value);
            }
        }

        /// <summary>
        /// Call once at startup. On the very first run ever, this honors the default
        /// (enabled) and writes the registry entry. On every later run it re-applies the
        /// saved preference, which self-heals the registry entry if the app has moved or
        /// been rebuilt to a different output folder.
        /// </summary>
        public static void EnsureAppliedOnStartup()
        {
            var pref = LoadPreference();
            if (pref == null)
            {
                SavePreference(true);
                Apply(true);
            }
            else
            {
                Apply(pref.Value);
            }
        }

        private static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (enabled)
                    key.SetValue(ValueName, $"\"{ExePath}\"");
                else
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not update the Windows startup entry: {ex.Message}");
            }
        }

        private static bool? LoadPreference()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                return doc.RootElement.TryGetProperty("autoStart", out var v)
                       && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? v.GetBoolean()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SavePreference(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { autoStart = enabled }));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not save settings: {ex.Message}");
            }
        }
    }
}
