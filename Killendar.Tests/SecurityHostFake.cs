using System.Collections.Generic;
using System.Windows;
using Killendar.Features;

namespace Killendar.Tests
{
    /// <summary>Records what SecurityController tells its window, for controller tests that never
    /// show a dialog.</summary>
    internal sealed class SecurityHostFake : ISecurityHost
    {
        internal readonly List<string> Statuses = [];
        internal bool? LockState;
        public Window Window => null!;
        public string Loc(string key) => key + " {0}";
        public void SetStatus(string text) => Statuses.Add(text);
        public void ShowLockState(bool encrypted) => LockState = encrypted;
        public void ShowActiveKillendar(string name, bool visible) { }
        public void RefreshView() { }
        public bool IsSidebarOpen => false;
        public void RestoreSidebar() { }
        public void CloseSidebar() { }
    }
}
