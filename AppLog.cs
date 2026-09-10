using System;
using System.Diagnostics;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Tiny pub/sub log. There's no console any more (this runs as a tray app), so
    /// background logic reports through here instead - the status window subscribes to
    /// show it, and it always mirrors to Debug.WriteLine for when you're attached with
    /// a debugger.
    /// </summary>
    internal static class AppLog
    {
        public static event Action<string>? MessageLogged;

        public static void Write(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Debug.WriteLine(line);
            MessageLogged?.Invoke(line);
        }
    }
}
