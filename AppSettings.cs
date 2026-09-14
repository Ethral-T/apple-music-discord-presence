using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Single JSON settings file at %APPDATA%\AppleMusicDiscordPresence\settings.json,
    /// shared by everything that needs to remember something across restarts (the
    /// autostart preference, your Discord Client ID). All reads/writes go through here
    /// as a read-modify-write over the whole document, so one setting's save can never
    /// clobber another's.
    /// </summary>
    internal static class AppSettings
    {
        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppleMusicDiscordPresence", "settings.json");

        private static readonly object Lock = new();

        public static bool? GetAutoStart()
        {
            lock (Lock)
            {
                var v = Load()["autoStart"];
                return v != null && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;
            }
        }

        public static void SetAutoStart(bool value)
        {
            lock (Lock)
            {
                var root = Load();
                root["autoStart"] = value;
                Save(root);
            }
        }

        public static string? GetDiscordClientId()
        {
            lock (Lock)
            {
                var v = Load()["discordClientId"]?.GetValue<string>();
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
        }

        /// <param name="value">Pass null or empty to remove it.</param>
        public static void SetDiscordClientId(string? value)
        {
            lock (Lock)
            {
                var root = Load();
                if (string.IsNullOrWhiteSpace(value))
                    root.Remove("discordClientId");
                else
                    root["discordClientId"] = value.Trim();
                Save(root);
            }
        }

        // caller holds Lock
        private static JsonObject Load()
        {
            try
            {
                if (File.Exists(Path) && JsonNode.Parse(File.ReadAllText(Path)) is JsonObject obj)
                    return obj;
            }
            catch
            {
                // missing, empty, or corrupt - start fresh rather than crash the app over it
            }
            return new JsonObject();
        }

        // caller holds Lock
        private static void Save(JsonObject root)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not save settings: {ex.Message}");
            }
        }
    }
}
