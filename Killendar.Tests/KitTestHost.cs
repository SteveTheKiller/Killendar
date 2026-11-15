using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Shared plumbing for the kit regression tests: a one-shot STA thread with an
    /// Application, source readers, and reflection helpers for internal kit members.</summary>
    internal static class KitTestHost
    {
        private static readonly object Gate = new object();

        public static void Sta(Action body, bool needApp = false)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (needApp) TestApplication.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The kit test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        public static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Killendar.csproj"))) return dir.FullName;
            throw new InvalidOperationException("Killendar.csproj was not found above the test output.");
        }

        public static string Source(string relative) => File.ReadAllText(Path.Combine(Root(), relative));

        public static Type AppType(string fullName) =>
            typeof(App).Assembly.GetType(fullName, throwOnError: false)
            ?? throw new InvalidOperationException("Type not found: " + fullName);

        /// <summary>Invokes a static method of a kit type, asserting it exists.</summary>
        public static object? CallStatic(Type type, string name, params object?[] args)
        {
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(method != null, type.Name + "." + name + " does not exist.");
            return method!.Invoke(null, args);
        }
    }
}
