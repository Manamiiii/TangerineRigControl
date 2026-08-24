using System;
using System.Threading;
using System.Windows.Forms;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.UI;

namespace TangerineRigControl
{
    internal static class Program
    {
        private const string MutexName = "Local\\TangerineRigControl.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return SelfTest.Run();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("TangerineRigControl 已经在运行。", "TangerineRigControl",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 2;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var settings = SettingsStore.Load();
                Application.Run(new MainForm(settings));
            }
            return 0;
        }
    }
}
