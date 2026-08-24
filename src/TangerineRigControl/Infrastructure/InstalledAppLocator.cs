using System;
using System.IO;
using Microsoft.Win32;

namespace TangerineRigControl.Infrastructure
{
    internal static class InstalledAppLocator
    {
        private static readonly string[] RegistryPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        public static string FindExecutable(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;

            var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (var hive in hives)
            {
                foreach (var path in RegistryPaths)
                {
                    using (var root = hive.OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (var keyName in root.GetSubKeyNames())
                        {
                            using (var key = root.OpenSubKey(keyName))
                            {
                                if (key == null) continue;
                                var candidateName = Convert.ToString(key.GetValue("DisplayName"));
                                if (!string.Equals(candidateName, displayName, StringComparison.OrdinalIgnoreCase)) continue;

                                var icon = CleanExecutableValue(Convert.ToString(key.GetValue("DisplayIcon")));
                                if (File.Exists(icon)) return icon;

                                var location = Convert.ToString(key.GetValue("InstallLocation"));
                                var probablePath = Path.Combine(location ?? string.Empty, displayName + ".exe");
                                if (File.Exists(probablePath)) return probablePath;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static string CleanExecutableValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim().Trim('"');
            var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeIndex >= 0 ? value.Substring(0, exeIndex + 4) : value;
        }
    }
}

