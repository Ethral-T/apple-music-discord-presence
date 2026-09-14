using System;
using System.IO;
using Microsoft.Win32;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Registers/unregisters launching this app at sign-in via the per-user Run key.
    /// The on/off preference is remembered in AppSettings, separately from the registry
    /// key's mere presence, so turning it off via the tray menu sticks - otherwise a
    /// later run would just see the key missing and "helpfully" recreate it.
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AppleMusicDiscordPresence";

        // AppContext.BaseDirectory (not Environment.ProcessPath) so this is correct even
        // when the current process is `dotnet.exe` hosting us during development - the
        // apphost this build produces always sits next to the managed dll.
        private static string ExePath => Path.Combine(AppContext.BaseDirectory, "AppleMusicDiscordPresence.exe");

        public static bool IsEnabled
        {
            get => AppSettings.GetAutoStart() ?? true;
            set
            {
                AppSettings.SetAutoStart(value);
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
            var pref = AppSettings.GetAutoStart();
            if (pref == null)
            {
                AppSettings.SetAutoStart(true);
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
    }
}
