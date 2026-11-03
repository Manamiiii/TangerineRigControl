// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TangerineRigControl.Infrastructure
{
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = AppIdentity.Name;

        public static void SetEnabled(bool enabled)
        {
            using (var key = enabled ? Registry.CurrentUser.CreateSubKey(RunKeyPath) : Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) return;
                if (enabled)
                {
                    var command = "\"" + Application.ExecutablePath + "\" --startup";
                    if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal)) key.SetValue(ValueName, command);
                }
                else if (IsCurrentCommand(key.GetValue(ValueName) as string, Application.ExecutablePath))
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        internal static Tuple<object, RegistryValueKind> ReadValue()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null) return Tuple.Create<object, RegistryValueKind>(null, RegistryValueKind.String);
                var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return Tuple.Create(value, value == null ? RegistryValueKind.String : key.GetValueKind(ValueName));
            }
        }

        internal static void RestoreValue(Tuple<object, RegistryValueKind> value)
        {
            using (var key = value.Item1 == null ? Registry.CurrentUser.OpenSubKey(RunKeyPath, true) : Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) return;
                if (value.Item1 == null) key.DeleteValue(ValueName, false);
                else key.SetValue(ValueName, value.Item1, value.Item2);
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && IsCurrentCommand(key.GetValue(ValueName) as string, Application.ExecutablePath);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsCurrentCommand(string command, string executable)
        {
            return string.Equals(command, "\"" + executable + "\" --startup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command, "\"" + executable + "\"", StringComparison.OrdinalIgnoreCase);
        }
    }
}
