using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TangerineRigControl.Infrastructure
{
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TangerineRigControl";

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户的启动项设置。");
                if (enabled)
                {
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
