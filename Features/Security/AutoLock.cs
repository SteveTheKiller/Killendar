using System;
using System.Runtime.InteropServices;

namespace Killendar.Features
{
    /// <summary>When an encrypted Killendar locks itself: after a chosen idle time, or when
    /// Windows locks. Both are off by default; the lock button's menu sets them.</summary>
    internal static class AutoLock
    {
        internal const string IdleSetting = "LockAfterIdleMinutes";
        internal const string WindowsSetting = "LockWithWindows";

        /// <summary>The idle choices the menu offers. Anything else in the setting means off.</summary>
        internal static readonly int[] IdleChoices = [5, 15, 30];

        internal static int IdleMinutes(string? setting) =>
            int.TryParse(setting, out int minutes) && Array.IndexOf(IdleChoices, minutes) >= 0 ? minutes : 0;

        internal static bool IdleDue(TimeSpan idle, int minutes) =>
            minutes > 0 && idle >= TimeSpan.FromMinutes(minutes);

        /// <summary>Time since the last keyboard or mouse input anywhere in this Windows session,
        /// so reading another window counts as being there.</summary>
        internal static TimeSpan IdleTime()
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);
    }
}
