using System;
using System.IO;
using System.Reflection;

namespace RevitTrueGltf.Live
{
    internal static class LiveAssemblyResolver
    {
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered) return;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
            _registered = true;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            string dir = Path.GetDirectoryName(typeof(LiveAssemblyResolver).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return null;
            string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        }
    }
}
