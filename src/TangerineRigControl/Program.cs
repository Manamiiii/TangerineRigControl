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
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("TangerineRigControl 已经在运行。", "TangerineRigControl",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var settings = SettingsStore.Load();
                Application.Run(new MainForm(settings));
            }
        }
    }
}

