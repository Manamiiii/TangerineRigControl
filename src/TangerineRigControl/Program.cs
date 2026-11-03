// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.UI;

namespace TangerineRigControl
{
    internal static class Program
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                var result = SelfTest.Run();
                if (result != 0) Console.Error.WriteLine("Self-test assertion exit code: " + result);
                return result;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, AppIdentity.MutexName, out createdNew))
            {
                if (!createdNew)
                {
#if !INSTALLER_TEST
                    MessageBox.Show("TangerineRigControl 已经在运行。请双击托盘图标，或按 Ctrl+Alt+F12 显示控制台。", "TangerineRigControl",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
#endif
                    return 2;
                }

                FileStream lifecycle;
                try { lifecycle = AppIdentity.AcquireLifecycleLock(); }
                catch (IOException)
                {
#if !INSTALLER_TEST
                    MessageBox.Show("程序正在运行，或安装/卸载尚未完成。请完成当前操作后重试。", "TangerineRigControl");
#endif
                    return 3;
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("无法访问本机配置目录，请检查当前用户的文件权限。", "TangerineRigControl");
                    return 3;
                }
                using (lifecycle)
                {
                    SetCurrentProcessExplicitAppUserModelID(AppIdentity.ModelId);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
#if INSTALLER_TEST
                    return InstallerSmokeTest.Run(args);
#else
                    var settings = SettingsStore.Load();
                    settings.StartMinimized = args != null && Array.Exists(args, arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(SettingsStore.LoadWarning))
                        MessageBox.Show(SettingsStore.LoadWarning, "配置读取提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Run(new MainForm(settings));
#endif
                }
            }
#if !INSTALLER_TEST
            return 0;
#endif
        }
    }
}
